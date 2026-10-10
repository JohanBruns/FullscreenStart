using FullscreenStart.Services;

namespace FullscreenStart.Tests;

public class WinKeyFilterTests
{
    private const int LWin = WinKeyFilter.VK_LWIN, RWin = WinKeyFilter.VK_RWIN;
    private const int Shift = 0xA0, Ctrl = 0xA2, E = 0x45, S = 0x53, D = 0x44;

    private static WinKeyDecision Tap(WinKeyFilter f, int vk)
    {
        f.Process(vk, true);
        return f.Process(vk, false);
    }

    [Fact]
    public void LoneWinTapOpensMenu()
    {
        var f = new WinKeyFilter();
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(LWin, true));
        Assert.Equal(WinKeyDecision.OpenMenu, f.Process(LWin, false));
    }

    [Fact]
    public void RightWinWorksToo() => Assert.Equal(WinKeyDecision.OpenMenu, Tap(new WinKeyFilter(), RWin));

    [Fact]
    public void AutoRepeatStillCountsAsLoneTap()
    {
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        f.Process(LWin, true);
        f.Process(LWin, true);
        Assert.Equal(WinKeyDecision.OpenMenu, f.Process(LWin, false));
    }

    [Theory]
    [InlineData(E)]
    [InlineData(D)]
    public void WinPlusKeyIsLeftToWindows(int key)
    {
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(key, true));
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(key, false));
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(LWin, false));
    }

    [Fact]
    public void WinShiftSIsLeftToWindows()
    {
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        f.Process(Shift, true);
        f.Process(S, true);
        f.Process(S, false);
        f.Process(Shift, false);
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(LWin, false));
    }

    [Fact]
    public void ModifierHeldBeforeWinMakesItAShortcut()
    {
        var f = new WinKeyFilter();
        f.Process(Ctrl, true);
        f.Process(LWin, true);
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(LWin, false));
        f.Process(Ctrl, false);

        // The next lone tap works again.
        Assert.Equal(WinKeyDecision.OpenMenu, Tap(f, LWin));
    }

    [Fact]
    public void ComboStateDoesNotLeakIntoNextTap()
    {
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        Tap(f, E);
        f.Process(LWin, false);

        Assert.Equal(WinKeyDecision.OpenMenu, Tap(f, LWin));
    }

    [Fact]
    public void BothWinKeysTogetherAreNotALoneTap()
    {
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        f.Process(RWin, true);
        f.Process(RWin, false);
        Assert.Equal(WinKeyDecision.PassThrough, f.Process(LWin, false));
    }

    [Fact]
    public void ResetClearsAStuckWinKey()
    {
        // Win+L locks the session; the Win key-up never reaches the hook.
        var f = new WinKeyFilter();
        f.Process(LWin, true);
        f.Process(0x4C, true);
        f.Reset();

        Assert.Equal(WinKeyDecision.OpenMenu, Tap(f, LWin));
    }

    [Fact]
    public void SendInputStructHasTheSizeWindowsExpects()
    {
        // SendInput silently ignores input whose cbSize does not match sizeof(INPUT).
        int expected = IntPtr.Size == 8 ? 40 : 28;
        Assert.Equal(expected, System.Runtime.InteropServices.Marshal.SizeOf<FullscreenStart.Interop.NativeMethods.INPUT>());
    }

    [Fact]
    public void StrayWinUpWithoutDownDoesNothing()
    {
        Assert.Equal(WinKeyDecision.PassThrough, new WinKeyFilter().Process(LWin, false));
    }
}
