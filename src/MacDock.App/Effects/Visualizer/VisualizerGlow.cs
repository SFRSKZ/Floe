using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// A soft, coloured light that hugs the bottom edge of a capsule (the dock's
/// glass shell). Put this element behind the capsule fill and give it the
/// capsule's bounds in its own coordinate space; the glow reaches about 18 DIP
/// below the edge and 10 DIP past either side.
/// <para>
/// While <see cref="IsActive"/> is set, the element samples the default audio
/// endpoint on every rendered frame. Setting it back to false fades the glow
/// out over ~400 ms and then unhooks the render callback, so an idle
/// visualizer costs nothing per frame.
/// </para>
/// </summary>
public sealed class VisualizerGlow : FrameworkElement
{
    private const double MaxAlpha = 0.55;
    private const double AttackSeconds = 0.06;
    private const double ReleaseSeconds = 0.35;
    private const double FadeOutSeconds = 0.40;
    private const double FadeInSeconds = 0.22;
    private const double BalanceSeconds = 0.18;

    // Resting lobe layout, normalised across the capsule: centre first, then
    // pairs fanning outward. The last pair sits on the sides themselves.
    private static readonly double[] LobeX = { 0.5, 0.35, 0.65, 0.20, 0.80, 0.06, 0.94 };
    private static readonly double[] LobeWeight = { 1.0, 0.78, 0.78, 0.55, 0.55, 0.34, 0.34 };
    private static readonly double[] LobeWidth = { 0.17, 0.135, 0.135, 0.105, 0.105, 0.0, 0.0 };
    private static readonly double[] LobeHeight = { 14, 11.5, 11.5, 9.5, 9.5, 7.5, 7.5 };
    private static readonly int[] LobeColor = { 0, 1, 2, 2, 1, 1, 2 };
    private static readonly double[] LobeSpeed = { 0.33, 0.47, 0.41, 0.59, 0.53, 0.71, 0.67 };
    private static readonly double[] LobePhase = { 0, 1.7, 3.1, 4.6, 0.9, 2.4, 5.3 };

    private static readonly Color[] DefaultPalette =
    {
        Color.FromRgb(0xFF, 0x7A, 0xB8), // soft pink
        Color.FromRgb(0xAF, 0x8C, 0xFF), // violet
        Color.FromRgb(0x70, 0xE2, 0xF5)  // cyan
    };

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private AudioPeakMeter? _meter;
    private Color[] _palette = DefaultPalette;
    private Rect _capsule = Rect.Empty;
    private bool _active;
    private bool _hooked;
    private double _level;
    private double _balance;
    private double _fade;
    private double _lastFrameSeconds = -1;
    private double _debugLevel = -1;

    public VisualizerGlow()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Colours cycled across the glow lobes. Never empty.</summary>
    public Color[] Palette
    {
        get => _palette;
        set
        {
            _palette = value is { Length: > 0 } ? value : DefaultPalette;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Capsule bounds in this element's coordinates. The light is anchored to
    /// the bottom edge: it spills roughly 18 DIP below and 10 DIP past the sides.
    /// </summary>
    public Rect Capsule
    {
        get => _capsule;
        set
        {
            if (_capsule == value)
            {
                return;
            }
            _capsule = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Turns sampling on or off. Off fades the light out (~400 ms) and then
    /// stops the render callback entirely.
    /// </summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            _active = value;
            if (value)
            {
                _lastFrameSeconds = -1;
                if (IsLoaded)
                {
                    Hook();
                }
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// Diagnostic override for offscreen renders: 0..1 paints that fixed level
    /// without sampling audio; -1 (default) uses the live meter.
    /// </summary>
    internal double DebugLevel
    {
        get => _debugLevel;
        set
        {
            _debugLevel = value;
            InvalidateVisual();
        }
    }

    /// <summary>True when a media session is actually playing. Never throws.</summary>
    public static async Task<bool> ShouldRunAsync() => await MediaService.IsPlayingAsync();

    protected override void OnRender(DrawingContext dc)
    {
        double debug = _debugLevel;
        double fade = debug >= 0 ? 1 : _fade;
        var capsule = _capsule;
        if (fade <= 0.001 || capsule.IsEmpty || capsule.Width <= 0 || capsule.Height <= 0 ||
            !double.IsFinite(capsule.X) || !double.IsFinite(capsule.Y) ||
            !double.IsFinite(capsule.Width) || !double.IsFinite(capsule.Height))
        {
            return;
        }

        double level = debug >= 0 ? Math.Clamp(debug, 0, 1) : Math.Clamp(_level, 0, 1);
        double intensity = Math.Sqrt(level) * fade;
        if (intensity <= 0.001)
        {
            return;
        }

        var palette = _palette;
        if (palette is not { Length: > 0 })
        {
            palette = DefaultPalette;
        }

        double t = _clock.Elapsed.TotalSeconds;
        double breath = 0.5 + 0.5 * Math.Sin(t * 1.45);
        double balance = Math.Clamp(_balance, -1, 1);
        double width = capsule.Width;
        double bottom = capsule.Bottom;
        double centerX = capsule.Left + width * 0.5;

        // Wide, low ambient wash: the quiet body of the light.
        double ambienceX = width * 0.50 + 12;
        double ambienceY = 14 + 4 * intensity;
        var ambienceCenter = new Point(centerX, bottom + 2);
        var ambienceBrush = CreateBlobBrush(
            Blend(palette), ambienceCenter, ambienceX, ambienceY, 0.24 * intensity, soft: true);
        if (ambienceBrush is not null)
        {
            dc.DrawEllipse(ambienceBrush, null, ambienceCenter, ambienceX, ambienceY);
        }

        // Overlapping lobe lights. Each drifts on its own slow sine so the
        // glow breathes and ripples instead of pumping as one shape.
        var lobes = new (double X, double Y, double RadiusX, double RadiusY, double Alpha, Color Color)[LobeX.Length];
        for (int i = 0; i < LobeX.Length; i++)
        {
            double dx = (LobeX[i] - 0.5) * 2;
            double drift = Math.Sin(t * LobeSpeed[i] + LobePhase[i]) * (2.8 + (i >= 3 ? 1.6 : 0));
            double sway = Math.Cos(t * LobeSpeed[i] * 0.63 + LobePhase[i] * 1.3) * 1.5;
            double lobeX = i switch
            {
                5 => capsule.Left - 1 + drift,
                6 => capsule.Right + 1 + drift,
                _ => capsule.Left + width * LobeX[i] + drift
            };
            double lobeY = bottom + 2.5 + sway + (i == 0 ? 0 : 0.8);
            double expand = 1 + 0.30 * intensity;
            double radiusX = Math.Clamp(width * LobeWidth[i] + 20, 16, 165) * expand;
            double radiusY = LobeHeight[i] * (0.85 + 0.50 * intensity) + 2 * breath;

            // Left/right channel energy leans the light toward the louder side.
            double channel = Math.Clamp(1 + balance * dx * 0.55, 0.2, 1.6);
            double flicker = 0.88 + 0.12 * Math.Sin(t * LobeSpeed[i] * 1.9 + LobePhase[i]);
            double alpha = Math.Min(MaxAlpha, MaxAlpha * intensity * LobeWeight[i] * channel * flicker);
            lobes[i] = (lobeX, lobeY, radiusX, radiusY, alpha, palette[LobeColor[i] % palette.Length]);
        }

        // A wider, taller halo underneath every lobe gives the falloff a
        // blurred bloom rather than seven clean gradients.
        foreach (var lobe in lobes)
        {
            var haloCenter = new Point(lobe.X, lobe.Y + 1.5);
            double haloX = lobe.RadiusX * 1.45;
            double haloY = lobe.RadiusY * 1.55;
            var haloBrush = CreateBlobBrush(lobe.Color, haloCenter, haloX, haloY, lobe.Alpha * 0.42, soft: true);
            if (haloBrush is not null)
            {
                dc.DrawEllipse(haloBrush, null, haloCenter, haloX, haloY);
            }
        }

        foreach (var lobe in lobes)
        {
            var center = new Point(lobe.X, lobe.Y);
            var brush = CreateBlobBrush(lobe.Color, center, lobe.RadiusX, lobe.RadiusY, lobe.Alpha, soft: false);
            if (brush is not null)
            {
                dc.DrawEllipse(brush, null, center, lobe.RadiusX, lobe.RadiusY);
            }
        }

        // A luminous lip along the edge itself — still a gradient, never a bar.
        double lipX = width * 0.44;
        double lipY = 3.0 + 1.3 * intensity;
        var lipCenter = new Point(centerX + balance * 2, bottom - 0.5);
        var lipBrush = CreateBlobBrush(BlendPalette(palette, 0.55), lipCenter, lipX, lipY,
            0.30 * intensity, soft: true);
        if (lipBrush is not null)
        {
            dc.DrawEllipse(lipBrush, null, lipCenter, lipX, lipY);
        }

        // Tiny white-hot core: the premium "light source" at the centre.
        double coreX = width * 0.12 + 16;
        double coreY = 5 + 2 * intensity;
        var coreCenter = new Point(centerX + balance * 3, bottom);
        var coreBrush = CreateBlobBrush(Colors.White, coreCenter, coreX, coreY,
            0.20 * intensity, soft: true);
        if (coreBrush is not null)
        {
            dc.DrawEllipse(coreBrush, null, coreCenter, coreX, coreY);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_active)
        {
            Hook();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unhook();
        _meter?.Dispose();
        _meter = null;
        _lastFrameSeconds = -1;
    }

    private void Hook()
    {
        if (_hooked)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs args
            ? args.RenderingTime.TotalSeconds
            : _clock.Elapsed.TotalSeconds;

        // Rendering can be raised more than once for one frame; ignore repeats.
        if (now <= _lastFrameSeconds)
        {
            return;
        }

        double dt = _lastFrameSeconds < 0
            ? 1.0 / 60
            : Math.Clamp(now - _lastFrameSeconds, 0.001, 0.1);
        _lastFrameSeconds = now;

        if (_active)
        {
            _fade = Math.Min(1, _fade + dt / FadeInSeconds);
            SampleAudio(dt);
        }
        else
        {
            _fade = Math.Max(0, _fade - dt / FadeOutSeconds);
            _level += (0 - _level) * (1 - Math.Exp(-dt / ReleaseSeconds));
            _balance += (0 - _balance) * (1 - Math.Exp(-dt / 0.25));
            if (_fade <= 0)
            {
                _level = 0;
                _balance = 0;
                _lastFrameSeconds = -1;
                Unhook();
                InvalidateVisual();
                return;
            }
        }

        InvalidateVisual();
    }

    private void SampleAudio(double dt)
    {
        _meter ??= new AudioPeakMeter();

        double target = Math.Clamp(_meter.GetPeak(), 0, 1);
        double tau = target > _level ? AttackSeconds : ReleaseSeconds;
        _level += (target - _level) * (1 - Math.Exp(-dt / tau));

        var channels = _meter.GetChannelPeaks();
        double left = 0;
        double right = 0;
        if (channels.Length >= 2)
        {
            left = channels[0];
            right = channels[1];
        }
        else if (channels.Length == 1)
        {
            left = right = channels[0];
        }

        double sum = left + right;
        double balance = sum > 0.0001 ? Math.Clamp((right - left) / sum, -1, 1) : 0;
        _balance += (balance - _balance) * (1 - Math.Exp(-dt / BalanceSeconds));
    }

    private static RadialGradientBrush? CreateBlobBrush(
        Color color, Point center, double radiusX, double radiusY, double alpha, bool soft)
    {
        if (alpha <= 0.001 || radiusX <= 0 || radiusY <= 0 ||
            !double.IsFinite(radiusX) || !double.IsFinite(radiusY))
        {
            return null;
        }

        double peak = Math.Min(MaxAlpha, alpha);
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = radiusX,
            RadiusY = radiusY,
            SpreadMethod = GradientSpreadMethod.Pad
        };

        if (soft)
        {
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak), 0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak * 0.62), 0.28));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak * 0.32), 0.52));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak * 0.12), 0.74));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak), 0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak * 0.55), 0.34));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, peak * 0.22), 0.66));
            brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        }

        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 255),
            color.R, color.G, color.B);

    private static Color Blend(Color[] palette)
    {
        int r = 0, g = 0, b = 0;
        foreach (var color in palette)
        {
            r += color.R;
            g += color.G;
            b += color.B;
        }

        int n = Math.Max(1, palette.Length);
        return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    private static Color BlendPalette(Color[] palette, double towardWhite)
    {
        var blend = Blend(palette);
        double amount = Math.Clamp(towardWhite, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(blend.R + (255 - blend.R) * amount),
            (byte)Math.Round(blend.G + (255 - blend.G) * amount),
            (byte)Math.Round(blend.B + (255 - blend.B) * amount));
    }
}
