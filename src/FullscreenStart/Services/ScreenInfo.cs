using System.Runtime.InteropServices;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

public static class ScreenInfo
{
    /// <summary>Monitor bounds of the primary monitor (where the taskbar lives), in physical pixels.</summary>
    public static (PixelRect Monitor, PixelRect WorkArea) PrimaryMonitor()
    {
        IntPtr monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return (ToPixelRect(info.rcMonitor), ToPixelRect(info.rcWork));
    }

    /// <summary>Where the menu window should go on the primary monitor.</summary>
    public static PixelRect MenuBounds()
    {
        var (monitor, work) = PrimaryMonitor();
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        bool hasTaskbar = SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != UIntPtr.Zero;

        var state = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        bool autoHide = ((int)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) != 0;

        return MenuPlacement.Compute(monitor, work, hasTaskbar ? ToPixelRect(data.rc) : null, (ScreenEdge)data.uEdge, autoHide);
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
