namespace FullscreenStart.Services;

/// <summary>CSS-style cubic Bézier timing curve from (0,0) to (1,1).</summary>
public readonly struct CubicBezier(double x1, double y1, double x2, double y2)
{
    /// <summary>Fluent "decelerate": fast start, long soft landing (entrances).</summary>
    public static readonly CubicBezier Decelerate = new(0.1, 0.9, 0.2, 1.0);

    /// <summary>Fluent "accelerate": gentle start, quick exit (dismissals).</summary>
    public static readonly CubicBezier Accelerate = new(0.7, 0.0, 1.0, 0.5);

    /// <summary>Eased value for linear progress <paramref name="t"/> in [0, 1].</summary>
    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;

        // Find the curve parameter s with X(s) = t (Newton, falling back to bisection), then return Y(s).
        double s = t;
        for (int i = 0; i < 8; i++)
        {
            double x = Sample(x1, x2, s) - t;
            if (Math.Abs(x) < 1e-6) return Sample(y1, y2, s);
            double dx = Derivative(x1, x2, s);
            if (Math.Abs(dx) < 1e-6) break;
            s -= x / dx;
        }

        double lo = 0, hi = 1;
        s = t;
        for (int i = 0; i < 30; i++)
        {
            double x = Sample(x1, x2, s);
            if (Math.Abs(x - t) < 1e-6) break;
            if (x < t) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Sample(y1, y2, s);
    }

    private static double Sample(double p1, double p2, double s) =>
        ((1 - 3 * p2 + 3 * p1) * s + (3 * p2 - 6 * p1)) * s * s + 3 * p1 * s;

    private static double Derivative(double p1, double p2, double s) =>
        3 * (1 - 3 * p2 + 3 * p1) * s * s + 2 * (3 * p2 - 6 * p1) * s + 3 * p1;
}

/// <summary>What the menu looks like at a point of its open/close animation.</summary>
public readonly record struct MenuFrame(double WindowOpacity, double ContentOpacity, double ContentOffsetY);

/// <summary>
/// The open and close motion, modelled on the Windows 10 start screen: the background fades in quickly
/// while the tiles glide up into place; closing is shorter and drops them slightly while fading out.
/// </summary>
public static class MenuMotion
{
    public const double OpenMilliseconds = 330;
    public const double CloseMilliseconds = 180;
    public const double OpenDistance = 56;
    public const double CloseDistance = 28;

    public static MenuFrame Opening(double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        return new MenuFrame(
            WindowOpacity: CubicBezier.Decelerate.Ease(Math.Min(1, p / 0.45)),
            ContentOpacity: CubicBezier.Decelerate.Ease(Math.Min(1, p / 0.6)),
            ContentOffsetY: OpenDistance * (1 - CubicBezier.Decelerate.Ease(p)));
    }

    public static MenuFrame Closing(double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        double eased = CubicBezier.Accelerate.Ease(p);
        return new MenuFrame(
            WindowOpacity: 1 - eased,
            ContentOpacity: 1 - CubicBezier.Accelerate.Ease(Math.Min(1, p / 0.8)),
            ContentOffsetY: CloseDistance * eased);
    }

    /// <summary>Opening progress at which the window is as opaque as <paramref name="windowOpacity"/> (to reverse a close smoothly).</summary>
    public static double OpeningProgressFor(double windowOpacity)
    {
        double lo = 0, hi = 0.45;
        for (int i = 0; i < 30; i++)
        {
            double mid = (lo + hi) / 2;
            if (Opening(mid).WindowOpacity < windowOpacity) lo = mid; else hi = mid;
        }
        return lo;
    }
}
