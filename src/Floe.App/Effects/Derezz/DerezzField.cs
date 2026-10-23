using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Floe.App.Effects.Derezz;

/// <summary>
/// Tron Legacy derezz of a snapshot. Ahead of the front the picture turns into a lattice: each cell
/// dims and its edges light up, with holes opening where cells let go early. At the front the cubes
/// break loose and fall, tumbling a little, each one carrying the colour of the picture it came from,
/// until they drop out of the window. Played as a rezz the same simulation runs backwards: cubes rise
/// into place and the lattice closes.
/// Cube faces are batched per quantised colour, shade and alpha grade; <see cref="Render"/> also hands
/// the lit edges to a second context so the host can bloom them with a blur.
/// </summary>
public sealed class DerezzField
{
    private const double Lattice = .24;
    private const double Gravity = 560;
    private const double FlashTime = .08;
    private const double Life = 1.1;
    private const int Grades = 8;
    private const double SolidAlpha = .05;
    // Cells above this share of the hash catch the light hard and bloom; the rest only rim and glint softly.
    private const double Bright = .85;
    // Cube colours snap to this many levels per channel so faces still batch into a few geometries.
    private const int Levels = 8;
    private const double FaceAlpha = .8;
    private const double Saturation = 1.7;

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

    private static readonly Color Ink = Color.FromRgb(0x03, 0x08, 0x10);
    private static readonly Brush[] ShellFills = CreateLadder(Ink, .22);
    // Liquid glass: clear rims, a white glint along the edge facing the light and a soft white bloom.
    private static readonly Brush[] FlashFills = CreateLadder(Colors.White, .55);
    // Lines are filled as ready-made outlines: a pen would make the render thread widen every edge, every frame.
    private const double LatticeWidth = .5, EdgeWidth = .55, GlintWidth = 1.1, GlowWidth = 1.6;
    private static readonly Brush[] LatticeInk = CreateLadder(Color.FromRgb(0xEE, 0xF6, 0xFF), .6);
    private static readonly Brush[] EdgeInk = CreateLadder(Colors.White, .7);
    private static readonly Brush[] GlintInk = CreateLadder(Colors.White, 1);
    private static readonly Brush[] GlowInk = CreateLadder(Color.FromRgb(0xD8, 0xE8, 0xFF), .32);
    private static readonly Vector3D Light = new Vector3D(-.45, -.8, .4) / new Vector3D(-.45, -.8, .4).Length;

    private readonly BitmapSource _source;
    private readonly Rect _bounds;
    private readonly double _cell;
    private readonly int _cols, _rows;
    private readonly bool _rezz;
    private readonly double _speed;
    private readonly double _span;
    private readonly double _floor;

    private readonly bool[] _solid;
    private readonly Rect[] _home;
    private readonly double[] _trigger, _spin, _size, _tilt, _lit, _gone;
    private readonly int[] _hue;
    private readonly Vector[] _velocity;
    private readonly Vector3D[] _axis;

    private readonly Point[] _projected = new Point[8];
    private readonly Point[] _quad = new Point[4];
    private readonly Point[] _hull = new Point[8];
    private readonly Point[] _sorted = new Point[8];
    private readonly Batch[] _shell = NewBatches(Grades);
    private readonly Batch[] _lattice = NewBatches(Grades);
    private readonly Dictionary<int, Batch> _faces = new();
    private readonly Dictionary<int, Brush> _faceBrushes = new();
    private readonly Batch[] _edges = NewBatches(Grades);
    private readonly Batch[] _flash = NewBatches(Grades);
    private readonly Batch[] _glint = NewBatches(Grades);
    private readonly Batch[] _glow = NewBatches(Grades);

    /// <param name="source">Snapshot of the content, any pixel density.</param>
    /// <param name="bounds">Where the snapshot sits, in the caller's coordinates.</param>
    /// <param name="cell">Edge of one cube in the caller's units.</param>
    /// <param name="origin">A derezz breaks out from here; a rezz settles here first.</param>
    /// <param name="floor">Cubes fade out as they fall past this line (the bottom of what the host can show).</param>
    /// <param name="rezz">True to assemble instead of break apart.</param>
    /// <param name="spread">Seconds of simulation the front takes to cross the content.</param>
    /// <param name="speed">Simulation seconds per real second.</param>
    public DerezzField(BitmapSource source, Rect bounds, double cell, Point origin, double floor, bool rezz,
        double spread = .9, double speed = 1, int seed = 0)
    {
        _bounds = bounds;
        _cell = cell;
        _rezz = rezz;
        _speed = speed;
        _floor = floor;
        _cols = Math.Max(1, (int)Math.Ceiling(bounds.Width / cell));
        _rows = Math.Max(1, (int)Math.Ceiling(bounds.Height / cell));
        int count = _cols * _rows;

        var pixels = ReadPixels(source, out int width, out int height);
        _source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        _source.Freeze();

        double scaleX = width / bounds.Width, scaleY = height / bounds.Height;
        _solid = new bool[count];
        _home = new Rect[count];
        _trigger = new double[count];
        _spin = new double[count];
        _size = new double[count];
        _tilt = new double[count];
        _lit = new double[count];
        _gone = new double[count];
        _hue = new int[count];
        _velocity = new Vector[count];
        _axis = new Vector3D[count];

        double reach = 1e-6;
        for (int i = 0; i < count; i++)
        {
            reach = Math.Max(reach, Math.Abs(Center(CellRect(i)).X - origin.X));
        }

        int salt = seed * 7919;
        for (int i = 0; i < count; i++)
        {
            var home = CellRect(i);
            _home[i] = home;
            int px = (int)Math.Floor((home.X - bounds.X) * scaleX);
            int py = (int)Math.Floor((home.Y - bounds.Y) * scaleY);
            int pw = Math.Clamp((int)Math.Ceiling(home.Width * scaleX), 1, width - px);
            int ph = Math.Clamp((int)Math.Ceiling(home.Height * scaleY), 1, height - py);
            if (px >= width || py >= height || AverageAlpha(pixels, width, px, py, pw, ph) < SolidAlpha)
            {
                continue;
            }

            _solid[i] = true;
            _hue[i] = AverageHue(pixels, width, px, py, pw, ph);
            var center = Center(home);
            double along = Math.Abs(center.X - origin.X) / reach;
            // Each column collapses from the bottom up, a little behind the sweep along the dock.
            double rise = (bounds.Bottom - center.Y) / Math.Max(1, bounds.Height);
            double early = Hash(i, salt + 1);
            double front = (spread * (rezz ? 1 - along : along)) + (.16 * rise) + ((Hash(i, salt + 2) - .5) * .08);
            // One cell in seven lets go early, which opens the holes in the lattice.
            if (early < .14)
            {
                front -= Lattice * (.4 + (.6 * Hash(i, salt + 3)));
            }

            _trigger[i] = Math.Max(Lattice, front + Lattice);
            _spin[i] = (.8 + (3.2 * Hash(i, salt + 4))) * (Hash(i, salt + 5) < .5 ? -1 : 1);
            _size[i] = Hash(i, salt + 6) < .2 ? .62 : .94;
            _tilt[i] = .25 + (.45 * Hash(i, salt + 7));
            _lit[i] = Hash(i, salt + 8);
            double outward = Math.Sign(center.X - origin.X);
            _velocity[i] = new Vector((outward * 14 * along) + ((Hash(i, salt + 9) - .5) * 34), -(8 + (46 * Hash(i, salt + 10))));
            var axis = new Vector3D(1, (Hash(i, salt + 11) - .5) * .9, (Hash(i, salt + 12) - .5) * .9);
            axis.Normalize();
            _axis[i] = axis;
            // A cube is over once it has fallen out of sight, so a rezz starts with cubes already in view.
            double drop = floor + (cell * 1.5) - center.Y;
            double vy = _velocity[i].Y;
            _gone[i] = Math.Min(Life, (-vy + Math.Sqrt((vy * vy) + (2 * Gravity * Math.Max(0, drop)))) / Gravity);
            _span = Math.Max(_span, _trigger[i] + _gone[i]);
        }

        _span += .02;
        Duration = _span / _speed;
    }

    /// <summary>Real seconds until the effect has finished.</summary>
    public double Duration { get; }

    /// <summary>
    /// False when the live content stays on screen under the field (masked to <see cref="ShownRegion"/>),
    /// so a derezz must not paint the snapshot over it.
    /// </summary>
    public bool DrawIntact { get; set; } = true;

    /// <summary>The cells that are still, or again, part of the picture at <paramref name="time"/>.</summary>
    public Geometry ShownRegion(double time)
    {
        double tau = SimTime(time);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddRuns(g, i => _solid[i] && tau < _trigger[i]);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Share of the picture's cells in place at <paramref name="time"/>, 0 to 1.</summary>
    public double ShownShare(double time)
    {
        double tau = SimTime(time);
        int solid = 0, shown = 0;
        for (int i = 0; i < _solid.Length; i++)
        {
            if (!_solid[i]) continue;
            solid++;
            if (tau < _trigger[i]) shown++;
        }

        return solid == 0 ? 0 : (double)shown / solid;
    }

    /// <summary>Everything in <paramref name="outer"/> around the cell grid.</summary>
    public Geometry OutsideRegion(Rect outer)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddRect(g, new Rect(outer.Left, outer.Top, outer.Width, Math.Max(0, _bounds.Top - outer.Top)));
            AddRect(g, new Rect(outer.Left, _bounds.Bottom, outer.Width, Math.Max(0, outer.Bottom - _bounds.Bottom)));
            AddRect(g, new Rect(outer.Left, _bounds.Top, Math.Max(0, _bounds.Left - outer.Left), _bounds.Height));
            AddRect(g, new Rect(_bounds.Right, _bounds.Top, Math.Max(0, outer.Right - _bounds.Right), _bounds.Height));
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Where the live content should show at <paramref name="time"/> inside <paramref name="outer"/>:
    /// everything except the cells this field is still holding (derezz) or has not yet put back (rezz).
    /// </summary>
    public Geometry LiveRegion(double time, Rect outer)
    {
        double tau = SimTime(time);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddRect(g, new Rect(outer.Left, outer.Top, outer.Width, Math.Max(0, _bounds.Top - outer.Top)));
            AddRect(g, new Rect(outer.Left, _bounds.Bottom, outer.Width, Math.Max(0, outer.Bottom - _bounds.Bottom)));
            AddRect(g, new Rect(outer.Left, _bounds.Top, Math.Max(0, _bounds.Left - outer.Left), _bounds.Height));
            AddRect(g, new Rect(_bounds.Right, _bounds.Top, Math.Max(0, outer.Right - _bounds.Right), _bounds.Height));
            AddRuns(g, i => !Held(i, tau));
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>The cells that have broken loose (derezz) or are still on their way back (rezz).</summary>
    public Geometry LooseRegion(double time)
    {
        double tau = SimTime(time);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            AddRuns(g, i => _solid[i] && tau >= _trigger[i]);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Draws the effect; the lit edges are drawn again into <paramref name="glow"/> for blooming.</summary>
    public void Render(DrawingContext dc, DrawingContext? glow, double time)
    {
        double tau = SimTime(time);
        if (!_rezz && DrawIntact)
        {
            var intact = new StreamGeometry();
            using (var g = intact.Open())
            {
                AddRuns(g, i => _solid[i] && tau < _trigger[i]);
            }

            intact.Freeze();
            dc.PushClip(intact);
            dc.DrawImage(_source, _bounds);
            dc.Pop();
        }

        Begin(_shell, _lattice, _edges, _flash, _glint, _glow);
        foreach (var batch in _faces.Values) batch.Begin();
        for (int i = 0; i < _solid.Length; i++)
        {
            if (!_solid[i])
            {
                continue;
            }

            double u = tau - _trigger[i];
            if (u < -Lattice || u >= _gone[i])
            {
                continue;
            }

            if (u < 0)
            {
                LatticeCell(i, 1 + (u / Lattice));
            }
            else
            {
                FallingCube(i, u);
            }
        }

        Fill(dc, _shell, ShellFills);
        Fill(dc, _lattice, LatticeInk);
        foreach (var (key, batch) in _faces)
        {
            batch.Fill(dc, FaceBrush(key));
        }

        Fill(dc, _edges, EdgeInk);
        Fill(dc, _glint, GlintInk);
        Fill(dc, _flash, FlashFills);
        if (glow is not null)
        {
            Fill(glow, _glow, GlowInk);
            Fill(glow, _flash, FlashFills);
        }
    }

    // Derezz runs forwards; a rezz runs the same simulation backwards, faster.
    private double SimTime(double time) => _rezz ? _span - (time * _speed) : time * _speed;

    // Held cells show the picture (derezz) or the live content has not got them back yet (rezz).
    private bool Held(int i, double tau) => _solid[i] && (_rezz ? tau >= _trigger[i] : tau < _trigger[i]);

    // In place, still part of the picture: the cell dims and its glass edges catch the light.
    private void LatticeCell(int i, double k)
    {
        var r = _home[i];
        double inset = _cell * .04;
        _quad[0] = new Point(r.Left + inset, r.Top + inset);
        _quad[1] = new Point(r.Right - inset, r.Top + inset);
        _quad[2] = new Point(r.Right - inset, r.Bottom - inset);
        _quad[3] = new Point(r.Left + inset, r.Bottom - inset);
        double ease = k * k * (3 - (2 * k));
        _shell[Grade(ease)].Quad(_quad, true);
        double light = Math.Min(1, k * 1.6);
        bool bright = _lit[i] > Bright;
        _lattice[Grade(light * (bright ? .9 : .18 + (.2 * _lit[i])))].Outline(_quad, LatticeWidth / 2);
        if (bright)
        {
            _glint[Grade(light * .8)].Segment(_quad[0], _quad[1], GlintWidth / 2);
            _glow[Grade(light * .7)].Outline(_quad, GlowWidth / 2);
        }
    }

    private void FallingCube(int i, double u)
    {
        var home = _home[i];
        var v = _velocity[i];
        var center = Center(home) + new Vector(v.X * u, (v.Y * u) + (.5 * Gravity * u * u));
        double fade = Math.Clamp((_floor - center.Y) / (_cell * 3), 0, 1) * (1 - Math.Pow(u / Life, 3));
        if (fade <= .02)
        {
            return;
        }

        double size = _cell * _size[i] * (1 - (.25 * u / Life));
        double angle = (_tilt[i] * Math.Min(1, u / .12)) + (_spin[i] * u);
        var rotation = Matrix3D.Identity;
        rotation.Rotate(new Quaternion(_axis[i], angle * 180 / Math.PI));
        for (int c = 0; c < Corners.Length; c++)
        {
            var p = rotation.Transform(Corners[c]);
            _projected[c] = new Point(center.X + (p.X * size), center.Y + (p.Y * size));
        }

        double flash = Math.Pow(Math.Max(0, 1 - (u / FlashTime)), 1.4);
        int grade = Grade(fade);
        bool bright = _lit[i] > Bright;
        int edge = Grade(fade * (bright ? 1 : .3 + (.3 * _lit[i])));
        // one rim around the whole cube instead of one per face: the render thread tessellates every rim each frame,
        // and the faces' shading already shows the inner edges
        _edges[edge].Outline(_hull, Hull(_projected, _hull), EdgeWidth / 2);

        for (int f = 0; f < Faces.Length; f++)
        {
            var n = rotation.Transform(Normals[f]);
            if (n.Z <= .02)
            {
                continue;
            }

            for (int c = 0; c < 4; c++) _quad[c] = _projected[Faces[f][c]];
            // The face toward the viewer shows the cell's colour; faces turned up catch the light, the rest fall into shade.
            var shade = n.Y < -.3 ? (bright ? Shade.Lit : Shade.Top) : n.Z > .7 ? Shade.Front : Shade.Side;
            Face(_hue[i], shade, grade).Quad(_quad, true);

            // Faces turned to the light glint along their upper edge, sharply, like polished glass.
            double glint = Math.Pow(Math.Max(0, Vector3D.DotProduct(n, Light)), 3) * (bright ? 1 : .55);
            if (glint > .05)
            {
                int top = TopEdge(_quad);
                _glint[Grade(fade * glint)].Segment(_quad[top], _quad[(top + 1) % 4], GlintWidth / 2);
            }
            if (bright)
            {
                _glow[Grade(fade * .9)].Outline(_quad, GlowWidth / 2);
            }
            if (bright && flash > .02 && n.Z > .7)
            {
                _flash[Grade(flash)].Quad(_quad, true);
            }
        }
    }

    // Convex hull of the projected corners (monotone chain), counter-clockwise into hull; returns its corner count.
    private int Hull(Point[] points, Point[] hull)
    {
        int n = points.Length;
        Array.Copy(points, _sorted, n);
        for (int a = 1; a < n; a++)
        {
            var p = _sorted[a];
            int b = a - 1;
            while (b >= 0 && (_sorted[b].X > p.X || (_sorted[b].X == p.X && _sorted[b].Y > p.Y)))
            {
                _sorted[b + 1] = _sorted[b];
                b--;
            }

            _sorted[b + 1] = p;
        }

        static double Cross(Point o, Point a, Point b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        Span<Point> chain = stackalloc Point[(2 * 8) + 1];
        int k = 0;
        for (int a = 0; a < n; a++)
        {
            while (k >= 2 && Cross(chain[k - 2], chain[k - 1], _sorted[a]) <= 1e-9) k--;
            chain[k++] = _sorted[a];
        }

        for (int a = n - 2, low = k + 1; a >= 0; a--)
        {
            while (k >= low && Cross(chain[k - 2], chain[k - 1], _sorted[a]) <= 1e-9) k--;
            chain[k++] = _sorted[a];
        }

        int count = Math.Min(8, k - 1);
        for (int a = 0; a < count; a++) hull[a] = chain[a];
        return count;
    }

    private enum Shade { Front, Side, Top, Lit }

    private static int TopEdge(Point[] quad)
    {
        int best = 0;
        double y = double.MaxValue;
        for (int e = 0; e < 4; e++)
        {
            double mid = quad[e].Y + quad[(e + 1) % 4].Y;
            if (mid < y)
            {
                y = mid;
                best = e;
            }
        }

        return best;
    }

    private Batch Face(int hue, Shade shade, int grade)
    {
        int key = (hue << 8) | ((int)shade << 4) | grade;
        if (!_faces.TryGetValue(key, out var batch))
        {
            batch = new Batch();
            _faces[key] = batch;
        }

        return batch;
    }

    private Brush FaceBrush(int key)
    {
        if (_faceBrushes.TryGetValue(key, out var brush)) return brush;
        int hue = key >> 8, grade = key & 0xF;
        var shade = (Shade)((key >> 4) & 0xF);
        double step = 255.0 / (Levels - 1);
        double r = (hue / (Levels * Levels)) * step, g = ((hue / Levels) % Levels) * step, b = (hue % Levels) * step;
        (double mul, double lift) = shade switch
        {
            Shade.Side => (.62, 0),
            Shade.Top => (1.05, .22),
            Shade.Lit => (1, .55),
            _ => (1, 0),
        };
        byte Channel(double c) => (byte)Math.Clamp((c * mul) + ((255 - (c * mul)) * lift), 0, 255);
        var color = Color.FromArgb((byte)(255 * FaceAlpha * grade / (Grades - 1.0)), Channel(r), Channel(g), Channel(b));
        brush = new SolidColorBrush(color);
        brush.Freeze();
        _faceBrushes[key] = brush;
        return brush;
    }

    private static Point Center(Rect r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private Rect CellRect(int i)
    {
        int col = i % _cols, row = i / _cols;
        double x = _bounds.X + (col * _cell), y = _bounds.Y + (row * _cell);
        return new Rect(x, y, Math.Min(_cell, _bounds.Right - x), Math.Min(_cell, _bounds.Bottom - y));
    }

    // Vertical runs per column keep a region down to a few hundred rectangles.
    private void AddRuns(StreamGeometryContext g, Func<int, bool> inside)
    {
        for (int col = 0; col < _cols; col++)
        {
            int start = -1;
            for (int row = 0; row <= _rows; row++)
            {
                bool on = row < _rows && inside((row * _cols) + col);
                if (on && start < 0)
                {
                    start = row;
                }
                else if (!on && start >= 0)
                {
                    var top = CellRect((start * _cols) + col);
                    var bottom = CellRect(((row - 1) * _cols) + col);
                    AddRect(g, new Rect(top.TopLeft, bottom.BottomRight));
                    start = -1;
                }
            }
        }
    }

    private static void AddRect(StreamGeometryContext g, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        g.BeginFigure(r.TopLeft, true, true);
        g.LineTo(r.TopRight, false, false);
        g.LineTo(r.BottomRight, false, false);
        g.LineTo(r.BottomLeft, false, false);
    }

    private static int Grade(double alpha) => Math.Clamp((int)Math.Round(alpha * (Grades - 1)), 0, Grades - 1);

    private static double Hash(int i, int salt)
    {
        unchecked
        {
            uint h = (uint)((i * 374761393) + (salt * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }

    private static byte[] ReadPixels(BitmapSource source, out int width, out int height)
    {
        var converted = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        width = converted.PixelWidth;
        height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static double AverageAlpha(byte[] pixels, int width, int x, int y, int w, int h)
    {
        long sum = 0;
        for (int row = y; row < y + h; row++)
        {
            for (int col = x; col < x + w; col++)
            {
                sum += pixels[(((row * width) + col) * 4) + 3];
            }
        }

        return sum / (255.0 * w * h);
    }

    // Mean colour of a cell, un-premultiplied and snapped to the palette.
    private static int AverageHue(byte[] pixels, int width, int x, int y, int w, int h)
    {
        long b = 0, g = 0, r = 0, a = 0;
        for (int row = y; row < y + h; row++)
        {
            for (int col = x; col < x + w; col++)
            {
                int k = ((row * width) + col) * 4;
                b += pixels[k];
                g += pixels[k + 1];
                r += pixels[k + 2];
                a += pixels[k + 3];
            }
        }

        if (a == 0) return 0;
        // A cell mixes the icon with the glass around it, so its colour is pushed back out of the grey.
        double R = r * 255.0 / a, G = g * 255.0 / a, B = b * 255.0 / a;
        double luma = (.3 * R) + (.59 * G) + (.11 * B);
        int Snap(double c) => (int)Math.Round(Math.Clamp(luma + ((c - luma) * Saturation), 0, 255) * (Levels - 1) / 255);
        return (Snap(R) * Levels * Levels) + (Snap(G) * Levels) + Snap(B);
    }

    private static void Begin(params Batch[][] sets)
    {
        foreach (var set in sets)
        {
            foreach (var batch in set) batch.Begin();
        }
    }

    private static void Fill(DrawingContext dc, Batch[] set, Brush[] brushes)
    {
        for (int g = 1; g < Grades; g++) set[g].Fill(dc, brushes[g]);
    }

    private static Batch[] NewBatches(int count)
    {
        var batches = new Batch[count];
        for (int i = 0; i < count; i++) batches[i] = new Batch();
        return batches;
    }

    private static Brush[] CreateLadder(Color color, double max)
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

    /// <summary>One brush worth of quads, built into a single geometry per frame.</summary>
    private sealed class Batch
    {
        private StreamGeometry? _geometry;
        private StreamGeometryContext? _context;
        private bool _any;

        public void Begin()
        {
            _geometry = null;
            _context = null;
            _any = false;
        }

        public void Quad(Point[] points, bool filled)
        {
            if (_context is null)
            {
                _geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                _context = _geometry.Open();
            }

            _any = true;
            _context.BeginFigure(points[0], filled, true);
            _context.LineTo(points[1], true, false);
            _context.LineTo(points[2], true, false);
            _context.LineTo(points[3], true, false);
        }

        /// <summary>A quad's outline <paramref name="half"/> wide either side of its edges, as a ring to fill.</summary>
        public void Outline(Point[] q, double half) => Outline(q, 4, half);

        public void Outline(Point[] q, int count, double half)
        {
            if (count < 3) return;
            var centre = new Point();
            for (int c = 0; c < count; c++)
            {
                if ((q[(c + 1) % count] - q[c]).LengthSquared < 1e-6) return;
                centre.X += q[c].X / count;
                centre.Y += q[c].Y / count;
            }

            // the normal of the first edge tells which way is out for the whole (convex) polygon
            var first = Normal(q[0], q[1]);
            double side = Vector.Multiply(first, new Point((q[0].X + q[1].X) / 2, (q[0].Y + q[1].Y) / 2) - centre) < 0 ? -1 : 1;
            for (int c = 0; c < count; c++)
            {
                var prev = Normal(q[(c + count - 1) % count], q[c]) * side;
                var next = Normal(q[c], q[(c + 1) % count]) * side;
                // a miter: as far out along both normals as the edges themselves, kept short at sharp corners
                double reach = half / Math.Max(.35, 1 + Vector.Multiply(prev, next));
                var miter = (prev + next) * reach;
                _outer[c] = q[c] + miter;
                _inner[c] = q[c] - miter;
            }

            Begin(_outer[0]);
            for (int c = 1; c < count; c++) _context!.LineTo(_outer[c], false, false);
            // wound the other way, so the fill leaves the inside open
            _context!.BeginFigure(_inner[count - 1], true, true);
            for (int c = count - 2; c >= 0; c--) _context.LineTo(_inner[c], false, false);
        }

        /// <summary>A straight line <paramref name="half"/> wide either side, as a thin quad to fill.</summary>
        public void Segment(Point from, Point to, double half)
        {
            if ((to - from).LengthSquared < 1e-6) return;
            var n = Normal(from, to) * half;
            Begin(from + n);
            _context!.LineTo(to + n, false, false);
            _context.LineTo(to - n, false, false);
            _context.LineTo(from - n, false, false);
        }

        private readonly Point[] _outer = new Point[8];
        private readonly Point[] _inner = new Point[8];

        private static Vector Normal(Point from, Point to)
        {
            var d = to - from;
            d.Normalize();
            return new Vector(d.Y, -d.X);
        }

        private void Begin(Point start)
        {
            if (_context is null)
            {
                _geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                _context = _geometry.Open();
            }

            _any = true;
            _context.BeginFigure(start, true, true);
        }

        public void Fill(DrawingContext dc, Brush brush)
        {
            if (Close() is { } geometry) dc.DrawGeometry(brush, null, geometry);
        }

        private StreamGeometry? Close()
        {
            if (_context is not null)
            {
                _context.Close();
                _context = null;
                _geometry!.Freeze();
            }

            return _any ? _geometry : null;
        }
    }
}
