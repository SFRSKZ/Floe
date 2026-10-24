using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{

    private void EnsureAnimation()
    {
        if (!_perfEvents)
        {
            _perfEvents = true;
            Performance.Changed += OnPerformanceChanged;
            Closed += (_, _) => Performance.Changed -= OnPerformanceChanged;
        }
        if (!_renderingHooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
            Effects.Visualizer.VisualizerPacing.DockAnimating = true;
            _lastTick = Stopwatch.GetTimestamp();
            // the gap up to this first frame is idle time, not a late frame
            _renderDrawn = false;
            _renderStamp = double.NaN;
            _renderTicks = 0;
        }
    }

    private void OnPerformanceChanged()
    {
        // the cap may have come or gone: restart the frame counter and skip the gap across the change
        _renderDrawn = false;
        _renderStamp = double.NaN;
        _renderTicks = 0;
        ApplyShadowLevel();
    }

    private void StopAnimation()
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            Effects.Visualizer.VisualizerPacing.DockAnimating = _derezzRendering;
        }
    }

    private static readonly bool PerfLog = Environment.GetEnvironmentVariable("FLOE_PERF") is not null;
    // the loose cubes scatter on the dock's own frame time
    private double _frameDt = 1 / 60.0;
    private readonly List<TronCubes.Column> _tronColumns = new();
    private int _perfFrames;
    private double _perfWork;
    private double _perfMaxWork;
    private double _perfMaxGap;
    private long _perfStart;
    internal static int PerfBackdrops;
    internal static int PerfMoves;
    internal static double PerfMoveMs;
    private TimeSpan _perfCpu;
    private double _perfBusy;
    private long _perfOpStart;
    private bool _perfHooked;
    // Cap: Rendering can fire twice per monitor frame, so frames are counted by distinct timestamps.
    private double _renderStamp = double.NaN;
    private double _renderTickTime = 1 / 60.0;
    private int _renderTicks;
    private bool _renderDrawn;
    private bool _perfEvents;

    private void OnRendering(object? sender, EventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        // frames the cap skips are not counted as drawn
        if (OnRenderingCore(e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : -1) && PerfLog)
        {
            if (!_perfHooked)
            {
                _perfHooked = true;
                Log($"perf tier={RenderCapability.Tier >> 16} window={_windowWidth:F0}x{WindowHeight:F0}dip dpi={VisualTreeHelper.GetDpi(this).DpiScaleX} mode={(PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.CompositionTarget.RenderMode} nofx={NoFx}");
                Dispatcher.Hooks.OperationStarted += (_, _) => _perfOpStart = Stopwatch.GetTimestamp();
                Dispatcher.Hooks.OperationCompleted += (_, e) =>
                {
                    if (_perfOpStart == 0) return;
                    var ms = (Stopwatch.GetTimestamp() - _perfOpStart) * 1000.0 / Stopwatch.Frequency;
                    _perfBusy += ms;
                    if (ms > 15)
                    {
                        var method = typeof(System.Windows.Threading.DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(e.Operation) as Delegate;
                        Log($"perf slowop {ms:F0}ms {e.Operation.Priority} {method?.Method.DeclaringType?.Name}.{method?.Method.Name}");
                    }
                };
            }
            var work = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _perfFrames++;
            _perfWork += work;
            _perfMaxWork = Math.Max(_perfMaxWork, work);
            if (_perfStart == 0) _perfStart = started;
            var elapsed = (started - _perfStart) / (double)Stopwatch.Frequency;
            if (elapsed >= 1)
            {
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                Log($"perf cpu={(cpu - _perfCpu).TotalMilliseconds / elapsed:F0}ms/s uiOps={_perfBusy / elapsed:F0}ms/s moves={PerfMoves} moveMs={PerfMoveMs:F1}");
                _perfCpu = cpu; _perfBusy = 0; PerfMoves = 0; PerfMoveMs = 0;
                Log($"perf frames={_perfFrames} avg={_perfWork / _perfFrames:F2}ms max={_perfMaxWork:F2}ms maxGap={_perfMaxGap:F1}ms backdrops={PerfBackdrops} bulge={_currentBulge:F1} scale={_dockScale:F3}");
                _perfFrames = 0; _perfWork = 0; _perfMaxWork = 0; _perfMaxGap = 0; _perfStart = started; PerfBackdrops = 0;
            }
        }
    }

    /// <returns>False on a monitor frame the cap skips.</returns>
    private bool OnRenderingCore(double stamp)
    {
        var cap = Performance.DockFrameCap;
        if (cap > 0 && cap < Performance.RefreshRate && stamp >= 0)
        {
            // draw on every n-th distinct monitor frame: an even cadence, where a time threshold would judder.
            if (stamp <= _renderStamp) return false;
            // n comes from the frames actually arriving: a loaded render thread delivers fewer than the monitor's rate
            if (!double.IsNaN(_renderStamp) && stamp - _renderStamp < .05) _renderTickTime += (stamp - _renderStamp - _renderTickTime) * .1;
            _renderStamp = stamp;
            var every = Math.Max(1, (int)Math.Round(1 / cap / _renderTickTime));
            if (_renderDrawn && ++_renderTicks < every) return false;
            _renderTicks = 0;
        }

        // the gap is real time since the last frame drawn, so the springs stay dt-robust under the cap
        var now = Stopwatch.GetTimestamp();
        var gap = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        _perfMaxGap = Math.Max(_perfMaxGap, gap * 1000);
        if (_renderDrawn)
        {
            // judged against 60 fps at most: 100 fps on a 180 Hz monitor is smooth, not late
            Performance.ReportFrame(gap, 1 / Math.Min(60, cap > 0 ? Math.Min(cap, Performance.RefreshRate) : Performance.RefreshRate));
        }
        _renderDrawn = true;

        var dt = gap;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        _frameDt = dt;

        if (_metalEffect is not null && !_glassPreview)
        {
            // Lite holds the rim still; the drop flash lives on FlashAge, so it still plays.
            if (Performance.AmbientMotion) _metalTime += dt;
            // the folded reflection drifts at time * 0.025, so 10 Hz is enough while the rim rests;
            // a drop flash or a shape change (UpdateGlassShape) already re-renders, so keep time current then
            if (_dropTime >= 0 || _metalTime - _metalTimeApplied >= 0.1)
            {
                _metalTimeApplied = _metalTime;
                _metalEffect.Time = _metalTime;
            }
            var cursorY = IsMouseOver ? Math.Clamp((Mouse.GetPosition(Root).Y - RestTop) / RestBarHeight, 0, 1) : 0;
            if (Changed(ref _metalCursorY, cursorY)) _metalEffect.CursorY = cursorY;
        }
        _currentHoverX = Spring(_currentHoverX, ref _hoverVel, _targetHoverX, dt);
        _currentBulge = Spring(_currentBulge, ref _bulgeVel, _targetBulge, dt);
        _dockScale = Spring(_dockScale, ref _dockScaleVel, _targetDockScale, dt, 180, 24);
        AnimateSections(dt);
        _currentGlowColor = Lerp(_currentGlowColor, _targetGlowColor, Math.Min(1.0, dt * 9));
        _rootScale.ScaleX = _dockScale;
        _rootScale.ScaleY = _dockScale;

        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        UpdateGlow();

        var settled = _targetBulge == 0
            && _currentBulge < 0.3
            && Math.Abs(_currentHoverX - _targetHoverX) < 0.3
            && Math.Abs(_hoverVel) < 4
            && Math.Abs(_bulgeVel) < 4
            && Math.Abs(_dockScale - _targetDockScale) < 0.002
            && Math.Abs(_dockScaleVel) < 0.02
            && scales.All(s => Math.Abs(s - 1) < 0.0015)
            && SectionsSettled;

        if (settled)
        {
            _currentHoverX = _targetHoverX;
            _currentBulge = 0;
            _hoverVel = 0;
            _bulgeVel = 0;
            _dockScale = _targetDockScale;
            _dockScaleVel = 0;
            _rootScale.ScaleX = _dockScale;
            _rootScale.ScaleY = _dockScale;
            UpdateShape();
            // UpdateShape may skip its rebuild here, but the warp has to go once the bulge is gone
            UpdateRimWarp();
            // a held-still rim must not keep the loop alive by itself; a loose cube still in the air must
            if ((_metalEffect is null || !IsMouseOver || !Performance.AmbientMotion) && !_tronCubes.Busy) StopAnimation();
        }

        return true;
    }

    private static double Spring(double position, ref double velocity, double target, double dt, double stiffness = 260, double damping = 27)
    {
        // Explicit integration blows up once dt * damping nears 2 (a slow 12 fps frame is enough),
        // so step in small slices whatever the frame time was.
        const double maxStep = 1.0 / 240;
        var steps = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
        var step = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var accel = (stiffness * (target - position)) - (damping * velocity);
            velocity += accel * step;
            position += velocity * step;
        }

        return position;
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromArgb(
            (byte)(from.A + ((to.A - from.A) * t)),
            (byte)(from.R + ((to.R - from.R) * t)),
            (byte)(from.G + ((to.G - from.G) * t)),
            (byte)(from.B + ((to.B - from.B) * t)));
    }

    private void UpdateShape()
    {
        // the loose cubes follow the springs every frame, even when the geometry itself is skipped
        UpdateTronCubes(_frameDt);

        // skip rebuilding the geometry when nothing meaningful changed (per-frame cost)
        if (Math.Abs(_shapeHover - _currentHoverX) < 0.25
            && Math.Abs(_shapeBulge - _currentBulge) < 0.25
            && Math.Abs(_shapeLeft - _capsuleLeft) < 0.25
            && Math.Abs(_shapeWidth - _capsuleWidth) < 0.25
            && Math.Abs(_shapeScale - _dockScale) < 0.002
            && _shapeCount == _slots.Count)
        {
            return;
        }

        _shapeHover = _currentHoverX;
        _shapeBulge = _currentBulge;
        _shapeLeft = _capsuleLeft;
        _shapeWidth = _capsuleWidth;
        _shapeScale = _dockScale;
        _shapeCount = _slots.Count;

        var points = BuildShape();
        if (points.Count >= 2)
        {
            _shapeFigure.StartPoint = points[0];
            var segmentPoints = _shapeSegment.Points;
            segmentPoints.Clear();
            for (var i = 1; i < points.Count; i++)
            {
                segmentPoints.Add(points[i]);
            }
        }

        UpdateGlassShape();
        var ratio = _windowWidth > 0 ? Math.Min(1, Math.Max(0, _currentHoverX) / _windowWidth) : 0.5;
        var sheen = (LinearGradientBrush)_sheen.Fill;
        if (!sheen.IsFrozen)
        {
            sheen.StartPoint = new Point(ratio, 0);
            sheen.EndPoint = new Point(ratio, 1);
        }
        if (_glassPreview) Root.UpdateLayout();
    }

    private void UpdateGlow()
    {
        if (_slots.Count == 0 || _currentBulge <= 0.5)
        {
            _glow.Visibility = Visibility.Collapsed;
            return;
        }

        var intensity = Math.Min(1.0, _currentBulge / MaxBulge);
        const double glowWidth = 230;
        var left = _currentHoverX - (glowWidth / 2);

        _glowPairs.Clear();
        _glowPairs.Add((0.0, Colors.Transparent));
        foreach (var slot in _slots)
        {
            var rel = (slot.Center - left) / glowWidth;
            if (rel <= 0.03 || rel >= 0.97)
            {
                continue;
            }

            var weight = Falloff(Math.Abs(slot.Center - _currentHoverX), GlowRadius);
            if (weight <= 0.03)
            {
                continue;
            }

            var color = GlassSettings.Tron ? ThemePalette.LineColor : GlassSettings.Mordor ? AuraSettings.Embers[1] : slot.Item.Glow;
            var alpha = (byte)(0x6E * weight * intensity);
            _glowPairs.Add((rel, Color.FromArgb(alpha, color.R, color.G, color.B)));
        }

        _glowPairs.Add((1.0, Colors.Transparent));
        _glowPairs.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var stopCount = _glowBrush.GradientStops.Count;
        for (var i = 0; i < stopCount; i++)
        {
            if (i < _glowPairs.Count)
            {
                _glowBrush.GradientStops[i].Offset = _glowPairs[i].Offset;
                _glowBrush.GradientStops[i].Color = _glowPairs[i].Color;
            }
            else
            {
                _glowBrush.GradientStops[i].Offset = 1;
                _glowBrush.GradientStops[i].Color = Colors.Transparent;
            }
        }

        _glow.Width = glowWidth;
        Canvas.SetLeft(_glow, left);
        Canvas.SetTop(_glow, RestTop - EffectiveBulge - 2);
        _glow.Visibility = Visibility.Visible;
    }

    private List<Point> BuildShape()
    {
        var offset = _capsuleLeft;
        var width = _capsuleWidth;
        var r = CornerRadius;
        _shapePoints.Clear();
        var points = _shapePoints;

        double TopAt(double localX)
            => Math.Max(0, RestTop - (EffectiveBulge * BulgeProfile(localX - (_currentHoverX - offset))));

        if (GlassSettings.Tron) BuildCubeColumns(_tronColumns, false);
        var stepped = GlassSettings.Tron && _tronColumns.Count > 0;
        var y0 = stepped ? _tronColumns[^1].Top : TopAt(r);
        var y1 = stepped ? _tronColumns[0].Top : TopAt(width - r);

        AddArc(points, offset + r, Bottom - r, r, 180, 90, 6);
        points.Add(new Point(offset + width - r, Bottom));
        AddArc(points, offset + width - r, Bottom - r, r, 90, 0, 6);
        points.Add(new Point(offset + width, y1 + r));
        AddArc(points, offset + width - r, y1 + r, r, 0, -90, 6);

        if (stepped)
        {
            // whole cubes: two points per column, leaving a vertical riser where the step changes
            foreach (var column in _tronColumns)
            {
                points.Add(new Point(column.Left + column.Width, column.Top));
                points.Add(new Point(column.Left, column.Top));
            }
        }
        else
        {
            var steps = Math.Max(16, (int)(width / 3));
            for (var i = 0; i <= steps; i++)
            {
                var localX = (width - r) - ((width - (2 * r)) * i / steps);
                points.Add(new Point(offset + localX, TopAt(localX)));
            }
        }

        AddArc(points, offset + r, y0 + r, r, -90, -180, 6);
        points.Add(new Point(offset, Bottom - r));

        ApplySideStretch(points, offset, width);
        return points;
    }

    // Tron's top edge as whole cubes, right to left. The loose cubes get the side stretch too, since
    // the shape's own points carry it once BuildShape hands them to ApplySideStretch.
    private void BuildCubeColumns(List<TronCubes.Column> target, bool stretch)
    {
        target.Clear();
        var offset = _capsuleLeft;
        var width = _capsuleWidth;
        var r = CornerRadius;
        var hover = _currentHoverX - offset;
        var bulge = EffectiveBulge;
        var strength = MaxBulge > 0 ? Math.Max(0, Math.Min(1, bulge / MaxBulge)) : 0;
        var mid = offset + (width / 2);
        var leftShift = stretch && strength > .001 ? MaxSideStretch * Gaussian(0, hover) * strength : 0;
        var rightShift = stretch && strength > .001 ? MaxSideStretch * Gaussian(width, hover) * strength : 0;

        for (var right = width - r; right > r + .01; right -= TronCubes.Cube)
        {
            var left = Math.Max(r, right - TronCubes.Cube);
            var cubes = (bulge * BulgeProfile(((right + left) / 2) - hover)) / TronCubes.Cube;
            var whole = Math.Floor(cubes);
            var x0 = offset + left;
            var x1 = offset + right;
            if (leftShift > 0 || rightShift > 0)
            {
                if (x0 < mid) x0 -= leftShift; else x0 += rightShift;
                if (x1 < mid) x1 -= leftShift; else x1 += rightShift;
            }

            target.Add(new TronCubes.Column(x0, RestTop - (whole * TronCubes.Cube), x1 - x0, whole * TronCubes.Cube, cubes - whole));
        }
    }

    // feed the loose cubes once per frame: the columns plus the frame time that drives the scatter
    private void UpdateTronCubes(double dt)
    {
        if (GlassSettings.Tron) BuildCubeColumns(_tronColumns, true);
        else _tronColumns.Clear();
        _tronCubes.Frame(_tronColumns, _currentHoverX, dt);
    }

    private void ApplySideStretch(List<Point> points, double offset, double width)
    {
        var strength = MaxBulge > 0 ? Math.Max(0, Math.Min(1, EffectiveBulge / MaxBulge)) : 0.0;
        if (strength <= 0.001)
        {
            return;
        }

        var hoverLocal = _currentHoverX - offset;
        var leftInf = Gaussian(0, hoverLocal) * strength;
        var rightInf = Gaussian(width, hoverLocal) * strength;
        var mid = offset + (width / 2);

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var vertical = Smoothstep(Math.Min(1.0, Math.Max(0, (Bottom - p.Y) / RestBarHeight)));
            if (p.X < mid)
            {
                p.X -= MaxSideStretch * leftInf * vertical;
            }
            else
            {
                p.X += MaxSideStretch * rightInf * vertical;
            }

            points[i] = p;
        }
    }

    private static void AddArc(List<Point> points, double cx, double cy, double radius, double startDeg, double endDeg, int steps)
    {
        var step = (endDeg - startDeg) / steps;
        for (var i = 0; i <= steps; i++)
        {
            var angle = (startDeg + (step * i)) * Math.PI / 180.0;
            points.Add(new Point(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))));
        }
    }

    // Hover bump shape per GlassSettings.Bulge: height factor, flat-top half-width, shoulder sigma.
    private static (double Height, double Plateau, double Sigma) BulgeShape => GlassSettings.Bulge switch
    {
        BulgeStyle.Pedestal => (1.0, 46, 12),
        BulgeStyle.Flat => (0, 0, BulgeSigma),
        _ => (0.72, 0, 52),
    };

    private double EffectiveBulge => _currentBulge * BulgeShape.Height;

    private static double BulgeProfile(double distance)
    {
        var shape = BulgeShape;
        var d = Math.Max(0, Math.Abs(distance) - shape.Plateau);
        return Math.Exp(-(d * d) / (2 * shape.Sigma * shape.Sigma));
    }

    private static double Gaussian(double x, double center)
    {
        var d = x - center;
        return Math.Exp(-(d * d) / (2 * BulgeSigma * BulgeSigma));
    }

    private static double Smoothstep(double t)
    {
        t = Math.Min(1, Math.Max(0, t));
        return t * t * (3 - (2 * t));
    }

    private static double Falloff(double distance, double radius)
    {
        if (distance >= radius)
        {
            return 0;
        }

        return 0.5 * (1 + Math.Cos(Math.PI * (distance / radius)));
    }

}
