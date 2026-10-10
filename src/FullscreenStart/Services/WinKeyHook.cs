using System.ComponentModel;
using System.Runtime.InteropServices;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Low-level keyboard hook that turns a lone press of the Windows key into "open our menu" while
/// keeping every Win+… shortcut working. Runs on its own thread with its own message loop, so a busy
/// UI thread never delays typing (Windows drops hooks that answer too slowly).
/// </summary>
public sealed class WinKeyHook : IDisposable
{
    /// <summary>Marks input we inject ourselves, so the hook lets it pass untouched.</summary>
    private static readonly IntPtr OwnInputMarker = new(0x46534D4B); // "FSMK"

    /// <summary>An unassigned virtual key: Windows sees "Win, something, Win up" and keeps its Start menu closed.</summary>
    private const ushort MaskKey = 0xE8;

    private readonly Action _onLoneTap;
    private readonly WinKeyFilter _filter = new();
    private readonly LowLevelKeyboardProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _resetRequested;

    public WinKeyHook(Action onLoneTap)
    {
        _onLoneTap = onLoneTap;
        _proc = HookProc; // keep the delegate alive while the hook exists
    }

    public bool IsInstalled => _thread != null;

    public void Install()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
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
        { IsBackground = true, Name = "FullscreenStart.WinKeyHook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait();

        if (error != null)
        {
            _thread = null;
            throw error;
        }
        Log.Info("Windows key hook installed");
    }

    public void Uninstall()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
        Log.Info("Windows key hook removed");
    }

    /// <summary>Forget held keys (call after unlocking the session: Win+L swallows the Win key-up).</summary>
    public void RequestReset() => _resetRequested = true;

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != OwnInputMarker)
            {
                if (_resetRequested)
                {
                    _resetRequested = false;
                    _filter.Reset();
                }

                int msg = wParam.ToInt32();
                bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
                if ((isDown || isUp) && _filter.Process((int)info.vkCode, isDown) == WinKeyDecision.OpenMenu)
                {
                    // Swallow the real key-up and replay it after a masking key, all in one SendInput.
                    SendMaskedWinUp((ushort)info.vkCode);
                    _onLoneTap(); // must not block: the caller posts to the UI thread
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static void SendMaskedWinUp(ushort winVk)
    {
        var inputs = new[]
        {
            KeyInput(MaskKey, 0),
            KeyInput(MaskKey, KEYEVENTF_KEYUP),
            KeyInput(winVk, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY),
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Log.Error($"SendInput for the Windows key mask failed: {Marshal.GetLastWin32Error()}");
    }

    private static INPUT KeyInput(ushort vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = OwnInputMarker } },
    };

    public void Dispose() => Uninstall();
}
