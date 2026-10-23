using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.App.Services;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Frame clock for ambient animations that share the render thread with the visualizer.
/// Every repaint re-presents a whole layered window, so while the visualizer plays the clock
/// ticks on the visualizer's own frames instead of filling the frames in between; otherwise at
/// <see cref="Performance.AmbientFps"/>.
/// <para>
/// It only holds <see cref="CompositionTarget.Rendering"/> while the dock animates, because the
/// dock repaints every frame then anyway. Idle it wakes a background <see cref="DispatcherTimer"/>
/// instead, or nothing at all when <see cref="Performance.AmbientFps"/> is 0, so the render thread
/// sleeps while the dock sits still and the content holds its last frame.
/// </para>
/// </summary>
public sealed class PacedClock
{
    // Two ticks this close together belong to the same animation frame: a
    // duplicate would only repaint the same content twice.
    private const double SameFrameSeconds = 0.002;

    // While a visualizer repaints it drives the clock; a tick in between would
    // only repaint the same content.
    private const double VisualizerQuietSeconds = 0.25;

    private readonly Action<double> _tick;
    private readonly int _divider;
    private readonly bool _everyFrameWhileDockAnimates;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _timer;
    private bool _frameHooked;
    private bool _running;
    private double _lastTick = double.NegativeInfinity;
    private double _lastVisualizerRepaint = double.NegativeInfinity;
    private double _lastRendering = double.NegativeInfinity;
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

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Running => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        VisualizerPacing.Repainted += OnVisualizerRepainted;
        VisualizerPacing.DockAnimatingChanged += Sync;
        Performance.Changed += Sync;
        Sync();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        VisualizerPacing.Repainted -= OnVisualizerRepainted;
        VisualizerPacing.DockAnimatingChanged -= Sync;
        Performance.Changed -= Sync;
        UseFrames(false);
        UseTimer(false);
    }

    /// <summary>
    /// Picks the tick source: the dock's own frames while it animates, a background timer while it
    /// rests, and nothing when the level holds idle decoration still.
    /// </summary>
    private void Sync()
    {
        bool animate = VisualizerPacing.DockAnimating && Performance.AmbientMotion;
        UseFrames(animate);
        // AmbientFps 0 means no idle ticks at all: the content keeps its last frame.
        UseTimer(!animate && Performance.AmbientFps > 0);
    }

    private void UseFrames(bool on)
    {
        if (on == _frameHooked)
        {
            return;
        }

        if (on)
        {
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }

        _frameHooked = on;
    }

    private void UseTimer(bool on)
    {
        if (!on)
        {
            _timer?.Stop();
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += OnTimerTick;
        }

        // a level change keeps the timer, only its pace moves
        var interval = TimeSpan.FromSeconds(1 / Performance.AmbientFps);
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
        }

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => AmbientTick(_clock.Elapsed.TotalSeconds);

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can be raised more than once for one frame; the timestamp
        // identifies the frame.
        double frame = e is RenderingEventArgs args
            ? args.RenderingTime.TotalSeconds
            : _clock.Elapsed.TotalSeconds;
        if (frame <= _lastRendering)
        {
            return;
        }

        _lastRendering = frame;

        var now = _clock.Elapsed.TotalSeconds;
        if (_everyFrameWhileDockAnimates && VisualizerPacing.DockAnimating)
        {
            Tick(now);
        }
        else
        {
            AmbientTick(now);
        }
    }

    /// <summary>Idle pace: <see cref="Performance.AmbientFps"/>, skipped while a visualizer repaints.</summary>
    private void AmbientTick(double now)
    {
        double fps = Performance.AmbientFps;
        if (!_running || fps <= 0 || now - _lastVisualizerRepaint <= VisualizerQuietSeconds || now - _lastTick < 1 / fps)
        {
            return;
        }

        Tick(now);
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
        // Idle decoration holds its last frame when the level cannot afford motion, whatever woke us.
        if (!Performance.AmbientMotion)
        {
            return;
        }

        // Repainted is raised from inside a visualizer's Rendering handler, so a
        // frame can reach Tick from both sources; keep only the first.
        if (now - _lastTick < SameFrameSeconds)
        {
            return;
        }

        _lastTick = now;
        _tick(now);
    }
}
