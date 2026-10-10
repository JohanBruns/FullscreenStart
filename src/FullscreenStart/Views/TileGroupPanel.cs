using System.Windows;
using System.Windows.Controls;
using FullscreenStart.Layout;
using FullscreenStart.Models;
using FullscreenStart.ViewModels;

namespace FullscreenStart.Views;

/// <summary>
/// Lays out the tiles of one group (or one open folder) on the Windows 10 grid:
/// 60 px cells with 4 px gaps, 6 cells wide. A folder drawer item spans the full width
/// below its folder and pushes the tiles underneath down.
/// </summary>
public sealed class TileGroupPanel : Panel
{
    public const double Cell = 60;
    public const double Gap = 4;
    public const double Unit = Cell + Gap;
    public static readonly double GroupWidth = TileLayoutEngine.GroupColumns * Unit - Gap;

    private Rect[] _rects = Array.Empty<Rect>();

    public static Size TileSizeOf(TileSize size)
    {
        var (w, h) = size.Cells();
        return new Size(w * Unit - Gap, h * Unit - Gap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tiles = new List<(UIElement Element, TileViewModel Tile)>();
        UIElement? drawer = null;
        int expandedIndex = -1;

        foreach (UIElement child in InternalChildren)
        {
            switch ((child as FrameworkElement)?.DataContext)
            {
                case TileViewModel tile:
                    if (tile.IsExpanded) expandedIndex = tiles.Count;
                    tiles.Add((child, tile));
                    break;
                case FolderDrawerViewModel:
                    drawer = child;
                    break;
            }
        }

        double drawerHeight = 0;
        if (drawer != null)
        {
            drawer.Measure(new Size(GroupWidth, double.PositiveInfinity));
            drawerHeight = drawer.DesiredSize.Height;
        }

        // The drawer occupies one placeholder row in the engine; its real height is applied below.
        bool hasDrawer = drawer != null && expandedIndex >= 0;
        var arrangement = TileLayoutEngine.Arrange(tiles.Select(t => t.Tile.Size).ToList(), hasDrawer ? expandedIndex : -1, hasDrawer ? 1 : 0);

        double RowToY(int row)
        {
            if (arrangement.Drawer is not { } d || row <= d.Row) return row * Unit;
            return d.Row * Unit + drawerHeight + Gap + (row - d.Row - 1) * Unit;
        }

        var rects = new List<Rect>();
        foreach (UIElement child in InternalChildren)
        {
            var ctx = (child as FrameworkElement)?.DataContext;
            int tileIndex = ctx is TileViewModel ? tiles.FindIndex(t => ReferenceEquals(t.Element, child)) : -1;
            Rect rect;
            if (tileIndex >= 0)
            {
                var cell = arrangement.Tiles[tileIndex];
                rect = new Rect(cell.Col * Unit, RowToY(cell.Row), cell.Width * Unit - Gap, cell.Height * Unit - Gap);
                child.Measure(rect.Size);
            }
            else if (ReferenceEquals(child, drawer) && arrangement.Drawer is { } d)
            {
                rect = new Rect(0, RowToY(d.Row), GroupWidth, drawerHeight);
            }
            else
            {
                rect = Rect.Empty;
                child.Measure(new Size(0, 0));
            }
            rects.Add(rect);
        }
        _rects = rects.ToArray();

        double height = arrangement.TotalRows == 0 ? 0 : RowToY(arrangement.TotalRows) - Gap;
        if (arrangement.Drawer is { } dr && dr.Bottom == arrangement.TotalRows)
            height = RowToY(dr.Row) + drawerHeight;
        return new Size(GroupWidth, Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var rect = i < _rects.Length ? _rects[i] : Rect.Empty;
            InternalChildren[i].Arrange(rect.IsEmpty ? new Rect(0, 0, 0, 0) : rect);
        }
        return finalSize;
    }
}
