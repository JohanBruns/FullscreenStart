using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullscreenStart.Services;
using FullscreenStart.ViewModels;

namespace FullscreenStart.Views;

/// <summary>Developer aid: renders the menu views to PNG files without showing the window.</summary>
public partial class StartWindow
{
    public async Task RenderSnapshotsAsync(string directory, string? searchQuery)
    {
        Directory.CreateDirectory(directory);
        var bounds = ScreenInfo.MenuBounds();
        var size = new Size(bounds.Width, bounds.Height);

        // Give the backdrop capture and the icon loader a moment.
        RefreshBackdrop();
        await Task.Delay(2500);

        _vm.Reset();
        _vm.View = MenuView.Pinned;
        await RenderAsync(size, Path.Combine(directory, "pinned.png"));

        if (_vm.Columns.SelectMany(c => c.Groups).SelectMany(g => g.Tiles).FirstOrDefault(t => t.IsFolder) is { } folder)
        {
            _vm.ToggleFolder(folder);
            await RenderAsync(size, Path.Combine(directory, "folder-open.png"));
            _vm.CollapseFolders();
        }

        _vm.View = MenuView.AllApps;
        await RenderAsync(size, Path.Combine(directory, "allapps.png"));

        _vm.IsSidebarExpanded = true;
        await Task.Delay(300);
        await RenderAsync(size, Path.Combine(directory, "sidebar.png"));
        _vm.IsSidebarExpanded = false;

        _vm.View = MenuView.Pinned;
        _vm.SearchText = searchQuery ?? "co";
        await RenderAsync(size, Path.Combine(directory, "search.png"));
        _vm.Reset();

        // The other palette, regardless of the Windows setting. Render once before opening the folder:
        // in this never-shown window, switching the theme and opening a folder in the same layout pass
        // leaves the folder with a stale height (the live, shown window is not affected).
        bool light = ThemeColors.UseLightTheme(_vm.Settings.Theme);
        ThemeColors.Apply(Application.Current.Resources, !light);
        string suffix = light ? "dark" : "light";
        await RenderAsync(size, Path.Combine(directory, $"pinned-{suffix}.png"));
        if (_vm.Columns.SelectMany(c => c.Groups).SelectMany(g => g.Tiles).FirstOrDefault(t => t.IsFolder) is { } folderTile)
            _vm.ToggleFolder(folderTile);
        await RenderAsync(size, Path.Combine(directory, $"folder-open-{suffix}.png"));
        _vm.CollapseFolders();
        _vm.View = MenuView.AllApps;
        await RenderAsync(size, Path.Combine(directory, $"allapps-{suffix}.png"));
        _vm.Reset();
        ThemeColors.Apply(Application.Current.Resources, light);
    }

    private async Task RenderAsync(Size size, string file)
    {
        RootGrid.Measure(size);
        RootGrid.Arrange(new Rect(size));
        RootGrid.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        RootGrid.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            // The system acrylic is not part of the WPF scene; paint its fallback colour instead.
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), null, new Rect(size));
        }
        bitmap.Render(background);
        bitmap.Render(RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
