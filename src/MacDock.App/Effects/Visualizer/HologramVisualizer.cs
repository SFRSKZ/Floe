using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// A cold-flame "hologram" made of hundreds of thin vertical filaments. The
/// strands rise from the bottom edge of <see cref="Capsule"/> in a bell-shaped
/// envelope: tallest in the middle, jagged and irregular strand by strand,
/// fading out to nothing at the tips. Drawn with layered low-alpha strokes so
/// the overlap reads as translucent light.
/// <para>
/// Sampling follows the same lifecycle as <see cref="VisualizerGlow"/>: the
/// render callback is hooked only while <see cref="IsActive"/> is set or the
/// ~400 ms fade-out is still running.
/// </para>
/// </summary>
public sealed class HologramVisualizer : FrameworkElement
{
    // Audio shaping, matching VisualizerGlow so both visualizers react alike.
    private const double GainDecaySeconds = 4.0;
    private const double GainFloor = 0.002;
    private const double SilenceFloor = 0.0003;
    private const double AttackSeconds = 0.05;
    private const double ReleaseSeconds = 0.30;
    private const double BalanceSeconds = 0.18;
    private const double FadeOutSeconds = 0.40;
    private const double FadeInSeconds = 0.22;

    // Geometry budget.
    private const int MaxFilaments = 640;
    private const double FilamentSpacing = 3.0;
    private const int ColorFamilies = 3;
    private const int ParticleCount = 20;
    private const int AlphaGrades = 8;

    private static readonly Color[] FamilyColors =
    {
        Color.FromRgb(0xEA, 0xF6, 0xFF), // icy core
        Color.FromRgb(0x6F, 0xB8, 0xFF), // body blue
        Color.FromRgb(0x2A, 0x6F, 0xD6)  // faint edge
    };

    private static readonly double[] FamilyScale = { 1.0, 0.90, 0.62 };

    // Pre-frozen alpha ladder: a strand picks one grade per frame, the brush
    // itself never changes.
    private static readonly double[] AlphaLadder = { 0.06, 0.09, 0.13, 0.18, 0.25, 0.33, 0.43, 0.56 };

    // A strand sprite holds the old pen stack (bloom 8.5, glow 4.6, core 1.15 DIP; heavy adds 2.4)
    // composited once, with the base-to-tip fade baked in.
    private const double SpriteWidth = 10;
    private static readonly (double Thickness, double Alpha)[] StrandLayers = { (8.5, 0.10), (4.6, 0.32), (1.15, 1.0) };
    private static readonly (double Thickness, double Alpha)[] HeavyLayers = { (8.5, 0.10), (4.6, 0.32), (2.4, 0.55), (1.15, 1.0) };
    private static readonly BitmapSource[][] StrandSprites = CreateSprites(StrandLayers);
    private static readonly BitmapSource[][] HeavySprites = CreateSprites(HeavyLayers);

    // Opacity ladders for the soft shapes, so nothing needs PushOpacity (an offscreen layer each).
    private const int OpacitySteps = 24;
    private static readonly Brush[] SparkBrushes = Ladder(CreateSparkBrush());
    private static readonly Brush[] HazeBlueBrushes = Ladder(CreateHazeBrush(FamilyColors[1]));
    private static readonly Brush[] HazeCoreBrushes = Ladder(CreateHazeBrush(Color.FromRgb(0xD2, 0xEC, 0xFF)));
    private static readonly Brush[] PoolBrushes = Ladder(CreatePoolBrush());
    private static readonly Brush[] PoolWideBrushes = Ladder(CreateHazeBrush(FamilyColors[1]));

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Per-strand deterministic constants, allocated once.
    private readonly double[] _jitter = new double[MaxFilaments];
    private readonly double[] _tipSeed = new double[MaxFilaments];
    private readonly double[] _swaySpeed = new double[MaxFilaments];
    private readonly double[] _swayPhase = new double[MaxFilaments];
    private readonly double[] _flickerSpeed = new double[MaxFilaments];

    // Particles are a fixed pool animated from the clock; no state churn.
    private readonly double[] _particleX = new double[ParticleCount];
    private readonly double[] _particlePhase = new double[ParticleCount];
    private readonly double[] _particleSpeed = new double[ParticleCount];
    private readonly double[] _particleDrift = new double[ParticleCount];
    private readonly double[] _particleSize = new double[ParticleCount];

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
    private double _debugLevel = -1;

    public HologramVisualizer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        for (int i = 0; i < MaxFilaments; i++)
        {
            _jitter[i] = Hash01(i, 0x51ED27) - 0.5;
            _tipSeed[i] = Hash01(i, 0x2F91A3);
            _swaySpeed[i] = 0.35 + 1.25 * Hash01(i, 0x7C3B19);
            _swayPhase[i] = 6.283 * Hash01(i, 0x19AF43);
            _flickerSpeed[i] = 0.55 + 1.65 * Hash01(i, 0x4D8C61);
        }

        for (int i = 0; i < ParticleCount; i++)
        {
            _particleX[i] = Hash01(i, 0x71B2D9);
            _particlePhase[i] = Hash01(i, 0x38E4A1);
            _particleSpeed[i] = 0.018 + 0.030 * Hash01(i, 0x5FA0C7);
            _particleDrift[i] = 0.12 + 0.38 * Hash01(i, 0x27C6E5);
            _particleSize[i] = 0.55 + 1.05 * Hash01(i, 0x6A14B3);
        }

        _fade = 0;
    }

    /// <summary>
    /// Capsule bounds in this element's coordinates. The base line sits 4 DIP
    /// above the bottom; the tallest filaments reach 95% of the capsule height.
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
    /// Turns sampling on or off. Off fades the hologram out (~400 ms) and then
    /// unhooks the render callback entirely.
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
        double energy = fade * (0.30 + 0.70 * Math.Sqrt(level));
        if (energy <= 0.001)
        {
            return;
        }

        double t = _clock.Elapsed.TotalSeconds;
        double width = capsule.Width;
        double baseY = capsule.Bottom - 4;
        double maxHeight = Math.Max(1, capsule.Height * 0.95);
        double centerX = capsule.Left + width * 0.5 + Math.Clamp(_balance, -1, 1) * width * 0.035;

        int grade = Math.Clamp((int)Math.Round(energy * (AlphaGrades - 1)), 0, AlphaGrades - 1);

        // ------------------------------------------------------------------
        // Wide atmospheric bloom behind the strands.
        // ------------------------------------------------------------------
        dc.DrawEllipse(Graded(HazeBlueBrushes, 0.11 + 0.27 * energy), null,
            new Point(centerX, baseY - maxHeight * 0.30), width * 0.37, maxHeight * 0.64);

        dc.DrawEllipse(Graded(HazeCoreBrushes, 0.08 + 0.20 * energy), null,
            new Point(centerX, baseY - maxHeight * 0.36), width * 0.125, maxHeight * 0.62);

        dc.DrawEllipse(Graded(HazeCoreBrushes, 0.05 + 0.13 * energy), null,
            new Point(centerX, baseY - maxHeight * 0.44), width * 0.042, maxHeight * 0.54);

        // ------------------------------------------------------------------
        // Filaments: one pre-rendered sprite per strand, picked by colour
        // family and alpha grade.
        // ------------------------------------------------------------------
        int count = Math.Clamp((int)Math.Round(width / FilamentSpacing), 8, MaxFilaments);
        double spacing = width / count;

        double primaryCenter = Math.Sin(t * 0.23) * 0.014;
        double secondaryCenter = Math.Sin(t * 0.31 + 1.3) * 0.105;
        double primarySigma = 0.22;
        double secondarySigma = 0.085;
        double amplitude = 0.12 + 1.10 * Math.Pow(level, 0.66);

        // Every strand is one pre-rendered sprite stretched to its height. Stroking hundreds of
        // wide gradient pens (and a PushOpacity per tip) cost ~100 ms a frame on the shared
        // render thread and dragged the dock's own animation down to 10 fps.
        for (int i = 0; i < count; i++)
        {
            double xn = (i + 0.5) / count - 0.5;
            double x = capsule.Left + (i + 0.5) * spacing + _jitter[i] * spacing * 0.70;
            x = Math.Clamp(x, capsule.Left + 0.75, capsule.Right - 0.75);

            double dxPrimary = xn - primaryCenter;
            double dxSecondary = xn - secondaryCenter;
            double primary = Math.Exp(-(dxPrimary * dxPrimary) / (2 * primarySigma * primarySigma));
            double secondary = Math.Exp(-(dxSecondary * dxSecondary) / (2 * secondarySigma * secondarySigma));
            double envelope = Math.Min(1.12, primary * 0.92 + secondary * 0.34);

            double seed = _tipSeed[i];
            double noise = 0.62 * ValueNoise(t * _flickerSpeed[i] + seed * 63.0, (int)(seed * 100003)) +
                           0.38 * ValueNoise(t * (_flickerSpeed[i] * 2.3 + 0.4) + seed * 17.0, (int)(seed * 70001));
            noise = Math.Clamp(noise, 0, 1);

            double h = envelope * maxHeight * amplitude * (0.35 + 0.65 * noise);

            // Slow spatial clumps: neighbouring strands share a tongue of
            // height so the silhouette breaks into peaks instead of a lawn.
            double clump = 0.72 + 0.50 * ValueNoise(xn * 9.0 + t * 0.18 + 4.7, 0x1F3D);
            if (Hash01(i, 0x6B12) > 0.90)
            {
                clump *= 1.22;
            }

            h = Math.Min(maxHeight, h * Math.Clamp(clump, 0.45, 1.24));
            if (h < 0.8)
            {
                continue;
            }

            double heightRatio = h / maxHeight;
            double sway = Math.Sin(t * _swaySpeed[i] + _swayPhase[i]) * 1.5 * heightRatio * (0.35 + 0.65 * noise);
            double y0 = baseY - Math.Abs(_jitter[(i * 7 + 3) % MaxFilaments]) * 0.8;
            double distance = Math.Abs(xn - primaryCenter);
            int family = distance < 0.055 ? 0 : distance < 0.26 ? 1 : 2;
            bool heavy = Hash01(i, 0x33A7) > 0.86;

            var sprite = (heavy ? HeavySprites : StrandSprites)[family][grade];
            double cx = x + sway * 0.5;
            dc.DrawImage(sprite, new Rect(cx - (SpriteWidth / 2), y0 - h - 1, SpriteWidth, h + 1));

            double tipAlpha = (0.06 + 0.24 * noise) * energy * (0.35 + 0.65 * heightRatio);
            if (tipAlpha > 0.002)
            {
                dc.DrawEllipse(Graded(SparkBrushes, tipAlpha), null, new Point(x + sway, y0 - h), 1.05, 1.05);
            }
        }

        // ------------------------------------------------------------------
        // Base pool: a wide, soft light spill along the bottom line.
        // ------------------------------------------------------------------
        double poolAlpha = Math.Clamp((0.30 + 0.45 * Math.Sqrt(level)) * fade, 0, 1);
        dc.DrawEllipse(Graded(PoolWideBrushes, poolAlpha * 0.46), null, new Point(centerX, baseY + 1.5), width * 0.48, 8.5);

        dc.DrawEllipse(Graded(PoolBrushes, poolAlpha * 0.72), null, new Point(centerX, baseY + 0.5), width * 0.36, 4.2 + 3.4 * level);

        // A narrow white-hot line inside the pool.
        dc.DrawEllipse(Graded(SparkBrushes, poolAlpha * 0.36), null, new Point(centerX, baseY), width * 0.20, 1.6);

        // ------------------------------------------------------------------
        // A few faint particles drifting upward through and above the cluster.
        // ------------------------------------------------------------------
        for (int i = 0; i < ParticleCount; i++)
        {
            double progress = _particlePhase[i] + t * _particleSpeed[i];
            progress -= Math.Floor(progress);
            double span = Math.Min(maxHeight * 1.02, baseY - capsule.Top - 4);
            double y = baseY - 4 - progress * span;
            double alpha = Math.Pow(Math.Sin(Math.PI * progress), 1.6) * 0.42 * energy;
            if (alpha <= 0.002)
            {
                continue;
            }

            double px = centerX + (_particleX[i] - 0.5) * width * 0.78 +
                        Math.Sin(t * _particleDrift[i] + _particlePhase[i] * 9) * 3.0;
            dc.DrawEllipse(Graded(SparkBrushes, alpha), null, new Point(px, y), _particleSize[i], _particleSize[i]);
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

        // See VisualizerPacing: the strip shares the render thread with the dock.
        _sinceRepaint += dt;
        if (_sinceRepaint < VisualizerPacing.RepaintInterval)
        {
            return;
        }

        _sinceRepaint = 0;
        VisualizerPacing.MarkRepaint();
        InvalidateVisual();
    }

    private void SampleAudio(double dt)
    {
        _meter ??= new AudioPeakMeter();

        // Auto gain: normalise the endpoint meter against a slowly decaying
        // recent peak so quiet apps still fill the capsule.
        double raw = Math.Clamp(_meter.GetPeak(), 0, 1);
        _gainPeak = Math.Max(raw, _gainPeak * Math.Exp(-dt / GainDecaySeconds));
        double target = raw < SilenceFloor ? 0 : Math.Clamp(raw / Math.Max(_gainPeak, GainFloor), 0, 1);
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

    private static BitmapSource[][] CreateSprites((double Thickness, double Alpha)[] layers)
    {
        const int scale = 3;
        int width = (int)(SpriteWidth * scale);
        const int height = 96;
        var sprites = new BitmapSource[ColorFamilies][];
        for (int family = 0; family < ColorFamilies; family++)
        {
            sprites[family] = new BitmapSource[AlphaGrades];
            var color = FamilyColors[family];
            for (int grade = 0; grade < AlphaGrades; grade++)
            {
                double baseAlpha = AlphaLadder[grade] * FamilyScale[family];
                var pixels = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    // 0 at the base (bottom row) to 1 at the tip, the old pen gradient's stops
                    double v = 1 - ((y + 0.5) / height);
                    double fade = Ramp(v, (0, 1), (0.30, 0.68), (0.62, 0.28), (0.85, 0.07), (1, 0));
                    for (int x = 0; x < width; x++)
                    {
                        double dx = Math.Abs(((x + 0.5) / scale) - (SpriteWidth / 2));
                        double clear = 1;
                        foreach (var (thickness, alpha) in layers)
                        {
                            double coverage = Math.Clamp((thickness / 2) - dx + 0.5, 0, 1);
                            clear *= 1 - (Math.Clamp(baseAlpha * alpha * fade, 0, 1) * coverage);
                        }

                        double a = 1 - clear;
                        int i = ((y * width) + x) * 4;
                        pixels[i] = (byte)Math.Round(color.B * a);
                        pixels[i + 1] = (byte)Math.Round(color.G * a);
                        pixels[i + 2] = (byte)Math.Round(color.R * a);
                        pixels[i + 3] = (byte)Math.Round(255 * a);
                    }
                }

                var bitmap = BitmapSource.Create(width, height, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32, null, pixels, width * 4);
                bitmap.Freeze();
                sprites[family][grade] = bitmap;
            }
        }

        return sprites;
    }

    private static double Ramp(double v, params (double At, double Value)[] stops)
    {
        for (int i = 1; i < stops.Length; i++)
        {
            if (v <= stops[i].At)
            {
                var (a0, v0) = stops[i - 1];
                var (a1, v1) = stops[i];
                return v0 + ((v1 - v0) * (v - a0) / Math.Max(1e-6, a1 - a0));
            }
        }

        return stops[^1].Value;
    }

    private static Brush[] Ladder(Brush brush)
    {
        var ladder = new Brush[OpacitySteps + 1];
        for (int i = 0; i <= OpacitySteps; i++)
        {
            var step = brush.Clone();
            step.Opacity = (double)i / OpacitySteps;
            step.Freeze();
            ladder[i] = step;
        }

        return ladder;
    }

    private static Brush Graded(Brush[] ladder, double opacity) =>
        ladder[(int)Math.Round(Math.Clamp(opacity, 0, 1) * OpacitySteps)];

    private static Brush CreateSparkBrush()
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, 0xDA, 0xF2, 0xFF), 0.30));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, 0x8F, 0xCD, 0xFF), 0.64));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x6F, 0xB8, 0xFF), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateHazeBrush(Color color)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.34), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.18), 0.34));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.06), 0.68));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreatePoolBrush()
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0xF2, 0xFB, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xA8, 0xB8, 0xE4, 0xFF), 0.22));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, 0x6F, 0xB8, 0xFF), 0.52));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0x2A, 0x6F, 0xD6), 0.80));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x2A, 0x6F, 0xD6), 1));
        brush.Freeze();
        return brush;
    }

    // Smooth 1-D value noise from a deterministic integer hash.
    private static double ValueNoise(double x, int seed)
    {
        double floor = Math.Floor(x);
        int i = (int)floor;
        double f = x - floor;
        double smooth = f * f * (3 - 2 * f);
        double a = Hash01(i, seed);
        double b = Hash01(i + 1, seed);
        return a + (b - a) * smooth;
    }

    private static double Hash01(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215.0;
        }
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 255),
            color.R, color.G, color.B);
}
