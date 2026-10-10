using System.Collections.ObjectModel;
using FullscreenStart.Models;

namespace FullscreenStart.ViewModels;

public sealed class TileViewModel : ObservableObject
{
    private bool _isExpanded;

    private readonly bool _showAppLabel;

    public TileViewModel(Tile model, AppEntry? app, GroupViewModel group, TileViewModel? parentFolder, bool showAppLabel)
    {
        Model = model;
        App = app;
        Group = group;
        ParentFolder = parentFolder;
        _showAppLabel = showAppLabel;
    }

    public Tile Model { get; }
    public AppEntry? App { get; }
    public GroupViewModel Group { get; }

    /// <summary>The folder this tile sits in, if any.</summary>
    public TileViewModel? ParentFolder { get; }

    public ObservableCollection<TileViewModel> Children { get; } = new();

    public bool IsFolder => Model.Kind == TileKind.Folder;
    public bool IsApp => !IsFolder;
    public bool IsMissing => IsApp && App == null;
    public string Name => IsFolder ? Model.Name : App?.Name ?? Model.Name;
    public TileSize Size => Model.Size;
    /// <summary>
    /// Folders always show their name (their content is several icons). App tiles show it only when
    /// labels are switched on; otherwise the icon fills the tile and the name is the tooltip.
    /// </summary>
    public bool ShowLabel => Size != TileSize.Small && (IsFolder || _showAppLabel);

    public double IconSize => (ShowLabel, Size) switch
    {
        (true, TileSize.Large) => 72,
        (true, _) => 40,
        (false, TileSize.Small) => 44,
        (false, TileSize.Large) => 188,
        _ => 92,
    };

    public IEnumerable<TileViewModel> PreviewItems => Children.Take(Size switch { TileSize.Small => 4, TileSize.Medium => 9, TileSize.Wide => 12, _ => 12 });
    public int PreviewColumns => Size switch { TileSize.Small => 2, TileSize.Medium => 3, TileSize.Wide => 6, _ => 4 };
    public double PreviewIconSize => Size switch { TileSize.Small => 20, TileSize.Medium => 24, TileSize.Wide => 28, _ => 44 };

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    public override string ToString() => Name;
}

/// <summary>The expanded area under an open folder tile.</summary>
public sealed class FolderDrawerViewModel(TileViewModel folder, Action<string> rename)
{
    public TileViewModel Folder { get; } = folder;
    public ObservableCollection<TileViewModel> Children => Folder.Children;

    public string Name
    {
        get => Folder.Model.Name;
        set => rename(value);
    }
}
