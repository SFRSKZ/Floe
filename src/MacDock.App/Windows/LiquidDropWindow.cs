using MacDock.App.Services;
using MacDock.App.Effects.Glass;
using MacDock.App.Effects.MetalRim;
using System.Windows.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

/// <summary>
/// Click-through overlay above the dock that plays the "now playing" droplet: a bead forms,
/// falls as a tear, necks into the dock's rim and is absorbed with a splash, a rim ripple
/// and a flash of light inside the glass. The dock window has too little headroom for the fall.
/// </summary>
internal sealed class LiquidDropWindow : Window
{
    /// <summary>Overlay size and where the dock's rim sits inside it (DIP).</summary>
    public const double OverlayWidth = 560;
    public const double OverlayHeight = 290;
    public const double RimY = 230;

    private readonly LiquidDropVisual _visual = new();
    private readonly bool _excludeFromCapture;
    private IntPtr _hwnd;

    public LiquidDropWindow(bool excludeFromCapture)
    {
        _excludeFromCapture = excludeFromCapture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "MacDock Drop";
        Width = OverlayWidth;
        Height = OverlayHeight;
        Content = _visual;
    }

    public static double ImpactTime => LiquidDropVisual.ImpactTime;

    protected override void OnClosed(EventArgs e)
    {
        _visual.ReleaseLens();
        CaptureReveal.Forget(_hwnd);
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeClickThrough(_hwnd);
        if (_excludeFromCapture)
        {
            CaptureReveal.Exclude(_hwnd);
        }
    }

    /// <summary>Starts a drop that lands on the rim point <paramref name="rim"/> (screen DIP).</summary>
    public void Start(Point rim)
    {
        Left = rim.X - (OverlayWidth / 2);
        Top = rim.Y - RimY;
        _visual.ReleaseLens();
        _visual.Reset(OverlayWidth / 2);
        if (!IsVisible)
        {
            Show();
        }

        WindowStyling.BringToTop(_hwnd);
        // Native window bounds are physical pixels, including on mixed-DPI monitors.
        if (DropLensEffect.IsEnabled && ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            try
            {
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                var pixels = ScreenCapture.CaptureScaled(rect.Left, rect.Top, width, height, width, height, out var stride);
                if (pixels is not null)
                {
                    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
                    bitmap.Freeze();
                    _visual.SetBackdrop(bitmap);
                }
            }
            catch (Exception ex) { ShaderCompiler.Log($"Drop capture failed: {ex.Message}"); }
        }
    }

    /// <summary>Advances the drop; <paramref name="rimX"/> follows the dock as it widens. False once finished.</summary>
    public bool Advance(double dt, double rimX)
    {
        var alive = _visual.Advance(dt, rimX - Left);
        if (!alive)
        {
            _visual.ReleaseLens();
            Hide();
        }

        return alive;
    }
}

internal sealed class LiquidDropVisual : FrameworkElement
{
    public const double ImpactTime = FormSeconds + FallSeconds;

    private const double FormSeconds = 0.30;
    private const double FallSeconds = 0.44;
    private const double MergeSeconds = 0.42;
    private const double AfterSeconds = 0.85;
    private const double Radius = 11;
    private const double StartY = 22;
    private const double Gravity = 900;
    private const double RippleSpeed = 250;

    private static readonly Brush BodyBrush = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x96, 0xDC, 0xEC, 0xFF), 0),
            new(Color.FromArgb(0x78, 0x6A, 0x86, 0xA8), 0.45),
            new(Color.FromArgb(0xB4, 0x1E, 0x28, 0x3A), 1),
        }, 90));

    private static readonly Pen RimPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)), 0.9));
    private static readonly Brush Specular = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Caustic = Frozen(new SolidColorBrush(Color.FromArgb(0x58, 0xB8, 0xE4, 0xFF)));

    private readonly List<Splash> _splashes = new();
    private readonly Random _random;
    private DrawingVisual? _lensVisual;
    internal bool MaskOnly { get; set; }
    internal bool DecorationOnly { get; set; }
    internal Point LensCenter { get; private set; }
    internal double LensRadius { get; private set; } = Radius;

    internal LiquidDropVisual(int? seed = null) => _random = seed.HasValue ? new Random(seed.Value) : new Random();

    protected override int VisualChildrenCount => _lensVisual is null ? 0 : 1;
    protected override Visual GetVisualChild(int index) => index == 0 && _lensVisual is not null
        ? _lensVisual : throw new ArgumentOutOfRangeException(nameof(index));

    internal void SetBackdrop(BitmapSource bitmap)
    {
        ReleaseLens();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        _lensVisual = new DrawingVisual { Effect = new DropLensEffect(brush) };
        AddVisualChild(_lensVisual);
        InvalidateVisual();
    }

    internal void ReleaseLens()
    {
        if (_lensVisual is null) return;
        _lensVisual.Effect = null;
        RemoveVisualChild(_lensVisual);
        _lensVisual = null;
        InvalidateVisual();
    }
    private double _time = -1;
    private double _x;
    private double _impactX;

    public void Reset(double x)
    {
        _time = 0;
        _x = x;
        _splashes.Clear();
        InvalidateVisual();
    }

    public bool Advance(double dt, double x)
    {
        if (_time < 0)
        {
            return false;
        }

        var before = _time;
        _time += dt;
        _x = x;
        if (before < ImpactTime && _time >= ImpactTime)
        {
            Impact();
        }

        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            var s = _splashes[i];
            s.Age += dt;
            s.Vy += Gravity * dt;
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            if (s.Y > LiquidDropWindow.RimY && s.Vy > 0)
            {
                _splashes.RemoveAt(i);
            }
        }

        InvalidateVisual();
        if (_time > ImpactTime + AfterSeconds && _splashes.Count == 0)
        {
            _time = -1;
            return false;
        }

        return true;
    }

    private void Impact()
    {
        _impactX = _x;
        for (int i = 0; i < 8; i++)
        {
            var side = i % 2 == 0 ? -1 : 1;
            _splashes.Add(new Splash
            {
                X = _x + (side * Radius * (0.2 + (0.5 * _random.NextDouble()))),
                Y = LiquidDropWindow.RimY - 2,
                Vx = side * (25 + (85 * _random.NextDouble())),
                Vy = -(110 + (150 * _random.NextDouble())),
                Size = 1.1 + (1.7 * _random.NextDouble()),
            });
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_lensVisual is null)
        {
            DrawFrame(dc, MaskOnly, DecorationOnly);
            return;
        }

        DrawFrame(dc, false, true);
        using (var mask = _lensVisual.RenderOpen()) DrawFrame(mask, true, false);
        if (_lensVisual.Effect is DropLensEffect lens)
        {
            lens.Center = LensCenter;
            lens.Radius = LensRadius;
        }
    }

    private void DrawFrame(DrawingContext dc, bool mask, bool decoration)
    {
        // Anchor shader UVs to the complete overlay, not the changing shape bounds.
        if (mask) dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, LiquidDropWindow.OverlayWidth, LiquidDropWindow.OverlayHeight));
        if (_time < 0)
        {
            return;
        }

        double rim = LiquidDropWindow.RimY;
        if (!mask && _time >= ImpactTime)
        {
            DrawGlow(dc, rim, _time - ImpactTime);
        }

        // everything liquid stays above the rim: below it the drop is "inside" the glass
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, rim + 0.4)));
        if (!decoration && _time < FormSeconds)
        {
            // condensing: the bead swells with a springy overshoot and a little wobble
            var f = _time / FormSeconds;
            var r = Radius * EaseOutBack(f);
            var wobble = Math.Sin(f * Math.PI * 3) * 0.10 * (1 - f);
            DrawTear(dc, _x, StartY + r, Math.Max(0.1, r), 1 - wobble, 0.2 * f, mask);
        }
        else if (!decoration && _time < ImpactTime)
        {
            // falling: gravity, stretching into a tear as it picks up speed
            var f = (_time - FormSeconds) / FallSeconds;
            var landY = rim - (Radius * 0.9);
            var cy = StartY + Radius + ((landY - StartY - Radius) * f * f);
            DrawTear(dc, _x, cy, Radius, 1 - (0.10 * f), 0.2 + (1.3 * f), mask);
        }
        else if (!decoration && _time < ImpactTime + MergeSeconds)
        {
            var m = (_time - ImpactTime) / MergeSeconds;
            DrawMerge(dc, _x, rim, EaseInOut(m), mask);
        }

        if (!mask) DrawRipples(dc, rim, _time - ImpactTime);
        if (!decoration) foreach (var s in _splashes)
        {
            var fade = Math.Clamp(1 - (s.Age / 0.7), 0, 1);
            dc.DrawEllipse(Alpha(mask ? Colors.White : Color.FromRgb(0xD8, 0xEE, 0xFF), (mask ? 1 : 0.85) * fade), null, new Point(s.X, s.Y), s.Size, s.Size * 1.15);
        }

        dc.Pop();
    }

    /// <summary>A round body with a pointed tail above it; <paramref name="tail"/> is in radii.</summary>
    private void DrawTear(DrawingContext dc, double cx, double cy, double r, double squeeze, double tail, bool mask)
    {
        var w = r * squeeze;
        var tipY = cy - r - (r * tail);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx, tipY), true, true);
            c.BezierTo(new Point(cx + (r * 0.12), tipY + (r * tail * 0.45)), new Point(cx + w, cy - (r * 0.8)), new Point(cx + w, cy), true, true);
            c.ArcTo(new Point(cx - w, cy), new Size(w, r), 0, false, SweepDirection.Clockwise, true, true);
            c.BezierTo(new Point(cx - w, cy - (r * 0.8)), new Point(cx - (r * 0.12), tipY + (r * tail * 0.45)), new Point(cx, tipY), true, true);
        }

        g.Freeze();
        LensCenter = new Point(cx, cy);
        LensRadius = r;
        dc.DrawGeometry(mask ? Brushes.White : BodyBrush, mask ? null : RimPen, g);
        if (!mask) DrawHighlights(dc, cx, cy, r);
    }

    /// <summary>The drop necking into the rim and sinking through it, like two blobs of liquid joining.</summary>
    private void DrawMerge(DrawingContext dc, double cx, double rim, double m, bool mask)
    {
        var r = Radius * (1 - (0.30 * m));
        var cy = rim - (Radius * 0.9) + (m * Radius * 2.6);
        var neck = Radius * (0.6 + (2.8 * m));
        var attachY = cy + (r * 0.34);
        var k = Math.Max(2, (rim - attachY) * 0.6);
        var left = new Point(cx - (r * 0.94), attachY);
        var right = new Point(cx + (r * 0.94), attachY);

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - neck, rim + 2), true, true);
            c.BezierTo(new Point(cx - neck + ((neck - r) * 0.6), rim), new Point(left.X, left.Y + k), left, true, true);
            c.ArcTo(right, new Size(r, r), 0, true, SweepDirection.Clockwise, true, true);
            c.BezierTo(new Point(right.X, right.Y + k), new Point(cx + neck - ((neck - r) * 0.6), rim), new Point(cx + neck, rim + 2), true, true);
        }

        g.Freeze();
        LensCenter = new Point(cx, cy);
        LensRadius = r;
        dc.DrawGeometry(mask ? Brushes.White : BodyBrush, mask ? null : RimPen, g);
        if (!mask && cy - r < rim)
        {
            DrawHighlights(dc, cx, cy, r);
        }
    }

    private static void DrawHighlights(DrawingContext dc, double cx, double cy, double r)
    {
        dc.DrawEllipse(Caustic, null, new Point(cx + (r * 0.1), cy + (r * 0.5)), r * 0.55, r * 0.26);
        dc.DrawEllipse(Specular, null, new Point(cx - (r * 0.36), cy - (r * 0.4)), r * 0.30, r * 0.20);
    }

    /// <summary>Two swells running out along the rim, each with a glint on its crest.</summary>
    private void DrawRipples(DrawingContext dc, double rim, double age)
    {
        if (age <= 0 || age > AfterSeconds)
        {
            return;
        }

        var fade = Math.Pow(1 - (age / AfterSeconds), 1.6);
        var travel = RippleSpeed * age;
        foreach (var side in new[] { -1.0, 1.0 })
        {
            var x = _impactX + (side * travel);
            var height = 3.2 * fade;
            dc.DrawEllipse(Alpha(Color.FromRgb(0x9C, 0xB8, 0xD8), 0.55 * fade), null, new Point(x, rim), 18, height);

            var glint = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(230 * fade), 0xFF, 0xFF, 0xFF), 0.5));
            glint.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1));
            glint.Freeze();
            dc.DrawRectangle(glint, null, new Rect(x - 26, rim - height - 0.4, 52, 1.4));
        }
    }

    /// <summary>The absorbed drop lighting up the glass from inside.</summary>
    private void DrawGlow(DrawingContext dc, double rim, double age)
    {
        var g = Math.Clamp(age / 0.7, 0, 1);
        var spread = 1 - Math.Pow(1 - g, 3);
        var glow = new RadialGradientBrush { Opacity = 0.75 * (1 - g) };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, 0xD8, 0xEE, 0xFF), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0x8C, 0xC4, 0xFF), 0.5));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x8C, 0xC4, 0xFF), 1));
        glow.Freeze();
        dc.DrawEllipse(glow, null, new Point(_impactX, rim + 12), 18 + (150 * spread), 8 + (24 * spread));
    }

    private static Brush Alpha(Color color, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static double EaseOutBack(double t)
    {
        const double c = 1.9;
        var u = t - 1;
        return 1 + ((c + 1) * u * u * u) + (c * u * u);
    }

    private static double EaseInOut(double t) => t < 0.5 ? 2 * t * t : 1 - (Math.Pow(-2 * t + 2, 2) / 2);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private sealed class Splash
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Size;
        public double Age;
    }
}
