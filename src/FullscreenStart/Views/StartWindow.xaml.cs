using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FullscreenStart.Models;
using FullscreenStart.Services;
using FullscreenStart.ViewModels;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Views;

public partial class StartWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly StaWorker _backdropWorker = new("FullscreenStart.Backdrop");
    private IntPtr _hwnd;
    private DateTime _lastDeactivationHide = DateTime.MinValue;
    private DateTime _lastBackdrop = DateTime.MinValue;
    private bool _backdropPending;
    private bool _closeOnMouseUp;

    public StartWindow(MainViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        BackdropTint.Opacity = Math.Clamp(viewModel.Settings.BackdropTintOpacity, 0, 1);
        BackdropNoise.Fill = CreateNoiseBrush();
        UserNameText.Text = UserInfo.Name;
        UserButton.ToolTip = UserInfo.Name;
        if (UserInfo.Picture() is { } picture)
        {
            UserPicture.Fill = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
            UserPicture.Visibility = Visibility.Visible;
            UserGlyph.Visibility = Visibility.Collapsed;
        }

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        UpdateNavIndicator();
        Deactivated += (_, _) => { if (!_isDragging) HideMenu(fromDeactivation: true); };
        InitializeDragDrop();
    }

    /// <summary>Creates the native window up front so that showing it later is instant.</summary>
    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();

        // Keep it out of Alt+Tab and the taskbar; layered so the whole window can fade (alpha set per frame).
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        SetWindowOpacity(0);

        int dark = 1, square = DWMWCP_DONOTROUND, backdrop = DWMSBT_TRANSIENTWINDOW, noTransitions = 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));
        // Windows' own show/hide animation would play on top of ours.
        DwmSetWindowAttribute(_hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref noTransitions, sizeof(int));

        // System acrylic underneath: visible only when no wallpaper image could be captured.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        HwndSource.FromHwnd(_hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        Background = Brushes.Transparent;

        RefreshBackdrop();
        Prewarm();
    }

    /// <summary>Runs one layout pass while hidden so the first opening does not build all visuals.</summary>
    private void Prewarm()
    {
        var bounds = ScreenInfo.MenuBounds();
        double scale = Math.Max(1, GetDpiForWindow(_hwnd)) / 96.0;
        var size = new Size(bounds.Width / scale, bounds.Height / scale);
        RootGrid.Measure(size);
        RootGrid.Arrange(new Rect(size));
    }

    // ---------------------------------------------------------------------------------------
    // Show / hide
    // ---------------------------------------------------------------------------------------

    public IntPtr Handle => _hwnd;

    private enum MenuState
    {
        Hidden,
        Opening,
        Open,
        Closing,
    }

    private MenuState _state = MenuState.Hidden;

    private bool IsMenuShown => _state is MenuState.Opening or MenuState.Open;

    public void Toggle()
    {
        if (IsMenuShown) HideMenu(restoreFocus: true);
        // A click on the tray icon first deactivates (and hides) the menu; don't reopen it right away.
        else if ((DateTime.Now - _lastDeactivationHide).TotalMilliseconds > 300) ShowMenu();
    }

    public void ShowMenu()
    {
        if (IsMenuShown)
        {
            EnsureForeground();
            return;
        }

        double startProgress = 0;
        if (_state == MenuState.Closing)
        {
            // Reopened while fading out: turn around from the current opacity.
            startProgress = MenuMotion.OpeningProgressFor(_windowOpacity);
        }
        else
        {
            LogFirstFrame(Stopwatch.StartNew());
            var foreground = GetForegroundWindow();
            _previousForeground = foreground != _hwnd ? foreground : IntPtr.Zero;
            var bounds = ScreenInfo.MenuBounds();
            SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
            ApplyFrame(MenuMotion.Opening(0));
            Show();
        }

        RootGrid.IsHitTestVisible = true;
        StartAnimation(MenuState.Opening, startProgress);
        Activate();
        EnsureForeground();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Windows may refuse to hand the foreground to a background process. Typing into the search and
    /// closing on focus loss both need it, so fall back to sharing the foreground thread's input state.
    /// </summary>
    private void EnsureForeground()
    {
        if (SetForegroundWindow(_hwnd) && GetForegroundWindow() == _hwnd) return;

        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint ownThread = GetCurrentThreadId();
        if (foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true))
        {
            SetForegroundWindow(_hwnd);
            AttachThreadInput(ownThread, foregroundThread, false);
        }
        if (GetForegroundWindow() != _hwnd) Log.Info("Menu could not take the foreground");
    }

    private int _openCount;

    /// <summary>Logs how long it took until the first frame was rendered (first opening, or when slow).</summary>
    private void LogFirstFrame(Stopwatch opening)
    {
        int count = ++_openCount;
        // ContextIdle runs after the layout and render passes of the newly shown window.
        Dispatcher.BeginInvoke(() =>
        {
            long ms = opening.ElapsedMilliseconds;
            if (count == 1 || ms > 100) Log.Info($"Menu opened (#{count}) in {ms} ms");
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private IntPtr _previousForeground;

    /// <param name="restoreFocus">
    /// Give the focus back to the window that had it before the menu opened (closing with Esc, the
    /// hotkey, the Windows key or a click on empty space). Not wanted after launching an app or when
    /// another window was clicked.
    /// </param>
    /// <param name="fromDeactivation">The menu lost focus to another window (e.g. a click on the tray icon).</param>
    public void HideMenu(bool restoreFocus = false, bool fromDeactivation = false)
    {
        if (!IsMenuShown) return;
        try
        {
            CancelDrag();
        }
        catch (Exception ex)
        {
            // Never let a broken drag keep the menu on screen.
            Log.Error("Cancelling drag failed", ex);
        }
        if (fromDeactivation) _lastDeactivationHide = DateTime.Now;

        // Ignore input while fading out; hand the keyboard back right away.
        RootGrid.IsHitTestVisible = false;
        if (restoreFocus && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground))
            SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;

        StartAnimation(MenuState.Closing, startProgress: 1 - _windowOpacity);
    }

    /// <summary>Called once the close animation has finished: hide and prepare the next opening.</summary>
    private void OnHidden()
    {
        Hide();
        LetterIndex.Visibility = Visibility.Collapsed;
        _vm.Reset();
        PinnedScroll.ScrollToHome();
        AllAppsList.ScrollIntoView(_vm.AllAppsView?.Cast<object>().FirstOrDefault());

        // Slow work happens while the menu is hidden, never during the animation.
        if ((DateTime.Now - _lastBackdrop).TotalSeconds > 30) RefreshBackdrop();
        if ((DateTime.Now - _vm.Catalog.LastRefresh).TotalMinutes > 5) _ = _vm.Catalog.RefreshAsync();
    }

    private void RefreshBackdrop()
    {
        if (!_vm.Settings.WallpaperBackdrop || _backdropPending) return;
        _backdropPending = true;
        var bounds = ScreenInfo.PrimaryMonitor().Monitor;
        int radius = _vm.Settings.BackdropBlurRadius;
        _backdropWorker.Run(() => WallpaperBackdrop.Capture(bounds, radius)).ContinueWith(t =>
        {
            _backdropPending = false;
            _lastBackdrop = DateTime.Now;
            if (t.IsFaulted) Debug.WriteLine(t.Exception);
            else if (t.Result != null) BackdropImage.Source = t.Result;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static Brush CreateNoiseBrush()
    {
        const int size = 128;
        var random = new Random(7);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte v = (byte)random.Next(256);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    // ---------------------------------------------------------------------------------------
    // Keyboard and search
    // ---------------------------------------------------------------------------------------

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (!IsMenuShown)
        {
            e.Handled = true; // fading out: the keyboard already belongs to the previous window
            return;
        }
        if (Keyboard.FocusedElement is TextBox || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        // Typing anywhere starts a search, like the Windows start screen.
        _vm.SearchText += e.Text;
        e.Handled = true;
        FocusSearchBox();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        if (!IsMenuShown)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Keyboard.FocusedElement is TextBox box && box != SearchBox)
            {
                // Escape in a rename box discards the edit.
                if (box.DataContext is GroupViewModel group) group.CancelRename();
                else box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                Keyboard.Focus(this);
            }
            else if (_vm.IsSearching) _vm.SearchText = "";
            else if (LetterIndex.Visibility == Visibility.Visible) LetterIndex.Visibility = Visibility.Collapsed;
            else if (_vm.IsSidebarExpanded) _vm.IsSidebarExpanded = false;
            else if (_vm.Columns.SelectMany(c => c.Groups).Any(g => g.ExpandedFolder != null)) _vm.CollapseFolders();
            else HideMenu(restoreFocus: true);
        }
    }

    private void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (_vm.SelectedResult is { } app) Launch(app);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Back when SearchBox.Text.Length <= 1 && SearchBox.SelectionLength == 0:
                _vm.SearchText = "";
                Keyboard.Focus(this);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_vm.SearchResults.Count == 0) return;
        int index = _vm.SelectedResult == null ? -1 : _vm.SearchResults.IndexOf(_vm.SelectedResult);
        index = Math.Clamp(index + delta, 0, _vm.SearchResults.Count - 1);
        _vm.SelectedResult = _vm.SearchResults[index];
        SearchList.ScrollIntoView(_vm.SelectedResult);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.View):
                UpdateNavIndicator();
                break;
            case nameof(MainViewModel.IsSidebarExpanded):
                AnimateSidebar(_vm.IsSidebarExpanded);
                break;
            case nameof(MainViewModel.IsSearching) when !_vm.IsSearching && SearchBox.IsKeyboardFocused:
                Keyboard.Focus(this);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Launching
    // ---------------------------------------------------------------------------------------

    private void Launch(AppEntry app, bool asAdmin = false)
    {
        if (_launchOverride != null) _launchOverride(app);
        else AppLauncher.Launch(app, asAdmin);
        HideMenu();
    }

    private void AppList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: AppEntry app })
        {
            e.Handled = true;
            Launch(app);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Click on empty space closes the menu
    // ---------------------------------------------------------------------------------------

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only reached when nothing interactive handled the press.
        _closeOnMouseUp = !e.Handled;
        if (_vm.IsSidebarExpanded)
        {
            _vm.IsSidebarExpanded = false;
            _closeOnMouseUp = false;
        }
        if (LetterIndex.Visibility == Visibility.Visible)
        {
            LetterIndex.Visibility = Visibility.Collapsed;
            _closeOnMouseUp = false;
        }
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_closeOnMouseUp && !e.Handled && !_isDragging) HideMenu(restoreFocus: true);
        _closeOnMouseUp = false;
    }

    // ---------------------------------------------------------------------------------------
    // Sidebar
    // ---------------------------------------------------------------------------------------

    private void UpdateNavIndicator()
    {
        PinnedButton.Tag = _vm.View == MenuView.Pinned ? "active" : null;
        AllAppsButton.Tag = _vm.View == MenuView.AllApps ? "active" : null;
    }

    private void AnimateSidebar(bool expand)
    {
        Sidebar.Background = expand ? (Brush)FindResource("SidebarExpandedBrush") : Brushes.Transparent;
        Sidebar.Effect = expand ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.5 } : null;
        if (!IsVisible)
        {
            // No animation while hidden: the next opening must start in the final state.
            Sidebar.BeginAnimation(WidthProperty, null);
            Sidebar.Width = expand ? 256 : 48;
            return;
        }

        var animation = new DoubleAnimation(expand ? 256 : 48, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Sidebar.BeginAnimation(WidthProperty, animation);
    }

    private void Hamburger_Click(object sender, RoutedEventArgs e) => _vm.IsSidebarExpanded = !_vm.IsSidebarExpanded;

    private void PinnedView_Click(object sender, RoutedEventArgs e)
    {
        _vm.SearchText = "";
        _vm.View = MenuView.Pinned;
        _vm.IsSidebarExpanded = false;
    }

    private void AllAppsView_Click(object sender, RoutedEventArgs e)
    {
        _vm.SearchText = "";
        _vm.View = MenuView.AllApps;
        _vm.IsSidebarExpanded = false;
    }

    // Downloads has no Environment.SpecialFolder entry; the shell alias resolves even if the folder was moved.
    private void Downloads_Click(object sender, RoutedEventArgs e) => OpenAndHide("shell:Downloads");

    private void Pictures_Click(object sender, RoutedEventArgs e) =>
        OpenAndHide(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenAndHide("ms-settings:");

    private void User_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Kontoeinstellungen ändern", "", () => OpenAndHide("ms-settings:yourinfo")));
        menu.Items.Add(CreateItem("Sperren", "", () => { HideMenu(); PowerActions.Lock(); }));
        menu.Items.Add(CreateItem("Abmelden", "", () => { HideMenu(); PowerActions.SignOut(); }));
        OpenMenuBeside(menu, UserButton);
    }

    private void Power_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Energie sparen", "", () => { HideMenu(); PowerActions.Sleep(); }));
        menu.Items.Add(CreateItem("Herunterfahren", "", () => { HideMenu(); PowerActions.Shutdown(); }));
        menu.Items.Add(CreateItem("Neu starten", "", () => { HideMenu(); PowerActions.Restart(); }));
        OpenMenuBeside(menu, PowerButton);
    }

    private void OpenAndHide(string target)
    {
        AppLauncher.Open(target);
        HideMenu();
    }

    // ---------------------------------------------------------------------------------------
    // All apps: letter index
    // ---------------------------------------------------------------------------------------

    private void LetterHeader_Click(object sender, RoutedEventArgs e)
    {
        LetterIndex.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void LetterIndex_Click(object sender, RoutedEventArgs e)
    {
        LetterIndex.Visibility = Visibility.Collapsed;
        if (sender is not Button { DataContext: LetterItem letter } || _vm.AllAppsView == null) return;

        var first = _vm.AllAppsView.Cast<AppEntry>().FirstOrDefault(a => a.Letter == letter.Letter);
        if (first == null) return;

        // Scroll so the letter's group starts at the top: jump to the end first, then back to the item.
        var last = _vm.AllAppsView.Cast<object>().LastOrDefault();
        if (last != null) AllAppsList.ScrollIntoView(last);
        AllAppsList.UpdateLayout();
        AllAppsList.ScrollIntoView(first);
        e.Handled = true;
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    internal static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var current = start; current != null; current = ParentOf(current))
        {
            if (current is T match) return match;
        }
        return null;
    }

    /// <summary>Nearest ancestor FrameworkElement whose Tag is one of the given markers.</summary>
    internal static FrameworkElement? FindTagged(DependencyObject? start, params string[] tags)
    {
        for (var current = start; current != null; current = ParentOf(current))
        {
            if (current is FrameworkElement { Tag: string tag } fe && tags.Contains(tag)) return fe;
        }
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject child) =>
        child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);

    protected override void OnClosed(EventArgs e)
    {
        _backdropWorker.Dispose();
        base.OnClosed(e);
    }
}
