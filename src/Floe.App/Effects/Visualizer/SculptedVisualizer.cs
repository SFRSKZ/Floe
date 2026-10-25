using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Effects.Visualizer;

public enum SculptedLook { Prism, Wheat, Ribbon }

/// <summary>Three studio looks driven by the real spectrum, within the shared repaint budget.</summary>
public sealed class SculptedVisualizer : FrameworkElement
{
    private readonly SpectrumFrame _frame = new(24);
    private readonly double[] _bands = new double[24];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private AudioSpectrum? _capture;
    private Rect _capsule = Rect.Empty;
    private SculptedLook _look;
    private bool _active, _hooked;
    private double _fade, _level, _bass, _time, _last = -1, _elapsed;
    private int _paletteVersion = -1;
    private Brush _foot = Brushes.Transparent;
    private Pen _ribbonCrest = new(Brushes.Transparent, .8);
    private Brush _glass = Brushes.Transparent, _side = Brushes.Transparent, _cap = Brushes.Transparent;
    private Brush _glint = Brushes.Transparent, _floorLine = Brushes.Transparent;
    private WriteableBitmap? _foldMap;
    private int[] _foldPixels = Array.Empty<int>();
    private readonly int[] _foldColors = new int[256];
    private Brush _pool = Brushes.Transparent, _baseHaze = Brushes.Transparent;
    private Pen _edge = new(Brushes.Transparent, .5), _crest = new(Brushes.Transparent, .8);
    private Pen _bloom = new(Brushes.Transparent, 3);
    private Pen _ribbonHalo = new(Brushes.Transparent, 9);
    private Pen _halo = new(Brushes.Transparent, 4);
    private Pen _foldSeam = new(Brushes.Transparent, .55);
    // the wheat field: three depth layers, with the far and middle rows kept simple
    private Brush _wheatHeadFar = Brushes.Transparent, _wheatHeadMid = Brushes.Transparent, _wheatHeadNear = Brushes.Transparent;
    private Brush _wheatGlint = Brushes.Transparent, _fieldHaze = Brushes.Transparent, _soilLine = Brushes.Transparent;
    private Brush _wheatBase = Brushes.Transparent;
    private Pen _wheatStemFar = new(Brushes.Transparent, 1), _wheatStemMid = new(Brushes.Transparent, 1), _wheatStemNear = new(Brushes.Transparent, 1);
    private Pen _wheatAwnMid = new(Brushes.Transparent, .5), _wheatAwnNear = new(Brushes.Transparent, .5);
    private Pen _wheatRachisNear = new(Brushes.Transparent, .3);
    private Pen _wheatOutline = new(Brushes.Transparent, .4);
    private Pen _wheatLeaf = new(Brushes.Transparent, .8);

    public SculptedVisualizer()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Loaded += (_, _) => { if (_active) Hook(); };
        Unloaded += (_, _) => { Unhook(); Release(); _fade = _level = _bass = _elapsed = 0; Array.Clear(_bands); _last = -1; };
    }

    public SculptedLook Look { get => _look; set { if (_look == value) return; _look = value; InvalidateVisual(); } }
    public Rect Capsule
    {
        get => _capsule;
        set
        {
            if (value.IsEmpty || !double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Width)
                || !double.IsFinite(value.Height) || value.Width < 2 || value.Height < 2) value = Rect.Empty;
            if (_capsule == value) return;
            _capsule = value; InvalidateVisual();
        }
    }
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) { _last = -1; _elapsed = 0; if (IsLoaded) Hook(); }
            else Release();
        }
    }
    internal SpectrumFrame? DebugFrame { get; set; }

    internal void Step(double dt)
    {
        dt = Math.Clamp(dt, 0, .1);
        _time += dt;
        var frame = DebugFrame ?? _frame;
        double level = Math.Clamp(frame.Level, 0, 1);
        _level += (level - _level) * (1 - Math.Exp(-dt / (level > _level ? .065 : .30)));
        _bass += (Math.Clamp(frame.Bass, 0, 1) - _bass) * (1 - Math.Exp(-dt / .12));
        for (int i = 0; i < _bands.Length; i++)
        {
            double target = i < frame.Bands.Length ? Math.Clamp(frame.Bands[i], 0, 1) : 0;
            _bands[i] += (target - _bands[i]) * (1 - Math.Exp(-dt / (target > _bands[i] ? .075 : .28)));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_capsule.IsEmpty) return;
        double opacity = (DebugFrame is null ? _fade : 1) * Math.Clamp(_level * 5, 0, 1);
        if (opacity < .002) return;
        Palette();
        // One opacity layer for the entire small strip, never one per strand/face.
        dc.PushOpacity(opacity);
        dc.PushClip(new RectangleGeometry(_capsule));
        // The solid looks keep a few pixels below the base for their reflection; the field grows
        // straight out of the strip instead.
        bool wheat = _look == SculptedLook.Wheat;
        double floor = _capsule.Bottom - (wheat ? 3 : 8);
        double height = Math.Max(1, Math.Min(floor - _capsule.Top - 2, _capsule.Width / 7.8));
        double left = _capsule.Left + 10, width = Math.Max(1, _capsule.Width - 20);
        double middle = left + (width * .5);
        if (wheat)
        {
            dc.DrawEllipse(_fieldHaze, null, new Point(middle, floor), width * .58, 22 + (_bass * 5));
            dc.DrawRectangle(_soilLine, null, new Rect(left, floor - .35, width, .7));
        }
        else
        {
            dc.DrawEllipse(_baseHaze, null, new Point(middle, floor), width * .46, 11 + (_bass * 3));
            dc.DrawEllipse(_pool, null, new Point(middle, floor), width * .48, 4 + (_bass * 2));
            dc.DrawRectangle(_floorLine, null, new Rect(left + (width * .015), floor - .45, width * .97, .9));
        }
        switch (_look)
        {
            case SculptedLook.Prism: Prisms(dc, left, floor, width, height); break;
            case SculptedLook.Wheat: WheatField(dc, left, floor, width, height); break;
            case SculptedLook.Ribbon: Ribbons(dc, left, floor, width, height); break;
        }
        if (!wheat) Reflect(dc, left, floor, width, height);
        dc.Pop(); dc.Pop();
    }

    // A short, fading mirror under the base. The vector modes repeat their own shapes, which
    // keeps the falls in step with the blades; the ribbon reuses its one cached fill bitmap so
    // the extra pass stays cheap. Everything is clipped to the shallow band below the floor.
    private void Reflect(DrawingContext dc, double left, double floor, double width, double height)
    {
        double depth = Math.Min(9, _capsule.Bottom - floor);
        if (depth < 2) return;
        var band = new RectangleGeometry(new Rect(_capsule.Left, floor, _capsule.Width, depth));
        band.Freeze();
        dc.PushClip(band);
        dc.PushOpacityMask(Fade(floor, depth));
        dc.PushTransform(new ScaleTransform(1, -1, 0, floor));
        dc.PushOpacity(_look == SculptedLook.Ribbon ? .16 : .12);
        if (_look == SculptedLook.Ribbon)
        {
            if (_foldMap is { } map) dc.DrawImage(map, new Rect(left, floor - height, width, height));
        }
        else
        {
            Prisms(dc, left, floor, width, height, reflection: true);
        }
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }

    private static LinearGradientBrush Fade(double top, double depth)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, top),
            EndPoint = new Point(0, top + depth),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(210, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        brush.Freeze();
        return brush;
    }

    private double Band(double x)
    {
        double at = Math.Clamp(x, 0, 1) * (_bands.Length - 1);
        int i = Math.Min(_bands.Length - 2, (int)at);
        return _bands[i] + (_bands[i + 1] - _bands[i]) * (at - i);
    }
    private static double Bell(double x, double centre, double width) => Math.Exp(-Math.Pow((x - centre) / width, 2));
    private static double Edge(double x) => Math.Pow(Math.Max(0, Math.Sin(x * Math.PI)), .65);

    private void Prisms(DrawingContext dc, double left, double floor, double width, double height, bool reflection = false)
    {
        int count = Math.Clamp((int)(width / 26), 16, 76);
        double pitch = width / count, w = pitch * .62, depth = Math.Min(4.0, pitch * .18);
        for (int i = 0; i < count; i++)
        {
            double u = (i + .5) / count;
            double silhouette = .13 + .78 * Bell(u, .48, .23) + .22 * Bell(u, .76, .12);
            double variation = .82 + .18 * Math.Sin(i * 2.39 + .18 * Math.Sin(_time));
            double h = Math.Max(1, height * Edge(u) * silhouette * variation * (.38 + .38 * _level + .32 * Band(u)));
            double x = left + i * pitch, y = floor - h;
            var front = Polygon(new(x, floor), new(x, y), new(x + w, y - depth * .4), new(x + w, floor));
            if (!reflection)
            {
                dc.DrawGeometry(null, _halo, front);
                dc.DrawGeometry(_glass, _edge, front);
                dc.DrawRectangle(_foot, null, new Rect(x + .6, floor - Math.Min(h, 14), Math.Max(.1, w - 1.2), Math.Min(h, 14)));
                dc.DrawRectangle(_glint, null, new Rect(x + (w * .27), y + 2, Math.Max(.7, w * .10), Math.Max(0, h - 4)));
                dc.DrawGeometry(_side, null, Polygon(new(x + w, floor), new(x + w, y - depth * .4), new(x + w + depth, y + depth * .15), new(x + w + depth, floor)));
                dc.DrawGeometry(_cap, null, Polygon(new(x, y), new(x + depth, y - depth), new(x + w + depth, y - depth * 1.4), new(x + w, y - depth * .4)));
                dc.DrawLine(_crest, new Point(x, y), new Point(x + w, y - depth * .4));
                // A few refractive catches; most of the glass stays quiet and transparent.
                dc.DrawEllipse(_pool, null, new Point(x + w * .5, floor), w * .78, 3.4 + (_bass * .8));
                if (i % 9 == 4)
                {
                    dc.DrawEllipse(_pool, null, new Point(x + w, y), w * .8, 2.2);
                    dc.DrawLine(_crest, new Point(x + w, y), new Point(x + w, Math.Min(floor, y + 3)));
                }
            }
            else
            {
                dc.DrawGeometry(_glass, null, front);
            }
        }
    }

    private static readonly double[] HeadAt = { 0, .14, .30, .46, .62, .78, 1 };
    private static readonly double[] HeadSpread = { .04, .62, .34, .56, .28, .40, .05 };

    private static readonly double[] HeadAtSimple = { 0, .35, .72, 1 };
    private static readonly double[] HeadSpreadSimple = { .04, .55, .38, .05 };

    // Three rows of wheat: the far and middle rows are simple silhouettes, the front row keeps
    // the serrated ear and awns. The rows are capped so a 1920-wide dock costs the same as the
    // preview strip; density comes from the layering, not from an unbounded stalk count.
    private void WheatField(DrawingContext dc, double left, double floor, double width, double height)
    {
        double level = Math.Clamp(_level, 0, 1);
        double breezeTime = _time * .62;
        double under = Math.Min(30, height * .22);
        dc.DrawRectangle(_wheatBase, null, new Rect(left, floor - under, width, under));
        for (int layer = 0; layer < 3; layer++)
        {
            int count = layer == 0
                ? Math.Clamp((int)(width / 12), 35, 85)
                : layer == 1
                    ? Math.Clamp((int)(width / 15), 30, 75)
                    : Math.Clamp((int)(width / 19), 22, 60);
            double pitch = width / count;
            double baseY = floor - ((2 - layer) * 1.2);
            double scale = layer == 0 ? .58 : layer == 1 ? .78 : 1.0;
            var stems = new StreamGeometry();
            var leaves = new StreamGeometry();
            var awns = new StreamGeometry();
            var heads = new StreamGeometry { FillRule = FillRule.Nonzero };
            var glints = new StreamGeometry { FillRule = FillRule.Nonzero };
            var rachis = new StreamGeometry();
            using (var s = stems.Open())
            using (var l = leaves.Open())
            using (var a = awns.Open())
            using (var h = heads.Open())
            using (var g = glints.Open())
            using (var r = rachis.Open())
            {
                for (int i = 0; i < count; i++)
                {
                    double u = (i + .5) / count;
                    double grain = Grain(i, layer + 1);
                    double grain2 = Grain(i, layer + 11);
                    if (Grain(i, layer + 21) < .04) continue;
                    double x = left + (i * pitch) + (pitch * .5) + ((grain - .5) * pitch * .45);
                    if (x < left + 2 || x > left + width - 2) continue;

                    double patch = .90 + (.12 * Math.Sin((u * 6.0) + (layer * 1.7))) + (.04 * Math.Sin((u * 14.0) + layer));
                    double hgt = height * (.34 + (.22 * level) + (.08 * Band(u))) * (.76 + (.32 * grain2)) * patch * scale;
                    if (grain2 > .92) hgt *= 1.12;
                    hgt = Math.Clamp(hgt, 8, height * .75);

                    double breeze = Math.Sin(breezeTime + (x * .0105) + (layer * 1.85) + (grain * .55));
                    double gust = Math.Sin((_time * .21) - (x * .0025) + 1.2) * _bass;
                    double sway = (hgt * (.05 + (.06 * level)) * breeze) + (hgt * .10 * gust) + ((grain - .5) * 4);
                    var root = new Point(x, baseY);
                    var tip = new Point(x + sway, baseY - hgt);
                    if (layer == 2)
                    {
                        double side = grain > .5 ? 1 : -1;
                        double leaf = hgt * (.30 + (.10 * grain2));
                        l.BeginFigure(root, false, false);
                        l.QuadraticBezierTo(new Point(x + (side * leaf * .45), baseY - (leaf * .55)),
                            new Point(x + (side * leaf), baseY - leaf), true, false);
                    }
                    s.BeginFigure(root, false, false);
                    s.QuadraticBezierTo(new Point(x + (sway * .42), baseY - (hgt * .58)), tip, true, false);

                    double dx = tip.X - (x + (sway * .42)), dy = tip.Y - (baseY - (hgt * .58));
                    double len = Math.Sqrt((dx * dx) + (dy * dy));
                    if (len < .001) { dx = 0; dy = -1; len = 1; }
                    dx /= len; dy /= len;
                    double px = -dy, py = dx;

                    if (layer < 2)
                    {
                        double headLen = Math.Clamp(hgt * (layer == 0 ? .26 : .30), 4, layer == 0 ? 10 : 13) * (.90 + (.20 * grain));
                        double headWide = Math.Clamp(headLen * (layer == 0 ? .20 : .22), 1.8, layer == 0 ? 3.0 : 3.8) * (.85 + (.30 * grain2));
                        AddHeadSimple(h, tip, dx, dy, px, py, headLen, headWide);
                        if (layer == 1)
                        {
                            AddAwn(a, tip.X - (dx * headLen * .20), tip.Y - (dy * headLen * .20), dx, dy, px, py,
                                Math.Clamp(hgt * .26, 4, 12) * (.70 + (.50 * grain)), -.40, grain);
                        }
                    }
                    else
                    {
                        double headLen = Math.Clamp(hgt * .36, 8, 20) * (.90 + (.20 * grain));
                        double headWide = Math.Clamp(headLen * .26, 3.5, 7.2) * (.85 + (.30 * grain2));
                        AddHead(h, tip, dx, dy, px, py, headLen, headWide);
                        r.BeginFigure(new Point(tip.X - (dx * headLen * .04), tip.Y - (dy * headLen * .04)), false, false);
                        r.LineTo(new Point(tip.X - (dx * headLen * .96), tip.Y - (dy * headLen * .96)), true, false);

                        double awnX = tip.X - (dx * headLen * .20), awnY = tip.Y - (dy * headLen * .20);
                        double awnLen = Math.Clamp(hgt * .34, 5, 17) * (.70 + (.50 * grain));
                        AddAwn(a, awnX, awnY, dx, dy, px, py, awnLen, -.50, grain);
                        AddAwn(a, awnX, awnY, dx, dy, px, py, awnLen * .88, .50, grain);

                        if (Band(u) > .50 + (.15 * grain) && i % 4 == 0)
                        {
                            double gw = .55 + (.35 * grain2);
                            g.BeginFigure(new Point(tip.X + (px * gw), tip.Y + (py * gw)), true, true);
                            g.LineTo(new Point(tip.X + (dx * headLen * .14), tip.Y + (dy * headLen * .14)), true, false);
                            g.LineTo(new Point(tip.X - (px * gw), tip.Y - (py * gw)), true, false);
                            g.LineTo(new Point(tip.X - (dx * headLen * .05), tip.Y - (dy * headLen * .05)), true, false);
                        }
                    }
                }
            }
            stems.Freeze(); leaves.Freeze(); awns.Freeze(); heads.Freeze(); glints.Freeze(); rachis.Freeze();
            var stem = layer == 0 ? _wheatStemFar : layer == 1 ? _wheatStemMid : _wheatStemNear;
            var awn = layer == 0 ? null : layer == 1 ? _wheatAwnMid : _wheatAwnNear;
            var head = layer == 0 ? _wheatHeadFar : layer == 1 ? _wheatHeadMid : _wheatHeadNear;
            if (layer == 2) dc.DrawGeometry(null, _wheatLeaf, leaves);
            dc.DrawGeometry(null, stem, stems);
            if (awn is not null) dc.DrawGeometry(null, awn, awns);
            dc.DrawGeometry(head, layer == 2 ? _wheatOutline : null, heads);
            if (layer == 2)
            {
                dc.DrawGeometry(null, _wheatRachisNear, rachis);
                dc.DrawGeometry(_wheatGlint, null, glints);
            }
        }
    }

    // A deterministic per-stalk value, so the field is irregular but identical between frames.
    private static double Grain(int i, int seed)
    {
        double v = (Math.Sin((i * 12.9898) + (seed * 4.1414)) * 43758.5453) % 1;
        return v < 0 ? v + 1 : v;
    }

    // A simple four-point ear for the two rows behind the front; its silhouette is enough at
    // that distance and it keeps the dense rows cheap to build.
    private static void AddHeadSimple(StreamGeometryContext c, Point tip, double dx, double dy, double px, double py, double length, double wide)
    {
        c.BeginFigure(new Point(tip.X + (px * wide * HeadSpreadSimple[0]), tip.Y + (py * wide * HeadSpreadSimple[0])), true, true);
        for (int k = 1; k < HeadAtSimple.Length; k++)
        {
            double x = tip.X - (dx * length * HeadAtSimple[k]) + (px * wide * HeadSpreadSimple[k]);
            double y = tip.Y - (dy * length * HeadAtSimple[k]) + (py * wide * HeadSpreadSimple[k]);
            c.LineTo(new Point(x, y), true, false);
        }
        for (int k = HeadAtSimple.Length - 2; k >= 0; k--)
        {
            double x = tip.X - (dx * length * HeadAtSimple[k]) - (px * wide * HeadSpreadSimple[k]);
            double y = tip.Y - (dy * length * HeadAtSimple[k]) - (py * wide * HeadSpreadSimple[k]);
            c.LineTo(new Point(x, y), true, false);
        }
        c.LineTo(new Point(tip.X + (px * wide * HeadSpreadSimple[0]), tip.Y + (py * wide * HeadSpreadSimple[0])), true, false);
    }

    // The ear is a tapered, serrated blade along the stem's tangent. Building it as one
    // figure per ear keeps the whole foreground row in a single fill pass.
    private static void AddHead(StreamGeometryContext c, Point tip, double dx, double dy, double px, double py, double length, double wide)
    {
        c.BeginFigure(new Point(tip.X + (px * wide * HeadSpread[0]), tip.Y + (py * wide * HeadSpread[0])), true, true);
        for (int k = 1; k < HeadAt.Length; k++)
        {
            double x = tip.X - (dx * length * HeadAt[k]) + (px * wide * HeadSpread[k]);
            double y = tip.Y - (dy * length * HeadAt[k]) + (py * wide * HeadSpread[k]);
            c.LineTo(new Point(x, y), true, false);
        }
        for (int k = HeadAt.Length - 2; k >= 0; k--)
        {
            double x = tip.X - (dx * length * HeadAt[k]) - (px * wide * HeadSpread[k]);
            double y = tip.Y - (dy * length * HeadAt[k]) - (py * wide * HeadSpread[k]);
            c.LineTo(new Point(x, y), true, false);
        }
        c.LineTo(new Point(tip.X + (px * wide * HeadSpread[0]), tip.Y + (py * wide * HeadSpread[0])), true, false);
    }

    private static void AddAwn(StreamGeometryContext c, double x, double y, double dx, double dy, double px, double py, double length, double spread, double grain)
    {
        double cs = Math.Cos(spread), sn = Math.Sin(spread);
        double ux = (dx * cs) + (px * sn), uy = (dy * cs) + (py * sn);
        double endX = x + (ux * length) + (px * (grain - .5) * 2);
        double endY = y + (uy * length) + (py * (grain - .5) * 2);
        c.BeginFigure(new Point(x, y), false, false);
        c.LineTo(new Point(endX, endY), true, false);
    }

    private void Ribbons(DrawingContext dc, double left, double floor, double width, double height)
    {
        const int segments = 240;
        var crests = new StreamGeometry[3];
        StreamGeometry? seam = null;
        for (int layer = 2; layer >= 0; layer--)
        {
            var upper = new Point[segments + 1];
            var lower = new Point[segments + 1];
            double drift = Math.Sin(_time * .36 + layer * 1.8) * .025;
            double centre = layer == 0 ? .46 : layer == 1 ? .55 : .38;
            for (int i = 0; i <= segments; i++)
            {
                double u = i / (double)segments;
                double shape = (.36 * Bell(u, .22 + layer * .05 + drift, .085 + layer * .015)
                    + .86 * Bell(u, centre + drift, .12)
                    + .49 * Bell(u, .77 - layer * .045 - drift, .085 + layer * .012));
                double h = height * shape * Edge(u) * (.48 + .40 * _level + .12 * Band(u)) * (1 - layer * .12);
                double fold = height * .22 * Bell(u, .58 - layer * .08 + Math.Sin(_time * .3) * .02, .19) * Edge(u) * (.45 + .55 * _level);
                upper[i] = new Point(left + width * u, floor - h);
                lower[i] = new Point(left + width * u, floor - Math.Min(h * .85, fold));
            }
            PaintFold(dc, upper, lower, left, floor, width, height, layer);
            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(upper[0], false, false);
                for (int i = 1; i <= segments; i++) c.LineTo(upper[i], true, false);
            }
            line.Freeze();
            crests[layer] = line;
            if (layer == 0)
            {
                // the inner edge of the top fold: a quiet seam that gives the flat fill a crease
                var fold = new StreamGeometry();
                using (var c = fold.Open())
                {
                    c.BeginFigure(lower[0], false, false);
                    for (int i = 1; i <= segments; i++) c.LineTo(lower[i], true, false);
                }
                fold.Freeze(); seam = fold;
            }
        }
        if (seam is not null) dc.DrawGeometry(null, _foldSeam, seam);
        for (int layer = 2; layer >= 0; layer--)
        {
            dc.DrawGeometry(null, _ribbonHalo, crests[layer]);
            dc.DrawGeometry(null, _bloom, crests[layer]);
            dc.DrawGeometry(null, layer == 0 ? _ribbonCrest : _edge, crests[layer]);
        }
    }

    // Rasterise only the translucent interior. The cached small bitmaps avoid hundreds
    // of antialiased mesh seams and leave the crisp crest to the vector renderer.
    private void PaintFold(DrawingContext dc, Point[] upper, Point[] lower, double left, double floor, double width, double height, int layer)
    {
        int w = Math.Clamp((int)Math.Ceiling(width), 2, 1200), h = Math.Clamp((int)Math.Ceiling(height), 2, 160);
        if (_foldMap is not { } bitmap || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
        {
            bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            _foldMap = bitmap; _foldPixels = new int[w * h];
        }
        var pixels = _foldPixels; if (layer == 2) Array.Clear(pixels);
        double yOrigin = floor - height;
        for (int x = 0; x < w; x++)
        {
            double at = (x + .5) / w * (upper.Length - 1);
            int i = Math.Min(upper.Length - 2, (int)at); double f = at - i;
            double top = upper[i].Y + (upper[i + 1].Y - upper[i].Y) * f;
            double bottom = lower[i].Y + (lower[i + 1].Y - lower[i].Y) * f;
            double span = bottom - top;
            if (span < .01) continue;
            int start = Math.Clamp((int)Math.Floor((top - yOrigin) / height * h), 0, h - 1);
            int end = Math.Clamp((int)Math.Ceiling((bottom - yOrigin) / height * h), 0, h - 1);
            for (int y = start; y <= end; y++)
            {
                double py = yOrigin + (y + .5) / h * height;
                double t = (py - top) / span;
                double step = height / h;
                double coverage = Math.Clamp((Math.Min(bottom, py + step * .5) - Math.Max(top, py - step * .5)) / step, 0, 1);
                if (coverage <= 0) continue;
                uint color = (uint)_foldColors[Math.Clamp((int)(t * 255), 0, 255)];
                int a = (int)((color >> 24) * coverage);
                // a one-step ordered dither, so the dark translucent fill does not band into stripes
                a = Math.Clamp(a + ((((x + y) & 1) == 0) ? 1 : -1), 0, 255);
                int r = (int)(((color >> 16) & 255) * coverage);
                int g = (int)(((color >> 8) & 255) * coverage), b = (int)((color & 255) * coverage);
                uint previous = (uint)pixels[y * w + x]; int inverse = 255 - a;
                a += (int)(previous >> 24) * inverse / 255;
                r += (int)((previous >> 16) & 255) * inverse / 255;
                g += (int)((previous >> 8) & 255) * inverse / 255;
                b += (int)(previous & 255) * inverse / 255;
                pixels[y * w + x] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }
        if (layer == 0)
        {
            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            dc.DrawImage(bitmap, new Rect(left, yOrigin, width, height));
        }
    }

    private static StreamGeometry Polygon(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(points[0], true, true); for (int i = 1; i < points.Length; i++) c.LineTo(points[i], true, false); }
        g.Freeze(); return g;
    }
    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static LinearGradientBrush Gradient(Point start, Point end, params (Color color, double at)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var s in stops) b.GradientStops.Add(new GradientStop(s.color, s.at));
        b.Freeze(); return b;
    }
    private static Pen Pen(Color color, double width)
    {
        var p = new Pen(new SolidColorBrush(color), width); p.Freeze(); return p;
    }
    private static Pen Pen(Brush brush, double width)
    {
        var p = new Pen(brush, width); p.Freeze(); return p;
    }
    private void Palette()
    {
        if (_paletteVersion == GlassSettings.ColorVersion) return;
        _paletteVersion = GlassSettings.ColorVersion;
        var blue = HologramVisualizer.Tint(Color.FromRgb(85, 156, 225));
        var ice = HologramVisualizer.Tint(Color.FromRgb(218, 241, 255));
        _foot = Gradient(new(0, 0), new(0, 1), (Alpha(blue, 0), 0), (Alpha(blue, 12), .5), (Alpha(ice, 115), 1));
        var crestLight = Gradient(new(0, 0), new(1, 0), (Alpha(ice, 0), 0), (Alpha(ice, 130), .23), (Alpha(ice, 255), .46), (Alpha(ice, 150), .63), (Alpha(ice, 235), .78), (Alpha(ice, 0), 1));
        _ribbonCrest = new Pen(crestLight, 1.45); _ribbonCrest.Freeze();
        // The glass reads from the base up: a lit foot, a transparent body, a dark tip.
        _glass = Gradient(new(0, 1), new(0, 0), (Alpha(ice, 210), 0), (Alpha(blue, 145), .16), (Alpha(blue, 58), .56), (Alpha(blue, 0), 1));
        _side = Gradient(new(0, 0), new(1, 0), (Alpha(blue, 55), 0), (Alpha(ice, 10), 1));
        _cap = Gradient(new(0, 1), new(1, 0), (Alpha(blue, 78), 0), (Alpha(ice, 165), .62), (Alpha(blue, 62), 1));
        _glint = Gradient(new(0, 0), new(0, 1), (Alpha(ice, 0), 0), (Alpha(ice, 75), .50), (Alpha(ice, 185), 1));
        _floorLine = Gradient(new(0, 0), new(1, 0), (Alpha(ice, 0), 0), (Alpha(ice, 100), .50), (Alpha(ice, 0), 1));
        for (int i = 0; i < _foldColors.Length; i++)
        {
            double t = i / 255.0;
            int alpha = (int)(128 * Math.Exp(-t * 4.6) + 10 * Math.Sin(Math.PI * t));
            double highlight = .58 * Math.Exp(-t * 9);
            int r = (int)(blue.R + (ice.R - blue.R) * highlight) * alpha / 255;
            int g = (int)(blue.G + (ice.G - blue.G) * highlight) * alpha / 255;
            int b = (int)(blue.B + (ice.B - blue.B) * highlight) * alpha / 255;
            _foldColors[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
        }
        var pool = new RadialGradientBrush();
        pool.GradientStops.Add(new GradientStop(Alpha(ice, 95), 0));
        pool.GradientStops.Add(new GradientStop(Alpha(blue, 42), .24));
        pool.GradientStops.Add(new GradientStop(Alpha(blue, 0), 1)); pool.Freeze(); _pool = pool;
        var baseHaze = new RadialGradientBrush();
        baseHaze.GradientStops.Add(new GradientStop(Alpha(ice, 18), 0));
        baseHaze.GradientStops.Add(new GradientStop(Alpha(blue, 7), .52));
        baseHaze.GradientStops.Add(new GradientStop(Alpha(blue, 0), 1)); baseHaze.Freeze(); _baseHaze = baseHaze;
        _edge = Pen(Alpha(ice, 120), .55); _crest = Pen(Alpha(ice, 165), .85);
        _bloom = Pen(Alpha(blue, 55), 5.5);
        _ribbonHalo = Pen(Alpha(blue, 22), 9.0);
        _halo = Pen(Alpha(ice, 10), 3.8);
        _foldSeam = Pen(Alpha(ice, 62), .55);

        // Wheat: warm bronze stems, gold ears, pale awns. The tint still sets the hue, so a
        // custom dock colour turns the whole crop with it.
        var wheatStem = HologramVisualizer.Tint(Color.FromRgb(0x6E, 0x55, 0x28));
        var wheatFar = HologramVisualizer.Tint(Color.FromRgb(0x9A, 0x77, 0x38));
        var wheatGold = HologramVisualizer.Tint(Color.FromRgb(0xDB, 0xB4, 0x5C));
        var wheatLight = HologramVisualizer.Tint(Color.FromRgb(0xF7, 0xDD, 0x94));
        var wheatAwn = HologramVisualizer.Tint(Color.FromRgb(0xE6, 0xC7, 0x7E));
        _wheatHeadNear = Gradient(new(0, 0), new(1, 0), (Alpha(wheatGold, 235), 0), (Alpha(wheatLight, 255), .35), (Alpha(wheatGold, 245), .70), (Alpha(wheatFar, 215), 1));
        var headFar = new SolidColorBrush(Alpha(wheatFar, 125)); headFar.Freeze(); _wheatHeadFar = headFar;
        var headMid = new SolidColorBrush(Alpha(wheatGold, 195)); headMid.Freeze(); _wheatHeadMid = headMid;
        var fieldHaze = new RadialGradientBrush();
        fieldHaze.GradientStops.Add(new GradientStop(Alpha(wheatGold, 34), 0));
        fieldHaze.GradientStops.Add(new GradientStop(Alpha(wheatFar, 12), .52));
        fieldHaze.GradientStops.Add(new GradientStop(Alpha(wheatFar, 0), 1));
        fieldHaze.Freeze(); _fieldHaze = fieldHaze;
        _wheatBase = Gradient(new(0, 0), new(0, 1), (Alpha(wheatStem, 0), 0), (Alpha(wheatStem, 62), .55), (Alpha(wheatStem, 105), 1));
        _soilLine = Gradient(new(0, 0), new(1, 0), (Alpha(wheatStem, 0), 0), (Alpha(wheatStem, 85), .50), (Alpha(wheatStem, 0), 1));
        _wheatStemFar = Pen(Alpha(wheatStem, 85), .92);
        _wheatStemMid = Pen(Alpha(wheatStem, 125), 1.06);
        _wheatStemNear = Pen(Alpha(wheatStem, 195), 1.28);
        _wheatAwnMid = Pen(Alpha(wheatAwn, 75), .52);
        _wheatAwnNear = Pen(Alpha(wheatAwn, 150), .62);
        _wheatRachisNear = Pen(Alpha(wheatStem, 165), .40);
        _wheatOutline = Pen(Alpha(wheatStem, 210), .55);
        _wheatLeaf = Pen(Alpha(wheatStem, 145), .85);
        var glint = new SolidColorBrush(Alpha(wheatLight, 230)); glint.Freeze(); _wheatGlint = glint;
    }
    private void Hook() { if (_hooked) return; CompositionTarget.Rendering += RenderFrame; _hooked = true; }
    private void Unhook() { if (!_hooked) return; CompositionTarget.Rendering -= RenderFrame; _hooked = false; }
    private void Release() { _capture?.Dispose(); _capture = null; }
    private void RenderFrame(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs r ? r.RenderingTime.TotalSeconds : _clock.Elapsed.TotalSeconds;
        if (now <= _last) return;
        double dt = _last < 0 ? 1 / 60.0 : Math.Clamp(now - _last, .001, .1); _last = now;
        _elapsed += dt;
        if (_elapsed < VisualizerPacing.RepaintInterval) return;
        dt = Math.Min(_elapsed, .1); _elapsed = 0;
        if (_active) { _fade = Math.Min(1, _fade + dt / .22); _capture ??= new AudioSpectrum(); _capture.Read(_frame, dt); }
        else { _fade = Math.Max(0, _fade - dt / .4); if (_fade <= 0) { Unhook(); _last = -1; InvalidateVisual(); return; } }
        Step(dt); InvalidateVisual(); VisualizerPacing.MarkRepaint();
    }
}
