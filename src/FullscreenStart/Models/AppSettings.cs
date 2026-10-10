namespace FullscreenStart.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Global hotkey, e.g. "Ctrl+Shift+Space".</summary>
    public string Hotkey { get; set; } = "Ctrl+Shift+Space";

    /// <summary>Show the blurred desktop wallpaper behind the menu (falls back to system acrylic).</summary>
    public bool WallpaperBackdrop { get; set; } = true;

    /// <summary>Strength of the tint laid over the backdrop, 0..1.</summary>
    public double BackdropTintOpacity { get; set; } = 0.66;

    /// <summary>Blur radius in screen pixels.</summary>
    public int BackdropBlurRadius { get; set; } = 48;

    /// <summary>Open in the "Alle Apps" view instead of the pinned tiles.</summary>
    public bool StartInAllApps { get; set; }
}
