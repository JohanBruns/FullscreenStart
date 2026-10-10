namespace FullscreenStart.Services;

public enum WinKeyDecision
{
    /// <summary>Let the key event through unchanged.</summary>
    PassThrough,

    /// <summary>
    /// A lone Windows key press ended: swallow this key-up, inject a masking key plus the key-up, and
    /// open our menu. Windows only opens its own Start menu if nothing happened between Win down and up.
    /// </summary>
    OpenMenu,
}

/// <summary>
/// Decides, key event by key event, whether a Windows key press was a lone tap (open our menu) or part
/// of a shortcut such as Win+E or Win+Shift+S (leave it to Windows). Pure logic, no Win32 calls.
/// </summary>
public sealed class WinKeyFilter
{
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    private readonly HashSet<int> _keysDown = new();
    private int _winKey;       // the Windows key currently held, 0 if none
    private bool _usedInCombo; // another key was involved while it was held

    public static bool IsWinKey(int vk) => vk is VK_LWIN or VK_RWIN;

    /// <param name="vk">Virtual-key code.</param>
    /// <param name="isDown">Key-down (true) or key-up (false).</param>
    public WinKeyDecision Process(int vk, bool isDown)
    {
        if (IsWinKey(vk) && (_winKey == 0 || _winKey == vk))
        {
            if (isDown)
            {
                if (_winKey == 0)
                {
                    _winKey = vk;
                    // Win pressed while e.g. Shift or Ctrl is held: a shortcut, not a lone tap.
                    _usedInCombo = _keysDown.Count > 0;
                }
                // Auto-repeat while held: nothing changes.
                return WinKeyDecision.PassThrough;
            }

            bool lone = _winKey != 0 && !_usedInCombo;
            _winKey = 0;
            _usedInCombo = false;
            return lone ? WinKeyDecision.OpenMenu : WinKeyDecision.PassThrough;
        }

        // Any other key (including the second Windows key).
        if (isDown)
        {
            _keysDown.Add(vk);
            if (_winKey != 0) _usedInCombo = true;
        }
        else
        {
            _keysDown.Remove(vk);
        }
        return WinKeyDecision.PassThrough;
    }

    /// <summary>Forget held keys, e.g. after the hook was re-installed or the session was locked.</summary>
    public void Reset()
    {
        _keysDown.Clear();
        _winKey = 0;
        _usedInCombo = false;
    }
}
