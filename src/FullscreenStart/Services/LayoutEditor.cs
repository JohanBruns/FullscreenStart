using FullscreenStart.Models;

namespace FullscreenStart.Services;

/// <summary>Editing operations on a <see cref="StartLayout"/>. Every operation leaves the layout normalized.</summary>
public sealed class LayoutEditor(StartLayout layout)
{
    public StartLayout Layout { get; } = layout;

    /// <summary>Pins an app to the end of the last group (creating one if needed).</summary>
    public Tile Pin(string appId, string name)
    {
        var existing = Layout.AllAppTiles.FirstOrDefault(t => string.Equals(t.AppId, appId, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var tile = Tile.ForApp(appId, name);
        var group = Layout.Columns.LastOrDefault()?.Groups.LastOrDefault();
        if (group == null)
        {
            group = new TileGroup();
            Layout.Columns.Add(new LayoutColumn { Groups = { group } });
        }
        group.Tiles.Add(tile);
        return tile;
    }

    /// <summary>Removes every tile of the given app (also from folders).</summary>
    public void UnpinApp(string appId)
    {
        foreach (var tile in Layout.AllAppTiles.Where(t => string.Equals(t.AppId, appId, StringComparison.OrdinalIgnoreCase)).ToList())
            Detach(tile);
        Layout.Normalize();
    }

    public void Remove(Tile tile)
    {
        Detach(tile);
        Layout.Normalize();
    }

    public void Resize(Tile tile, TileSize size)
    {
        tile.Size = size;
    }

    /// <summary>Moves a tile into a group at the given index (index is clamped).</summary>
    public void MoveTile(Tile tile, TileGroup target, int index)
    {
        if (!Contains(target)) return;
        var (sourceGroup, _) = Locate(tile);
        int oldIndex = sourceGroup?.Tiles.IndexOf(tile) ?? -1;

        Detach(tile);
        if (sourceGroup == target && oldIndex >= 0 && oldIndex < index) index--;

        target.Tiles.Insert(Math.Clamp(index, 0, target.Tiles.Count), tile);
        Layout.Normalize();
    }

    /// <summary>Drops <paramref name="dragged"/> onto <paramref name="target"/>: adds it to a folder or creates one.</summary>
    public void MergeIntoFolder(Tile dragged, Tile target)
    {
        if (ReferenceEquals(dragged, target) || dragged.Kind == TileKind.Folder) return;

        if (target.Kind == TileKind.Folder)
        {
            Detach(dragged);
            target.Items.Add(dragged);
            Layout.Normalize();
            return;
        }

        var (group, folder) = Locate(target);
        if (group == null) return;

        Detach(dragged);
        if (folder != null)
        {
            // Target sits inside a folder already: just add next to it.
            folder.Items.Insert(folder.Items.IndexOf(target) + 1, dragged);
        }
        else
        {
            int index = group.Tiles.IndexOf(target);
            var newFolder = Tile.ForFolder("", new[] { target, dragged }, target.Size == TileSize.Small ? TileSize.Small : TileSize.Medium);
            group.Tiles[index] = newFolder;
        }
        Layout.Normalize();
    }

    /// <summary>Moves an app tile into a folder at the given index.</summary>
    public void MoveIntoFolder(Tile tile, Tile folder, int index)
    {
        if (tile.Kind == TileKind.Folder || folder.Kind != TileKind.Folder) return;
        var (_, sourceFolder) = Locate(tile);
        int oldIndex = ReferenceEquals(sourceFolder, folder) ? folder.Items.IndexOf(tile) : -1;

        Detach(tile);
        if (oldIndex >= 0 && oldIndex < index) index--;
        folder.Items.Insert(Math.Clamp(index, 0, folder.Items.Count), tile);
        Layout.Normalize();
    }

    /// <summary>
    /// Moves a tile into a new, unnamed group placed in the given column (a new column if out of range),
    /// optionally at a free position (<see cref="TileGroup.Top"/>).
    /// </summary>
    public TileGroup MoveTileToNewGroup(Tile tile, int columnIndex, int groupIndex, int top = 0)
    {
        Detach(tile);
        var group = new TileGroup { Tiles = { tile }, Top = Math.Max(0, top) };
        InsertGroup(group, columnIndex, groupIndex);
        Layout.Normalize();
        return group;
    }

    /// <summary>Takes a tile out of its folder and places it right after the folder.</summary>
    public void TakeOutOfFolder(Tile tile)
    {
        var (group, folder) = Locate(tile);
        if (group == null || folder == null) return;
        folder.Items.Remove(tile);
        group.Tiles.Insert(group.Tiles.IndexOf(folder) + 1, tile);
        Layout.Normalize();
    }

    /// <summary>Replaces a folder by its contents.</summary>
    public void DissolveFolder(Tile folder)
    {
        var (group, _) = Locate(folder);
        if (group == null || folder.Kind != TileKind.Folder) return;
        int index = group.Tiles.IndexOf(folder);
        group.Tiles.RemoveAt(index);
        group.Tiles.InsertRange(index, folder.Items);
        Layout.Normalize();
    }

    public TileGroup AddGroup(int columnIndex, string name = "")
    {
        var group = new TileGroup { Name = name };
        InsertGroup(group, columnIndex, int.MaxValue);
        return group;
    }

    public void RenameGroup(TileGroup group, string name) => group.Name = name.Trim();

    /// <summary>Deletes a group and unpins its tiles.</summary>
    public void DeleteGroup(TileGroup group)
    {
        foreach (var column in Layout.Columns) column.Groups.Remove(group);
        Layout.Normalize();
    }

    /// <summary>Moves a group to an index in a column and gives it a new position (<see cref="TileGroup.Top"/>).</summary>
    public void MoveGroup(TileGroup group, int columnIndex, int groupIndex, int top = 0)
    {
        var source = Layout.Columns.FirstOrDefault(c => c.Groups.Contains(group));
        if (source == null) return;
        int sourceColumn = Layout.Columns.IndexOf(source);
        int oldIndex = source.Groups.IndexOf(group);
        if (sourceColumn == columnIndex && oldIndex < groupIndex) groupIndex--;

        // Keep the source column alive while inserting so that column indexes stay valid.
        source.Groups.Remove(group);
        group.Top = Math.Max(0, top);
        InsertGroup(group, columnIndex, groupIndex);
        Layout.Normalize();
    }

    /// <summary>Lets a group close up to the group above it again.</summary>
    public void RemoveSpaceAbove(TileGroup group) => group.Top = 0;

    /// <summary>Finds the group holding a tile and, if it sits in a folder, that folder.</summary>
    public (TileGroup? Group, Tile? Folder) Locate(Tile tile)
    {
        foreach (var group in Layout.AllGroups)
        {
            foreach (var t in group.Tiles)
            {
                if (ReferenceEquals(t, tile)) return (group, null);
                if (t.Kind == TileKind.Folder && t.Items.Any(i => ReferenceEquals(i, tile))) return (group, t);
            }
        }
        return (null, null);
    }

    private void InsertGroup(TileGroup group, int columnIndex, int groupIndex)
    {
        if (columnIndex < 0) columnIndex = 0;
        if (columnIndex >= Layout.Columns.Count)
        {
            Layout.Columns.Add(new LayoutColumn());
            columnIndex = Layout.Columns.Count - 1;
        }
        var groups = Layout.Columns[columnIndex].Groups;
        groups.Insert(Math.Clamp(groupIndex, 0, groups.Count), group);
    }

    private bool Contains(TileGroup group) => Layout.AllGroups.Any(g => ReferenceEquals(g, group));

    private void Detach(Tile tile)
    {
        var (group, folder) = Locate(tile);
        if (folder != null) folder.Items.Remove(tile);
        else group?.Tiles.Remove(tile);
    }
}
