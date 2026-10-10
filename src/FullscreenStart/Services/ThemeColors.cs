using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FullscreenStart.Services;

/// <summary>Light/dark palette and accent colour, following Windows unless overridden in settings.</summary>
public static class ThemeColors
{
    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

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

    /// <param name="setting">"light", "dark" or "system" (anything else counts as system).</param>
    public static bool UseLightTheme(string? setting) => setting?.ToLowerInvariant() switch
    {
        "light" or "hell" => true,
        "dark" or "dunkel" => false,
        // Start and taskbar follow the "Windows mode" (not the app mode) in Windows settings.
        _ => ReadDword("SystemUsesLightTheme") == 1,
    };

    /// <summary>Swaps the palette dictionary and the accent brush in the application resources.</summary>
    public static void Apply(ResourceDictionary resources, bool light)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{(light ? "Light" : "Dark")}.xaml"),
        };
        var merged = resources.MergedDictionaries;
        int index = merged.ToList().FindIndex(d => d.Source?.OriginalString.Contains("/Colors.") == true);
        if (index >= 0) merged[index] = palette;
        else merged.Insert(0, palette);

        resources["AccentBrush"] = new SolidColorBrush(Accent());
    }

    private static int? ReadDword(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizePath);
        return key?.GetValue(name) as int?;
    }
}
