using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FullscreenStart.Models;
using FullscreenStart.Services;
using FullscreenStart.ViewModels;

namespace FullscreenStart.Views;

/// <summary>Tile and group interaction: click, drag &amp; drop, context menus, renaming.</summary>
public partial class StartWindow
{
    private const double DragThreshold = 6;

    private abstract record DropTarget;
    private sealed record InsertTile(GroupViewModel Group, int Index) : DropTarget;
    private sealed record InsertInFolder(TileViewModel Folder, int Index) : DropTarget;
    private sealed record MergeWith(TileViewModel Tile) : DropTarget;
    private sealed record NewGroup(int Column) : DropTarget;
    private sealed record InsertGroup(int Column, int Index) : DropTarget;

    private FrameworkElement? _pressElement;
    private object? _pressData;
    private Point _pressPoint;
    private Point _grabOffset;
    private Point _lastDragPoint;
    private bool _isDragging;
    private DropTarget? _dropTarget;
    private Image? _ghost;
    private readonly Rectangle _indicator = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private void InitializeDragDrop()
    {
        DragLayer.Children.Add(_indicator);
        ColumnsHost.PreviewMouseLeftButtonDown += Tiles_PreviewMouseLeftButtonDown;
        RootGrid.PreviewMouseMove += Root_PreviewMouseMove;
        RootGrid.PreviewMouseLeftButtonUp += Root_PreviewMouseLeftButtonUp;
        RootGrid.LostMouseCapture += (_, _) => { if (_pressElement != null && Mouse.LeftButton == MouseButtonState.Released) EndPress(); };
        _autoScroll.Tick += (_, _) => AutoScroll();
    }

    // ---- press / click -----------------------------------------------------------------------

    private void Tiles_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<TextBox>(source) != null || FindAncestor<ScrollBar>(source) != null) return;

        var element = FindTagged(source, "tile", "groupheader");
        if (element == null || !Press(element, e.GetPosition(RootGrid))) return;
        Mouse.Capture(RootGrid, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void Root_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressElement != null) MovePress(e.GetPosition(RootGrid));
    }

    private void Root_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressElement == null) return;
        e.Handled = true;
        Release();
    }

    // Pointer-independent steps of a press, shared by the mouse handlers and the self-test.

    private bool Press(FrameworkElement element, Point position)
    {
        if (element.DataContext is GroupViewModel { IsRenaming: true }) return false;
        _pressElement = element;
        _pressData = element.DataContext;
        _pressPoint = position;
        if (_pressData is TileViewModel) SetPressed(element, true);
        return true;
    }

    private void MovePress(Point position)
    {
        if (_pressElement == null) return;
        if (!_isDragging && (position - _pressPoint).Length > DragThreshold) BeginDrag();
        if (_isDragging) UpdateDrag(position);
    }

    private void Release()
    {
        if (_pressElement == null) return;
        var data = _pressData;
        var element = _pressElement;
        var target = _dropTarget;
        bool wasDragging = _isDragging;
        EndPress();

        if (wasDragging) ApplyDrop(data, target);
        else Click(data, element);
    }

    private void Click(object? data, FrameworkElement element)
    {
        switch (data)
        {
            case TileViewModel { IsFolder: true } folder:
                _vm.ToggleFolder(folder);
                break;
            case TileViewModel { App: { } app }:
                Launch(app);
                break;
            case GroupViewModel group:
                StartRename(group);
                break;
        }
    }

    private static void SetPressed(FrameworkElement element, bool pressed)
    {
        // Transforms declared in a template are frozen, so assign a new one instead of changing it.
        element.RenderTransform = pressed ? new ScaleTransform(0.97, 0.97) : Transform.Identity;
    }

    // ---- drag & drop ---------------------------------------------------------------------------

    private void BeginDrag()
    {
        if (_pressElement == null) return;
        _isDragging = true;
        SetPressed(_pressElement, false);

        var bounds = BoundsOf(_pressElement);
        _grabOffset = new Point(_pressPoint.X - bounds.X, _pressPoint.Y - bounds.Y);
        _ghost = new Image
        {
            Source = Snapshot(_pressElement),
            Width = bounds.Width,
            Height = bounds.Height,
            Opacity = 0.85,
            IsHitTestVisible = false,
        };
        DragLayer.Children.Add(_ghost);
        _pressElement.Opacity = 0.3;

        // A dragged folder must not be dropped into its own drawer.
        if (_pressData is TileViewModel { IsFolder: true, IsExpanded: true }) _vm.CollapseFolders();

        NewColumnTarget.Visibility = Visibility.Visible;
        _autoScroll.Start();
    }

    private void UpdateDrag(Point position)
    {
        _lastDragPoint = position;
        if (_ghost != null)
        {
            Canvas.SetLeft(_ghost, position.X - _grabOffset.X);
            Canvas.SetTop(_ghost, position.Y - _grabOffset.Y);
        }
        _dropTarget = FindDropTarget(position, out var indicator, out bool asBar);
        ShowIndicator(_dropTarget == null ? null : indicator, asBar);
    }

    private void ApplyDrop(object? data, DropTarget? target)
    {
        if (target == null) return;
        if (data is TileViewModel tile)
        {
            var model = tile.Model;
            switch (target)
            {
                case InsertTile t: _vm.Edit(e => e.MoveTile(model, t.Group.Model, t.Index)); break;
                case InsertInFolder f: _vm.Edit(e => e.MoveIntoFolder(model, f.Folder.Model, f.Index)); break;
                case MergeWith m: _vm.Edit(e => e.MergeIntoFolder(model, m.Tile.Model)); break;
                case NewGroup n:
                    TileGroup? created = null;
                    _vm.Edit(e => created = e.MoveTileToNewGroup(model, n.Column, int.MaxValue));
                    if (created != null) StartRename(created);
                    break;
            }
        }
        else if (data is GroupViewModel group && target is InsertGroup g)
        {
            _vm.Edit(e => e.MoveGroup(group.Model, g.Column, g.Index));
        }
    }

    private void CancelDrag()
    {
        if (_pressElement != null) EndPress();
    }

    private void EndPress()
    {
        // Reset the state first: nothing below may leave the menu stuck in a drag.
        var element = _pressElement;
        var ghost = _ghost;
        _pressElement = null;
        _pressData = null;
        _dropTarget = null;
        _isDragging = false;
        _ghost = null;
        _autoScroll.Stop();

        if (ghost != null) DragLayer.Children.Remove(ghost);
        _indicator.Visibility = Visibility.Collapsed;
        NewColumnTarget.Visibility = Visibility.Collapsed;
        if (element != null)
        {
            SetPressed(element, false);
            element.Opacity = 1;
        }
        if (Mouse.Captured == RootGrid) RootGrid.ReleaseMouseCapture();
    }

    private DropTarget? FindDropTarget(Point position, out Rect indicator, out bool asBar)
    {
        indicator = Rect.Empty;
        asBar = false;

        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(ColumnsHost, null, result =>
        {
            hit = result.VisualHit;
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(RootGrid.TranslatePoint(position, ColumnsHost)));
        if (hit == null) return null;

        return _pressData switch
        {
            TileViewModel tile => FindTileTarget(tile, hit, position, ref indicator, ref asBar),
            GroupViewModel group => FindGroupTarget(group, hit, position, ref indicator, ref asBar),
            _ => null,
        };
    }

    private DropTarget? FindTileTarget(TileViewModel dragged, DependencyObject hit, Point position, ref Rect indicator, ref bool asBar)
    {
        var element = FindTagged(hit, "tile", "drawer", "groupheader", "group", "column", "newcolumn");
        if (element == null) return null;
        var bounds = BoundsOf(element);

        switch (element.Tag, element.DataContext)
        {
            case ("tile", TileViewModel target):
            {
                if (ReferenceEquals(target, dragged)) return null;
                if (dragged.IsFolder && target.ParentFolder != null)
                {
                    indicator = BoundsOf(FindTagged(element, "group")!);
                    return new InsertTile(target.Group, int.MaxValue);
                }

                double fx = (position.X - bounds.X) / bounds.Width;
                double fy = (position.Y - bounds.Y) / bounds.Height;
                if (dragged.IsApp && target.ParentFolder == null && fx is > 0.25 and < 0.75 && fy is > 0.2 and < 0.8)
                {
                    indicator = bounds;
                    return new MergeWith(target);
                }

                bool after = fx >= 0.5;
                asBar = true;
                indicator = new Rect(after ? bounds.Right - 1 : bounds.Left - 3, bounds.Top, 4, bounds.Height);
                if (target.ParentFolder is { } folder)
                    return new InsertInFolder(folder, folder.Children.IndexOf(target) + (after ? 1 : 0));
                return new InsertTile(target.Group, target.Group.Tiles.IndexOf(target) + (after ? 1 : 0));
            }
            case ("drawer", FolderDrawerViewModel drawer):
                indicator = bounds;
                return dragged.IsApp ? new InsertInFolder(drawer.Folder, int.MaxValue) : new InsertTile(drawer.Folder.Group, int.MaxValue);
            case ("groupheader" or "group", GroupViewModel group):
                indicator = BoundsOf(FindTagged(element, "group") ?? element);
                return new InsertTile(group, int.MaxValue);
            case ("column", ColumnViewModel column):
                indicator = NewGroupSlot(element);
                return new NewGroup(column.Index);
            case ("newcolumn", _):
                indicator = new Rect(bounds.X, bounds.Y + 44, TileGroupPanel.GroupWidth, 124);
                return new NewGroup(_vm.Columns.Count);
        }
        return null;
    }

    private DropTarget? FindGroupTarget(GroupViewModel dragged, DependencyObject hit, Point position, ref Rect indicator, ref bool asBar)
    {
        var element = FindTagged(hit, "group", "column", "newcolumn");
        if (element == null) return null;
        var bounds = BoundsOf(element);
        asBar = true;

        switch (element.Tag, element.DataContext)
        {
            case ("group", GroupViewModel target):
            {
                if (ReferenceEquals(target, dragged)) return null;
                bool before = position.Y < bounds.Top + bounds.Height / 2;
                indicator = new Rect(bounds.X, before ? bounds.Top - 2 : bounds.Bottom - 12, bounds.Width, 4);
                return new InsertGroup(target.Column.Index, target.Column.Groups.IndexOf(target) + (before ? 0 : 1));
            }
            case ("column", ColumnViewModel column):
            {
                var slot = NewGroupSlot(element);
                indicator = new Rect(slot.X, slot.Y, slot.Width, 4);
                return new InsertGroup(column.Index, int.MaxValue);
            }
            case ("newcolumn", _):
                indicator = new Rect(bounds.X, bounds.Y + 20, TileGroupPanel.GroupWidth, 4);
                return new InsertGroup(_vm.Columns.Count, 0);
        }
        return null;
    }

    /// <summary>Area below the last group of a column, where a new group would appear.</summary>
    private Rect NewGroupSlot(FrameworkElement column)
    {
        var bounds = BoundsOf(column);
        double used = (column as Border)?.Child is FrameworkElement content ? content.ActualHeight : 0;
        return new Rect(bounds.X, bounds.Y + used + 44, TileGroupPanel.GroupWidth, 124);
    }

    private void ShowIndicator(Rect? rect, bool asBar)
    {
        if (rect is not { IsEmpty: false } r)
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }
        var accent = (Brush)FindResource("AccentBrush");
        _indicator.Fill = asBar ? accent : (Brush)FindResource("DropTargetFillBrush");
        _indicator.Stroke = asBar ? null : accent;
        _indicator.StrokeThickness = asBar ? 0 : 2;
        _indicator.StrokeDashArray = asBar ? null : new DoubleCollection { 3, 2 };
        _indicator.RadiusX = _indicator.RadiusY = asBar ? 2 : 6;
        _indicator.Width = r.Width;
        _indicator.Height = r.Height;
        Canvas.SetLeft(_indicator, r.X);
        Canvas.SetTop(_indicator, r.Y);
        _indicator.Visibility = Visibility.Visible;
    }

    private void AutoScroll()
    {
        if (!_isDragging) return;
        const double edge = 60, step = 14;
        var p = RootGrid.TranslatePoint(_lastDragPoint, PinnedScroll);
        double dx = p.X > PinnedScroll.ActualWidth - edge ? step : p.X < edge ? -step : 0;
        double dy = p.Y > PinnedScroll.ActualHeight - edge ? step : p.Y < edge ? -step : 0;
        if (dx == 0 && dy == 0) return;
        PinnedScroll.ScrollToHorizontalOffset(PinnedScroll.HorizontalOffset + dx);
        PinnedScroll.ScrollToVerticalOffset(PinnedScroll.VerticalOffset + dy);
        UpdateDrag(_lastDragPoint);
    }

    private Rect BoundsOf(FrameworkElement element) =>
        element.TransformToAncestor(RootGrid).TransformBounds(new Rect(element.RenderSize));

    private static BitmapSource Snapshot(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        int w = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x66, 0x40, 0x40, 0x40)), null, new Rect(element.RenderSize), 6, 6);
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(element.RenderSize));
        }
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ---- renaming --------------------------------------------------------------------------

    private void StartRename(TileGroup model)
    {
        var group = _vm.Columns.SelectMany(c => c.Groups).FirstOrDefault(g => ReferenceEquals(g.Model, model));
        if (group != null) StartRename(group);
    }

    private void StartRename(GroupViewModel group)
    {
        group.BeginRename();
        Dispatcher.BeginInvoke(() =>
        {
            var box = FindDescendants<TextBox>(ColumnsHost).FirstOrDefault(b => b.Name == "RenameBox" && ReferenceEquals(b.DataContext, group));
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: GroupViewModel group }) return;
        group.CommitRename();
        Keyboard.Focus(this);
        e.Handled = true;
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: GroupViewModel group }) group.CommitRename();
    }

    private void FolderName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Keyboard.Focus(this); // commits the binding (UpdateSourceTrigger=LostFocus)
        e.Handled = true;
    }

    private void StartFolderRename(TileViewModel folder)
    {
        if (!folder.IsExpanded) _vm.ToggleFolder(folder);
        Dispatcher.BeginInvoke(() =>
        {
            var box = FindDescendants<TextBox>(ColumnsHost).FirstOrDefault(b => b.Name == "FolderName"
                && b.DataContext is FolderDrawerViewModel d && ReferenceEquals(d.Folder.Model, folder.Model));
            if (box == null) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    // ---- context menus -----------------------------------------------------------------------

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<TextBox>(source) != null) return;

        ContextMenu? menu = null;
        if (FindAncestor<ListBoxItem>(source) is { DataContext: AppEntry app })
        {
            menu = AppMenu(app);
        }
        else if (FindTagged(source, "tile", "groupheader") is { } element)
        {
            menu = element.DataContext switch
            {
                TileViewModel tile => TileMenu(tile),
                GroupViewModel group => GroupMenu(group),
                _ => null,
            };
        }
        if (menu == null) return;

        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu AppMenu(AppEntry app)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Öffnen", "", () => Launch(app)));
        if (_vm.IsPinned(app))
            menu.Items.Add(CreateItem("Von Start lösen", "", () => _vm.Edit(e => e.UnpinApp(app.Id))));
        else
            menu.Items.Add(CreateItem("An Start anheften", "", () => _vm.Edit(e => e.Pin(app.Id, app.Name))));
        AddLaunchOptions(menu, app);
        return menu;
    }

    private ContextMenu TileMenu(TileViewModel tile)
    {
        var menu = new ContextMenu();
        var model = tile.Model;

        if (tile.IsFolder)
        {
            menu.Items.Add(CreateItem(tile.IsExpanded ? "Ordner schließen" : "Ordner öffnen", "", () => _vm.ToggleFolder(tile)));
            menu.Items.Add(CreateItem("Ordner umbenennen", "", () => StartFolderRename(tile)));
            menu.Items.Add(CreateItem("Ordner auflösen", "", () => _vm.Edit(e => e.DissolveFolder(model))));
        }
        else if (tile.App is { } app)
        {
            menu.Items.Add(CreateItem("Öffnen", "", () => Launch(app)));
            AddLaunchOptions(menu, app);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Von Start lösen", "", () => _vm.Edit(e => e.Remove(model))));
        if (tile.ParentFolder != null)
            menu.Items.Add(CreateItem("Aus Ordner nehmen", "", () => _vm.Edit(e => e.TakeOutOfFolder(model))));
        else
            menu.Items.Add(CreateItem("In neue Gruppe verschieben", "", () =>
            {
                TileGroup? created = null;
                int column = tile.Group.Column.Index;
                _vm.Edit(e => created = e.MoveTileToNewGroup(model, column, tile.Group.Column.Groups.IndexOf(tile.Group) + 1));
                if (created != null) StartRename(created);
            }));

        var resize = new MenuItem { Header = "Größe ändern", Tag = "" };
        foreach (var (size, label) in new[] { (TileSize.Small, "Klein"), (TileSize.Medium, "Mittel"), (TileSize.Wide, "Breit"), (TileSize.Large, "Groß") })
        {
            var item = CreateItem(label, "", () => _vm.Edit(e => e.Resize(model, size)));
            item.IsChecked = model.Size == size;
            resize.Items.Add(item);
        }
        menu.Items.Add(resize);
        return menu;
    }

    private ContextMenu GroupMenu(GroupViewModel group)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Gruppe umbenennen", "", () => StartRename(group)));
        menu.Items.Add(CreateItem("Gruppe von Start lösen", "", () => _vm.Edit(e => e.DeleteGroup(group.Model))));
        return menu;
    }

    private void AddLaunchOptions(ContextMenu menu, AppEntry app)
    {
        if (AppLauncher.CanRunAsAdmin(app))
            menu.Items.Add(CreateItem("Als Administrator ausführen", "", () => Launch(app, asAdmin: true)));
        if (AppLauncher.CanOpenFileLocation(app))
            menu.Items.Add(CreateItem("Dateispeicherort öffnen", "", () => { AppLauncher.OpenFileLocation(app); HideMenu(); }));
    }

    private static MenuItem CreateItem(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header, Tag = glyph };
        item.Click += (_, _) => action();
        return item;
    }

    private static void OpenMenuBeside(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Right;
        menu.IsOpen = true;
    }

    internal static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }
}
