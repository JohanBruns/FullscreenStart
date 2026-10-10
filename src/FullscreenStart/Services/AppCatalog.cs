using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using FullscreenStart.Interop;
using FullscreenStart.Models;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// All installed apps, read from shell:AppsFolder (desktop and Store apps, exactly what the Windows
/// Start menu lists) and matched to their shortcuts in the two Start Menu "Programs" folders.
/// </summary>
public sealed class AppCatalog : IDisposable
{
    public const int IconSize = 96;

    private readonly StaWorker _sta = new("FullscreenStart.Shell");
    private readonly Dispatcher _dispatcher;
    private Dictionary<string, AppEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private int _iconGeneration;

    public AppCatalog(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public IReadOnlyList<AppEntry> Apps { get; private set; } = Array.Empty<AppEntry>();

    public DateTime LastRefresh { get; private set; }

    public event EventHandler? Changed;

    public AppEntry? Find(string? id) => id != null && _byId.TryGetValue(id, out var app) ? app : null;

    /// <summary>Re-reads the app list. Existing entries keep their loaded icons.</summary>
    public async Task RefreshAsync()
    {
        var sw = Stopwatch.StartNew();
        var raw = await _sta.Run(Enumerate);

        var byId = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name, pidl, link) in raw)
        {
            if (byId.ContainsKey(id)) continue;
            var entry = _byId.TryGetValue(id, out var old) && old.Name == name ? old : new AppEntry(id, name, pidl, link);
            byId[id] = entry;
        }

        _byId = byId;
        Apps = byId.Values
            .OrderBy(a => a.Letter == "#" ? 0 : 1)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        LastRefresh = DateTime.Now;
        Debug.WriteLine($"[Catalog] {Apps.Count} apps in {sw.ElapsedMilliseconds} ms");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads missing icons in the background, the given apps first.</summary>
    public void LoadIcons(IEnumerable<AppEntry> first)
    {
        int generation = ++_iconGeneration;
        var order = first.Concat(Apps).Where(a => a.Icon == null).Distinct().ToList();
        if (order.Count == 0) return;

        _ = _sta.Run(() =>
        {
            const int batchSize = 16;
            var batch = new List<(AppEntry App, System.Windows.Media.ImageSource Icon)>(batchSize);
            foreach (var app in order)
            {
                if (generation != _iconGeneration) return;
                if (app.Icon != null) continue;
                var icon = ShellImaging.LoadIcon(app.Pidl, IconSize);
                if (icon != null) batch.Add((app, icon));
                if (batch.Count >= batchSize) Flush();
            }
            Flush();

            void Flush()
            {
                if (batch.Count == 0) return;
                var items = batch.ToArray();
                batch.Clear();
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    foreach (var (app, icon) in items) app.Icon = icon;
                });
            }
        });
    }

    private static List<(string Id, string Name, byte[] Pidl, string? Link)> Enumerate()
    {
        var links = ReadStartMenuLinks();
        var result = new List<(string, string, byte[], string?)>(400);

        SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ShellGuids.IShellItem, out var folderObj);
        var folder = (IShellItem)folderObj;
        var enumerator = (IEnumShellItems)folder.BindToHandler(IntPtr.Zero, ShellGuids.BHID_EnumItems, ShellGuids.IEnumShellItems);
        try
        {
            var buffer = new IShellItem[1];
            while (enumerator.Next(1, buffer, out var fetched) == 0 && fetched == 1)
            {
                var item = buffer[0];
                try
                {
                    var name = item.GetDisplayName(SIGDN.NORMALDISPLAY);
                    var id = item.GetDisplayName(SIGDN.PARENTRELATIVEPARSING);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;
                    var pidl = PidlOf(item);
                    if (pidl == null) continue;
                    links.TryGetValue(SearchScorer.Normalize(name), out var link);
                    result.Add((id, name, pidl, link));
                }
                catch (COMException)
                {
                    // Skip items the shell cannot describe.
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(folder);
        }
        return result;
    }

    private static byte[]? PidlOf(object item)
    {
        SHGetIDListFromObject(item, out var pidl);
        if (pidl == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[ILGetSize(pidl)];
            Marshal.Copy(pidl, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    /// <summary>Maps normalized display names (incl. localized desktop.ini names) to .lnk/.url paths.</summary>
    internal static Dictionary<string, string> ReadStartMenuLinks()
    {
        var map = new Dictionary<string, string>();
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (IOException)
            {
                continue;
            }

            var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ini in Directory.EnumerateFiles(root, "desktop.ini", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                ReadLocalizedNames(ini, localized);

            foreach (var file in files)
            {
                map.TryAdd(SearchScorer.Normalize(Path.GetFileNameWithoutExtension(file)), file);
                if (localized.TryGetValue(file, out var display))
                    map.TryAdd(SearchScorer.Normalize(display), file);
            }
        }
        return map;
    }

    private static void ReadLocalizedNames(string iniPath, Dictionary<string, string> into)
    {
        string[] lines;
        try { lines = File.ReadAllLines(iniPath); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        bool inSection = false;
        var dir = Path.GetDirectoryName(iniPath)!;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[LocalizedFileNames]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var file = line[..eq];
            var value = line[(eq + 1)..];
            if (value.StartsWith('@'))
            {
                var sb = new StringBuilder(512);
                if (SHLoadIndirectString(Environment.ExpandEnvironmentVariables(value), sb, sb.Capacity, IntPtr.Zero) != 0) continue;
                value = sb.ToString();
            }
            into[Path.Combine(dir, file)] = value;
        }
    }

    public void Dispose() => _sta.Dispose();
}
