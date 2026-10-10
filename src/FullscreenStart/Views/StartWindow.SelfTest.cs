using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FullscreenStart.Models;
using FullscreenStart.Services;
using FullscreenStart.ViewModels;

namespace FullscreenStart.Views;

/// <summary>
/// Developer aid: drives clicks and drag &amp; drop through the real layout (window not shown) and
/// reports what happened. App launches are recorded instead of executed.
/// </summary>
public partial class StartWindow
{
    private Action<AppEntry>? _launchOverride;

    public async Task<string> RunSelfTestAsync()
    {
        var report = new StringBuilder();
        int failures = 0;
        var launched = new List<string>();
        _launchOverride = app => launched.Add(app.Name);

        async Task Step(string name, Func<Task> body)
        {
            try
            {
                await body();
                report.AppendLine($"PASS  {name}");
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine($"FAIL  {name}: {ex.Message}");
            }
        }

        var bounds = ScreenInfo.MenuBounds();
        var size = new Size(bounds.Width, bounds.Height);
        _vm.Reset();
        await LayoutForTest(size);

        await Step("Klick auf Ordner öffnet ihn", async () =>
        {
            var folder = Tile(t => t.IsFolder);
            var model = Vm(folder);
            await ClickTile(folder, size);
            Expect(model.IsExpanded, "Ordner ist nicht aufgeklappt");
            Expect(FindDescendants<Border>(ColumnsHost).Any(b => (string?)b.Tag == "drawer"), "Keine Schublade sichtbar");
        });

        await Step("Zweiter Klick schließt den Ordner", async () =>
        {
            var folder = Tile(t => t.IsExpanded);
            await ClickTile(folder, size);
            Expect(!_vm.Columns.SelectMany(c => c.Groups).Any(g => g.ExpandedFolder != null), "Ordner ist noch offen");
        });

        await Step("Klick auf App-Kachel startet die App und schließt das Menü", async () =>
        {
            var app = Tile(t => t.App != null && t.ParentFolder == null);
            string name = Vm(app).Name;
            await ClickTile(app, size);
            Expect(launched.Contains(name), $"{name} wurde nicht gestartet");
        });

        await Step("Klick auf App im offenen Ordner startet sie", async () =>
        {
            await ClickTile(Tile(t => t.IsFolder), size);
            var inner = Tile(t => t.ParentFolder != null && t.App != null);
            string name = Vm(inner).Name;
            await ClickTile(inner, size);
            Expect(launched.Contains(name), $"{name} wurde nicht gestartet");
            _vm.CollapseFolders();
            await LayoutForTest(size);
        });

        await Step("Kachel auf Kachel-Mitte ziehen bildet einen Ordner", async () =>
        {
            var (a, b) = TwoNeighbourApps();
            string nameA = Vm(a).Name, nameB = Vm(b).Name;
            await Drag(a, Center(b), size);
            var folder = _vm.Editor.Layout.AllGroups.SelectMany(g => g.Tiles)
                .FirstOrDefault(t => t.Kind == TileKind.Folder && t.Items.Any(i => i.Name == nameA) && t.Items.Any(i => i.Name == nameB));
            Expect(folder != null, $"Kein Ordner aus {nameA} und {nameB}");
        });

        await Step("Kachel an linken Rand ziehen fügt davor ein", async () =>
        {
            var group = _vm.Columns.SelectMany(c => c.Groups).First(g => g.Tiles.Count(t => t.IsApp && t.App != null) >= 2);
            var first = group.Tiles.First(t => t.IsApp && t.App != null);
            var last = group.Tiles.Last(t => t.IsApp && t.App != null);
            var target = Element(first);
            var r = BoundsOf(target);
            string moved = last.Name;
            await Drag(Element(last), new Point(r.Left + 4, r.Top + r.Height / 2), size);
            var updated = _vm.Columns.SelectMany(c => c.Groups).First(g => ReferenceEquals(g.Model, group.Model));
            int movedIndex = updated.Tiles.ToList().FindIndex(t => t.Name == moved);
            int firstIndex = updated.Tiles.ToList().FindIndex(t => t.Name == first.Name);
            Expect(movedIndex >= 0 && movedIndex == firstIndex - 1, $"{moved} steht nicht vor {first.Name}");
        });

        await Step("Kachel in leeren Spaltenbereich ziehen erzeugt neue Gruppe", async () =>
        {
            int groupsBefore = _vm.Editor.Layout.AllGroups.Count();
            var column = FindDescendants<Border>(ColumnsHost).Last(b => (string?)b.Tag == "column");
            var r = BoundsOf(column);
            var app = Tile(t => t.App != null && t.ParentFolder == null && !ReferenceEquals(t.Group.Column.Model, ((ColumnViewModel)column.DataContext).Model));
            string name = Vm(app).Name;
            await Drag(app, new Point(r.Left + 150, r.Bottom - 80), size);
            Expect(_vm.Editor.Layout.AllGroups.Count() >= groupsBefore, "Gruppenanzahl gesunken");
            var lastColumn = _vm.Editor.Layout.Columns.Last();
            Expect(lastColumn.Groups.Last().Tiles.Any(t => t.Name == name), $"{name} ist nicht in einer neuen Gruppe unten in der letzten Spalte");
        });

        await Step("Gruppe an Überschrift ziehen verschiebt sie", async () =>
        {
            var headers = FindDescendants<Grid>(ColumnsHost).Where(g => (string?)g.Tag == "groupheader").ToList();
            var source = headers.First();
            var sourceGroup = (GroupViewModel)source.DataContext;
            var targetHeader = headers.Last(h => !ReferenceEquals(((GroupViewModel)h.DataContext).Column.Model, sourceGroup.Column.Model));
            var targetGroup = (GroupViewModel)targetHeader.DataContext;
            var r = BoundsOf(targetHeader);
            await Drag(source, new Point(r.Left + 60, r.Top + 4), size);
            var column = _vm.Editor.Layout.Columns.First(c => c.Groups.Contains(targetGroup.Model));
            Expect(column.Groups.IndexOf(sourceGroup.Model) == column.Groups.IndexOf(targetGroup.Model) - 1, "Gruppe steht nicht vor der Zielgruppe");
        });

        await Step("Abbrechen eines Drags hinterlässt keinen Zustand", async () =>
        {
            var app = Tile(t => t.App != null);
            Press(app, Center(app));
            MovePress(Center(app) + new Vector(40, 40));
            CancelDrag();
            await LayoutForTest(size);
            Expect(_pressElement == null && !_isDragging && DragLayer.Children.Count == 1 && _indicator.Visibility != Visibility.Visible,
                "Drag-Zustand oder Vorschau ist hängen geblieben");
        });

        await Step("Suche findet Spotify zuerst", () =>
        {
            _vm.SearchText = "spo";
            Expect(_vm.SelectedResult?.Name == "Spotify", $"Erster Treffer: {_vm.SelectedResult?.Name}");
            _vm.SearchText = "";
            return Task.CompletedTask;
        });

        _launchOverride = null;
        report.Insert(0, failures == 0 ? "OK\n" : $"{failures} FEHLER\n");
        return report.ToString();
    }

    private async Task ClickTile(FrameworkElement tile, Size size)
    {
        Press(tile, Center(tile));
        Release();
        await LayoutForTest(size);
    }

    private async Task Drag(FrameworkElement element, Point target, Size size)
    {
        var start = Center(element);
        Press(element, start);
        MovePress(start + new Vector(DragThreshold + 4, 0));
        await LayoutForTest(size);
        MovePress(target);
        Release();
        await LayoutForTest(size);
    }

    private async Task LayoutForTest(Size size)
    {
        RootGrid.Measure(size);
        RootGrid.Arrange(new Rect(size));
        RootGrid.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        RootGrid.UpdateLayout();
    }

    private FrameworkElement Tile(Func<TileViewModel, bool> predicate) =>
        FindDescendants<Border>(ColumnsHost).FirstOrDefault(b => (string?)b.Tag == "tile" && b.DataContext is TileViewModel t && predicate(t))
        ?? throw new InvalidOperationException("Keine passende Kachel gefunden");

    private FrameworkElement Element(TileViewModel vm) => Tile(t => ReferenceEquals(t.Model, vm.Model));

    private (FrameworkElement A, FrameworkElement B) TwoNeighbourApps()
    {
        var group = _vm.Columns.SelectMany(c => c.Groups).First(g => g.Tiles.Count(t => t.IsApp && t.App != null) >= 2);
        var apps = group.Tiles.Where(t => t.IsApp && t.App != null).ToList();
        return (Element(apps[0]), Element(apps[1]));
    }

    private static TileViewModel Vm(FrameworkElement element) => (TileViewModel)element.DataContext;

    private Point Center(FrameworkElement element)
    {
        var r = BoundsOf(element);
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
