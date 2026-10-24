using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using Floe.App.Effects.Derezz;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The Tron bulge, built of solid glass cubes. Under the hovered icon the flat glass grows a stepped mound,
/// seen from above and from the hover, so each cube shows its lit top and the face turned to the middle.
/// The mound never sits still: every column rises, a new cube rezzing out of the glass at its foot in a flash
/// while the one on top breaks off, tumbles away from the hover and derezzes, splitting into shards that fade.
/// Edges are drawn twice, sharp and blurred into bloom, in the theme's colours.
/// The dock feeds it the columns once per frame; it keeps no clock of its own.
/// </summary>
public sealed class TronCubes : FrameworkElement
{
    /// <summary>Edge of one cube of the mound, in DIP: a column's width and a step's height.</summary>
    public const double Cube = 7;

    private const double Size = Cube * .9;
    private const int MaxParticles = 700;
    // cubes a second each column carries up and throws off its top
    private const double Flow = 2.4;
    private const double RezzTime = .3;
    private const double Gravity = 70;
    private const int Grades = 8;
    private const double EdgeWidth = .6, GlintWidth = 1.1, GlowWidth = 2.2;
    private const double Tilt = -26 * Math.PI / 180;
    private const double MaxYaw = 34 * Math.PI / 180;

    /// <summary>One column of the mound, in this element's coordinates: its foot (Top), and its height.</summary>
    public readonly record struct Column(double Left, double Top, double Width, double Height, double Fraction);

    private struct Particle
    {
        public double X, Y, Vx, Vy, Age, Life, Size, Spin, Angle, Split;
        public Vector3D Axis;
        public bool Shard;
    }

    private static readonly Point3D[] Corners =
    {
        new(-.5, -.5, -.5), new(.5, -.5, -.5), new(.5, .5, -.5), new(-.5, .5, -.5),
        new(-.5, -.5, .5), new(.5, -.5, .5), new(.5, .5, .5), new(-.5, .5, .5),
    };

    private static readonly int[][] Faces =
    {
        new[] { 4, 5, 6, 7 }, new[] { 5, 4, 7, 6 }, new[] { 5, 1, 2, 6 },
        new[] { 0, 4, 7, 3 }, new[] { 7, 6, 2, 3 }, new[] { 0, 1, 5, 4 },
    };

    private static readonly Vector3D[] Normals =
    {
        new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0),
    };

    private enum Shade { Front, Side, Top }

    private readonly DrawingVisual _sharp = new();
    private readonly DrawingVisual _glow = new()
    {
        Effect = new BlurEffect { Radius = 4, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
    };

    private readonly List<Column> _columns = new();
    private readonly List<double> _phases = new();
    private readonly List<double> _heights = new();
    private readonly List<Particle> _particles = new();
    private readonly List<int> _order = new();
    private readonly Random _random = new(20261024);

    private readonly DerezzField.Batch[] _front = NewBatches();
    private readonly DerezzField.Batch[] _side = NewBatches();
    private readonly DerezzField.Batch[] _top = NewBatches();
    private readonly DerezzField.Batch[] _edges = NewBatches();
    private readonly DerezzField.Batch[] _glint = NewBatches();
    private readonly DerezzField.Batch[] _flash = NewBatches();
    private readonly DerezzField.Batch[] _bloom = NewBatches();
    private Brush[] _frontInk = Array.Empty<Brush>(), _sideInk = Array.Empty<Brush>(), _topInk = Array.Empty<Brush>();
    private Brush[] _edgeInk = Array.Empty<Brush>(), _glintInk = Array.Empty<Brush>(), _flashInk = Array.Empty<Brush>();
    private Brush[] _bloomInk = Array.Empty<Brush>();
    private Color _line, _hot;
    private bool _tinted;

    private readonly Point[] _projected = new Point[8];
    private readonly Point[] _quad = new Point[4];
    private readonly Point[] _hull = new Point[8];
    private readonly Point[] _sorted = new Point[8];

    private bool _flowing;
    private bool _empty = true;
    private double _time;
    private double _hoverX;

    public TronCubes()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        AddVisualChild(_sharp);
        AddVisualChild(_glow);
    }

    /// <summary>True while the mound flows or a cube or shard is still in the air, so the dock keeps its frames coming.</summary>
    public bool Busy => _flowing || _particles.Count > 0;

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _sharp : _glow;

    /// <summary>
    /// One frame: <paramref name="columns"/> stand on the glass's top edge (Top) and want Height / Cube plus
    /// Fraction cubes of mound; <paramref name="dt"/> drives the flow and the scatter.
    /// </summary>
    public void Frame(IReadOnlyList<Column> columns, double hoverX, double dt)
    {
        _columns.Clear();
        _flowing = false;
        if (!GlassSettings.Tron || Visibility != Visibility.Visible)
        {
            if (_particles.Count > 0 || _heights.Count > 0 || !_empty) Clear();
            return;
        }

        _time += dt;
        _hoverX = hoverX;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            _columns.Add(column);
            var cubes = (column.Height / Cube) + column.Fraction;
            if (i >= _phases.Count)
            {
                // columns start out of step, so the mound shimmers instead of pumping as one
                _phases.Add(_random.NextDouble());
                _heights.Add(cubes);
            }

            var middle = column.Left + (column.Width / 2);
            if (cubes >= 1)
            {
                _flowing = true;
                var phase = _phases[i] + (dt * Flow * (.75 + (.5 * Hash(i, 3))));
                if (phase >= 1)
                {
                    // the top cube breaks off and flies, and the stack moves up a place
                    phase -= 1;
                    Launch(middle, column.Top - ((Math.Floor(cubes) + .5) * Cube), hoverX, false);
                }

                _phases[i] = phase;
            }

            // a sinking mound throws off the cubes it no longer holds
            var lost = (int)Math.Floor(_heights[i]) - (int)Math.Floor(cubes);
            for (var k = 0; k < Math.Min(lost, 8); k++)
            {
                Launch(middle, column.Top - ((Math.Floor(cubes) + k + .5) * Cube), hoverX, true);
            }

            _heights[i] = cubes;
        }

        if (_phases.Count > columns.Count)
        {
            _phases.RemoveRange(columns.Count, _phases.Count - columns.Count);
            _heights.RemoveRange(columns.Count, _heights.Count - columns.Count);
        }

        Step(dt);
        Render();
    }

    private void Step(double dt)
    {
        var count = _particles.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _particles.RemoveAt(i);
                continue;
            }

            // a whole cube derezzes partway through its flight: it bursts into shards that carry on
            if (!p.Shard && p.Age >= p.Split)
            {
                _particles.RemoveAt(i);
                Shatter(p);
                continue;
            }

            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += Gravity * dt;
            p.Vx *= 1 - (dt * .8);
            p.Angle += p.Spin * dt;
            _particles[i] = p;
        }
    }

    // a cube breaks loose: out away from the hover and up, tumbling
    private void Launch(double x, double y, double hoverX, bool falling)
    {
        if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
        var away = x < hoverX ? -1 : x > hoverX ? 1 : _random.Next(2) == 0 ? -1 : 1;
        var reach = Math.Min(1, Math.Abs(x - hoverX) / 60);
        _particles.Add(new Particle
        {
            X = x,
            Y = y,
            Vx = away * (35 + (75 * _random.NextDouble())) * (.55 + (.45 * reach)),
            Vy = falling ? -(10 + (30 * _random.NextDouble())) : -(20 + (45 * _random.NextDouble())),
            Life = 2,
            Split = .18 + (.3 * _random.NextDouble()),
            Size = Size,
            Axis = RandomAxis(),
            Spin = (4 + (8 * _random.NextDouble())) * (_random.Next(2) == 0 ? -1 : 1),
        });
    }

    private void Shatter(Particle parent)
    {
        var pieces = 4 + _random.Next(4);
        for (var k = 0; k < pieces; k++)
        {
            if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
            var angle = _random.NextDouble() * Math.PI * 2;
            var burst = 25 + (45 * _random.NextDouble());
            _particles.Add(new Particle
            {
                X = parent.X + ((_random.NextDouble() - .5) * parent.Size),
                Y = parent.Y + ((_random.NextDouble() - .5) * parent.Size),
                Vx = parent.Vx + (Math.Cos(angle) * burst),
                Vy = parent.Vy + (Math.Sin(angle) * burst) - 15,
                Life = .45 + (.5 * _random.NextDouble()),
                Size = parent.Size * (.32 + (.2 * _random.NextDouble())),
                Axis = RandomAxis(),
                Angle = parent.Angle,
                Spin = (8 + (14 * _random.NextDouble())) * (_random.Next(2) == 0 ? -1 : 1),
                Shard = true,
            });
        }
    }

    private void Render()
    {
        EnsureInks();
        foreach (var set in new[] { _front, _side, _top, _edges, _glint, _flash, _bloom })
        {
            foreach (var batch in set) batch.Begin();
        }

        // outer columns first: the ones nearer the hover stand in front of them
        _order.Clear();
        for (var i = 0; i < _columns.Count; i++) _order.Add(i);
        _order.Sort((a, b) => Distance(b).CompareTo(Distance(a)));
        foreach (var i in _order)
        {
            var column = _columns[i];
            var cubes = (column.Height / Cube) + column.Fraction;
            if (cubes < 1) continue;
            var phase = i < _phases.Count ? _phases[i] : 0;
            var x = column.Left + (column.Width / 2);
            var whole = (int)Math.Floor(cubes);
            var view = View(Math.Clamp((x - _hoverX) / 80, -1, 1) * MaxYaw);

            // the stack rides up by its phase: the bottom cube is still rezzing out of the glass, the top one
            // is about to leave
            for (var k = 0; k < whole; k++)
            {
                var y = column.Top - ((k + phase + .5) * Cube);
                var alpha = k == 0 ? Math.Min(1, phase * 2.5) : 1;
                var flash = k == 0 ? Math.Max(0, 1 - (phase * Flow / RezzTime)) : 0;
                // now and then a cube of the mound catches the light
                if (Hash((i * 64) + k, (int)(_time * 4)) > .96) flash = Math.Max(flash, .6);
                var size = k == 0 ? Size * (.55 + (.45 * Math.Min(1, phase * 2))) : Size;
                DrawCube(new Point(x, y), size, view, alpha, flash, k == whole - 1);
            }
        }

        foreach (var p in _particles)
        {
            var t = p.Age / p.Life;
            var alpha = p.Shard ? Math.Pow(1 - t, 1.4) * (.7 + (.3 * Hash((int)(p.X * 7), (int)(_time * 20)))) : 1;
            var size = p.Shard ? p.Size * (1 - (.5 * t)) : p.Size;
            var own = new Quaternion(p.Axis, p.Angle * 180 / Math.PI);
            var rotation = Matrix3D.Identity;
            rotation.Rotate(own);
            var view = Matrix3D.Multiply(rotation, View(0));
            var flash = p.Shard ? Math.Max(0, 1 - (p.Age / .12)) : Math.Max(0, (p.Age - (p.Split - .08)) / .08);
            DrawCube(new Point(p.X, p.Y), size, view, alpha, Math.Min(1, flash), true);
        }

        using (var dc = _sharp.RenderOpen())
        using (var glow = _glow.RenderOpen())
        {
            Fill(dc, _side, _sideInk);
            Fill(dc, _front, _frontInk);
            Fill(dc, _top, _topInk);
            Fill(dc, _edges, _edgeInk);
            Fill(dc, _glint, _glintInk);
            Fill(dc, _flash, _flashInk);
            Fill(glow, _bloom, _bloomInk);
            Fill(glow, _flash, _flashInk);
        }

        _empty = false;
    }

    private double Distance(int i) => Math.Abs(_columns[i].Left + (_columns[i].Width / 2) - _hoverX);

    // seen from above, and turned so the face toward the hover shows: a cheap perspective centred on it
    private static Matrix3D View(double yaw)
    {
        var turn = new Matrix3D(
            Math.Cos(yaw), 0, -Math.Sin(yaw), 0,
            0, 1, 0, 0,
            Math.Sin(yaw), 0, Math.Cos(yaw), 0,
            0, 0, 0, 1);
        var tilt = new Matrix3D(
            1, 0, 0, 0,
            0, Math.Cos(Tilt), Math.Sin(Tilt), 0,
            0, -Math.Sin(Tilt), Math.Cos(Tilt), 0,
            0, 0, 0, 1);
        return Matrix3D.Multiply(turn, tilt);
    }

    private void DrawCube(Point center, double size, Matrix3D view, double alpha, double flash, bool crest)
    {
        if (alpha <= .02 || size <= .3) return;
        for (var c = 0; c < Corners.Length; c++)
        {
            var p = view.Transform(Corners[c]);
            _projected[c] = new Point(center.X + (p.X * size), center.Y + (p.Y * size));
        }

        var grade = Grade(alpha);
        for (var f = 0; f < Faces.Length; f++)
        {
            var n = view.Transform(Normals[f]);
            if (n.Z <= .02) continue;
            for (var c = 0; c < 4; c++) _quad[c] = _projected[Faces[f][c]];
            var shade = n.Y < -.3 ? Shade.Top : n.Z > .8 ? Shade.Front : Shade.Side;
            var set = shade == Shade.Top ? _top : shade == Shade.Front ? _front : _side;
            set[grade].Quad(_quad, true);
            if (shade == Shade.Top)
            {
                // the top face's far edge catches the light; on the crest of a column it blooms
                var top = TopEdge(_quad);
                _glint[Grade(alpha * (crest ? 1 : .45))].Segment(_quad[top], _quad[(top + 1) % 4], GlintWidth / 2);
            }

            if (flash > .02) _flash[Grade(flash * alpha)].Quad(_quad, true);
        }

        var corners = Hull(_projected, _hull);
        _edges[grade].Outline(_hull, corners, EdgeWidth / 2);
        _bloom[Grade(alpha * (crest ? 1 : .55))].Outline(_hull, corners, GlowWidth / 2);
    }

    // Convex hull of the projected corners (monotone chain) into hull; returns its corner count.
    private int Hull(Point[] points, Point[] hull)
    {
        var n = points.Length;
        Array.Copy(points, _sorted, n);
        Array.Sort(_sorted, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        static double Cross(Point o, Point a, Point b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        Span<Point> chain = stackalloc Point[(2 * 8) + 1];
        var k = 0;
        for (var a = 0; a < n; a++)
        {
            while (k >= 2 && Cross(chain[k - 2], chain[k - 1], _sorted[a]) <= 1e-9) k--;
            chain[k++] = _sorted[a];
        }

        for (int a = n - 2, low = k + 1; a >= 0; a--)
        {
            while (k >= low && Cross(chain[k - 2], chain[k - 1], _sorted[a]) <= 1e-9) k--;
            chain[k++] = _sorted[a];
        }

        var count = Math.Min(8, k - 1);
        for (var a = 0; a < count; a++) hull[a] = chain[a];
        return count;
    }

    private static int TopEdge(Point[] quad)
    {
        var best = 0;
        var y = double.MaxValue;
        for (var e = 0; e < 4; e++)
        {
            var mid = quad[e].Y + quad[(e + 1) % 4].Y;
            if (mid < y)
            {
                y = mid;
                best = e;
            }
        }

        return best;
    }

    private void EnsureInks()
    {
        var line = ThemePalette.LineColor;
        var hot = ThemePalette.HotColor;
        if (_tinted && _line == line && _hot == hot) return;
        _tinted = true;
        _line = line;
        _hot = hot;
        var dim = ThemePalette.DimColor;
        // dark glass bodies tinted by the theme, a lit top, neon edges and a white-hot flash
        _frontInk = Ladder(Mix(Colors.Black, dim, .55), .92);
        _sideInk = Ladder(Mix(Colors.Black, dim, .3), .95);
        _topInk = Ladder(Mix(dim, line, .45), .9);
        _edgeInk = Ladder(line, 1);
        _glintInk = Ladder(hot, 1);
        _flashInk = Ladder(hot, .85);
        _bloomInk = Ladder(line, .55);
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));

    private static Brush[] Ladder(Color color, double max)
    {
        var brushes = new Brush[Grades];
        for (var g = 0; g < Grades; g++)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * max * g / (Grades - 1.0)), color.R, color.G, color.B));
            brush.Freeze();
            brushes[g] = brush;
        }

        return brushes;
    }

    private static void Fill(DrawingContext dc, DerezzField.Batch[] set, Brush[] inks)
    {
        for (var g = 1; g < Grades; g++) set[g].Fill(dc, inks[g]);
    }

    private static DerezzField.Batch[] NewBatches()
    {
        var batches = new DerezzField.Batch[Grades];
        for (var g = 0; g < Grades; g++) batches[g] = new DerezzField.Batch();
        return batches;
    }

    private static int Grade(double alpha) => Math.Clamp((int)Math.Round(alpha * (Grades - 1)), 0, Grades - 1);

    private Vector3D RandomAxis()
    {
        var axis = new Vector3D(_random.NextDouble() - .5, _random.NextDouble() - .5, _random.NextDouble() - .5);
        if (axis.LengthSquared < 1e-4) axis = new Vector3D(0, 0, 1);
        axis.Normalize();
        return axis;
    }

    private static double Hash(int i, int salt)
    {
        unchecked
        {
            var h = (uint)((i * 374761393) + (salt * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }

    private void Clear()
    {
        _particles.Clear();
        _phases.Clear();
        _heights.Clear();
        _columns.Clear();
        using (_sharp.RenderOpen()) { }
        using (_glow.RenderOpen()) { }
        _empty = true;
    }
}
