using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

/// <summary>
/// Music visualizer of glass cubes stacked into columns along the bottom edge of <see cref="Capsule"/>.
/// The columns rise with the music, tallest in the middle; each column's top cube is thrown up on a beat
/// and tumbles back down onto its stack.
/// <para>
/// Same lifecycle and audio shaping as <see cref="HologramVisualizer"/>. Everything is filled geometry,
/// batched per brush: a pen would make the shared render thread widen every edge, every frame.
/// </para>
/// </summary>
public sealed class CubeVisualizer : FrameworkElement
{
    // Audio shaping, matching the other visualizers so all of them react alike.
    private const double GainDecaySeconds = 4.0;
    private const double GainFloor = 0.002;
    private const double SilenceFloor = 0.0003;
    private const double AttackSeconds = 0.05;
    private const double ReleaseSeconds = 0.30;
    private const double BalanceSeconds = 0.18;
    private const double FadeOutSeconds = 0.40;
    private const double FadeInSeconds = 0.22;

    private static double Cell => GlassSettings.CubeSize;
    private const double Gap = 1.2;
    // the glass: a bright rim this wide around each front, a gloss over its upper part and a streak of light
    private const double Rim = .6;
    private const double Gloss = .32;
    private const int MaxColumns = 400;
    private const int MaxRows = 9;
    // The cube's depth: its side and top faces lean back up and to the right.
    private static Vector Depth => new(Cell * .30, -Cell * .22);

    // A column follows its target faster up than down, so the stacks jump on a beat and settle slowly.
    private const double RiseSeconds = .06;
    private const double FallSeconds = .35;
    private const double Gravity = 900;
    private const double Kick = 26;

    private const int Grades = 8;
    private const int Shades = 6;

    private static readonly Color BaseBody = Color.FromRgb(0x2A, 0x6F, 0xD6);
    private static readonly Color BaseCore = Color.FromRgb(0x6F, 0xB8, 0xFF);
    private static readonly Color BaseTip = Color.FromRgb(0xEA, 0xF6, 0xFF);

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

    // Rebuilt when the tint changes: [shade][grade].
    private static Brush[][] FrontBrushes = Array.Empty<Brush[]>();
    private static Brush[][] SideBrushes = Array.Empty<Brush[]>();
    private static Brush[][] TopBrushes = Array.Empty<Brush[]>();
    private static Brush[] GlintBrushes = Array.Empty<Brush>();
    private static Brush[][] RimBrushes = Array.Empty<Brush[]>();
    private static Brush[] GlossBrushes = Array.Empty<Brush>();
    private static Brush[] StreakBrushes = Array.Empty<Brush>();
    private static Brush[] PoolBrushes = Array.Empty<Brush>();
    private static int _paletteVersion = -1;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _height = new double[MaxColumns];
    private readonly double[] _peak = new double[MaxColumns];
    private readonly double[] _peakVelocity = new double[MaxColumns];
    private readonly double[] _peakSpin = new double[MaxColumns];
    private readonly double[] _seed = new double[MaxColumns];
    private readonly Vector3D[] _axis = new Vector3D[MaxColumns];
    private readonly float[] _channelPeaks = new float[2];

    private readonly Batch[][] _front = NewBatches();
    private readonly Batch[][] _side = NewBatches();
    private readonly Batch[][] _top = NewBatches();
    private readonly Batch[] _glint = NewRow();
    private readonly Batch[][] _rim = NewBatches();
    private readonly Batch[] _gloss = NewRow();
    private readonly Batch[] _streak = NewRow();
    private readonly Point[] _quad = new Point[4];
    private readonly Point[] _projected = new Point[8];

    private AudioPeakMeter? _meter;
    private Rect _capsule = Rect.Empty;
    private bool _active;
    private bool _hooked;
    private double _level;
    private double _gainPeak;
    private double _balance;
    private double _fade;
    private double _lastFrameSeconds = -1;
    private double _sinceRepaint;
    private double _time;

    public CubeVisualizer()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_active) Hook();
        };
        Unloaded += (_, _) =>
        {
            Unhook();
            _meter?.Dispose();
            _meter = null;
            _lastFrameSeconds = -1;
        };

        for (int i = 0; i < MaxColumns; i++)
        {
            _seed[i] = Hash01(i, 0x2F91A3);
            var axis = new Vector3D(1, (Hash01(i, 0x51ED27) - .5) * 1.2, (Hash01(i, 0x7C3B19) - .5) * 1.2);
            axis.Normalize();
            _axis[i] = axis;
        }
    }

    /// <summary>Bounds in this element's coordinates; the cubes stand on its bottom edge.</summary>
    public Rect Capsule
    {
        get => _capsule;
        set
        {
            if (_capsule == value) return;
            _capsule = value;
            InvalidateVisual();
        }
    }

    /// <summary>On samples audio and draws; off fades out (~400 ms) and then unhooks the render callback.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value)
            {
                _lastFrameSeconds = -1;
                if (IsLoaded) Hook();
            }

            InvalidateVisual();
        }
    }

    /// <summary>Offscreen renders: 0..1 paints that fixed level without audio; -1 (default) uses the live meter.</summary>
    internal double DebugLevel { get; set; } = -1;

    /// <summary>Offscreen renders: the animation clock in seconds to draw at; -1 (default) uses the real clock.</summary>
    internal double DebugTime { get; set; } = -1;

    /// <summary>Advances the columns and the thrown cubes by <paramref name="dt"/> at the current level.</summary>
    internal void Step(double dt)
    {
        _time += dt;
        if (DebugLevel >= 0)
        {
            _level = DebugLevel;
            _fade = 1;
        }

        int count = ColumnCount(out int rows, out _);
        if (count == 0) return;
        double level = Math.Clamp(_level, 0, 1);
        double amplitude = .2 + (1.25 * Math.Pow(level, .7));
        double t = DebugTime >= 0 ? DebugTime : _time;
        double centre = Math.Clamp(_balance, -1, 1) * .04;
        double rise = 1 - Math.Exp(-dt / RiseSeconds), fall = 1 - Math.Exp(-dt / FallSeconds);
        for (int i = 0; i < count; i++)
        {
            double xn = ((i + .5) / count) - .5 - centre;
            // a wide bell, so the stacks still show either side of the dock
            double envelope = .30 + (.70 * Math.Exp(-(xn * xn) / (2 * .2 * .2)));
            double seed = _seed[i];
            double noise = (.6 * ValueNoise((t * (1.4 + seed)) + (seed * 40), (int)(seed * 100003)))
                + (.4 * ValueNoise((t * 3.1) + (i * .35), 0x1F3D));
            double target = Math.Clamp(envelope * amplitude * (.25 + (.85 * noise)), 0, 1) * rows;
            _height[i] += (target - _height[i]) * (target > _height[i] ? rise : fall);

            // the top cube is thrown up when its stack jumps past it, then falls back with gravity
            if (_height[i] > _peak[i])
            {
                double jump = _height[i] - _peak[i];
                _peak[i] = _height[i];
                _peakVelocity[i] = Math.Max(_peakVelocity[i], Math.Min(jump * Kick, 16));
            }

            _peakVelocity[i] -= Gravity / Cell * dt;
            _peak[i] += _peakVelocity[i] * dt;
            _peakSpin[i] += Math.Abs(_peakVelocity[i]) * dt * 1.6;
            if (_peak[i] <= _height[i])
            {
                _peak[i] = _height[i];
                _peakVelocity[i] = 0;
                // settles square onto the stack
                _peakSpin[i] = Math.Round(_peakSpin[i] / (Math.PI / 2)) * (Math.PI / 2);
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsurePalette();
        double fade = DebugLevel >= 0 ? 1 : _fade;
        int count = ColumnCount(out int rows, out double spacing);
        if (fade <= .001 || count == 0) return;

        var capsule = _capsule;
        double level = Math.Clamp(_level, 0, 1);
        double energy = fade * (.35 + (.65 * Math.Sqrt(level)));
        int baseGrade = Math.Clamp((int)Math.Round(energy * (Grades - 1)), 1, Grades - 1);
        double floor = capsule.Bottom;

        // a soft pool of the tint along the bottom edge, under the stacks
        double poolAlpha = Math.Clamp((.25 + (.45 * Math.Sqrt(level))) * fade, 0, 1);
        dc.DrawEllipse(PoolBrushes[Math.Clamp((int)Math.Round(poolAlpha * (PoolBrushes.Length - 1)), 0, PoolBrushes.Length - 1)], null,
            new Point(capsule.Left + (capsule.Width / 2), floor), capsule.Width * .5, Cell * (1.2 + (1.4 * level)));

        foreach (var set in new[] { _front, _side, _top, _rim })
        {
            foreach (var row in set)
            {
                foreach (var batch in row) batch.Begin();
            }
        }

        foreach (var row in new[] { _glint, _gloss, _streak })
        {
            foreach (var batch in row) batch.Begin();
        }

        for (int i = 0; i < count; i++)
        {
            double x = capsule.Left + (i * spacing) + ((spacing - Cell) / 2);
            double h = _height[i];
            double neighbour = i + 1 < count ? _height[i + 1] : 0;
            // a cube shows once its slot is half full, like the blocks of a level meter
            int stack = Math.Min(rows, (int)Math.Round(h));
            for (int r = 0; r < stack; r++)
            {
                int shade = Math.Clamp((int)(r * Shades / (double)rows), 0, Shades - 1);
                int grade = baseGrade;
                bool top = r == stack - 1;
                // a side face only shows where the next column is lower
                bool side = r + 1 > neighbour;
                Box(x, floor - (r * Cell), shade, grade, top, side);
            }

            // the thrown cube, tumbling, while it is clear of its stack
            double lift = _peak[i] - h;
            if (lift > .15 && stack < rows)
            {
                int shade = Math.Clamp((int)(_peak[i] * Shades / rows), 0, Shades - 1);
                Tumbling(i, new Point(x + (Cell / 2), floor - ((stack + lift) * Cell) - (Cell / 2)), shade, baseGrade);
            }
        }

        // sides and lids lean back behind the fronts
        for (int s = 0; s < Shades; s++)
        {
            for (int g = 1; g < Grades; g++)
            {
                _side[s][g].Fill(dc, SideBrushes[s][g]);
                _top[s][g].Fill(dc, TopBrushes[s][g]);
            }
        }

        for (int s = 0; s < Shades; s++)
        {
            for (int g = 1; g < Grades; g++) _front[s][g].Fill(dc, FrontBrushes[s][g]);
        }

        // the glass lies over the faces: gloss, rims, the streak and the lid's glint
        for (int g = 1; g < Grades; g++) _gloss[g].Fill(dc, GlossBrushes[g]);
        for (int s = 0; s < Shades; s++)
        {
            for (int g = 1; g < Grades; g++) _rim[s][g].Fill(dc, RimBrushes[s][g]);
        }

        for (int g = 1; g < Grades; g++) _streak[g].Fill(dc, StreakBrushes[g]);
        for (int g = 1; g < Grades; g++) _glint[g].Fill(dc, GlintBrushes[g]);
    }

    private int ColumnCount(out int rows, out double spacing)
    {
        var capsule = _capsule;
        rows = 0;
        spacing = Cell;
        if (capsule.IsEmpty || !double.IsFinite(capsule.Width) || capsule.Width < Cell * 2 || capsule.Height < Cell) return 0;
        rows = Math.Clamp((int)((capsule.Height - Cell * .4) / Cell), 1, MaxRows);
        int count = Math.Min(MaxColumns, (int)(capsule.Width / Cell));
        spacing = capsule.Width / count;
        return count;
    }

    // One stacked cube: a front square, its right side where the next stack is lower and a lid on top.
    private void Box(double x, double bottom, int shade, int grade, bool top, bool side)
    {
        var tl = new Point(x + Gap, bottom - Cell + Gap);
        var tr = new Point(x + Cell - Gap, bottom - Cell + Gap);
        var br = new Point(x + Cell - Gap, bottom - Gap);
        var bl = new Point(x + Gap, bottom - Gap);
        var depth = Depth;
        if (side)
        {
            Set(tr, br, br + depth, tr + depth);
            _side[shade][grade].Quad(_quad);
        }

        if (top)
        {
            Set(tl, tr, tr + depth, tl + depth);
            _top[shade][grade].Quad(_quad);
            // the lid's front edge catches the light
            _glint[grade].Line(tl, tr, .55);
        }

        Set(tl, tr, br, bl);
        _front[shade][grade].Quad(_quad);

        double w = tr.X - tl.X, h = bl.Y - tl.Y;
        // rim as four bars that do not overlap, so the translucent edge keeps one strength all round
        var rim = _rim[shade][grade];
        Bar(rim, tl.X, tl.Y, w, Rim);
        // The lower edge catches less light than the upper and side edges.
        Bar(_rim[shade][Math.Max(1, grade - 2)], tl.X, bl.Y - Rim, w, Rim);
        Bar(rim, tl.X, tl.Y + Rim, Rim, h - (2 * Rim));
        Bar(rim, tr.X - Rim, tl.Y + Rim, Rim, h - (2 * Rim));
        Bar(_gloss[grade], tl.X + Rim, tl.Y + Rim, w - (2 * Rim), (h - (2 * Rim)) * Gloss);
        Set(new Point(tl.X + (w * .2), tl.Y + Rim), new Point(tl.X + (w * .34), tl.Y + Rim),
            new Point(tl.X + Rim, tl.Y + (h * .34)), new Point(tl.X + Rim, tl.Y + (h * .2)));
        _streak[grade].Quad(_quad);
    }

    private void Bar(Batch batch, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        Set(new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h));
        batch.Quad(_quad);
    }

    // A thrown cube, rotating about its own axis: every face turned to the viewer, shaded by where it points.
    private void Tumbling(int i, Point centre, int shade, int grade)
    {
        var rotation = Matrix3D.Identity;
        rotation.Rotate(new Quaternion(_axis[i], _peakSpin[i] * 180 / Math.PI));
        double size = Cell - (2 * Gap);
        for (int c = 0; c < Corners.Length; c++)
        {
            var p = rotation.Transform(Corners[c]);
            // a fixed lean, so a square cube still shows its lid and side like the stacked ones
            _projected[c] = new Point(centre.X + (p.X * size) - (p.Z * Depth.X), centre.Y + (p.Y * size) - (p.Z * Depth.Y));
        }

        for (int f = 0; f < Faces.Length; f++)
        {
            var n = rotation.Transform(Normals[f]);
            if (n.Z <= .02) continue;
            Set(_projected[Faces[f][0]], _projected[Faces[f][1]], _projected[Faces[f][2]], _projected[Faces[f][3]]);
            var set = n.Y < -.3 ? _top : n.Z > .7 ? _front : _side;
            set[shade][grade].Quad(_quad);
        }
    }

    private void Set(Point a, Point b, Point c, Point d)
    {
        _quad[0] = a;
        _quad[1] = b;
        _quad[2] = c;
        _quad[3] = d;
    }

    private void Hook()
    {
        if (_hooked) return;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : _clock.Elapsed.TotalSeconds;
        if (now <= _lastFrameSeconds) return;
        double dt = _lastFrameSeconds < 0 ? 1.0 / 60 : Math.Clamp(now - _lastFrameSeconds, .001, .1);
        _lastFrameSeconds = now;

        // See VisualizerPacing: the strip shares the render thread with the dock.
        _sinceRepaint += dt;
        if (_sinceRepaint < VisualizerPacing.RepaintInterval) return;
        dt = Math.Min(_sinceRepaint, .1);
        _sinceRepaint = 0;

        if (_active)
        {
            _fade = Math.Min(1, _fade + (dt / FadeInSeconds));
            SampleAudio(dt);
        }
        else
        {
            _fade = Math.Max(0, _fade - (dt / FadeOutSeconds));
            _level += (0 - _level) * (1 - Math.Exp(-dt / ReleaseSeconds));
            _balance += (0 - _balance) * (1 - Math.Exp(-dt / .25));
            if (_fade <= 0)
            {
                _level = 0;
                _balance = 0;
                _lastFrameSeconds = -1;
                Array.Clear(_height);
                Array.Clear(_peak);
                Array.Clear(_peakVelocity);
                Unhook();
                InvalidateVisual();
                return;
            }
        }

        Step(dt);
        VisualizerPacing.MarkRepaint();
        InvalidateVisual();
    }

    private void SampleAudio(double dt)
    {
        _meter ??= new AudioPeakMeter();

        // Auto gain: normalise the endpoint meter against a slowly decaying recent peak.
        double raw = Math.Clamp(_meter.GetPeak(), 0, 1);
        _gainPeak = Math.Max(raw, _gainPeak * Math.Exp(-dt / GainDecaySeconds));
        double target = raw < SilenceFloor ? 0 : Math.Clamp(raw / Math.Max(_gainPeak, GainFloor), 0, 1);
        double tau = target > _level ? AttackSeconds : ReleaseSeconds;
        _level += (target - _level) * (1 - Math.Exp(-dt / tau));

        int count = _meter.GetChannelPeaks(_channelPeaks);
        double left = count >= 1 ? _channelPeaks[0] : 0;
        double right = count >= 2 ? _channelPeaks[1] : left;
        double sum = left + right;
        double balance = sum > .0001 ? Math.Clamp((right - left) / sum, -1, 1) : 0;
        _balance += (balance - _balance) * (1 - Math.Exp(-dt / BalanceSeconds));
    }

    private static void EnsurePalette()
    {
        if (_paletteVersion == GlassSettings.ColorVersion) return;
        _paletteVersion = GlassSettings.ColorVersion;
        var body = HologramVisualizer.Tint(BaseBody);
        var core = HologramVisualizer.Tint(BaseCore);
        var tip = HologramVisualizer.Tint(BaseTip);
        FrontBrushes = new Brush[Shades][];
        SideBrushes = new Brush[Shades][];
        TopBrushes = new Brush[Shades][];
        RimBrushes = new Brush[Shades][];
        for (int s = 0; s < Shades; s++)
        {
            // low cubes in the deep body colour, the high ones paling towards the icy tip
            double k = s / (Shades - 1.0);
            var face = k < .5 ? Mix(body, core, k * 2) : Mix(core, tip, (k - .5) * 1.4);
            // tinted glass: the light behind shows through the faces, the rims carry the shape
            FrontBrushes[s] = Ladder(face, .34);
            SideBrushes[s] = Ladder(Mix(face, Colors.Black, .42), .46);
            TopBrushes[s] = Ladder(Mix(face, tip, .38), .64);
            RimBrushes[s] = Ladder(Mix(face, tip, .42), .64);
        }

        GlossBrushes = Ladder(tip, .10);
        StreakBrushes = Ladder(tip, .32);

        GlintBrushes = Ladder(Colors.White, .85);
        var pool = new RadialGradientBrush();
        pool.GradientStops.Add(new GradientStop(core, 0));
        pool.GradientStops.Add(new GradientStop(Color.FromArgb(0, core.R, core.G, core.B), 1));
        pool.Freeze();
        PoolBrushes = new Brush[16];
        for (int a = 0; a < PoolBrushes.Length; a++)
        {
            var brush = pool.Clone();
            brush.Opacity = .45 * a / (PoolBrushes.Length - 1.0);
            brush.Freeze();
            PoolBrushes[a] = brush;
        }
    }

    private static Brush[] Ladder(Color color, double max)
    {
        var brushes = new Brush[Grades];
        for (int g = 0; g < Grades; g++)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * max * g / (Grades - 1.0)), color.R, color.G, color.B));
            brush.Freeze();
            brushes[g] = brush;
        }

        return brushes;
    }

    private static Color Mix(Color a, Color b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromRgb((byte)(a.R + ((b.R - a.R) * k)), (byte)(a.G + ((b.G - a.G) * k)), (byte)(a.B + ((b.B - a.B) * k)));
    }

    private static Batch[][] NewBatches()
    {
        var set = new Batch[Shades][];
        for (int s = 0; s < Shades; s++) set[s] = NewRow();
        return set;
    }

    private static Batch[] NewRow()
    {
        var row = new Batch[Grades];
        for (int g = 0; g < Grades; g++) row[g] = new Batch();
        return row;
    }

    private static double ValueNoise(double x, int seed)
    {
        double floor = Math.Floor(x);
        int i = (int)floor;
        double f = x - floor;
        double smooth = f * f * (3 - (2 * f));
        double a = Hash01(i, seed);
        double b = Hash01(i + 1, seed);
        return a + ((b - a) * smooth);
    }

    private static double Hash01(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (seed * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215.0;
        }
    }

    /// <summary>One brush worth of quads, built into a single geometry per frame.</summary>
    private sealed class Batch
    {
        private StreamGeometry? _geometry;
        private StreamGeometryContext? _context;

        public void Begin()
        {
            _geometry = null;
            _context = null;
        }

        public void Quad(Point[] q)
        {
            Start(q[0]);
            _context!.LineTo(q[1], false, false);
            _context.LineTo(q[2], false, false);
            _context.LineTo(q[3], false, false);
        }

        /// <summary>A straight line <paramref name="half"/> wide either side, as a thin quad.</summary>
        public void Line(Point from, Point to, double half)
        {
            var d = to - from;
            if (d.LengthSquared < 1e-6) return;
            d.Normalize();
            var n = new Vector(d.Y, -d.X) * half;
            Start(from + n);
            _context!.LineTo(to + n, false, false);
            _context.LineTo(to - n, false, false);
            _context.LineTo(from - n, false, false);
        }

        public void Fill(DrawingContext dc, Brush brush)
        {
            if (_context is null) return;
            _context.Close();
            _context = null;
            _geometry!.Freeze();
            dc.DrawGeometry(brush, null, _geometry);
        }

        private void Start(Point start)
        {
            if (_context is null)
            {
                _geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                _context = _geometry.Open();
            }

            _context.BeginFigure(start, true, true);
        }
    }
}
