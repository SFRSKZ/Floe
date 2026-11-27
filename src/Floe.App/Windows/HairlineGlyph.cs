using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Floe.App.Windows;

/// <summary>
/// A glyph drawn on the screen's own pixel grid: every line is one device pixel wide and every lit pixel gets a dark
/// one pixel rim, so it stays sharp on the dock's grain at any display scale. Subclasses lay out lines in DIP; the
/// painter snaps them to whole pixels. Hover and press are two 0..1 values the subclass reads while drawing.
/// </summary>
public abstract class HairlineGlyph : FrameworkElement
{
    public static readonly DependencyProperty HoverProperty = DependencyProperty.Register(
        nameof(Hover), typeof(double), typeof(HairlineGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PressProperty = DependencyProperty.Register(
        nameof(Press), typeof(double), typeof(HairlineGlyph), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    protected static readonly Color Rim = Color.FromRgb(0x0A, 0x06, 0x12);

    private readonly Painter _painter = new();
    private Point _deviceOrigin = new(double.NaN, double.NaN);
    private double _deviceScale;

    protected HairlineGlyph(double width, double height)
    {
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        LayoutUpdated += (_, _) =>
        {
            // the drawing is laid on the pixel grid where the glyph stood; redraw when it lands somewhere else
            var (origin, scale) = DeviceFrame();
            if (origin != _deviceOrigin || scale != _deviceScale) InvalidateVisual();
        };
    }

    public double Hover
    {
        get => (double)GetValue(HoverProperty);
        set => SetValue(HoverProperty, value);
    }

    public double Press
    {
        get => (double)GetValue(PressProperty);
        set => SetValue(PressProperty, value);
    }

    /// <summary>The ink and accent the subclass draws with.</summary>
    protected Color Ink { get; private set; } = Colors.White;

    protected Color Accent { get; private set; } = Color.FromRgb(0x00, 0xFF, 0xD6);

    public void Paint(Color ink, Color accent)
    {
        Ink = ink;
        Accent = accent;
        InvalidateVisual();
    }

    /// <summary>
    /// The rim under every lit pixel: the dark one, or with <see cref="ContrastRim"/> the opposite of the ink, so a dark
    /// glyph keeps a light edge where the bar under it turns dark (the visualizer's light, a dark theme colour).
    /// </summary>
    protected virtual bool ContrastRim => false;

    private Color RimColor => ContrastRim ? Fade(Floe.App.Services.CyberLook.InkOn(Ink), .9) : Rim;

    /// <summary>Resizes the glyph's box; the subclass lays its lines out from <see cref="FrameworkElement.Width"/>.</summary>
    public void SetSize(double size)
    {
        if (Width == size && Height == size) return;
        Width = Height = size;
        InvalidateVisual();
    }

    /// <summary>Eases hover in or out.</summary>
    public void Engage(bool on) => Ease(HoverProperty, on, on ? HoverInMs : HoverOutMs);

    /// <summary>Snaps the pressed look in, or lets it go.</summary>
    public void Pressed(bool on) => Ease(PressProperty, on, on ? 70 : 160);

    protected virtual int HoverInMs => 220;

    protected virtual int HoverOutMs => 260;

    protected abstract void Draw(Painter p);

    protected override void OnRender(DrawingContext dc)
    {
        var (origin, scale) = DeviceFrame();
        _deviceOrigin = origin;
        _deviceScale = scale;

        // device pixel k (counted from the pixel the glyph's corner falls in) sits at (k - frac) / scale in DIP
        double fx = double.IsNaN(origin.X) ? 0 : origin.X - Math.Floor(origin.X);
        double fy = double.IsNaN(origin.Y) ? 0 : origin.Y - Math.Floor(origin.Y);
        dc.PushTransform(new MatrixTransform(1 / scale, 0, 0, 1 / scale, -fx / scale, -fy / scale));
        _painter.Begin(scale);
        Draw(_painter);
        _painter.Flush(dc, RimColor);
        dc.Pop();
    }

    private void Ease(DependencyProperty property, bool on, int ms)
    {
        var to = on ? 1.0 : 0.0;
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(property, null);
            SetValue(property, to);
            return;
        }

        BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private (Point Origin, double Scale) DeviceFrame()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.RootVisual is not Visual root || source.CompositionTarget is null)
        {
            return (new Point(double.NaN, double.NaN), Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        }

        var matrix = Matrix.Identity;
        if (TransformToAncestor(root) is Transform toRoot) matrix = toRoot.Value;
        matrix.Append(source.CompositionTarget.TransformToDevice);
        double scale = Math.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        if (scale <= 0.01 || double.IsNaN(scale)) scale = 1;
        var origin = matrix.Transform(new Point(0, 0));
        origin = new Point(Math.Round(origin.X, 3), Math.Round(origin.Y, 3));
        return (origin, Math.Round(scale, 4));
    }

    protected static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)Math.Round(a.A + ((b.A - a.A) * t)),
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    protected static Color Fade(Color c, double alpha) => Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);

    /// <summary>Collects one-pixel runs in device space and draws them rim first, ink on top.</summary>
    protected sealed class Painter
    {
        private readonly List<(Int32Rect Box, Color Color)> _fills = new();
        private readonly List<(Int32Rect Box, Color Color)> _lines = new();
        private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
        private double _scale = 1;
        private Rect? _clip;

        public double Scale => _scale;

        internal void Begin(double scale)
        {
            _scale = scale;
            _fills.Clear();
            _lines.Clear();
            _clip = null;
        }

        /// <summary>DIP to device pixel index.</summary>
        public int Px(double v) => (int)Math.Floor((v * _scale) + 1e-6);

        /// <summary>Restricts the following runs to a DIP box (null lifts it).</summary>
        public void Clip(Rect? box) => _clip = box;

        public void H(double x0, double x1, double y, Color c) => Add(_lines, Px(Math.Min(x0, x1)), Px(y), Px(Math.Max(x0, x1)), Px(y), c);

        public void V(double x, double y0, double y1, Color c) => Add(_lines, Px(x), Px(Math.Min(y0, y1)), Px(x), Px(Math.Max(y0, y1)), c);

        /// <summary>A diagonal as a pixel staircase.</summary>
        public void D(double x0, double y0, double x1, double y1, Color c)
        {
            int ax = Px(x0), ay = Px(y0), bx = Px(x1), by = Px(y1);
            int n = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            for (int i = 0; i <= n; i++)
            {
                int x = (int)Math.Round(ax + ((bx - ax) * (double)i / Math.Max(1, n)));
                int y = (int)Math.Round(ay + ((by - ay) * (double)i / Math.Max(1, n)));
                Add(_lines, x, y, x, y, c);
            }
        }

        /// <summary>A box outline whose sides are <paramref name="weight"/> device pixels thick, grown inward.</summary>
        public void Frame(double x0, double y0, double x1, double y1, Color c, int weight)
        {
            int ax = Px(x0), ay = Px(y0), bx = Px(x1), by = Px(y1), w = Math.Max(1, weight) - 1;
            Add(_lines, ax, ay, bx, ay + w, c);
            Add(_lines, ax, by - w, bx, by, c);
            Add(_lines, ax, ay, ax + w, by, c);
            Add(_lines, bx - w, ay, bx, by, c);
        }

        /// <summary>A box outline; <paramref name="cut"/> chamfers the top-left corner.</summary>
        public void Box(double x0, double y0, double x1, double y1, Color c, double cut = 0)
        {
            if (cut > 0)
            {
                H(x0 + cut, x1, y0, c);
                D(x0, y0 + cut, x0 + cut, y0, c);
                V(x0, y0 + cut, y1, c);
            }
            else
            {
                H(x0, x1, y0, c);
                V(x0, y0, y1, c);
            }

            H(x0, x1, y1, c);
            V(x1, y0, y1, c);
        }

        /// <summary>A solid block, drawn under the lines.</summary>
        public void Fill(double x0, double y0, double x1, double y1, Color c) => Add(_fills, Px(x0), Px(y0), Px(x1), Px(y1), c);

        private void Add(List<(Int32Rect, Color)> list, int x0, int y0, int x1, int y1, Color c)
        {
            if (c.A == 0) return;
            if (_clip is { } clip)
            {
                x0 = Math.Max(x0, Px(clip.Left));
                y0 = Math.Max(y0, Px(clip.Top));
                x1 = Math.Min(x1, Px(clip.Right));
                y1 = Math.Min(y1, Px(clip.Bottom));
            }

            if (x1 < x0 || y1 < y0) return;
            list.Add((new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), c));
        }

        internal void Flush(DrawingContext dc, Color rim)
        {
            // one rim pass under everything, so a rim never cuts through a neighbouring line
            foreach (var (box, c) in _fills) dc.DrawRectangle(Brush(Fade(rim, c.A / 255.0)), null, new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2));
            foreach (var (box, c) in _lines) dc.DrawRectangle(Brush(Fade(rim, c.A / 255.0)), null, new Rect(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2));
            foreach (var (box, c) in _fills) dc.DrawRectangle(Brush(c), null, new Rect(box.X, box.Y, box.Width, box.Height));
            foreach (var (box, c) in _lines) dc.DrawRectangle(Brush(c), null, new Rect(box.X, box.Y, box.Width, box.Height));
        }

        private SolidColorBrush Brush(Color c)
        {
            if (_brushes.TryGetValue(c, out var brush)) return brush;
            if (_brushes.Count > 512) _brushes.Clear();
            brush = new SolidColorBrush(c);
            brush.Freeze();
            _brushes[c] = brush;
            return brush;
        }
    }
}
