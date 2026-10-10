using System.Runtime.InteropServices;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Low-level mouse hook that opens our menu when the taskbar's Start button is left-clicked, and keeps
/// the click from reaching the taskbar. Windows 11 still keeps the classic (hidden) "Start" window
/// positioned exactly over its Start button, so locating it is a cheap window lookup that is safe to do
/// inside the hook. Right-click (the Win+X menu) is left alone.
/// </summary>
public sealed class StartButtonHook : IDisposable
{
    private readonly Action _onClick;
    private readonly LowLevelHookProc _proc;
    private readonly HookThread _thread;
    private bool _swallowNextUp;

    public StartButtonHook(Action onClick)
    {
        _onClick = onClick;
        _proc = HookProc;
        _thread = new HookThread(WH_MOUSE_LL, _proc, "FullscreenStart.StartButtonHook");
    }

    public bool IsInstalled => _thread.IsInstalled;

    public void Install()
    {
        if (IsInstalled) return;
        _thread.Install();
        Log.Info($"Start button hook installed (button found: {StartButtonRect() != null})");
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        _thread.Uninstall();
        _swallowNextUp = false;
        Log.Info("Start button hook removed");
    }

    /// <summary>Screen rectangle of the taskbar's Start button on the primary taskbar, if there is one.</summary>
    internal static RECT? StartButtonRect()
    {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;
        IntPtr start = FindWindowEx(tray, IntPtr.Zero, "Start", null);
        if (start == IntPtr.Zero || !GetWindowRect(start, out var rect) || rect.Width <= 0 || rect.Height <= 0) return null;
        return rect;
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            // Mouse moves are by far the most frequent event: leave them as fast as possible.
            if (msg == WM_LBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (StartButtonRect() is { } r && info.pt.X >= r.Left && info.pt.X < r.Right && info.pt.Y >= r.Top && info.pt.Y < r.Bottom)
                {
                    _swallowNextUp = true;
                    return new IntPtr(1);
                }
            }
            else if (msg == WM_LBUTTONUP && _swallowNextUp)
            {
                _swallowNextUp = false;
                _onClick(); // must not block: the caller posts to the UI thread
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
