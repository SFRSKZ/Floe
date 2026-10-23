using System.Diagnostics;
using System.Windows.Media;
using Floe.App.Effects.Derezz;

namespace Floe.App.Windows;

/// <summary>
/// One render loop drives the dock's derezz into an overlay layer.
/// The layer shares Root's scale so fields work in Root's own coordinates.
/// </summary>
public partial class DockWindow
{
    private const double TargetFrame = 1 / 60.0;

    private readonly DerezzLayer _derezzLayer = new();
    private readonly Stopwatch _derezzClock = new();
    private readonly List<(DerezzField, double)> _derezzRuns = new();
    private double _derezzLast;
    private bool _derezzRendering;
    // Monitor frames, from Rendering's timestamps: the loop draws on every n-th one.
    private double _derezzStamp;
    private double _derezzTickTime = TargetFrame;
    private int _derezzTicks;
    // FLOE_PERF: frames, work and worst gap of one derezz run
    private int _derezzPerfFrames;
    private double _derezzPerfWork;
    private double _derezzPerfMax;
    private double _derezzPerfGap;

    private void InitializeDerezz()
    {
        _derezzLayer.RenderTransformOrigin = Root.RenderTransformOrigin;
        _derezzLayer.RenderTransform = _rootScale;
        Stage.Children.Add(_derezzLayer);
    }

    private void StartDerezzLoop()
    {
        if (_derezzRendering) return;
        _derezzRendering = true;
        // The visualizer shares the render thread; it steps down to its slow pace while the dock animates.
        Effects.Visualizer.VisualizerPacing.DockAnimating = true;
        _derezzClock.Restart();
        _derezzLast = -1;
        _derezzStamp = -1;
        _derezzTicks = 0;
        CompositionTarget.Rendering += RenderDerezz;
    }

    private void StopDerezzLoop()
    {
        if (_derezzRendering) CompositionTarget.Rendering -= RenderDerezz;
        if (_derezzRendering && PerfLog && _derezzPerfFrames > 0)
            Log($"perf derezz frames={_derezzPerfFrames} secs={_derezzClock.Elapsed.TotalSeconds:F2} avg={_derezzPerfWork / _derezzPerfFrames:F2}ms max={_derezzPerfMax:F2}ms maxGap={_derezzPerfGap:F1}ms cell={DozeCell}");
        _derezzPerfFrames = 0;
        _derezzPerfWork = _derezzPerfMax = _derezzPerfGap = 0;
        _derezzRendering = false;
        Effects.Visualizer.VisualizerPacing.DockAnimating = _renderingHooked;
        _derezzClock.Stop();
        _derezzLayer.Clear();
    }

    private void RenderDerezz(object? sender, EventArgs e)
    {
        double now = _derezzClock.Elapsed.TotalSeconds;
        // Rendering fires once or more per monitor frame, 180 a second on a fast monitor. The cubes draw on every n-th
        // frame, n picked to land near 60 fps: an even cadence, where a time threshold would judder between 3 and 4
        // frames. A frame that changes nothing costs the layered window nothing.
        double stamp = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : now;
        if (stamp <= _derezzStamp) return;
        if (_derezzStamp >= 0 && stamp - _derezzStamp < .05)
        {
            _derezzTickTime += (stamp - _derezzStamp - _derezzTickTime) * .1;
        }

        _derezzStamp = stamp;
        int every = Math.Max(1, (int)Math.Round(TargetFrame / _derezzTickTime));
        if (_derezzLast >= 0 && ++_derezzTicks < every) return;
        _derezzTicks = 0;
        if (_derezzLast >= 0) _derezzPerfGap = Math.Max(_derezzPerfGap, (stamp - _derezzLast) * 1000);
        double dt = _derezzLast < 0 ? 0 : Math.Min(.05, stamp - _derezzLast);
        long started = Stopwatch.GetTimestamp();
        _derezzLast = stamp;
        _derezzRuns.Clear();
        if (!AdvanceDoze(dt, _derezzRuns))
        {
            StopDerezzLoop();
            return;
        }

        _derezzLayer.Draw(_derezzRuns);
        double work = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _derezzPerfFrames++;
        _derezzPerfWork += work;
        _derezzPerfMax = Math.Max(_derezzPerfMax, work);
    }
}
