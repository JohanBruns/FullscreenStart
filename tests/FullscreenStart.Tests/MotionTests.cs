using FullscreenStart.Services;

namespace FullscreenStart.Tests;

public class MotionTests
{
    [Fact]
    public void BezierHitsEndpointsAndIsMonotonic()
    {
        foreach (var curve in new[] { CubicBezier.Decelerate, CubicBezier.Accelerate })
        {
            Assert.Equal(0, curve.Ease(0));
            Assert.Equal(1, curve.Ease(1));
            double previous = 0;
            for (int i = 1; i <= 100; i++)
            {
                double v = curve.Ease(i / 100.0);
                Assert.True(v >= previous - 1e-9, $"not monotonic at {i}");
                previous = v;
            }
        }
    }

    [Fact]
    public void LinearBezierIsIdentity()
    {
        var linear = new CubicBezier(0.25, 0.25, 0.75, 0.75);
        Assert.Equal(0.3, linear.Ease(0.3), 4);
    }

    [Fact]
    public void DecelerateIsFastAtTheStart() => Assert.True(CubicBezier.Decelerate.Ease(0.25) > 0.6);

    [Fact]
    public void AccelerateIsSlowAtTheStart() => Assert.True(CubicBezier.Accelerate.Ease(0.25) < 0.1);

    [Fact]
    public void OpeningStartsHiddenAndEndsInPlace()
    {
        var start = MenuMotion.Opening(0);
        Assert.Equal(0, start.WindowOpacity);
        Assert.Equal(MenuMotion.OpenDistance, start.ContentOffsetY);

        var end = MenuMotion.Opening(1);
        Assert.Equal(new MenuFrame(1, 1, 0), end);

        // The background is fully there well before the tiles have landed.
        Assert.Equal(1, MenuMotion.Opening(0.5).WindowOpacity);
        Assert.True(MenuMotion.Opening(0.5).ContentOffsetY > 0);
    }

    [Fact]
    public void ClosingEndsInvisible()
    {
        Assert.Equal(new MenuFrame(1, 1, 0), MenuMotion.Closing(0));
        var end = MenuMotion.Closing(1);
        Assert.Equal(0, end.WindowOpacity);
        Assert.Equal(0, end.ContentOpacity);
    }

    [Fact]
    public void ReversingACloseContinuesFromTheCurrentOpacity()
    {
        double opacity = MenuMotion.Closing(0.6).WindowOpacity;
        double p = MenuMotion.OpeningProgressFor(opacity);
        Assert.Equal(opacity, MenuMotion.Opening(p).WindowOpacity, 2);
    }
}
