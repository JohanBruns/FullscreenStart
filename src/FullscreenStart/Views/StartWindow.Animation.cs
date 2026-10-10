using System.Windows.Media;
using FullscreenStart.Services;
using static FullscreenStart.Interop.NativeMethods;

namespace FullscreenStart.Views;

/// <summary>
/// Open/close motion. One per-frame driver (CompositionTarget.Rendering) sets the window opacity
/// (layered-window alpha, composed by DWM, so the whole window including the backdrop fades) and the
/// position/opacity of the content. Progress advances by rendered frames, capped per frame, so a slow
/// first frame cannot make the animation skip ahead.
/// </summary>
public partial class StartWindow
{
    /// <summary>Longest step a single frame may advance the animation (about two frames at 60 Hz).</summary>
    private const double MaxFrameStepMilliseconds = 34;

    private double _windowOpacity = 1;
    private double _animationElapsed;
    private double _animationStart;
    private TimeSpan? _lastFrameTime;
    private bool _animating;
    private int _frameCount;
    private double _maxFrameStep;
    private int _animationCount;

    private void StartAnimation(MenuState state, double startProgress)
    {
        _state = state;
        _animationStart = Math.Clamp(startProgress, 0, 1);
        _animationElapsed = 0;
        _lastFrameTime = null;
        _frameCount = 0;
        _maxFrameStep = 0;
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += OnAnimationFrame;
        }
    }

    private void OnAnimationFrame(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only advance when the frame time moves.
        var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        if (_lastFrameTime is { } last)
        {
            double step = (now - last).TotalMilliseconds;
            if (step <= 0) return;
            _animationElapsed += Math.Min(step, MaxFrameStepMilliseconds);
            _frameCount++;
            _maxFrameStep = Math.Max(_maxFrameStep, step);
        }
        _lastFrameTime = now;

        bool opening = _state == MenuState.Opening;
        double duration = opening ? MenuMotion.OpenMilliseconds : MenuMotion.CloseMilliseconds;
        double progress = Math.Min(1, _animationStart + _animationElapsed / duration);
        ApplyFrame(opening ? MenuMotion.Opening(progress) : MenuMotion.Closing(progress));
        if (progress < 1) return;

        CompositionTarget.Rendering -= OnAnimationFrame;
        _animating = false;
        // Smoothness check: report the first few animations and any with a visible hitch.
        if (++_animationCount <= 4 || _maxFrameStep > 40)
            Log.Info($"{(opening ? "Open" : "Close")} animation: {_frameCount} frames, longest frame {_maxFrameStep:F0} ms");
        if (opening)
        {
            _state = MenuState.Open;
        }
        else
        {
            _state = MenuState.Hidden;
            OnHidden();
        }
    }

    private void ApplyFrame(MenuFrame frame)
    {
        SetWindowOpacity(frame.WindowOpacity);
        ContentRoot.Opacity = frame.ContentOpacity;
        ((TranslateTransform)ContentRoot.RenderTransform).Y = frame.ContentOffsetY;
    }

    private void SetWindowOpacity(double opacity)
    {
        _windowOpacity = Math.Clamp(opacity, 0, 1);
        if (_hwnd != IntPtr.Zero)
            SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(_windowOpacity * 255), LWA_ALPHA);
    }
}
