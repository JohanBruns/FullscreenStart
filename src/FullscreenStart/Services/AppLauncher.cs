using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FullscreenStart.Models;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>Starts apps the same way Explorer does: ShellExecuteEx on the item's ID list.</summary>
public static partial class AppLauncher
{
    public static void Launch(AppEntry app, bool asAdmin = false)
    {
        var pidl = app.Pidl;
        var link = app.LinkPath;
        // ShellExecuteEx wants an STA thread; a short-lived one keeps the UI responsive.
        var thread = new Thread(() =>
        {
            if (asAdmin && link != null) Execute(link, IntPtr.Zero, "runas");
            else WithPidl(pidl, p => Execute(null, p, asAdmin ? "runas" : null));
        })
        { IsBackground = true, Name = "FullscreenStart.Launch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static bool CanRunAsAdmin(AppEntry app) => !app.IsPackaged && !app.IsWebLink;

    public static bool CanOpenFileLocation(AppEntry app) => FileLocationOf(app) != null;

    public static void OpenFileLocation(AppEntry app)
    {
        var path = FileLocationOf(app);
        if (path != null) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    /// <summary>Opens a URI, shell folder or settings page (e.g. "ms-settings:yourinfo").</summary>
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine(ex); }
    }

    internal static string? FileLocationOf(AppEntry app)
    {
        if (app.LinkPath != null && File.Exists(app.LinkPath)) return app.LinkPath;
        var path = ResolveKnownFolderPath(app.Id);
        return path != null && File.Exists(path) ? path : null;
    }

    /// <summary>Expands AppsFolder ids like "{6D809377-...}\obs-studio\bin\64bit\obs64.exe" to real paths.</summary>
    internal static string? ResolveKnownFolderPath(string id)
    {
        var match = KnownFolderPrefix().Match(id);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var folderId))
        {
            if (SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var ptr) != 0) return null;
            try { return Path.Combine(Marshal.PtrToStringUni(ptr)!, match.Groups[2].Value); }
            finally { CoTaskMemFree(ptr); }
        }
        return Path.IsPathFullyQualified(id) ? id : null;
    }

    [GeneratedRegex(@"^\{([0-9A-Fa-f\-]{36})\}\\(.+)$")]
    private static partial Regex KnownFolderPrefix();

    private static void WithPidl(byte[] pidl, Action<IntPtr> action)
    {
        IntPtr native = Marshal.AllocCoTaskMem(pidl.Length);
        try
        {
            Marshal.Copy(pidl, 0, native, pidl.Length);
            action(native);
        }
        finally
        {
            Marshal.FreeCoTaskMem(native);
        }
    }

    private static void Execute(string? file, IntPtr pidl, string? verb)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = pidl != IntPtr.Zero ? SEE_MASK_INVOKEIDLIST : 0,
            lpVerb = verb,
            lpFile = file,
            lpIDList = pidl,
            nShow = SW_SHOWNORMAL,
        };
        if (!ShellExecuteEx(ref info))
            Debug.WriteLine($"[Launch] ShellExecuteEx failed: {Marshal.GetLastWin32Error()}");
    }
}
