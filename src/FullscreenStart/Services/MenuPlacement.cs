namespace FullscreenStart.Services;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public enum ScreenEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>Computes where the menu window goes so that the taskbar stays reachable.</summary>
public static class MenuPlacement
{
    /// <summary>
    /// A hidden auto-hide taskbar keeps a 2 px sliver on screen; hovering it slides the taskbar in.
    /// Leaving exactly that sliver free keeps the taskbar reachable without showing a strip of desktop,
    /// and the menu is not treated as a fullscreen app (which would block the taskbar).
    /// </summary>
    public const int AutoHideSliver = 2;

    /// <param name="monitor">Full monitor bounds in physical pixels.</param>
    /// <param name="workArea">Monitor work area (already excludes a docked, always-visible taskbar).</param>
    /// <param name="taskbar">Taskbar rectangle as reported by the shell, if known.</param>
    /// <param name="edge">Screen edge the taskbar is docked to.</param>
    /// <param name="autoHide">Whether the taskbar auto-hides (then the work area covers the whole monitor).</param>
    public static PixelRect Compute(PixelRect monitor, PixelRect workArea, PixelRect? taskbar, ScreenEdge edge, bool autoHide)
    {
        if (!autoHide || taskbar is not { } tb) return workArea;

        bool horizontal = edge is ScreenEdge.Top or ScreenEdge.Bottom;
        bool onThisMonitor = horizontal
            ? tb.Left < monitor.Right && tb.Right > monitor.Left
            : tb.Top < monitor.Bottom && tb.Bottom > monitor.Top;
        if (!onThisMonitor) return workArea;

        int thickness = AutoHideSliver;

        return edge switch
        {
            ScreenEdge.Bottom => monitor with { Bottom = monitor.Bottom - thickness },
            ScreenEdge.Top => monitor with { Top = monitor.Top + thickness },
            ScreenEdge.Left => monitor with { Left = monitor.Left + thickness },
            ScreenEdge.Right => monitor with { Right = monitor.Right - thickness },
            _ => workArea,
        };
    }
}
