using System.ComponentModel;
using System.Runtime.InteropServices;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Hosts one low-level hook (keyboard or mouse) on its own thread with its own message loop, so a busy
/// UI thread never delays system input (Windows silently drops hooks that answer too slowly).
/// </summary>
internal sealed class HookThread(int hookType, LowLevelHookProc proc, string name)
{
    private Thread? _thread;
    private uint _threadId;

    public bool IsInstalled => _thread != null;

    public void Install()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IntPtr hook = SetWindowsHookEx(hookType, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (hook == IntPtr.Zero) return;

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(hook);
        })
        { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait();

        if (error != null)
        {
            _thread = null;
            throw error;
        }
    }

    public void Uninstall()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
