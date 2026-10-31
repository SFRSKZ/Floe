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
        if (_runs.Count == 0 && _overlay is null) Unhook();
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

    public static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Geometry Plate(Rect r, double chamfer)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new Point(r.Left + chamfer, r.Top) };
        figure.Segments.Add(new PolyLineSegment(new[]
        {
            new Point(r.Right, r.Top), new Point(r.Right, r.Bottom - chamfer), new Point(r.Right - chamfer, r.Bottom),
            new Point(r.Left, r.Bottom), new Point(r.Left, r.Top + chamfer),
        }, false));
        return new PathGeometry(new[] { figure });
    }

    public void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, TextAlignment align = TextAlignment.Left)
    {
        var format = new FormattedText(text, CultureInfo.GetCultureInfo("tr-TR"), FlowDirection.LeftToRight,
            new Typeface(CyberLook.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { TextAlignment = align };
        dc.DrawText(format, new Point(x, y));
    }

    public static void Corners(DrawingContext dc, Rect r, double length, Pen pen)
    {
        foreach (var (x, y, dx, dy) in new[] { (r.Left, r.Top, 1, 1), (r.Right, r.Top, -1, 1), (r.Right, r.Bottom, -1, -1), (r.Left, r.Bottom, 1, -1) })
        {
            dc.DrawLine(pen, new Point(x, y), new Point(x + (dx * length), y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + (dy * length)));
        }
    }
}
