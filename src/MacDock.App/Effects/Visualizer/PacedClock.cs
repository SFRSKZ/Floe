using System.Diagnostics;
using System.Windows.Media;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// Frame clock for ambient animations that share the render thread with the visualizer.
/// Every repaint re-presents a whole layered window, so while the visualizer plays the clock
/// ticks on the visualizer's own frames instead of filling the frames in between; otherwise at 30 fps.
/// </summary>
public sealed class PacedClock
{
    private readonly Action<double> _tick;
    private readonly int _divider;
    private readonly bool _everyFrameWhileDockAnimates;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTick = double.NegativeInfinity;
    private double _lastVisualizerRepaint = double.NegativeInfinity;
    private int _repaints;

    /// <param name="tick">Called with seconds since the clock was made.</param>
    /// <param name="divider">Tick on every n-th visualizer repaint.</param>
    /// <param name="everyFrameWhileDockAnimates">For content inside the dock window, which repaints every frame anyway then.</param>
    public PacedClock(Action<double> tick, int divider, bool everyFrameWhileDockAnimates)
    {
        _tick = tick;
        _divider = Math.Max(1, divider);
        _everyFrameWhileDockAnimates = everyFrameWhileDockAnimates;
    }

    public bool Running { get; private set; }

    public void Start()
    {
        if (Running)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        VisualizerPacing.Repainted += OnVisualizerRepainted;
        Running = true;
    }

    public void Stop()
    {
        if (!Running)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        VisualizerPacing.Repainted -= OnVisualizerRepainted;
        Running = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (_everyFrameWhileDockAnimates && VisualizerPacing.DockAnimating)
        {
            Tick(now);
        }
        else if (now - _lastVisualizerRepaint > 0.25 && now - _lastTick >= 1.0 / 30)
        {
            Tick(now);
        }
    }

    private void OnVisualizerRepainted()
    {
        _lastVisualizerRepaint = _clock.Elapsed.TotalSeconds;
        if (++_repaints % _divider == 0 && !(_everyFrameWhileDockAnimates && VisualizerPacing.DockAnimating))
        {
            Tick(_lastVisualizerRepaint);
        }
    }

    private void Tick(double now)
    {
        _lastTick = now;
        _tick(now);
    }
}
