using FullscreenStart.Models;

namespace FullscreenStart.Layout;

/// <summary>Position of a tile in grid cells.</summary>
public readonly record struct CellRect(int Col, int Row, int Width, int Height)
{
    public int Right => Col + Width;
    public int Bottom => Row + Height;

    public bool Contains(int col, int row) => col >= Col && col < Right && row >= Row && row < Bottom;
}

/// <summary>Result of arranging a group: tile cells plus the optional open-folder drawer.</summary>
public sealed record GroupArrangement(IReadOnlyList<CellRect> Tiles, CellRect? Drawer, int TotalRows);

/// <summary>
/// Packs tiles the way the Windows 10 start screen does: a 6-cell wide grid, larger tiles snap to
/// 2x2 blocks, and small tiles fill a 2x2 block before moving on to the next one.
/// </summary>
public static class TileLayoutEngine
{
    public const int GroupColumns = 6;

    public static IReadOnlyList<CellRect> Pack(IReadOnlyList<TileSize> sizes, int columns = GroupColumns)
    {
        var grid = new Occupancy(columns);
        var result = new List<CellRect>(sizes.Count);
        foreach (var size in sizes)
        {
            var (w, h) = size.Cells();
            w = Math.Min(w, columns);
            var rect = size == TileSize.Small ? PlaceSmall(grid, columns) : PlaceBlock(grid, columns, w, h);
            grid.Mark(rect);
            result.Add(rect);
        }
        return result;
    }

    /// <summary>
    /// Packs the group and, if a folder is open, inserts a full-width drawer of
    /// <paramref name="drawerRows"/> rows below it, pushing the tiles underneath down.
    /// </summary>
    public static GroupArrangement Arrange(IReadOnlyList<TileSize> sizes, int expandedIndex = -1, int drawerRows = 0, int columns = GroupColumns)
    {
        var tiles = Pack(sizes, columns).ToList();
        CellRect? drawer = null;

        if (expandedIndex >= 0 && expandedIndex < tiles.Count && drawerRows > 0)
        {
            // The drawer starts below every tile that begins above it, so nothing overlaps it.
            int drawerRow = tiles[expandedIndex].Bottom;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var t in tiles)
                {
                    if (t.Row < drawerRow && t.Bottom > drawerRow)
                    {
                        drawerRow = t.Bottom;
                        changed = true;
                    }
                }
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i].Row >= drawerRow)
                    tiles[i] = tiles[i] with { Row = tiles[i].Row + drawerRows };
            }
            drawer = new CellRect(0, drawerRow, columns, drawerRows);
        }

        int total = tiles.Count == 0 ? 0 : tiles.Max(t => t.Bottom);
        if (drawer is { } d) total = Math.Max(total, d.Bottom);
        return new GroupArrangement(tiles, drawer, total);
    }

    /// <summary>Rows needed to show the given tiles packed in a grid.</summary>
    public static int RowsFor(IReadOnlyList<TileSize> sizes, int columns = GroupColumns)
    {
        var packed = Pack(sizes, columns);
        return packed.Count == 0 ? 0 : packed.Max(r => r.Bottom);
    }

    private static CellRect PlaceSmall(Occupancy grid, int columns)
    {
        int blockCols = Math.Max(1, columns / 2);
        for (int blockRow = 0; ; blockRow++)
        {
            for (int blockCol = 0; blockCol < blockCols; blockCol++)
            {
                for (int sub = 0; sub < 4; sub++)
                {
                    int col = blockCol * 2 + (sub % 2);
                    int row = blockRow * 2 + (sub / 2);
                    if (col < columns && grid.IsFree(col, row))
                        return new CellRect(col, row, 1, 1);
                }
            }
        }
    }

    private static CellRect PlaceBlock(Occupancy grid, int columns, int w, int h)
    {
        for (int row = 0; ; row += 2)
        {
            for (int col = 0; col + w <= columns; col += 2)
            {
                var rect = new CellRect(col, row, w, h);
                if (grid.Fits(rect)) return rect;
            }
        }
    }

    private sealed class Occupancy(int columns)
    {
        private readonly List<bool[]> _rows = new();

        public bool IsFree(int col, int row) => row >= _rows.Count || !_rows[row][col];

        public bool Fits(CellRect r)
        {
            for (int y = r.Row; y < r.Bottom; y++)
                for (int x = r.Col; x < r.Right; x++)
                    if (!IsFree(x, y)) return false;
            return true;
        }

        public void Mark(CellRect r)
        {
            while (_rows.Count < r.Bottom) _rows.Add(new bool[columns]);
            for (int y = r.Row; y < r.Bottom; y++)
                for (int x = r.Col; x < r.Right; x++)
                    _rows[y][x] = true;
        }
    }
}
