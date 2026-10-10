using System.Runtime.InteropServices;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Low-level keyboard hook that turns a lone press of the Windows key into "open our menu" while
/// keeping every Win+… shortcut working.
/// </summary>
public sealed class WinKeyHook : IDisposable
{
    /// <summary>Marks input we inject ourselves, so the hook lets it pass untouched.</summary>
    private static readonly IntPtr OwnInputMarker = new(0x46534D4B); // "FSMK"

    /// <summary>An unassigned virtual key: Windows sees "Win, something, Win up" and keeps its Start menu closed.</summary>
    private const ushort MaskKey = 0xE8;

    private readonly Action _onLoneTap;
    private readonly WinKeyFilter _filter = new();
    private readonly LowLevelHookProc _proc;
    private readonly HookThread _thread;
    private volatile bool _resetRequested;

    public WinKeyHook(Action onLoneTap)
    {
        _onLoneTap = onLoneTap;
        _proc = HookProc; // keep the delegate alive while the hook exists
        _thread = new HookThread(WH_KEYBOARD_LL, _proc, "FullscreenStart.WinKeyHook");
    }

    public bool IsInstalled => _thread.IsInstalled;

    public void Install()
    {
        if (IsInstalled) return;
        _thread.Install();
        Log.Info("Windows key hook installed");
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        _thread.Uninstall();
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
