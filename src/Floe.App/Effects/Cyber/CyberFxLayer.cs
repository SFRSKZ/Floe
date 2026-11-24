using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// One overlay for everything the cyber themes play over the dock: timed runs from a click, a sleep or a
/// notification, and an open-ended overlay for the hover and the drag. It only hooks the render loop while
/// something is showing. Everything is drawn in hard steps, without blur.
/// </summary>
public sealed class CyberFxLayer : FrameworkElement
{
    public sealed class Run
    {
        internal double Seconds, Elapsed;
        internal Action<DrawingContext, double>? Draw;
        internal Action<double>? Tick;
        internal Action? Done;

        /// <summary>Stays inside the bar, so the click-through does not have to open up above it.</summary>
        public bool Inside { get; init; }
    }

    private const string Noise = "#%&/01X$7A";

    private readonly List<Run> _runs = new();
    private Action<DrawingContext, double>? _underlay;
    private Action<DrawingContext, double>? _overlay;
    private bool _hooked, _frozen;
    private double _last = -1, _time;

    public CyberFxLayer()
    {
        IsHitTestVisible = false;
        Unloaded += (_, _) => Unhook();
    }

    /// <summary>Whether a timed run is showing; those draw above the bar, outside its click-through region.</summary>
    public bool Busy => _runs.Any(run => !run.Inside);

    /// <summary>Elapsed animation clock time in seconds for the active effect layer.</summary>
    internal double Time => _time;

    /// <summary>Live layer drawn before timed runs (click hex dump) and overlay (slot frame / scan tags).</summary>
    public Action<DrawingContext, double>? Underlay
    {
        get => _underlay;
        set
        {
            if (_underlay == value) return;
            _underlay = value;
            if (value is not null) Hook();
            InvalidateVisual();
        }
    }

    public Action<DrawingContext, double>? Overlay
    {
        get => _overlay;
        set
        {
            if (_overlay == value) return;
            _overlay = value;
            if (value is not null) Hook();
            InvalidateVisual();
        }
    }

    public Run Play(double seconds, Action<DrawingContext, double>? draw, Action<double>? tick = null, Action? done = null, bool inside = false)
    {
        var run = new Run { Seconds = seconds, Draw = draw, Tick = tick, Done = done, Inside = inside };
        _runs.Add(run);
        tick?.Invoke(0);
        Hook();
        return run;
    }

    public void Cancel(Run? run)
    {
        if (run is not null && _runs.Remove(run)) InvalidateVisual();
    }

    /// <summary>Holds every run at one moment, for a still of the effect.</summary>
    internal void Seek(double progress)
    {
        _frozen = true;
        Unhook();
        foreach (var run in _runs)
        {
            run.Elapsed = run.Seconds * progress;
            run.Tick?.Invoke(progress);
        }

        _time = progress * 3;
        InvalidateVisual();
        UpdateLayout();
    }

    protected override void OnRender(DrawingContext dc)
    {
        _underlay?.Invoke(dc, _time);
        foreach (var run in _runs) run.Draw?.Invoke(dc, Math.Min(1, run.Elapsed / run.Seconds));
        _overlay?.Invoke(dc, _time);
    }

    private void Hook()
    {
        if (_hooked || _frozen) return;
        _hooked = true;
        _last = -1;
        CompositionTarget.Rendering += Advance;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        CompositionTarget.Rendering -= Advance;
    }

    private void Advance(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs r ? r.RenderingTime.TotalSeconds : _time;
        if (now == _last) return;
        double dt = _last < 0 ? 0 : Math.Min(.1, now - _last);
        _last = now;
        _time += dt;
        foreach (var run in _runs.ToArray())
        {
            run.Elapsed += dt;
            run.Tick?.Invoke(Math.Min(1, run.Elapsed / run.Seconds));
            if (run.Elapsed < run.Seconds) continue;
            _runs.Remove(run);
            run.Done?.Invoke();
        }

        InvalidateVisual();
        if (_runs.Count == 0 && _overlay is null && _underlay is null) Unhook();
    }

    public static double Segment(double p, double from, double to) => Math.Clamp((p - from) / (to - from), 0, 1);

    public static double Random(double seed)
    {
        var x = Math.Sin((seed * 127.1) + 11.7) * 43758.5453;
        return x - Math.Floor(x);
    }

    /// <summary>The text settles out of noise from the left; spaces stay spaces.</summary>
    public static string Decode(string text, double settled, double seed)
    {
        var count = (int)(settled * text.Length);
        var chars = text.ToCharArray();
        for (var i = count; i < chars.Length; i++)
        {
            if (chars[i] != ' ') chars[i] = Noise[(int)(Random(i + seed) * Noise.Length)];
        }

        return new string(chars);
    }

    [ThreadStatic]
    private static Dictionary<Color, SolidColorBrush>? _brushCache;

    [ThreadStatic]
    private static Dictionary<(Color Color, int ThicknessMillipx), Pen>? _penCache;

    [ThreadStatic]
    private static Dictionary<(string Text, int SizeMillipx, Brush Brush, TextAlignment Align, double Dpi), FormattedText>? _textCache;

    private static readonly Typeface CyberBoldTypeface = new(CyberLook.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static SolidColorBrush Solid(Color color)
    {
        var cache = _brushCache ??= new Dictionary<Color, SolidColorBrush>(128);
        if (cache.TryGetValue(color, out var brush))
        {
            return brush;
        }

        if (cache.Count >= 512)
        {
            cache.Clear();
        }

        brush = new SolidColorBrush(color);
        brush.Freeze();
        cache[color] = brush;
        return brush;
    }

    public static Pen CachedPen(Color color, double thickness)
    {
        var cache = _penCache ??= new Dictionary<(Color, int), Pen>(128);
        var key = (color, (int)Math.Round(thickness * 1000.0));
        if (cache.TryGetValue(key, out var pen))
        {
            return pen;
        }

        if (cache.Count >= 512)
        {
            cache.Clear();
        }

        pen = new Pen(Solid(color), thickness);
        pen.Freeze();
        cache[key] = pen;
        return pen;
    }

    private const double SlotCut = 9;

    /// <summary>
    /// An inventory slot: a square-cornered plate with a rarity bar down its left edge, a bracket at the top right and a
    /// cut bottom right corner with its stripe.
    /// </summary>
    public static void ItemFrame(DrawingContext dc, Rect r, Color accent, Color cyan, Color ink, bool hot = false, double bar = 7)
    {
        const double cut = 9, tick = 3;
        var outline = Polygon(new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom - cut), new Point(r.Right - cut, r.Bottom), new Point(r.Left, r.Bottom));
        dc.DrawGeometry(Solid(ink), null, outline);

        dc.DrawGeometry(null, CachedPen(hot ? cyan : Color.FromArgb(0x80, cyan.R, cyan.G, cyan.B), hot ? 2 : 1), outline);

        dc.DrawRectangle(Solid(accent), null, new Rect(r.Left, r.Top, bar, r.Height));
        var notch = CachedPen(ink, 1);
        for (double y = r.Top + 7; y < r.Top + 16; y += tick) dc.DrawLine(notch, new Point(r.Left + 2, y), new Point(r.Left + bar - 2, y));

        var bracket = CachedPen(cyan, 2);
        dc.DrawLine(bracket, new Point(r.Right - 10, r.Top), new Point(r.Right, r.Top));
        dc.DrawLine(bracket, new Point(r.Right, r.Top - 1), new Point(r.Right, r.Top + 6));
        dc.DrawGeometry(Solid(cyan), null, Polygon(new Point(r.Right, r.Bottom - cut - 6), new Point(r.Right, r.Bottom - cut), new Point(r.Right - cut, r.Bottom), new Point(r.Right - cut - 6, r.Bottom)));
    }

    /// <summary>
    /// The lines of <see cref="ItemFrame"/> around an icon, drawn over the icons so that its neighbours cannot hide them;
    /// <see cref="SlotOutline"/> fills it from under the icon. Its dark edge carries it across a dock of any colour and a
    /// neighbour of any shape.
    /// </summary>
    public static void SlotFrame(DrawingContext dc, Rect r, Color accent, Color cyan, Color ink)
    {
        const double cut = SlotCut, bar = 5, tick = 3;
        var outline = SlotOutline(r);
        r = Snap(r);
        var inkPen1 = CachedPen(ink, 1);
        dc.DrawGeometry(null, CachedPen(ink, 3), outline);
        dc.DrawGeometry(null, CachedPen(cyan, 1), outline);

        dc.DrawRectangle(Solid(accent), inkPen1, new Rect(r.Left - 2, r.Top, bar, r.Height));
        for (double y = r.Top + 7; y < r.Top + 16; y += tick) dc.DrawLine(inkPen1, new Point(r.Left - 2, y), new Point(r.Left - 2 + bar, y));

        dc.DrawGeometry(Solid(cyan), inkPen1, Polygon(new Point(r.Right, r.Bottom - cut - 6), new Point(r.Right, r.Bottom - cut), new Point(r.Right - cut, r.Bottom), new Point(r.Right - cut - 6, r.Bottom)));
    }

    /// <summary>The frame's shape, snapped to whole pixels so the lines stay sharp.</summary>
    public static Geometry SlotOutline(Rect r)
    {
        r = Snap(r);
        return Polygon(new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom - SlotCut), new Point(r.Right - SlotCut, r.Bottom), new Point(r.Left, r.Bottom));
    }

    private static Rect Snap(Rect r) => new(Math.Round(r.X) + .5, Math.Round(r.Y) + .5, Math.Round(r.Width), Math.Round(r.Height));

    /// <summary>The icon's silhouette in one colour.</summary>
    public static void Silhouette(DrawingContext dc, ImageSource icon, Rect r, Brush brush)
    {
        var mask = new ImageBrush(icon) { ViewportUnits = BrushMappingMode.Absolute, Viewport = r };
        mask.Freeze();
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(brush, null, r);
        dc.Pop();
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry Plate(Rect r, double chamfer)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(r.Left + chamfer, r.Top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(r.Right, r.Top), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Right, r.Bottom - chamfer), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Right - chamfer, r.Bottom), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Left, r.Bottom), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Left, r.Top + chamfer), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    public void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, TextAlignment align = TextAlignment.Left, bool upright = false)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var cache = _textCache ??= new Dictionary<(string, int, Brush, TextAlignment, double), FormattedText>(128);
        var key = (text, (int)Math.Round(size * 100.0), brush, align, dpi);
        if (!cache.TryGetValue(key, out var format))
        {
            if (cache.Count >= 256)
            {
                cache.Clear();
            }

            format = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, CyberBoldTypeface, size, brush, dpi)
            {
                TextAlignment = align,
            };
            cache[key] = format;
        }
        // a line of text keeps to its card or band and reads down a side like a book's spine; a glyph or two (a hex
        // byte, a mark) and the clock stand upright
        var turn = upright || text.Length <= 2 ? Upright : Spine;
        if (turn.IsIdentity)
        {
            dc.DrawText(format, new Point(x, y));
            return;
        }

        // the layer is turned with a dock on another edge: each line is turned back about its own middle
        double left = align switch { TextAlignment.Center => x - (format.Width / 2), TextAlignment.Right => x - format.Width, _ => x };
        var middle = new Point(left + (format.Width / 2), y + (format.Height / 2));
        var about = Matrix.Identity;
        about.Translate(-middle.X, -middle.Y);
        about.Append(turn);
        about.Translate(middle.X, middle.Y);
        dc.PushTransform(new MatrixTransform(about));
        dc.DrawText(format, new Point(x, y));
        dc.Pop();
    }

    /// <summary>The turn that puts text upright again when the dock (and this layer with it) stands on another edge.</summary>
    public Matrix Upright { get; set; } = Matrix.Identity;

    /// <summary>The turn for a line of text on a side: none on the left, and the right edge's fold undone so it does not mirror.</summary>
    public Matrix Spine { get; set; } = Matrix.Identity;

    public static void Corners(DrawingContext dc, Rect r, double length, Pen pen)
    {
        foreach (var (x, y, dx, dy) in new[] { (r.Left, r.Top, 1, 1), (r.Right, r.Top, -1, 1), (r.Right, r.Bottom, -1, -1), (r.Left, r.Bottom, 1, -1) })
        {
            dc.DrawLine(pen, new Point(x, y), new Point(x + (dx * length), y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + (dy * length)));
        }
    }
}
