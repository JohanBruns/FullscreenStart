using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
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
    private WinKeyHook? _winKeyHook;
    private TrayIcon? _tray;
    private StartWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int selfTestArg = Array.IndexOf(e.Args, "--selftest");
        if (selfTestArg >= 0 && selfTestArg + 1 < e.Args.Length)
        {
            await RunSelfTestAsync(e.Args[selfTestArg + 1]);
            return;
        }

        int snapshotArg = Array.IndexOf(e.Args, "--snapshot");
        if (snapshotArg >= 0 && snapshotArg + 1 < e.Args.Length)
        {
            await RunSnapshotAsync(e.Args[snapshotArg + 1], e.Args.Length > snapshotArg + 2 ? e.Args[snapshotArg + 2] : null);
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceName, out bool isFirst);
        if (!isFirst)
        {
            // Already running: ask the running instance to open the menu (and let it take the foreground).
            Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal)) signal.Set();
            Shutdown();
            return;
        }

        var startup = Stopwatch.StartNew();
        var store = new JsonStore();
        Log.Initialize(store.Directory);
        HookUnhandledExceptions();
        var settings = store.LoadSettings();
        ThemeColors.Apply(Resources, ThemeColors.UseLightTheme(settings.Theme));

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

        _hotkey = new HotkeyManager(_window.Handle);
        _hotkey.Pressed += (_, _) => _window.Toggle();
        bool hotkeyOk = _hotkey.Register(settings.Hotkey);

        _winKeyHook = new WinKeyHook(() => Dispatcher.BeginInvoke(() => _window.Toggle()));
        bool winKeyOk = settings.TakeOverWinKey && TrySetWinKeyHook(true);
        SystemEvents.SessionSwitch += OnSessionSwitch;

        var autostart = Autostart.ForCurrentUser();
        _tray = new TrayIcon($"Fullscreen Start (Windows-Taste oder {settings.Hotkey})", () => _window.Toggle())
            .AddItem("Startmenü öffnen", () => _window.Toggle())
            .AddSeparator()
            .AddToggle("Windows-Taste öffnet dieses Menü", winKeyOk, enable =>
            {
                bool active = TrySetWinKeyHook(enable);
                settings.TakeOverWinKey = active;
                store.SaveSettings(settings);
                return active;
            })
            .AddToggle("Mit Windows starten", autostart.IsEnabled, enable =>
            {
                try
                {
                    if (enable) autostart.Enable(Environment.ProcessPath!);
                    else autostart.Disable();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    Log.Error("Autostart could not be changed", ex);
                }
                return autostart.IsEnabled;
            })
            .AddSeparator()
            .AddItem("Apps neu einlesen", () => _ = _catalog.RefreshAsync())
            .AddItem("Einstellungsordner öffnen", () => AppLauncher.Open(store.Directory))
            .AddSeparator()
            .AddItem("Beenden", Shutdown);

        Log.Info($"Started in {startup.ElapsedMilliseconds} ms: {_catalog.Apps.Count} apps, hotkey {settings.Hotkey} {(hotkeyOk ? "registered" : "TAKEN")}");
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
    }

    /// <summary>Installs or removes the Windows key hook; returns whether it is active afterwards.</summary>
    private bool TrySetWinKeyHook(bool enable)
    {
        if (_winKeyHook == null) return false;
        try
        {
            if (enable) _winKeyHook.Install();
            else _winKeyHook.Uninstall();
        }
        catch (Exception ex)
        {
            Log.Error("Windows key hook could not be installed", ex);
        }
        return _winKeyHook.IsInstalled;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Win+L locks before the Win key-up reaches the hook.
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            _winKeyHook?.RequestReset();
    }

    /// <summary>The app lives in the background: log errors instead of crashing out of the tray.</summary>
    private void HookUnhandledExceptions()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Fatal exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>Developer aid: <c>FullscreenStart.exe --snapshot &lt;dir&gt; [query]</c> renders the views to PNG and exits.</summary>
    private async Task RunSnapshotAsync(string directory, string? query)
    {
        try
        {
            var store = new JsonStore();
            ThemeColors.Apply(Resources, ThemeColors.UseLightTheme(store.LoadSettings().Theme));
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

    /// <summary>Developer aid: <c>FullscreenStart.exe --selftest &lt;report file&gt;</c> exercises clicks and drag &amp; drop.</summary>
    private async Task RunSelfTestAsync(string reportFile)
    {
        int exitCode = 1;
        try
        {
            ThemeColors.Apply(Resources, light: false);
            _catalog = new AppCatalog(Dispatcher);
            await _catalog.RefreshAsync();

            // Works on the default layout in a throw-away folder; the real layout.json is never touched.
            var testStore = new JsonStore(Path.Combine(Path.GetTempPath(), "FullscreenStart.SelfTest"));
            var layout = DefaultLayout.Build(_catalog.Apps.Select(a => (a.Id, a.Name)).ToList());
            var window = new StartWindow(new MainViewModel(_catalog, testStore, new AppSettings { WallpaperBackdrop = false }, layout));
            var report = await window.RunSelfTestAsync();
            File.WriteAllText(reportFile, report);
            exitCode = report.StartsWith("OK") ? 0 : 1;
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportFile, "CRASH\n" + ex);
        }
        Shutdown(exitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _winKeyHook?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _catalog?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
