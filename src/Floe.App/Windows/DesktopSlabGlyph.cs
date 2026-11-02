using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Floe.App.Windows;

/// <summary>
/// The Cyber show-desktop button: an upright screen slab in isometric, drawn in one thin line, with two windows lying on
/// its face. Peek slides the windows off the edge of the screen and the desktop's icons appear behind them.
/// </summary>
public sealed class DesktopSlabGlyph : Canvas
{
    private const double View = 48;
    private const double Iso = .72;
    private const double SlabWidth = 60, SlabHeight = 38, SlabDepth = 4;
    private const double Travel = 58;
    private const double Line = 1.3, Thin = 1;

    private static readonly double C45 = Math.Sqrt(.5);

    private readonly double _unit;
    private readonly Point _origin;
    private readonly SolidColorBrush _edge = new(), _dim = new(), _low = new(), _ground = new(), _light = new();
    private readonly TranslateTransform _first = new(), _second = new();
    private readonly Path _led;
    private readonly Canvas _icons = new() { Opacity = 0 };
    private Color _ink, _accent;
    private bool _peek;

    public DesktopSlabGlyph(double size)
    {
        Width = Height = size;
        IsHitTestVisible = false;
        _unit = size / View;

        // the slab's corners, centred in the view box
        var corners = new[] { Iso3(0, 0, 0), Iso3(SlabWidth, 0, 0), Iso3(0, SlabDepth, SlabHeight), Iso3(SlabWidth, SlabDepth, SlabHeight),
            Iso3(0, SlabDepth, 0), Iso3(SlabWidth, SlabDepth, 0), Iso3(0, 0, SlabHeight), Iso3(SlabWidth, 0, SlabHeight) };
        _origin = new Point((View / 2) - ((corners.Min(p => p.X) + corners.Max(p => p.X)) / 2), (View / 2) - ((corners.Min(p => p.Y) + corners.Max(p => p.Y)) / 2));

        var outline = Rounded(0, 0, SlabWidth, SlabHeight, 5, 5);
        Children.Add(Stroke(Shape(Hull(OnFace(outline, 0).Concat(OnFace(outline, SlabDepth)))), _edge, Line, _ground));
        Children.Add(Stroke(Shape(OnFace(outline, SlabDepth)), _edge, Line, _ground));
        Children.Add(Stroke(Shape(OnFace(Rounded(1.8, 1.8, SlabWidth - 1.8, SlabHeight - 1.8, 3.4, 5), SlabDepth)), _low, Thin));

        foreach (var (x, z) in new[] { (6d, 28d), (6d, 21d), (6d, 14d), (13d, 28d) })
        {
            _icons.Children.Add(Stroke(Shape(OnFace(Rounded(x, z, x + 4.2, z + 4.2, 1, 2), SlabDepth)), _dim, Thin));
        }

        Children.Add(_icons);

        var screen = new Canvas { Clip = Shape(OnFace(Rounded(1.2, 1.2, SlabWidth - 1.2, SlabHeight - 1.2, 3.8, 5), SlabDepth)) };
        screen.Children.Add(Window(7, 12, 32, 30, _first));
        screen.Children.Add(Window(24, 5, 54, 24, _second));
        Children.Add(screen);

        _led = new Path { Data = Shape(OnFace(Rounded(51, 5, 54.4, 8.4, .6, 1), SlabDepth)), Fill = _light };
        Children.Add(_led);

        Breathe();
    }

    public void Paint(Color ink, Color accent, Color ground)
    {
        _ink = ink;
        _accent = accent;
        _edge.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _edge.Color = _peek ? accent : ink;
        _dim.Color = Color.FromArgb(0x8C, ink.R, ink.G, ink.B);
        _low.Color = Color.FromArgb(0x48, ink.R, ink.G, ink.B);
        _ground.Color = ground;
        _light.Color = accent;
    }

    public void Peek(bool on)
    {
        _peek = on;
        Slide(_first, on, TimeSpan.Zero);
        Slide(_second, on, TimeSpan.FromMilliseconds(70));
        _edge.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? _accent : _ink, TimeSpan.FromMilliseconds(250)));
        _icons.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? .95 : 0, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(on ? 200 : 0) });
        if (on) _led.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        else Breathe();
    }

    private void Slide(TranslateTransform shift, bool on, TimeSpan delay)
    {
        Move(shift, TranslateTransform.XProperty, on ? Travel * C45 * Iso * _unit : 0, delay);
        Move(shift, TranslateTransform.YProperty, on ? Travel * C45 * .5 * Iso * _unit : 0, delay);
    }

    private static void Move(TranslateTransform shift, DependencyProperty axis, double to, TimeSpan delay)
    {
        var move = new DoubleAnimationUsingKeyFrames { BeginTime = delay };
        move.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)), new KeySpline(.32, .72, 0, 1)));
        shift.BeginAnimation(axis, move);
    }

    // the light is the one thing that runs while the button rests, so it asks for few frames
    private void Breathe()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _led.BeginAnimation(OpacityProperty, new DoubleAnimation(.8, TimeSpan.FromMilliseconds(200)));
            return;
        }

        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var breath = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        breath.KeyFrames.Add(new LinearDoubleKeyFrame(.45, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        breath.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600)), ease));
        breath.KeyFrames.Add(new EasingDoubleKeyFrame(.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3200)), ease));
        Timeline.SetDesiredFrameRate(breath, 10);
        _led.BeginAnimation(OpacityProperty, breath);
    }

    private Canvas Window(double x0, double z0, double x1, double z1, TranslateTransform shift)
    {
        var window = new Canvas { RenderTransform = shift };
        window.Children.Add(Stroke(Shape(OnFace(Rounded(x0, z0, x1, z1, 1.6, 3), SlabDepth)), _dim, Thin, _ground));
        window.Children.Add(Stroke(Shape(new[] { Place(x0, SlabDepth, z1 - 3.2), Place(x1, SlabDepth, z1 - 3.2) }, closed: false), _low, Thin));
        return window;
    }

    private static Path Stroke(Geometry data, Brush brush, double thickness, Brush? fill = null) => new()
    {
        Data = data,
        Stroke = brush,
        StrokeThickness = thickness,
        StrokeLineJoin = PenLineJoin.Round,
        Fill = fill,
    };

    private static Geometry Shape(IEnumerable<Point> points, bool closed = true)
    {
        var all = points.ToList();
        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            pen.BeginFigure(all[0], true, closed);
            pen.PolyLineTo(all.Skip(1).ToList(), true, true);
        }

        shape.Freeze();
        return shape;
    }

    // 2:1 isometric: x runs down-right, y down-left, z up
    private static Point Iso3(double x, double y, double z) => new((x - y) * C45 * Iso, ((x + y) * C45 * .5 * Iso) - (z * .866 * Iso));

    private Point Place(double x, double y, double z)
    {
        var p = Iso3(x, y, z);
        return new Point((p.X + _origin.X) * _unit, (p.Y + _origin.Y) * _unit);
    }

    private IEnumerable<Point> OnFace(IEnumerable<Point> ring, double depth) => ring.Select(p => Place(p.X, depth, p.Y));

    // a rounded rectangle in the screen's own x-z plane
    private static IEnumerable<Point> Rounded(double x0, double z0, double x1, double z1, double r, int steps)
    {
        foreach (var (cx, cz, from) in new[] { (x1 - r, z1 - r, 0d), (x0 + r, z1 - r, 90d), (x0 + r, z0 + r, 180d), (x1 - r, z0 + r, 270d) })
        {
            for (int i = 0; i <= steps; i++)
            {
                double angle = (from + (90.0 * i / steps)) * Math.PI / 180;
                yield return new Point(cx + (r * Math.Cos(angle)), cz + (r * Math.Sin(angle)));
            }
        }
    }

    // the slab's silhouette is the convex hull of its front and back faces
    private static List<Point> Hull(IEnumerable<Point> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var lower = Half(sorted);
        sorted.Reverse();
        var upper = Half(sorted);
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static List<Point> Half(List<Point> points)
    {
        var chain = new List<Point>();
        foreach (var p in points)
        {
            while (chain.Count > 1 && Turn(chain[^2], chain[^1], p) <= 0) chain.RemoveAt(chain.Count - 1);
            chain.Add(p);
        }

        return chain;
    }

    private static double Turn(Point o, Point a, Point b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
}
