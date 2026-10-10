using FullscreenStart.Layout;
using FullscreenStart.Models;

namespace FullscreenStart.Tests;

public class TileLayoutEngineTests
{
    [Fact]
    public void ThreeMediumTilesFillOneRow()
    {
        var rects = TileLayoutEngine.Pack(new[] { TileSize.Medium, TileSize.Medium, TileSize.Medium });

        Assert.Equal(new CellRect(0, 0, 2, 2), rects[0]);
        Assert.Equal(new CellRect(2, 0, 2, 2), rects[1]);
        Assert.Equal(new CellRect(4, 0, 2, 2), rects[2]);
    }

    [Fact]
    public void SmallTilesFillA2x2BlockBeforeTheNextBlock()
    {
        var rects = TileLayoutEngine.Pack(Enumerable.Repeat(TileSize.Small, 5).ToList());

        Assert.Equal(new CellRect(0, 0, 1, 1), rects[0]);
        Assert.Equal(new CellRect(1, 0, 1, 1), rects[1]);
        Assert.Equal(new CellRect(0, 1, 1, 1), rects[2]);
        Assert.Equal(new CellRect(1, 1, 1, 1), rects[3]);
        Assert.Equal(new CellRect(2, 0, 1, 1), rects[4]);
    }

    [Fact]
    public void WideTileWrapsToNextRowWhenItDoesNotFit()
    {
        // Like "Entertainment & Communication": three mediums, then a wide WhatsApp and Discord.
        var rects = TileLayoutEngine.Pack(new[] { TileSize.Medium, TileSize.Medium, TileSize.Medium, TileSize.Wide, TileSize.Medium });

        Assert.Equal(new CellRect(0, 2, 4, 2), rects[3]);
        Assert.Equal(new CellRect(4, 2, 2, 2), rects[4]);
    }

    [Fact]
    public void LaterTilesFillEarlierGaps()
    {
        var rects = TileLayoutEngine.Pack(new[] { TileSize.Large, TileSize.Medium, TileSize.Medium });

        Assert.Equal(new CellRect(0, 0, 4, 4), rects[0]);
        Assert.Equal(new CellRect(4, 0, 2, 2), rects[1]);
        Assert.Equal(new CellRect(4, 2, 2, 2), rects[2]);
    }

    [Fact]
    public void OpenFolderPushesTilesBelowItDown()
    {
        var sizes = new[] { TileSize.Medium, TileSize.Medium, TileSize.Medium, TileSize.Medium };

        var arrangement = TileLayoutEngine.Arrange(sizes, expandedIndex: 0, drawerRows: 3);

        Assert.Equal(new CellRect(0, 2, 6, 3), arrangement.Drawer);
        Assert.Equal(0, arrangement.Tiles[1].Row);   // same row as the folder: stays
        Assert.Equal(5, arrangement.Tiles[3].Row);   // next row: pushed below the drawer
        Assert.Equal(7, arrangement.TotalRows);
    }

    [Fact]
    public void DrawerStartsBelowTallNeighbours()
    {
        // Folder (medium) next to a large tile: the drawer must not cut through the large tile.
        var sizes = new[] { TileSize.Medium, TileSize.Large };

        var arrangement = TileLayoutEngine.Arrange(sizes, expandedIndex: 0, drawerRows: 2);

        Assert.Equal(4, arrangement.Drawer!.Value.Row);
    }

    [Fact]
    public void EmptyGroupHasNoRows()
    {
        Assert.Equal(0, TileLayoutEngine.Arrange(Array.Empty<TileSize>()).TotalRows);
    }
}
