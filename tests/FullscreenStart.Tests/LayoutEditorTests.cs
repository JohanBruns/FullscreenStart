using FullscreenStart.Models;
using FullscreenStart.Services;

namespace FullscreenStart.Tests;

public class LayoutEditorTests
{
    private static (LayoutEditor Editor, TileGroup A, TileGroup B) TwoGroups()
    {
        var a = new TileGroup { Name = "A", Tiles = { Tile.ForApp("a1", "A1"), Tile.ForApp("a2", "A2"), Tile.ForApp("a3", "A3") } };
        var b = new TileGroup { Name = "B", Tiles = { Tile.ForApp("b1", "B1") } };
        var layout = new StartLayout { Columns = { new LayoutColumn { Groups = { a } }, new LayoutColumn { Groups = { b } } } };
        return (new LayoutEditor(layout), a, b);
    }

    private static string[] Ids(TileGroup group) => group.Tiles.Select(t => t.AppId ?? $"[{t.Name}]").ToArray();

    [Fact]
    public void PinAddsToLastGroupOnce()
    {
        var (editor, _, b) = TwoGroups();

        editor.Pin("new", "New");
        editor.Pin("new", "New");

        Assert.Equal(new[] { "b1", "new" }, Ids(b));
    }

    [Fact]
    public void PinIntoEmptyLayoutCreatesGroup()
    {
        var editor = new LayoutEditor(new StartLayout());

        editor.Pin("x", "X");

        Assert.Single(editor.Layout.Columns);
        Assert.True(editor.Layout.IsPinned("X"));
    }

    [Fact]
    public void MoveWithinGroupForward()
    {
        var (editor, a, _) = TwoGroups();

        editor.MoveTile(a.Tiles[0], a, 2); // insert before a3

        Assert.Equal(new[] { "a2", "a1", "a3" }, Ids(a));
    }

    [Fact]
    public void MoveToOtherGroupRemovesEmptySourceGroupAndColumn()
    {
        var (editor, a, b) = TwoGroups();

        editor.MoveTile(b.Tiles[0], a, 0);

        Assert.Equal(new[] { "b1", "a1", "a2", "a3" }, Ids(a));
        Assert.Single(editor.Layout.Columns);
    }

    [Fact]
    public void DroppingOnTileCreatesFolder()
    {
        var (editor, a, _) = TwoGroups();
        var target = a.Tiles[1];

        editor.MergeIntoFolder(a.Tiles[0], target);

        Assert.Equal(2, a.Tiles.Count);
        var folder = a.Tiles[0];
        Assert.Equal(TileKind.Folder, folder.Kind);
        Assert.Equal(new[] { "a2", "a1" }, folder.Items.Select(i => i.AppId));
    }

    [Fact]
    public void FolderWithOneItemLeftIsUnwrapped()
    {
        var (editor, a, b) = TwoGroups();
        editor.MergeIntoFolder(a.Tiles[0], a.Tiles[1]);
        var folder = a.Tiles[0];
        folder.Size = TileSize.Wide;

        editor.MoveTile(folder.Items[0], b, 1);

        Assert.Equal(TileKind.App, a.Tiles[0].Kind);
        Assert.Equal("a1", a.Tiles[0].AppId);
        Assert.Equal(TileSize.Wide, a.Tiles[0].Size); // keeps the folder's slot size
    }

    [Fact]
    public void FoldersCannotBeNested()
    {
        var (editor, a, _) = TwoGroups();
        editor.MergeIntoFolder(a.Tiles[0], a.Tiles[1]);
        var folder = a.Tiles[0];

        editor.MergeIntoFolder(folder, a.Tiles[1]);

        Assert.Equal(TileKind.Folder, a.Tiles[0].Kind);
        Assert.Equal("a3", a.Tiles[1].AppId);
    }

    [Fact]
    public void DissolveFolderRestoresTilesInPlace()
    {
        var (editor, a, _) = TwoGroups();
        editor.MergeIntoFolder(a.Tiles[0], a.Tiles[1]);

        editor.DissolveFolder(a.Tiles[0]);

        Assert.Equal(new[] { "a2", "a1", "a3" }, Ids(a));
    }

    [Fact]
    public void UnpinRemovesAppAlsoFromFolders()
    {
        var (editor, a, _) = TwoGroups();
        editor.MergeIntoFolder(a.Tiles[0], a.Tiles[1]);

        editor.UnpinApp("a1");

        Assert.False(editor.Layout.IsPinned("a1"));
        Assert.Equal(new[] { "a2", "a3" }, Ids(a));
    }

    [Fact]
    public void MoveTileToNewGroupInNewColumn()
    {
        var (editor, a, _) = TwoGroups();

        var group = editor.MoveTileToNewGroup(a.Tiles[2], columnIndex: 99, groupIndex: 0);

        Assert.Equal(3, editor.Layout.Columns.Count);
        Assert.Same(group, editor.Layout.Columns[2].Groups[0]);
        Assert.Equal("", group.Name);
    }

    [Fact]
    public void MoveGroupBetweenColumns()
    {
        var (editor, a, b) = TwoGroups();

        editor.MoveGroup(b, 0, 0);

        Assert.Single(editor.Layout.Columns);
        Assert.Equal(new[] { b, a }, editor.Layout.Columns[0].Groups);
    }

    [Fact]
    public void DeleteGroupUnpinsItsTiles()
    {
        var (editor, a, _) = TwoGroups();

        editor.DeleteGroup(a);

        Assert.False(editor.Layout.IsPinned("a1"));
        Assert.Single(editor.Layout.Columns);
    }

    [Fact]
    public void MoveIntoFolderAtIndex()
    {
        var (editor, a, b) = TwoGroups();
        editor.MergeIntoFolder(a.Tiles[0], a.Tiles[1]); // folder [a2, a1]
        var folder = a.Tiles[0];

        editor.MoveIntoFolder(b.Tiles[0], folder, 1);

        Assert.Equal(new[] { "a2", "b1", "a1" }, folder.Items.Select(i => i.AppId));
    }
}
