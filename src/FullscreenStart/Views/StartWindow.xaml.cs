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
    private DateTime _lastHidden = DateTime.MinValue;
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
        Deactivated += (_, _) => { if (!_isDragging) HideMenu(); };
        InitializeDragDrop();
    }

    /// <summary>Creates the native window up front so that showing it later is instant.</summary>
    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();

        // Keep it out of Alt+Tab and the taskbar.
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));

        int dark = 1, square = DWMWCP_DONOTROUND, backdrop = DWMSBT_TRANSIENTWINDOW;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));

        // System acrylic underneath: visible only when no wallpaper image could be captured.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        HwndSource.FromHwnd(_hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
        Background = Brushes.Transparent;

        RefreshBackdrop();
    }

    // ---------------------------------------------------------------------------------------
    // Show / hide
    // ---------------------------------------------------------------------------------------

    public void Toggle()
    {
        if (IsVisible) HideMenu();
        // A click on the tray icon first deactivates (and hides) the menu; don't reopen it right away.
        else if ((DateTime.Now - _lastHidden).TotalMilliseconds > 300) ShowMenu();
    }

    public void ShowMenu()
    {
        if (IsVisible)
        {
            Activate();
            return;
        }

        var bounds = ScreenInfo.MenuBounds();
        SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);

        _vm.Reset();
        if ((DateTime.Now - _vm.Catalog.LastRefresh).TotalMinutes > 5) _ = _vm.Catalog.RefreshAsync();
        if ((DateTime.Now - _lastBackdrop).TotalSeconds > 2) RefreshBackdrop();

        PinnedScroll.ScrollToHome();
        AllAppsList.ScrollIntoView(_vm.AllAppsView?.Cast<object>().FirstOrDefault());
        PlayOpenAnimation();

        Show();
        Activate();
        SetForegroundWindow(_hwnd);
        Keyboard.Focus(this);
    }

    public void HideMenu()
    {
        if (!IsVisible) return;
        CancelDrag();
        Hide();
        _lastHidden = DateTime.Now;
        LetterIndex.Visibility = Visibility.Collapsed;
        _vm.Reset();
    }

    private void PlayOpenAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(160);
        ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        ((TranslateTransform)ContentRoot.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(28, 0, duration) { EasingFunction = ease });
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
            else HideMenu();
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
        AppLauncher.Launch(app, asAdmin);
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
        if (_closeOnMouseUp && !e.Handled && !_isDragging) HideMenu();
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

    private void Documents_Click(object sender, RoutedEventArgs e) =>
        OpenAndHide(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

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
