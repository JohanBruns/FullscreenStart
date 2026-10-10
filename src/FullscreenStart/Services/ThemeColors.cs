using System.Windows.Media;
using Microsoft.Win32;

namespace FullscreenStart.Services;

public static class ThemeColors
{
    /// <summary>The Windows accent colour (Settings › Personalisation › Colours).</summary>
    public static Color Accent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is int abgr)
        {
            // Stored as 0xAABBGGRR.
            return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }
}
