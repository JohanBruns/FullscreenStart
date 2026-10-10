using FullscreenStart.Models;

namespace FullscreenStart.Services;

/// <summary>
/// First-run layout, modelled on the user's Windows 10 start screen. Each tile lists name aliases;
/// tiles whose app is not installed are left out. Everything stays editable afterwards.
/// </summary>
public static class DefaultLayout
{
    private sealed record Seed(string Name, TileSize Size, string[] Aliases, Seed[]? Items = null)
    {
        public bool IsFolder => Items != null;
    }

    private static Seed App(string name, params string[] aliases) => new(name, TileSize.Medium, aliases.Length == 0 ? new[] { name } : aliases);
    private static Seed Wide(string name, params string[] aliases) => App(name, aliases) with { Size = TileSize.Wide };
    private static Seed Folder(string name, params Seed[] items) => new(name, TileSize.Medium, Array.Empty<string>(), items);

    private static readonly (string Group, Seed[] Tiles)[][] Columns =
    {
        new[]
        {
            ("", new[]
            {
                Folder("Credential & Account", App("Bitwarden"), App("Yubico Authenticator", "Yubico Authenticator", "YubiKey Manager"), App("KeePassXC")),
                App("Zen", "Zen", "Zen Browser"),
                App("Brave", "Brave", "Brave Browser"),
            }),
            ("Development Environment", new[]
            {
                App("JetBrains Toolbox"),
                Folder("Code Editor", App("IntelliJ IDEA", "IntelliJ IDEA Ultimate", "IntelliJ IDEA Community Edition", "IntelliJ IDEA"), App("Antigravity", "Antigravity IDE", "Antigravity"), App("Visual Studio Code"), App("Cursor")),
                Folder("WSL (Windows Subsystem for Linux)", App("WSL"), App("WSL Settings"), App("Ubuntu"), App("Debian"), App("Kali Linux"), App("Fedora")),
                Folder("Terminal & Environment", App("Terminal", "Terminal", "Windows Terminal"), App("Node.js"), App("Eingabeaufforderung", "Eingabeaufforderung", "Command Prompt"), App("Git Bash"), App("Windows PowerShell"), App("Python 3.14", "Python 3.14 (64-bit)", "Python 3.14")),
                App("Docker Desktop"),
                App("GitHub Desktop"),
            }),
            ("Gaming Zone", new[]
            {
                App("Prism Launcher"),
                App("Roblox Player"),
                App("Stardew Valley"),
                App("Steam"),
                Folder("Minecraft Tools", App("Minecraft Launcher"), App("Lunar Client"), App("Badlion Client"), App("Feather Launcher")) with { Size = TileSize.Wide },
                App("gta_sa", "gta_sa", "Grand Theft Auto San Andreas", "GTA San Andreas"),
            }),
        },
        new[]
        {
            ("Multimedia & Editing", new[]
            {
                Folder("Audio", App("Audacity"), App("Voicemeeter", "Voicemeeter", "VoiceMeeter Banana")),
                App("DaVinci Resolve"),
                App("GIMP"),
                App("Inkscape"),
                App("Paint.NET", "Paint.NET", "paint.net"),
                App("VLC media player"),
            }),
            ("Microsoft & Windows", new[]
            {
                App("Control Panel", "Systemsteuerung", "Control Panel"),
                Folder("Microsoft Office", App("PowerPoint"), App("Word"), App("Excel")),
                App("OneDrive"),
                Folder("Windows Utilities", App("Snipping Tool"), App("Rechner", "Rechner", "Calculator")),
                Wide("Photos", "Fotos", "Photos", "Windows-Fotoanzeige"),
            }),
        },
        new[]
        {
            ("Productivity", new[]
            {
                App("Notion"),
                App("Samsung Notes"),
                App("Perplexity"),
                Wide("ChatGPT"),
                App("Obsidian"),
            }),
            ("Entertainment & Communication", new[]
            {
                App("Spotify"),
                App("Telegram", "Telegram", "Telegram Desktop"),
                App("Thunderbird", "Thunderbird", "Mozilla Thunderbird"),
                Wide("WhatsApp"),
                App("Discord"),
            }),
        },
        new[]
        {
            ("Utilities & Tools", new[]
            {
                App("Cloudflare WARP"),
                App("CPU-Z"),
                App("LocalSend"),
                App("OBS Studio", "OBS Studio (64bit)", "OBS Studio"),
                App("PDFgear"),
                App("PowerToys", "PowerToys (Preview)", "PowerToys"),
                App("qBittorrent"),
                App("SumatraPDF"),
                App("UniGetUI"),
                App("WizTree"),
                App("Google Drive"),
            }),
        },
    };

    /// <summary>Builds the seed layout from the installed apps (id, display name).</summary>
    public static StartLayout Build(IReadOnlyList<(string Id, string Name)> apps)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var layout = new StartLayout();

        foreach (var column in Columns)
        {
            var col = new LayoutColumn();
            foreach (var (groupName, seeds) in column)
            {
                var group = new TileGroup { Name = groupName };
                foreach (var seed in seeds)
                {
                    if (seed.IsFolder)
                    {
                        var items = seed.Items!.Select(s => Resolve(s, apps, used)).OfType<Tile>().ToList();
                        if (items.Count > 0)
                            group.Tiles.Add(Tile.ForFolder(seed.Name, items, seed.Size));
                    }
                    else if (Resolve(seed, apps, used) is { } tile)
                    {
                        group.Tiles.Add(tile);
                    }
                }
                col.Groups.Add(group);
            }
            layout.Columns.Add(col);
        }

        layout.Normalize();
        return layout;
    }

    private static Tile? Resolve(Seed seed, IReadOnlyList<(string Id, string Name)> apps, HashSet<string> used)
    {
        var match = FindApp(seed.Aliases, apps, used);
        if (match is not { } app) return null;
        used.Add(app.Id);
        return Tile.ForApp(app.Id, app.Name, seed.Size);
    }

    /// <summary>Exact (normalized) name match first, then "alias" followed by a version or qualifier.</summary>
    internal static (string Id, string Name)? FindApp(IEnumerable<string> aliases, IReadOnlyList<(string Id, string Name)> apps, ISet<string>? used = null)
    {
        var candidates = apps.Where(a => used == null || !used.Contains(a.Id)).ToList();
        var normalized = aliases.Select(SearchScorer.Normalize).ToList();

        foreach (var alias in normalized)
        {
            foreach (var app in candidates)
                if (SearchScorer.Normalize(app.Name) == alias) return app;
        }
        foreach (var alias in normalized)
        {
            foreach (var app in candidates)
            {
                var name = SearchScorer.Normalize(app.Name);
                if (name.Length > alias.Length && name.StartsWith(alias, StringComparison.Ordinal) && name[alias.Length] is ' ' or '(')
                    return app;
            }
        }
        return null;
    }
}
