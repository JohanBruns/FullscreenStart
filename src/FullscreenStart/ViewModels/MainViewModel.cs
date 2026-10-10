using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using FullscreenStart.Models;
using FullscreenStart.Services;

namespace FullscreenStart.ViewModels;

public enum MenuView
{
    Pinned,
    AllApps,
}

public sealed record LetterItem(string Letter, bool HasApps);

public sealed class MainViewModel : ObservableObject
{
    private readonly AppCatalog _catalog;
    private readonly JsonStore _store;
    private MenuView _view;
    private string _searchText = "";
    private AppEntry? _selectedResult;
    private bool _isSidebarExpanded;
    private ListCollectionView? _allAppsView;

    public MainViewModel(AppCatalog catalog, JsonStore store, AppSettings settings, StartLayout layout)
    {
        _catalog = catalog;
        _store = store;
        Settings = settings;
        Editor = new LayoutEditor(layout);
        _catalog.Changed += (_, _) => SyncWithCatalog();
        SyncWithCatalog();
    }

    public AppSettings Settings { get; }
    public LayoutEditor Editor { get; }
    public AppCatalog Catalog => _catalog;

    public ObservableCollection<ColumnViewModel> Columns { get; } = new();
    public ObservableCollection<AppEntry> SearchResults { get; } = new();
    public ObservableCollection<LetterItem> Letters { get; } = new();

    public ListCollectionView? AllAppsView
    {
        get => _allAppsView;
        private set => Set(ref _allAppsView, value);
    }

    public MenuView View
    {
        get => _view;
        set
        {
            if (Set(ref _view, value))
            {
                OnPropertyChanged(nameof(IsPinnedView));
                OnPropertyChanged(nameof(IsAllAppsView));
                NotifyContentChanged();
            }
        }
    }

    public bool IsPinnedView => View == MenuView.Pinned;
    public bool IsAllAppsView => View == MenuView.AllApps;

    // What the content area shows: search results win over the selected view.
    public bool ShowPinned => IsPinnedView && !IsSearching;
    public bool ShowAllApps => IsAllAppsView && !IsSearching;
    public bool ShowSearch => IsSearching;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            OnPropertyChanged(nameof(IsSearching));
            NotifyContentChanged();
            UpdateSearch();
        }
    }

    public bool IsSearching => SearchText.Length > 0;

    public AppEntry? SelectedResult
    {
        get => _selectedResult;
        set => Set(ref _selectedResult, value);
    }

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set => Set(ref _isSidebarExpanded, value);
    }

    private void NotifyContentChanged()
    {
        OnPropertyChanged(nameof(ShowPinned));
        OnPropertyChanged(nameof(ShowAllApps));
        OnPropertyChanged(nameof(ShowSearch));
    }

    public bool IsPinned(AppEntry app) => Editor.Layout.IsPinned(app.Id);

    /// <summary>Applies a layout change, saves it and (by default) rebuilds the tile view.</summary>
    public void Edit(Action<LayoutEditor> change, bool rebuild = true)
    {
        change(Editor);
        _store.SaveLayout(Editor.Layout);
        if (rebuild) RebuildLayout();
    }

    /// <summary>Opens <paramref name="folder"/> (closing any other folder), or closes it if already open.</summary>
    public void ToggleFolder(TileViewModel folder)
    {
        bool open = !folder.IsExpanded;
        foreach (var group in Columns.SelectMany(c => c.Groups))
            group.SetExpanded(open && ReferenceEquals(group, folder.Group) ? folder : null);
    }

    public void CollapseFolders()
    {
        foreach (var group in Columns.SelectMany(c => c.Groups))
            if (group.ExpandedFolder != null) group.SetExpanded(null);
    }

    /// <summary>Puts the menu back into its resting state (used when it is hidden).</summary>
    public void Reset()
    {
        SearchText = "";
        IsSidebarExpanded = false;
        CollapseFolders();
        foreach (var group in Columns.SelectMany(c => c.Groups)) group.CancelRename();
        View = Settings.StartInAllApps ? MenuView.AllApps : MenuView.Pinned;
    }

    public void RebuildLayout()
    {
        var expanded = Columns.SelectMany(c => c.Groups).Select(g => g.ExpandedFolder?.Model).FirstOrDefault(m => m != null);

        Columns.Clear();
        int index = 0;
        foreach (var column in Editor.Layout.Columns)
        {
            var columnVm = new ColumnViewModel(column, index++);
            foreach (var group in column.Groups)
            {
                var groupVm = new GroupViewModel(group, columnVm, this);
                foreach (var tile in group.Tiles)
                {
                    var tileVm = new TileViewModel(tile, _catalog.Find(tile.AppId), groupVm, null);
                    foreach (var child in tile.Items)
                        tileVm.Children.Add(new TileViewModel(child, _catalog.Find(child.AppId), groupVm, tileVm));
                    tileVm.IsExpanded = expanded != null && ReferenceEquals(tile, expanded);
                    groupVm.Tiles.Add(tileVm);
                }
                groupVm.RebuildItems();
                columnVm.Groups.Add(groupVm);
            }
            Columns.Add(columnVm);
        }
    }

    /// <summary>Rebinds tiles and lists to the current app catalog and loads missing icons (pinned apps first).</summary>
    private void SyncWithCatalog()
    {
        RebuildLayout();
        RebuildAllApps();
        UpdateSearch();
        var pinnedApps = Editor.Layout.AllAppTiles.Select(t => _catalog.Find(t.AppId)).OfType<AppEntry>();
        _catalog.LoadIcons(pinnedApps);
    }

    private void RebuildAllApps()
    {
        var view = new ListCollectionView(_catalog.Apps.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AppEntry.Letter)));
        AllAppsView = view;

        var present = _catalog.Apps.Select(a => a.Letter).ToHashSet();
        Letters.Clear();
        foreach (var letter in new[] { "#" }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString())))
            Letters.Add(new LetterItem(letter, present.Contains(letter)));
    }

    private void UpdateSearch()
    {
        SearchResults.Clear();
        if (SearchText.Trim().Length == 0)
        {
            SelectedResult = null;
            return;
        }
        foreach (var app in SearchScorer.Rank(_catalog.Apps, a => a.Name, SearchText, max: 40))
            SearchResults.Add(app);
        SelectedResult = SearchResults.FirstOrDefault();
    }
}
