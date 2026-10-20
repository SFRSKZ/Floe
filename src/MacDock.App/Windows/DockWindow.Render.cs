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
using MacDock.App.Services;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{

    private void EnsureAnimation()
    {
        if (!_renderingHooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
            _lastTick = Stopwatch.GetTimestamp();
        }
    }

    private void StopAnimation()
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (now - _lastTick) / (double)Stopwatch.Frequency;
        _lastTick = now;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        if (_metalEffect is not null && !_glassPreview)
        {
            _metalTime += dt;
            _metalEffect.Time = _metalTime;
            _metalEffect.CursorY = IsMouseOver ? Math.Clamp((Mouse.GetPosition(Root).Y - RestTop) / RestBarHeight, 0, 1) : 0;
        }
        _currentHoverX = Spring(_currentHoverX, ref _hoverVel, _targetHoverX, dt);
        _currentBulge = Spring(_currentBulge, ref _bulgeVel, _targetBulge, dt);
        _dockScale = Spring(_dockScale, ref _dockScaleVel, _targetDockScale, dt, 180, 24);
        _currentGlowColor = Lerp(_currentGlowColor, _targetGlowColor, Math.Min(1.0, dt * 9));
        _rootScale.ScaleX = _dockScale;
        _rootScale.ScaleY = _dockScale;

        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        UpdateGlow();
        EnsureWidgetRendering();

        var settled = _targetBulge == 0
            && _currentBulge < 0.3
            && Math.Abs(_currentHoverX - _targetHoverX) < 0.3
            && Math.Abs(_hoverVel) < 4
            && Math.Abs(_bulgeVel) < 4
            && Math.Abs(_dockScale - _targetDockScale) < 0.002
            && Math.Abs(_dockScaleVel) < 0.02
            && scales.All(s => Math.Abs(s - 1) < 0.0015);

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
            if (_metalEffect is null || !IsMouseOver) StopAnimation();
        }
    }

    private static double Spring(double position, ref double velocity, double target, double dt, double stiffness = 260, double damping = 27)
    {
        var accel = (stiffness * (target - position)) - (damping * velocity);
        velocity += accel * dt;
        return position + (velocity * dt);
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

            var color = slot.Item.Glow;
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

        var y0 = TopAt(r);
        var y1 = TopAt(width - r);

        AddArc(points, offset + r, Bottom - r, r, 180, 90, 6);
        points.Add(new Point(offset + width - r, Bottom));
        AddArc(points, offset + width - r, Bottom - r, r, 90, 0, 6);
        points.Add(new Point(offset + width, y1 + r));
        AddArc(points, offset + width - r, y1 + r, r, 0, -90, 6);

        var steps = Math.Max(16, (int)(width / 3));
        for (var i = 0; i <= steps; i++)
        {
            var localX = (width - r) - ((width - (2 * r)) * i / steps);
            points.Add(new Point(offset + localX, TopAt(localX)));
        }

        AddArc(points, offset + r, y0 + r, r, -90, -180, 6);
        points.Add(new Point(offset, Bottom - r));

        ApplySideStretch(points, offset, width);
        return points;
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
