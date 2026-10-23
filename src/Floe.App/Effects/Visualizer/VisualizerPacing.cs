using Floe.App.Services;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Repaint rate for the visualizers. Every repaint re-presents the full-width visualizer strip,
/// and all windows share one render thread, so the light slows down while the dock animates.
/// </summary>
public static class VisualizerPacing
{
    private static bool _dockAnimating;

    public static bool DockAnimating
    {
        get => _dockAnimating;
        set
        {
            if (_dockAnimating == value)
            {
                return;
            }

            _dockAnimating = value;
            DockAnimatingChanged?.Invoke();
        }
    }

    /// <summary>Never faster than <see cref="Performance.VisualizerFps"/> when that is set.</summary>
    public static double RepaintInterval
    {
        get
        {
            double interval = DockAnimating ? 1.0 / 15 : 1.0 / 30;
            double fps = Performance.VisualizerFps;
            return fps > 0 ? Math.Max(interval, 1 / fps) : interval;
        }
    }

    /// <summary>Raised when <see cref="DockAnimating"/> changes, so frame clocks can switch tick sources.</summary>
    public static event Action? DockAnimatingChanged;

    /// <summary>Raised on the frames a visualizer repaints, so other animated content can update in step.</summary>
    public static event Action? Repainted;

    public static void MarkRepaint() => Repainted?.Invoke();
}
