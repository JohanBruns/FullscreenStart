using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FullscreenStart.Models;
using FullscreenStart.Services;
using FullscreenStart.ViewModels;
using FullscreenStart.Views;

namespace FullscreenStart;

public partial class App : Application
{
    private const string InstanceName = "FullscreenStart.SingleInstance";
    private const string ShowSignalName = "FullscreenStart.ShowMenu";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private AppCatalog? _catalog;
    private HotkeyManager? _hotkey;
    private TrayIcon? _tray;
    private StartWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int snapshotArg = Array.IndexOf(e.Args, "--snapshot");
        if (snapshotArg >= 0 && snapshotArg + 1 < e.Args.Length)
        {
            await RunSnapshotAsync(e.Args[snapshotArg + 1], e.Args.Length > snapshotArg + 2 ? e.Args[snapshotArg + 2] : null);
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool isFirst);
        if (!isFirst)
        {
            // Already running: ask the running instance to open the menu.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal)) signal.Set();
            Shutdown();
            return;
        }

        var store = new JsonStore();
        var settings = store.LoadSettings();
        Resources["AccentBrush"] = new SolidColorBrush(ThemeColors.Accent());

        _catalog = new AppCatalog(Dispatcher);
        await _catalog.RefreshAsync();

        var layout = store.LoadLayout();
        if (layout == null)
        {
            layout = DefaultLayout.Build(_catalog.Apps.Select(a => (a.Id, a.Name)).ToList());
            store.SaveLayout(layout);
        }

        var viewModel = new MainViewModel(_catalog, store, settings, layout);
        _window = new StartWindow(viewModel);
        _window.Initialize();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += (_, _) => _window.Toggle();
        bool hotkeyOk = _hotkey.Register(settings.Hotkey);

        _tray = new TrayIcon(
            settings.Hotkey,
            toggleMenu: () => _window.Toggle(),
            refreshApps: () => _ = _catalog.RefreshAsync(),
            openDataFolder: () => AppLauncher.Open(store.Directory),
            exit: Shutdown);

        if (!hotkeyOk)
            _tray.ShowNotice($"Der Hotkey „{settings.Hotkey}“ ist schon belegt. Öffne das Menü über dieses Symbol oder ändere den Hotkey in settings.json.");

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => _window.ShowMenu());
        })
        { IsBackground = true, Name = "FullscreenStart.ShowSignal" };
        listener.Start();

        Debug.WriteLine($"[App] started, {_catalog.Apps.Count} apps");
    }

    /// <summary>Developer aid: <c>FullscreenStart.exe --snapshot &lt;dir&gt; [query]</c> renders the views to PNG and exits.</summary>
    private async Task RunSnapshotAsync(string directory, string? query)
    {
        try
        {
            var store = new JsonStore();
            Resources["AccentBrush"] = new SolidColorBrush(ThemeColors.Accent());
            _catalog = new AppCatalog(Dispatcher);
            await _catalog.RefreshAsync();
            var layout = store.LoadLayout() ?? DefaultLayout.Build(_catalog.Apps.Select(a => (a.Id, a.Name)).ToList());
            var window = new StartWindow(new MainViewModel(_catalog, new JsonStore(Path.Combine(Path.GetTempPath(), "FullscreenStart.Snapshot")), store.LoadSettings(), layout));
            await window.RenderSnapshotsAsync(directory, query);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString());
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _catalog?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
