using System.Diagnostics;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

public static class PowerActions
{
    public static void Sleep() => SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);

    public static void Shutdown() => RunShutdown("/s /t 0");

    public static void Restart() => RunShutdown("/r /t 0");

    public static void Lock() => LockWorkStation();

    public static void SignOut() => ExitWindowsEx(EWX_LOGOFF, 0);

    private static void RunShutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
}
