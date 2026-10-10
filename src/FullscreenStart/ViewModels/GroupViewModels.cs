using System.Collections.ObjectModel;
using FullscreenStart.Models;

namespace FullscreenStart.ViewModels;

public sealed class ColumnViewModel(LayoutColumn model, int index)
{
    public LayoutColumn Model { get; } = model;
    public int Index { get; } = index;
    public ObservableCollection<GroupViewModel> Groups { get; } = new();
}

public sealed class GroupViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _isRenaming;
    private string _editName = "";

    public GroupViewModel(TileGroup model, ColumnViewModel column, MainViewModel owner)
    {
        Model = model;
        Column = column;
        _owner = owner;
    }

    public TileGroup Model { get; }
    public ColumnViewModel Column { get; }

    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    /// <summary>Tiles plus, when a folder is open, its drawer (what the tile panel shows).</summary>
    public ObservableCollection<object> Items { get; } = new();

    public string Name => Model.Name;
    public bool HasName => !string.IsNullOrWhiteSpace(Model.Name);

    public bool IsRenaming
    {
        get => _isRenaming;
        set => Set(ref _isRenaming, value);
    }

    /// <summary>Text in the rename box while renaming.</summary>
    public string EditName
    {
        get => _editName;
        set => Set(ref _editName, value);
    }

    public TileViewModel? ExpandedFolder => Tiles.FirstOrDefault(t => t.IsExpanded);

    public void BeginRename()
    {
        EditName = Model.Name;
        IsRenaming = true;
    }

    public void CommitRename()
    {
        if (!IsRenaming) return;
        IsRenaming = false;
        if (EditName.Trim() != Model.Name) _owner.Edit(e => e.RenameGroup(Model, EditName));
    }

    public void CancelRename() => IsRenaming = false;

    public void SetExpanded(TileViewModel? folder)
    {
        foreach (var tile in Tiles) tile.IsExpanded = ReferenceEquals(tile, folder);
        RebuildItems();
    }

    internal void RebuildItems()
    {
        Items.Clear();
        foreach (var tile in Tiles) Items.Add(tile);
        if (ExpandedFolder is { } open)
        {
            Items.Add(new FolderDrawerViewModel(open, name =>
            {
                _owner.Edit(_ => open.Model.Name = name.Trim(), rebuild: false);
                open.RefreshName();
            }));
        }
    }
}
