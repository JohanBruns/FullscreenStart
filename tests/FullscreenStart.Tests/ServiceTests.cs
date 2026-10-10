using System.IO;
using FullscreenStart.Models;
using FullscreenStart.Services;

namespace FullscreenStart.Tests;

public class SearchScorerTests
{
    [Theory]
    [InlineData("Spotify", "spo")]
    [InlineData("Visual Studio Code", "code")]
    [InlineData("Visual Studio Code", "vsc")]
    [InlineData("Windows-Fotoanzeige", "foto")]
    [InlineData("Leistungsüberwachung", "uberw")]
    [InlineData("Microsoft Edge", "micro edge")]
    public void Matches(string name, string query) => Assert.True(SearchScorer.Score(name, query) > 0);

    [Theory]
    [InlineData("Spotify", "xyz")]
    [InlineData("Steam", "")]
    public void DoesNotMatch(string name, string query) => Assert.Equal(0, SearchScorer.Score(name, query));

    [Fact]
    public void PrefixBeatsWordStartBeatsContains()
    {
        var ranked = SearchScorer.Rank(new[] { "Discord", "Microsoft Teams", "Teams" }, n => n, "tea");
        Assert.Equal(new[] { "Teams", "Microsoft Teams" }, ranked);

        var mixed = SearchScorer.Rank(new[] { "Notepad", "Editor", "Credit" }, n => n, "edit");
        Assert.Equal("Editor", mixed[0]);
        Assert.Equal("Credit", mixed[1]);
    }

    [Fact]
    public void ShorterNamesWinTies()
    {
        var ranked = SearchScorer.Rank(new[] { "Discord PTB", "Discord" }, n => n, "disc");
        Assert.Equal("Discord", ranked[0]);
    }
}

public class MenuPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void VisibleTaskbarUsesWorkArea()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var result = MenuPlacement.Compute(Monitor, work, new PixelRect(0, 1032, 1920, 1080), ScreenEdge.Bottom, autoHide: false);
        Assert.Equal(work, result);
    }

    [Fact]
    public void AutoHideTaskbarLeavesItsSliverFree()
    {
        // Hidden auto-hide taskbar sits almost completely below the screen edge.
        var result = MenuPlacement.Compute(Monitor, Monitor, new PixelRect(0, 1078, 1920, 1126), ScreenEdge.Bottom, autoHide: true);
        Assert.Equal(new PixelRect(0, 0, 1920, 1078), result);
    }

    [Fact]
    public void AutoHideTaskbarOnOtherMonitorIsIgnored()
    {
        var left = new PixelRect(-1920, 0, 0, 1080);
        var result = MenuPlacement.Compute(left, left, new PixelRect(0, 1032, 1920, 1080), ScreenEdge.Bottom, autoHide: true);
        Assert.Equal(left, result);
    }

    [Fact]
    public void AutoHideTaskbarOnLeftEdge()
    {
        var result = MenuPlacement.Compute(Monitor, Monitor, new PixelRect(-60, 0, 2, 1080), ScreenEdge.Left, autoHide: true);
        Assert.Equal(new PixelRect(2, 0, 1920, 1080), result);
    }
}

public class ImageBlurTests
{
    [Fact]
    public void BlurSpreadsASinglePixelAndKeepsUniformAreas()
    {
        const int w = 21, h = 21;
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = pixels[i + 1] = pixels[i + 2] = 100; pixels[i + 3] = 255; }
        int center = (10 * w + 10) * 4;
        pixels[center] = 255;

        ImageBlur.Blur(pixels, w, h, radius: 2);

        Assert.InRange(pixels[center], 101, 254);            // peak flattened
        Assert.True(pixels[(10 * w + 12) * 4] > 100);         // neighbour brightened
        Assert.Equal(100, pixels[0 + 1]);                     // far corner untouched
        Assert.Equal(255, pixels[3]);                         // alpha stays opaque
    }

    [Fact]
    public void BlankDetection()
    {
        Assert.True(WallpaperBackdrop.LooksBlank(new byte[400]));
        Assert.False(WallpaperBackdrop.LooksBlank(Enumerable.Repeat((byte)120, 400).ToArray()));
    }
}

public class DefaultLayoutTests
{
    private static readonly (string Id, string Name)[] Apps =
    {
        ("cp", "Systemsteuerung"),
        ("gimp", "GIMP 2.10.30"),
        ("discord", "Discord"),
        ("ptb", "Discord PTB"),
        ("obs", "OBS Studio (64bit)"),
        ("word", "Word"),
        ("excel", "Excel"),
        ("wa", "WhatsApp"),
    };

    [Fact]
    public void FindAppPrefersExactMatchThenQualifiedPrefix()
    {
        Assert.Equal("discord", DefaultLayout.FindApp(new[] { "Discord" }, Apps)?.Id);
        Assert.Equal("gimp", DefaultLayout.FindApp(new[] { "GIMP" }, Apps)?.Id);
        Assert.Equal("cp", DefaultLayout.FindApp(new[] { "Control Panel", "Systemsteuerung" }, Apps)?.Id);
        Assert.Null(DefaultLayout.FindApp(new[] { "Gim" }, Apps));
    }

    [Fact]
    public void BuildKeepsOnlyInstalledAppsAndDropsEmptyGroups()
    {
        var layout = DefaultLayout.Build(Apps);

        var groups = layout.AllGroups.Select(g => g.Name).ToList();
        Assert.Contains("Microsoft & Windows", groups);
        Assert.DoesNotContain("Productivity", groups);

        var office = layout.AllGroups.SelectMany(g => g.Tiles).Single(t => t.Name == "Microsoft Office");
        Assert.Equal(TileKind.Folder, office.Kind);
        Assert.Equal(new[] { "word", "excel" }, office.Items.Select(i => i.AppId));

        var whatsApp = layout.AllAppTiles.Single(t => t.AppId == "wa");
        Assert.Equal(TileSize.Wide, whatsApp.Size);
    }
}

public class JsonStoreTests
{
    [Fact]
    public void LayoutRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fs-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonStore(dir);
            Assert.Null(store.LoadLayout());

            var layout = new StartLayout
            {
                Columns =
                {
                    new LayoutColumn
                    {
                        Groups =
                        {
                            new TileGroup
                            {
                                Name = "Spiele & Tools",
                                Tiles = { Tile.ForApp("steam", "Steam", TileSize.Wide), Tile.ForFolder("Ordner", new[] { Tile.ForApp("a", "A") }) },
                            },
                        },
                    },
                },
            };
            store.SaveLayout(layout);

            var json = File.ReadAllText(store.LayoutPath);
            Assert.Contains("\"Spiele & Tools\"", json);
            Assert.Contains("\"wide\"", json);

            var loaded = store.LoadLayout()!;
            var tiles = loaded.Columns[0].Groups[0].Tiles;
            Assert.Equal(TileSize.Wide, tiles[0].Size);
            Assert.Empty(tiles[0].Items);
            Assert.Equal(TileKind.Folder, tiles[1].Kind);
            Assert.Equal("a", tiles[1].Items[0].AppId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BrokenLayoutIsKeptAsBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fs-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonStore(dir);
            File.WriteAllText(store.LayoutPath, "{ not json");

            Assert.Null(store.LoadLayout());
            Assert.True(File.Exists(store.LayoutPath + ".broken"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ThemeSettingTests
{
    [Theory]
    [InlineData("light", true)]
    [InlineData("Hell", true)]
    [InlineData("dark", false)]
    [InlineData("DUNKEL", false)]
    public void ExplicitThemeOverridesWindows(string setting, bool light) =>
        Assert.Equal(light, ThemeColors.UseLightTheme(setting));
}

public class HotkeyParsingTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Space", 0x0006u, 0x20u)]
    [InlineData("Strg+Alt+Leertaste", 0x0003u, 0x20u)]
    [InlineData("Win+F1", 0x0008u, 0x70u)]
    public void Parses(string gesture, uint modifiers, uint vk)
    {
        Assert.True(HotkeyManager.TryParse(gesture, out var m, out var v));
        Assert.Equal(modifiers, m);
        Assert.Equal(vk, v);
    }

    [Fact]
    public void RejectsGestureWithoutKey() => Assert.False(HotkeyManager.TryParse("Ctrl+Alt", out _, out _));
}
