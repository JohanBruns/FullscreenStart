using System.Windows.Input;
using System.Windows.Interop;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Services;

/// <summary>
/// Registers one system-wide hotkey on an existing window (the hidden menu window). A separate helper
/// window would be picked up by Windows as the next foreground window whenever the menu hides.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x5354; // "ST"
    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyManager(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd) ?? throw new ArgumentException("Not a WPF window handle", nameof(hwnd));
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>Registers e.g. "Ctrl+Shift+Space". Returns false if the combination is taken.</summary>
    public bool Register(string gesture)
    {
        Unregister();
        if (!TryParse(gesture, out var modifiers, out var vk)) return false;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    internal static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "strg" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift" or "umschalt": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                default:
                    var name = part.Equals("Leertaste", StringComparison.OrdinalIgnoreCase) ? "Space" : part;
                    if (!Enum.TryParse<Key>(name, ignoreCase: true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }
}
