using System.Text.Json.Serialization;

namespace FullscreenStart.Models;

/// <summary>Tile footprint in grid cells (one cell = a small tile).</summary>
public enum TileSize
{
    Small,
    Medium,
    Wide,
    Large,
}

public enum TileKind
{
    App,
    Folder,
}

/// <summary>Persisted pinned layout: columns of groups of tiles, like the Windows 10 start screen.</summary>
public sealed class StartLayout
{
    public int Version { get; set; } = 1;
    public List<LayoutColumn> Columns { get; set; } = new();

    public IEnumerable<TileGroup> AllGroups => Columns.SelectMany(c => c.Groups);

    /// <summary>All app tiles, including those inside folders.</summary>
    public IEnumerable<Tile> AllAppTiles =>
        AllGroups.SelectMany(g => g.Tiles).SelectMany(t => t.Kind == TileKind.Folder ? t.Items : new List<Tile> { t });

    public bool IsPinned(string appId) => AllAppTiles.Any(t => string.Equals(t.AppId, appId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drops empty groups and columns and unwraps folders with a single item.</summary>
    public void Normalize()
    {
        foreach (var group in AllGroups)
        {
            for (int i = 0; i < group.Tiles.Count; i++)
            {
                var tile = group.Tiles[i];
                if (tile.Kind != TileKind.Folder) continue;
                if (tile.Items.Count == 0) group.Tiles.RemoveAt(i--);
                else if (tile.Items.Count == 1)
                {
                    // The remaining app takes over the folder's slot and size.
                    var only = tile.Items[0];
                    only.Size = tile.Size;
                    group.Tiles[i] = only;
                }
            }
        }
        foreach (var column in Columns)
            column.Groups.RemoveAll(g => g.Tiles.Count == 0);
        Columns.RemoveAll(c => c.Groups.Count == 0);
    }
}

public sealed class LayoutColumn
{
    public List<TileGroup> Groups { get; set; } = new();
}

public sealed class TileGroup
{
    public string Name { get; set; } = "";
    public List<Tile> Tiles { get; set; } = new();
}

public sealed class Tile
{
    public TileKind Kind { get; set; } = TileKind.App;

    /// <summary>shell:AppsFolder parsing name (AUMID or path); only for app tiles.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    /// <summary>Display name: app name as fallback when the app is missing, or the folder's name.</summary>
    public string Name { get; set; } = "";

    public TileSize Size { get; set; } = TileSize.Medium;

    /// <summary>Apps inside a folder tile.</summary>
    [JsonIgnore]
    public List<Tile> Items { get; set; } = new();

    /// <summary>Serialized form of <see cref="Items"/>, written for folders only.</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Tile>? SerializedItems
    {
        get => Kind == TileKind.Folder ? Items : null;
        set => Items = value ?? new();
    }

    public static Tile ForApp(string appId, string name, TileSize size = TileSize.Medium) =>
        new() { Kind = TileKind.App, AppId = appId, Name = name, Size = size };

    public static Tile ForFolder(string name, IEnumerable<Tile> items, TileSize size = TileSize.Medium) =>
        new() { Kind = TileKind.Folder, Name = name, Size = size, Items = items.ToList() };
}

public static class TileSizeExtensions
{
    /// <summary>Footprint in grid cells.</summary>
    public static (int Width, int Height) Cells(this TileSize size) => size switch
    {
        TileSize.Small => (1, 1),
        TileSize.Medium => (2, 2),
        TileSize.Wide => (4, 2),
        TileSize.Large => (4, 4),
        _ => (2, 2),
    };
}
