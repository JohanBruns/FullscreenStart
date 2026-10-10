using System.Collections.ObjectModel;
using FullscreenStart.Models;

namespace FullscreenStart.ViewModels;

public sealed class TileViewModel : ObservableObject
{
    private bool _isExpanded;

    public TileViewModel(Tile model, AppEntry? app, GroupViewModel group, TileViewModel? parentFolder)
    {
        Model = model;
        App = app;
        Group = group;
        ParentFolder = parentFolder;
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
    public bool ShowLabel => Size != TileSize.Small;

    public double IconSize => Size switch
    {
        TileSize.Small => 24,
        TileSize.Large => 72,
        _ => 40,
    };

    public IEnumerable<TileViewModel> PreviewItems => Children.Take(Size == TileSize.Small ? 4 : Size == TileSize.Medium ? 9 : 12);
    public int PreviewColumns => Size switch { TileSize.Small => 2, TileSize.Medium => 3, TileSize.Wide => 6, _ => 4 };
    public double PreviewIconSize => Size switch { TileSize.Small => 11, TileSize.Large => 32, _ => 20 };

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
