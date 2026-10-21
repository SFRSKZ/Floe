using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

/// <summary>
/// Click-through overlay above the dock that plays the "now playing" hologram intro:
/// a small glass cylinder fades in above the rim, the album art materialises inside it
/// as a cold-blue scanline hologram with flame strands rising from the floor, then the
/// whole projector sinks into the dock and flashes along the rim. Mirrors
/// <see cref="LiquidDropWindow"/>'s Start/Advance/ImpactTime shape.
/// </summary>
internal sealed class HologramIntroWindow : Window
{
    /// <summary>Overlay size and where the dock's rim sits inside it (DIP).</summary>
    public const double OverlayWidth = 560;
    public const double OverlayHeight = 290;
    public const double RimY = 230;

    private readonly HologramIntroVisual _visual = new();
    private readonly bool _excludeFromCapture;
    private IntPtr _hwnd;

    public HologramIntroWindow(bool excludeFromCapture)
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
        Title = "MacDock Intro";
        Width = OverlayWidth;
        Height = OverlayHeight;
        Content = _visual;
    }

    public static double ImpactTime => HologramIntroVisual.ImpactTime;

    protected override void OnClosed(EventArgs e)
    {
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

    /// <summary>Starts the intro above the rim point <paramref name="rim"/> (screen DIP).</summary>
    public void Start(Point rim, ImageSource? art)
    {
        Left = rim.X - (OverlayWidth / 2);
        Top = rim.Y - RimY;
        _visual.Reset(OverlayWidth / 2, art);
        if (!IsVisible)
        {
            Show();
        }

        WindowStyling.BringToTop(_hwnd);
    }

    /// <summary>Advances the intro; <paramref name="rimX"/> follows the dock as it widens. False once finished.</summary>
    public bool Advance(double dt, double rimX)
    {
        var alive = _visual.Advance(dt, rimX - Left);
        if (!alive)
        {
            Hide();
        }

        return alive;
    }
}

/// <summary>The projector and its hologram, drawn in the overlay's coordinates.</summary>
internal sealed class HologramIntroVisual : FrameworkElement
{
    // Timeline (seconds).
    private const double FadeInSeconds = 0.28;
    private const double ScanStart = 0.22;
    private const double ScanSeconds = 0.62;
    private const double SettleEnd = 1.08;
    private const double DescendEnd = 1.35;
    private const double AfterSeconds = 0.50;

    public const double ImpactTime = DescendEnd;

    // Cylinder geometry (DIP, before the descend scale).
    private const double CylinderWidth = 80;
    private const double CylinderHeight = 112;
    private const double ArtSize = 58;
    private const double BaseGap = 4;

    private const int StrandCount = 12;
    private const int ParticleCount = 12;
    private const int RibbonCount = 4;

    // Palette borrowed from HologramVisualizer: icy core, body blue, faint edge.
    private static readonly Color IcyCore = Color.FromRgb(0xEA, 0xF6, 0xFF);
    private static readonly Color BodyBlue = Color.FromRgb(0x6F, 0xB8, 0xFF);
    private static readonly Color EdgeBlue = Color.FromRgb(0x2A, 0x6F, 0xD6);

    private static readonly Brush GlassBody = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x2A, 0xDF, 0xF2, 0xFF), 0),
            new(Color.FromArgb(0x10, 0x8F, 0xC8, 0xFF), 0.18),
            new(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF), 0.5),
            new(Color.FromArgb(0x14, 0x8F, 0xC8, 0xFF), 0.82),
            new(Color.FromArgb(0x2C, 0xDF, 0xF2, 0xFF), 1),
        }, 0));

    private static readonly Brush GlassCap = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0xD8, 0xF0, 0xFF)));
    private static readonly Pen GlassRimPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xEE, 0xF8, 0xFF)), 0.8));
    private static readonly Pen GlassRimSoftPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x1E, 0x9F, 0xD4, 0xFF)), 3.2));

    // Cold light filling the shell so the chamber is not just an outline.
    private static readonly Brush GlassInner = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x24, 0xB8, 0xE4, 0xFF), 0),
            new(Color.FromArgb(0x10, 0x6F, 0xB8, 0xFF), 0.42),
            new(Color.FromArgb(0x08, 0x2A, 0x6F, 0xD6), 0.78),
            new(Color.FromArgb(0x1A, 0x9F, 0xD4, 0xFF), 1),
        }, 0));

    // Vertical glass sheen down one face of the chamber.
    private static readonly Brush GlassSheen = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
            new(Color.FromArgb(0x1E, 0xF2, 0xFB, 0xFF), 0.14),
            new(Color.FromArgb(0x3C, 0xFF, 0xFF, 0xFF), 0.29),
            new(Color.FromArgb(0x10, 0xD8, 0xF0, 0xFF), 0.45),
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.64),
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
        }, new Point(0, 0), new Point(1, 0)));

    private static readonly Brush SheenEdge = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
            new(Color.FromArgb(0x58, 0xFF, 0xFF, 0xFF), 0.22),
            new(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 0.72),
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
        }, new Point(0.5, 0), new Point(0.5, 1)));

    // Thick, soft white light ribbons that wrap around the chamber. One pen per
    // layer; the per-sub-arc opacity does the fading towards the sides.
    private static readonly Pen RibbonHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 0xF2, 0xFB, 0xFF)), 12.5)
    {
        StartLineCap = PenLineCap.Flat,
        EndLineCap = PenLineCap.Flat,
    });

    private static readonly Pen RibbonMidPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x74, 0xFF, 0xFF, 0xFF)), 6.0)
    {
        StartLineCap = PenLineCap.Flat,
        EndLineCap = PenLineCap.Flat,
    });

    private static readonly Pen RibbonCorePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), 2.0)
    {
        StartLineCap = PenLineCap.Flat,
        EndLineCap = PenLineCap.Flat,
    });

    private static readonly Brush Specular = Frozen(new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)));

    // Additive-looking bloom stacked behind the album art.
    private static readonly Brush ArtHaloWide = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x34, 0x6F, 0xB8, 0xFF), 0),
            new(Color.FromArgb(0x14, 0x3E, 0x8F, 0xE8), 0.5),
            new(Color.FromArgb(0x00, 0x2A, 0x6F, 0xD6), 1),
        }));

    private static readonly Brush ArtHalo = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x52, 0x9F, 0xD4, 0xFF), 0),
            new(Color.FromArgb(0x24, 0x6F, 0xB8, 0xFF), 0.42),
            new(Color.FromArgb(0x00, 0x2A, 0x6F, 0xD6), 1),
        }));

    private static readonly Brush ArtBloom = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x66, 0xD8, 0xF0, 0xFF), 0),
            new(Color.FromArgb(0x22, 0x9F, 0xD4, 0xFF), 0.55),
            new(Color.FromArgb(0x00, 0x9F, 0xD4, 0xFF), 1),
        }));

    // Soft edge falloff so the cover floats as light instead of sitting in a frame.
    private const double ArtFeather = 0.10;

    private static readonly Brush NoteBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0xBF, 0xE6, 0xFF)));
    private static readonly Pen ScanLinePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xF0, 0xE8, 0xF8, 0xFF)), 1.5));
    private static readonly Pen ScanGlowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x3C, 0x9F, 0xD4, 0xFF)), 5));
    private static readonly Pen ScanRingPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0xB8, 0xE4, 0xFF)), 0.9));

    // Alternating dark scanlines plus a faint icy line between them.
    private static readonly Pen ScanlineEvenPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x5C, 0x03, 0x09, 0x14)), 1));
    private static readonly Pen ScanlineOddPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x2C, 0x03, 0x09, 0x14)), 1));
    private static readonly Pen ScanlineGlowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xD8, 0xF0, 0xFF)), 0.8));
    private static readonly Pen GlitchTearPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x86, 0xEA, 0xF6, 0xFF)), 0.9));
    private static readonly Pen BracketPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0xD8, 0xF0, 0xFF)), 1));

    private static readonly Brush PoolBrush = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0xD8, 0xF2, 0xFB, 0xFF), 0),
            new(Color.FromArgb(0x70, 0x9F, 0xD4, 0xFF), 0.25),
            new(Color.FromArgb(0x24, 0x2A, 0x6F, 0xD6), 0.6),
            new(Color.FromArgb(0x00, 0x2A, 0x6F, 0xD6), 1),
        }));

    // Wide cyan spill on the chamber floor.
    private static readonly Brush CyanPool = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x9E, 0xD8, 0xFB, 0xFF), 0),
            new(Color.FromArgb(0x5A, 0x3E, 0xD8, 0xFF), 0.30),
            new(Color.FromArgb(0x20, 0x00, 0xC8, 0xFF), 0.64),
            new(Color.FromArgb(0x00, 0x00, 0xB4, 0xFF), 1),
        }));

    // Wide, low-alpha under-stroke that gives each flame strand its bloom.
    private static readonly Pen StrandHaloPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0x6F, 0xB8, 0xFF)), 5.4));
    private static readonly Pen StrandGlowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x3E, 0x8F, 0xE8)), 2.2));

    private static readonly Pen[] StrandPens =
    {
        Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xC4, 0xEA, 0xF6, 0xFF)), 0.9)),
        Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xA8, 0x6F, 0xB8, 0xFF)), 1.15)),
        Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, 0x2A, 0x6F, 0xD6)), 0.9)),
    };

    private static readonly Brush[] SparkBrushes =
    {
        Frozen(new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF))),
        Frozen(new SolidColorBrush(Color.FromArgb(0xA8, 0xDA, 0xF2, 0xFF))),
        Frozen(new SolidColorBrush(Color.FromArgb(0x82, 0x9F, 0xD4, 0xFF))),
    };

    private static readonly Brush FlashBrush = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF), 0),
            new(Color.FromArgb(0x70, 0xB8, 0xE4, 0xFF), 0.34),
            new(Color.FromArgb(0x22, 0x2A, 0x8F, 0xE6), 0.68),
            new(Color.FromArgb(0x00, 0x2A, 0x8F, 0xE6), 1),
        }));

    private static readonly Brush RimGlintBrush = Frozen(new LinearGradientBrush(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0),
            new(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), 0.5),
            new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
        }, new Point(0, 0.5), new Point(1, 0.5)));

    private static readonly Brush Haze = Frozen(Radial(
        new GradientStopCollection
        {
            new(Color.FromArgb(0x2C, 0x6F, 0xB8, 0xFF), 0),
            new(Color.FromArgb(0x0E, 0x2A, 0x6F, 0xD6), 0.55),
            new(Color.FromArgb(0x00, 0x2A, 0x6F, 0xD6), 1),
        }));

    private static readonly Geometry NoteGeometry = BuildNote();

    private readonly double[] _strandX = new double[StrandCount];
    private readonly double[] _strandSeed = new double[StrandCount];
    private readonly double[] _particleSeed = new double[ParticleCount];

    private double _time = -1;
    private double _x;
    private ImageSource? _artSource;
    private Brush? _artBrush;

    internal HologramIntroVisual()
    {
        IsHitTestVisible = false;
        for (int i = 0; i < StrandCount; i++)
        {
            _strandX[i] = Hash01(i, 0x1D3A);
            _strandSeed[i] = Hash01(i, 0x5C71);
        }

        for (int i = 0; i < ParticleCount; i++)
        {
            _particleSeed[i] = Hash01(i, 0x2B6F);
        }
    }

    internal void Reset(double x, ImageSource? art)
    {
        _time = 0;
        _x = x;
        if (!ReferenceEquals(_artSource, art))
        {
            _artSource = art;
            _artBrush = null;
            if (art is not null)
            {
                var brush = new ImageBrush(CreateHologramArt(art)) { Stretch = Stretch.Fill };
                if (brush.CanFreeze)
                {
                    brush.Freeze();
                }

                _artBrush = brush;
            }
        }

        InvalidateVisual();
    }

    public bool Advance(double dt, double x)
    {
        if (_time < 0)
        {
            return false;
        }

        _time += dt;
        _x = x;
        InvalidateVisual();
        if (_time > ImpactTime + AfterSeconds)
        {
            _time = -1;
            return false;
        }

        return true;
    }

    /// <summary>Diagnostic hook: paint one fixed moment without running the clock.</summary>
    internal void SetTimeForPreview(double time)
    {
        _time = time;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_time < 0)
        {
            return;
        }

        double t = _time;
        double rim = HologramIntroWindow.RimY;

        double fadeIn = EaseOutCubic(Math.Clamp(t / FadeInSeconds, 0, 1));
        double vanish = t <= ImpactTime ? 1 : 1 - Math.Clamp((t - ImpactTime) / 0.18, 0, 1);
        double alpha = fadeIn * vanish;

        double descend = EaseInOut(Math.Clamp((t - SettleEnd) / (ImpactTime - SettleEnd), 0, 1));
        double scale = 1 - (0.55 * descend);
        double width = CylinderWidth * scale;
        double height = CylinderHeight * scale;
        double baseY = rim - BaseGap + (descend * 70);
        double cx = _x;

        if (alpha > 0.001 && height > 2)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, HologramIntroWindow.OverlayWidth, rim + 1.5)));
            dc.PushOpacity(alpha);
            DrawCylinder(dc, cx, baseY, width, height, scale, t);
            dc.Pop();
            dc.Pop();
        }

        if (t >= ImpactTime)
        {
            DrawImpact(dc, _x, rim, t - ImpactTime);
        }
    }

    private void DrawCylinder(DrawingContext dc, double cx, double baseY, double width, double height, double scale, double t)
    {
        double rx = width / 2;
        double capRy = width * 0.105;
        double topY = baseY - height;

        // Cold haze around the projector.
        dc.DrawEllipse(Haze, null, new Point(cx, baseY - (height * 0.5)), width * 1.02, height * 0.78);

        // Glass body: a rectangle with elliptical caps reads as a cylinder in perspective.
        dc.DrawRectangle(GlassBody, null, new Rect(cx - rx, topY, width, height));
        dc.DrawRectangle(GlassInner, null, new Rect(cx - rx, topY, width, height));
        dc.DrawEllipse(GlassCap, GlassRimSoftPen, new Point(cx, topY), rx, capRy);
        dc.DrawEllipse(GlassCap, GlassRimSoftPen, new Point(cx, baseY), rx, capRy);
        dc.DrawEllipse(GlassCap, GlassRimPen, new Point(cx, topY), rx, capRy);
        dc.DrawEllipse(GlassCap, GlassRimPen, new Point(cx, baseY), rx, capRy);

        // Faint vertical sheen down one face of the glass.
        dc.PushClip(new RectangleGeometry(new Rect(cx - rx, topY + capRy, width, height - (capRy * 2))));
        dc.DrawRectangle(GlassSheen, null, new Rect(cx - rx, topY, width, height));
        dc.DrawRectangle(SheenEdge, null, new Rect(cx - (rx * 0.60), topY + (height * 0.08), 1.6, height * 0.84));
        dc.Pop();

        DrawHologram(dc, cx, baseY, width, height, scale, t);

        // Light ribbons wrapping around the chamber, in front of the hologram.
        DrawRibbons(dc, cx, rx, rx * 0.44, baseY, height, t);

        // A second, hair-thin specular edge to keep the glass readable.
        dc.DrawRectangle(Specular, null, new Rect(cx - (rx * 0.78), topY + (height * 0.14), 0.9, height * 0.66));
    }

    /// <summary>Soft white light ribbons: partial arcs that brighten on the front and drift slowly.</summary>
    private static void DrawRibbons(DrawingContext dc, double cx, double rx, double ry, double baseY, double height, double t)
    {
        const int subArcs = 18;

        for (int r = 0; r < RibbonCount; r++)
        {
            double phase = Hash01(r, 0x41A7) * Math.PI * 2;
            double f = 0.15 + (0.20 * r) + (0.03 * Math.Sin((t * 0.5) + phase));
            double y0 = baseY - (height * f);
            double tilt = ((Hash01(r, 0x2C93) * 2) - 1) * 2.4;
            double spin = (t * 0.34) + phase;
            double radius = rx * (0.97 + (0.06 * Hash01(r, 0x77E1)));
            double alphaScale = 0.72 + (0.28 * Hash01(r, 0x5D2B));

            for (int s = 0; s < subArcs; s++)
            {
                double a0 = (s / (double)subArcs) * Math.PI * 2;
                double a1 = ((s + 1) / (double)subArcs) * Math.PI * 2;
                double mid = (a0 + a1) / 2;

                // The back half of the loop is hidden by the chamber itself.
                double front = Math.Sin(mid);
                if (front <= 0.02)
                {
                    continue;
                }

                // Brightness peak slides around the shell so the ribbon reads as rotating.
                double light = Math.Max(0, Math.Cos(mid + spin));
                double weight = Math.Pow(front, 1.15) * (0.55 + (0.45 * light)) * alphaScale;

                var p0 = RibbonPoint(cx, radius, ry, y0, tilt, a0, spin);
                var p1 = RibbonPoint(cx, radius, ry, y0, tilt, a1, spin);

                dc.PushOpacity(weight);
                dc.DrawLine(RibbonHaloPen, p0, p1);
                dc.DrawLine(RibbonMidPen, p0, p1);
                dc.DrawLine(RibbonCorePen, p0, p1);
                dc.Pop();
            }
        }
    }

    private static Point RibbonPoint(double cx, double radius, double ry, double y0, double tilt, double angle, double spin)
    {
        double y = y0
            - (ry * Math.Sin(angle))
            + (tilt * Math.Cos(angle + spin))
            + (0.6 * Math.Sin((angle * 2) + (spin * 0.7)));
        return new Point(cx + (radius * Math.Cos(angle)), y);
    }

    private void DrawHologram(DrawingContext dc, double cx, double baseY, double width, double height, double scale, double t)
    {
        double floorY = baseY - (width * 0.105 * 0.35);

        // Flame strands rising from the cylinder floor: thin blue filaments with a
        // bell-shaped envelope, clumped heights and per-strand flicker.
        for (int i = 0; i < StrandCount; i++)
        {
            double seed = _strandSeed[i];
            double speed = 1.1 + (2.3 * Hash01(i, 0x71C5));
            double xn = ((i + 0.5) / StrandCount) - 0.5;
            xn += (_strandX[i] - 0.5) * (0.9 / StrandCount);

            double growth = Math.Clamp((t - 0.05 - (seed * 0.22)) / 0.48, 0, 1);
            if (growth <= 0.01)
            {
                continue;
            }

            double envelope = Math.Exp(-(xn * xn) / (2 * 0.24 * 0.24));
            double noise = (0.62 * ValueNoise((t * speed) + (seed * 37.0), i + 11)) +
                           (0.38 * ValueNoise((t * speed * 2.3) + (seed * 13.0), i + 71));
            double clump = 0.70 + (0.55 * ValueNoise((xn * 8.0) + (t * 0.22) + 3.1, 0x1F3D));

            double maxStrand = height * 0.76;
            double tongue = Hash01(i, 0x33A7) > 0.78 ? 1.28 : 1.0;
            double h = Math.Min(maxStrand, height * 0.74 * tongue * envelope * clump * (0.30 + (0.70 * noise)) * growth);
            if (h < 0.9)
            {
                continue;
            }

            double heightRatio = h / maxStrand;
            double sway = Math.Sin((t * 2.3) + (seed * 8.0)) * 1.5 * heightRatio * (0.4 + (0.6 * noise));
            double x = cx + (xn * width * 0.88);
            double y0 = floorY + ((_strandX[i] - 0.5) * 1.4);
            int family = seed > 0.80 ? 0 : seed > 0.32 ? 1 : 2;
            var root = new Point(x, y0);
            var knee = new Point(x + (sway * 0.25), y0 - (h * 0.55));
            var tip = new Point(x + sway, y0 - h);

            dc.DrawLine(StrandHaloPen, root, tip);
            dc.DrawLine(StrandGlowPen, root, tip);
            dc.DrawLine(StrandPens[family], root, knee);
            dc.DrawLine(StrandPens[family], knee, tip);

            if (noise > 0.42)
            {
                dc.DrawEllipse(SparkBrushes[noise > 0.66 ? 0 : 1], null, tip, 1.0, 1.0);
            }
        }

        // Soft cyan pool of light on the floor, then a tighter white-hot core.
        dc.DrawEllipse(CyanPool, null, new Point(cx, floorY + 2.6), width * 0.94, 9.4);
        dc.DrawEllipse(CyanPool, null, new Point(cx, floorY + 1.6), width * 0.66, 6.2);
        dc.DrawEllipse(PoolBrush, null, new Point(cx, floorY + 1), width * 0.52, 4.6);
        dc.DrawEllipse(SparkBrushes[1], null, new Point(cx, floorY), width * 0.20, 1.4);

        // Drifting sparks.
        for (int i = 0; i < ParticleCount; i++)
        {
            double progress = _particleSeed[i] + (t * (0.22 + (0.16 * _particleSeed[i])));
            progress -= Math.Floor(progress);
            double y = floorY - (progress * height * 0.92);
            double fade = Math.Pow(Math.Sin(Math.PI * progress), 1.4);
            if (fade <= 0.02)
            {
                continue;
            }

            double px = cx + ((_particleSeed[i] - 0.5) * width * 0.66) + (Math.Sin((t * 1.3) + (i * 2.1)) * 1.6);
            var brush = SparkBrushes[fade > 0.66 ? 0 : fade > 0.36 ? 1 : 2];
            dc.DrawEllipse(brush, null, new Point(px, y), 0.85 + (0.5 * _particleSeed[i]), 0.85 + (0.5 * _particleSeed[i]));
        }

        DrawArt(dc, cx, baseY, width, height, scale, t);
    }

    private void DrawArt(DrawingContext dc, double cx, double baseY, double width, double height, double scale, double t)
    {
        double t0 = Math.Max(0, t);
        double flicker = 0.90 + (0.10 * Math.Sin(t0 * 47.0) * Math.Sin((t0 * 11.7) + 0.8));
        flicker *= 0.94 + (0.06 * ValueNoise(t0 * 22.0, 0x4B1D));
        double jitter = (Math.Sin(t0 * 33.0) * 0.55) + (Math.Sin((t0 * 17.3) + 1.7) * 0.35);

        double artSize = Math.Max(6, ArtSize * scale);
        double artCx = cx + jitter;
        double artTop = baseY - (height * 0.52) - (artSize / 2);
        double artLeft = artCx - (artSize / 2);
        double artBottom = artTop + artSize;
        double artMidY = (artTop + artBottom) / 2;

        double reveal = Math.Clamp((t - ScanStart) / ScanSeconds, 0, 1);
        double revealY = artBottom - (reveal * artSize);

        if (reveal > 0.001)
        {
            dc.PushClip(new RectangleGeometry(new Rect(artLeft - 1, revealY, artSize + 2, (artBottom - revealY) + 1)));
            dc.PushOpacity(Math.Clamp(0.85 * flicker, 0.6, 1));

            // Additive-looking bloom: wide halo, tighter ring, then the artwork's
            // own light bled just past its frame before the sharp copy lands.
            dc.DrawEllipse(ArtHaloWide, null, new Point(artCx, artMidY), artSize * 1.14, artSize * 1.26);
            dc.DrawEllipse(ArtHalo, null, new Point(artCx, artMidY), artSize * 0.88, artSize * 0.98);
            dc.DrawEllipse(ArtBloom, null, new Point(artCx, artMidY), artSize * 0.60, artSize * 0.68);

            if (_artBrush is not null)
            {
                dc.PushOpacity(0.34);
                dc.DrawRectangle(_artBrush, null, new Rect(
                    artLeft - (artSize * 0.06), artTop - (artSize * 0.06), artSize * 1.12, artSize * 1.12));
                dc.Pop();

                dc.DrawRectangle(_artBrush, null, new Rect(artLeft, artTop, artSize, artSize));

                // Strong scanlines: a dark line every 2 DIP with alternating weight and
                // a faint icy line between them, so the image reads as projected light.
                double scanLeft = artLeft + (artSize * 0.10);
                double scanRight = artLeft + (artSize * 0.90);
                int row = 0;
                for (double y = artTop; y < artBottom; y += 2.0, row++)
                {
                    dc.DrawLine(row % 2 == 0 ? ScanlineEvenPen : ScanlineOddPen,
                        new Point(scanLeft, y), new Point(scanRight, y));
                    if (row % 2 == 0)
                    {
                        dc.DrawLine(ScanlineGlowPen, new Point(scanLeft, y + 1), new Point(scanRight, y + 1));
                    }
                }
            }
            else
            {
                dc.PushTransform(new MatrixTransform(artSize, 0, 0, artSize, artLeft, artTop));
                dc.DrawGeometry(NoteBrush, null, NoteGeometry);
                dc.Pop();
            }

            dc.Pop();
            dc.Pop();
        }

        if (reveal > 0.55)
        {
            DrawGlitch(dc, artLeft, artTop, artSize, t0, revealY);
        }

        if (reveal > 0.35)
        {
            DrawBrackets(dc, artLeft, artTop, artSize);
        }

        if (reveal > 0 && reveal < 1)
        {
            dc.DrawLine(ScanGlowPen, new Point(artLeft - 4, revealY), new Point(artLeft + artSize + 4, revealY));
            dc.DrawLine(ScanLinePen, new Point(artLeft - 4, revealY), new Point(artLeft + artSize + 4, revealY));
            dc.DrawEllipse(null, ScanRingPen, new Point(cx, revealY), width * 0.5, 2.1);
        }
    }

    /// <summary>Occasional torn horizontal slice: a band of the art offset by one to three DIP for a frame or two.</summary>
    private void DrawGlitch(DrawingContext dc, double left, double top, double size, double t, double revealY)
    {
        if (_artBrush is null)
        {
            return;
        }

        double slotTime = t * 9.0;
        int slot = (int)Math.Floor(slotTime);
        if (slotTime - slot > 0.34 || Hash01(slot, 0x6D2F) < 0.52)
        {
            return;
        }

        double bandHeight = size * (0.06 + (0.14 * Hash01(slot, 0x39C1)));
        double bandTop = top + (size * (0.12 + (0.62 * Hash01(slot, 0x1A47))));

        // Never tear past the scan front: the band is clipped to the revealed part.
        double clipTop = Math.Max(bandTop, revealY);
        double clipBottom = Math.Min(bandTop + bandHeight, top + size);
        if (clipBottom <= clipTop)
        {
            return;
        }

        double shift = (1.2 + (1.8 * Hash01(slot, 0x5B13))) * (Hash01(slot, 0x2F81) > 0.5 ? 1 : -1);

        dc.PushClip(new RectangleGeometry(new Rect(left - 1, clipTop, size + 2, clipBottom - clipTop)));
        dc.PushOpacity(0.88);
        dc.DrawRectangle(_artBrush, null, new Rect(left + shift, top, size, size));
        dc.DrawLine(GlitchTearPen, new Point(left + (size * 0.08), clipTop + 0.5), new Point(left + (size * 0.92), clipTop + 0.5));
        dc.Pop();
        dc.Pop();
    }

    private static void DrawBrackets(DrawingContext dc, double left, double top, double size)
    {
        double arm = Math.Max(3, size * 0.18);
        double right = left + size;
        double bottom = top + size;
        dc.DrawLine(BracketPen, new Point(left, top), new Point(left + arm, top));
        dc.DrawLine(BracketPen, new Point(left, top), new Point(left, top + arm));
        dc.DrawLine(BracketPen, new Point(right, top), new Point(right - arm, top));
        dc.DrawLine(BracketPen, new Point(right, top), new Point(right, top + arm));
        dc.DrawLine(BracketPen, new Point(left, bottom), new Point(left + arm, bottom));
        dc.DrawLine(BracketPen, new Point(left, bottom), new Point(left, bottom - arm));
        dc.DrawLine(BracketPen, new Point(right, bottom), new Point(right - arm, bottom));
        dc.DrawLine(BracketPen, new Point(right, bottom), new Point(right, bottom - arm));
    }

    /// <summary>
    /// Re-maps the cover to luminance and paints it through the cyan/ice ramp, keeping
    /// only a hint of the original hue. Darker pixels also turn more transparent, so the
    /// artwork reads as projected light rather than a colour photo.
    /// </summary>
    private static ImageSource CreateHologramArt(ImageSource art)
    {
        const int size = 160;

        try
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new ImageBrush(art) { Stretch = Stretch.UniformToFill }, null, new Rect(0, 0, size, size));
            }

            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);

            var gray = new FormatConvertedBitmap(rendered, PixelFormats.Gray8, null, 0);
            var luminance = new byte[size * size];
            gray.CopyPixels(luminance, size, 0);

            var original = new byte[size * size * 4];
            rendered.CopyPixels(original, size * 4, 0);

            var pixels = new byte[size * size * 4];
            double feather = size * ArtFeather;
            for (int i = 0; i < luminance.Length; i++)
            {
                double l = Math.Clamp(((luminance[i] / 255.0) - 0.03) / 0.86, 0, 1);
                l = Math.Pow(l, 0.9);
                Ramp(l, out var r, out var g, out var b);

                int o = i * 4;
                r = (byte)((r * 0.85) + (original[o + 2] * 0.15));
                g = (byte)((g * 0.85) + (original[o + 1] * 0.15));
                b = (byte)((b * 0.85) + (original[o] * 0.15));

                // Feather the frame so the cover reads as floating light, not a photo.
                int px = i % size;
                int py = i / size;
                double edge = Math.Clamp(Math.Min(Math.Min(px, size - 1 - px), Math.Min(py, size - 1 - py)) / feather, 0, 1);
                edge = edge * edge * (3 - (2 * edge));

                pixels[o] = b;
                pixels[o + 1] = g;
                pixels[o + 2] = r;
                pixels[o + 3] = (byte)(255 * (0.74 + (0.26 * l)) * (0.10 + (0.90 * edge)));
            }

            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // Diagnostics/odd sources: fall back to the plain cover rather than throwing mid-intro.
            return art;
        }
    }

    // Cyan / ice-blue ramp: near-black blue, then body blue, then icy white.
    private static readonly (double Pos, byte R, byte G, byte B)[] ArtRamp =
    {
        (0.00, 0x05, 0x12, 0x24),
        (0.24, 0x14, 0x42, 0x8A),
        (0.50, 0x3A, 0x8C, 0xE8),
        (0.74, 0x8F, 0xD0, 0xFF),
        (1.00, 0xF2, 0xFB, 0xFF),
    };

    private static void Ramp(double l, out byte r, out byte g, out byte b)
    {
        l = Math.Clamp(l, 0, 1);
        for (int i = 1; i < ArtRamp.Length; i++)
        {
            if (l > ArtRamp[i].Pos && i < ArtRamp.Length - 1)
            {
                continue;
            }

            var a = ArtRamp[i - 1];
            var z = ArtRamp[i];
            double f = Math.Clamp((l - a.Pos) / (z.Pos - a.Pos), 0, 1);
            r = (byte)(a.R + ((z.R - a.R) * f));
            g = (byte)(a.G + ((z.G - a.G) * f));
            b = (byte)(a.B + ((z.B - a.B) * f));
            return;
        }

        (r, g, b) = (ArtRamp[^1].R, ArtRamp[^1].G, ArtRamp[^1].B);
    }

    private static void DrawImpact(DrawingContext dc, double x, double rim, double age)
    {
        if (age > AfterSeconds)
        {
            return;
        }

        double fade = 1 - (age / AfterSeconds);
        double spread = EaseOutCubic(Math.Clamp(age / 0.30, 0, 1));
        dc.PushOpacity(fade);
        dc.DrawEllipse(FlashBrush, null, new Point(x, rim), 24 + (150 * spread), 4 + (10 * spread));
        dc.DrawRectangle(RimGlintBrush, null, new Rect(x - 100 - (70 * spread), rim - 1.2, 200 + (140 * spread), 2.4));
        dc.Pop();
    }

    private static double EaseOutCubic(double t)
    {
        var u = 1 - t;
        return 1 - (u * u * u);
    }

    private static double EaseInOut(double t) => t < 0.5
        ? 2 * t * t
        : 1 - (Math.Pow((-2 * t) + 2, 2) / 2);

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

    private static Brush Radial(GradientStopCollection stops) => new RadialGradientBrush(stops);

    private static Geometry BuildNote()
    {
        // A simple eighth note in a 0..1 box: head, stem and a drooping flag.
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new EllipseGeometry(new Point(0.30, 0.82), 0.17, 0.12));
        group.Children.Add(new RectangleGeometry(new Rect(0.42, 0.14, 0.09, 0.70)));
        var flag = new StreamGeometry();
        using (var c = flag.Open())
        {
            c.BeginFigure(new Point(0.51, 0.14), true, true);
            c.BezierTo(new Point(0.68, 0.24), new Point(0.78, 0.38), new Point(0.66, 0.58), true, true);
            c.BezierTo(new Point(0.70, 0.40), new Point(0.60, 0.28), new Point(0.51, 0.26), true, true);
        }

        flag.Freeze();
        group.Children.Add(flag);
        group.Freeze();
        return group;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
