using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
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
    private const int HeightBuckets = 12;
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

    private static readonly Pen[][] CorePens = CreatePens(1.15, 1.0);
    private static readonly Pen[][] HeavyPens = CreatePens(2.4, 0.55);
    private static readonly Pen[][] GlowPens = CreatePens(4.6, 0.32);
    private static readonly Pen[][] BloomPens = CreatePens(8.5, 0.10);

    private static readonly Brush SparkBrush = CreateSparkBrush();
    private static readonly Brush HazeBlueBrush = CreateHazeBrush(FamilyColors[1]);
    private static readonly Brush HazeCoreBrush = CreateHazeBrush(Color.FromRgb(0xD2, 0xEC, 0xFF));
    private static readonly Brush PoolBrush = CreatePoolBrush();
    private static readonly Brush PoolWideBrush = CreateHazeBrush(FamilyColors[1]);

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
    private readonly double[] _tipX = new double[MaxFilaments];
    private readonly double[] _tipY = new double[MaxFilaments];
    private readonly double[] _tipAlpha = new double[MaxFilaments];

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
    private const double RepaintInterval = 1.0 / 30;
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
        dc.PushOpacity(Math.Clamp(0.11 + 0.27 * energy, 0, 1));
        dc.DrawEllipse(HazeBlueBrush, null,
            new Point(centerX, baseY - maxHeight * 0.30), width * 0.37, maxHeight * 0.64);
        dc.Pop();

        dc.PushOpacity(Math.Clamp(0.08 + 0.20 * energy, 0, 1));
        dc.DrawEllipse(HazeCoreBrush, null,
            new Point(centerX, baseY - maxHeight * 0.36), width * 0.125, maxHeight * 0.62);
        dc.Pop();

        dc.PushOpacity(Math.Clamp(0.05 + 0.13 * energy, 0, 1));
        dc.DrawEllipse(HazeCoreBrush, null,
            new Point(centerX, baseY - maxHeight * 0.44), width * 0.042, maxHeight * 0.54);
        dc.Pop();

        // ------------------------------------------------------------------
        // Filaments, batched by (height bucket, colour family) so a whole
        // bucket shares one frozen pen and one StreamGeometry.
        // ------------------------------------------------------------------
        int count = Math.Clamp((int)Math.Round(width / FilamentSpacing), 8, MaxFilaments);
        double spacing = width / count;

        double primaryCenter = Math.Sin(t * 0.23) * 0.014;
        double secondaryCenter = Math.Sin(t * 0.31 + 1.3) * 0.105;
        double primarySigma = 0.22;
        double secondarySigma = 0.085;
        double amplitude = 0.12 + 1.10 * Math.Pow(level, 0.66);

        var geometries = new StreamGeometry?[HeightBuckets, ColorFamilies];
        var contexts = new StreamGeometryContext?[HeightBuckets, ColorFamilies];
        var heavyGeometries = new StreamGeometry?[HeightBuckets, ColorFamilies];
        var heavyContexts = new StreamGeometryContext?[HeightBuckets, ColorFamilies];
        var tipX = _tipX;
        var tipY = _tipY;
        var tipAlpha = _tipAlpha;

        for (int i = 0; i < count; i++)
        {
            tipAlpha[i] = 0;
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

            h *= Math.Clamp(clump, 0.45, 1.24);
            if (h > maxHeight)
            {
                h = maxHeight;
            }

            if (h < 0.8)
            {
                continue;
            }

            double heightRatio = h / maxHeight;
            double sway = Math.Sin(t * _swaySpeed[i] + _swayPhase[i]) * 1.5 * heightRatio *
                          (0.35 + 0.65 * noise);
            double baseJitter = -Math.Abs(_jitter[(i * 7 + 3) % MaxFilaments]) * 0.8;
            double y0 = baseY + baseJitter;
            double x0 = x;

            int heightBucket = Math.Clamp((int)(heightRatio * HeightBuckets), 0, HeightBuckets - 1);
            double distance = Math.Abs(xn - primaryCenter);
            int family = distance < 0.055 ? 0 : distance < 0.26 ? 1 : 2;

            bool heavy = Hash01(i, 0x33A7) > 0.86;
            var geometry = heavy
                ? heavyGeometries[heightBucket, family] ??= new StreamGeometry()
                : geometries[heightBucket, family] ??= new StreamGeometry();
            var context = heavy
                ? heavyContexts[heightBucket, family] ??= geometry.Open()
                : contexts[heightBucket, family] ??= geometry.Open();
            context.BeginFigure(new Point(x0, y0), false, false);
            context.LineTo(new Point(x0 + sway * 0.28, y0 - h * 0.55), true, false);
            context.LineTo(new Point(x0 + sway, y0 - h), true, false);

            tipX[i] = x0 + sway;
            tipY[i] = y0 - h;
            tipAlpha[i] = Math.Clamp((0.06 + 0.24 * noise) * energy * (0.35 + 0.65 * heightRatio), 0, 1);
        }

        for (int hb = 0; hb < HeightBuckets; hb++)
        {
            for (int family = 0; family < ColorFamilies; family++)
            {
                contexts[hb, family]?.Close();
                heavyContexts[hb, family]?.Close();
            }
        }

        // Wide soft pass first: overlapping strands build the plasma haze.
        for (int hb = 0; hb < HeightBuckets; hb++)
        {
            for (int family = 0; family < ColorFamilies; family++)
            {
                var geometry = geometries[hb, family];
                if (geometry is not null)
                {
                    dc.DrawGeometry(null, BloomPens[family][grade], geometry);
                    dc.DrawGeometry(null, GlowPens[family][grade], geometry);
                }

                var heavyGeometry = heavyGeometries[hb, family];
                if (heavyGeometry is not null)
                {
                    dc.DrawGeometry(null, HeavyPens[family][grade], heavyGeometry);
                }
            }
        }

        // Thin bright core pass.
        for (int hb = 0; hb < HeightBuckets; hb++)
        {
            for (int family = 0; family < ColorFamilies; family++)
            {
                var geometry = geometries[hb, family];
                if (geometry is not null)
                {
                    dc.DrawGeometry(null, CorePens[family][grade], geometry);
                }

                var heavyGeometry = heavyGeometries[hb, family];
                if (heavyGeometry is not null)
                {
                    dc.DrawGeometry(null, CorePens[family][grade], heavyGeometry);
                }
            }
        }

        // Tiny brighter spark at every strand tip.
        for (int i = 0; i < count; i++)
        {
            if (tipAlpha[i] <= 0.002 || tipY[i] > baseY - 1 ||
                tipX[i] < capsule.Left - 8 || tipX[i] > capsule.Right + 8)
            {
                continue;
            }

            dc.PushOpacity(tipAlpha[i]);
            dc.DrawEllipse(SparkBrush, null, new Point(tipX[i], tipY[i]), 1.05, 1.05);
            dc.Pop();
        }

        // ------------------------------------------------------------------
        // Base pool: a wide, soft light spill along the bottom line.
        // ------------------------------------------------------------------
        double poolAlpha = Math.Clamp((0.30 + 0.45 * Math.Sqrt(level)) * fade, 0, 1);
        dc.PushOpacity(poolAlpha * 0.46);
        dc.DrawEllipse(PoolWideBrush, null, new Point(centerX, baseY + 1.5), width * 0.48, 8.5);
        dc.Pop();

        dc.PushOpacity(poolAlpha * 0.72);
        dc.DrawEllipse(PoolBrush, null, new Point(centerX, baseY + 0.5), width * 0.36, 4.2 + 3.4 * level);
        dc.Pop();

        // A narrow white-hot line inside the pool.
        dc.PushOpacity(poolAlpha * 0.36);
        dc.DrawEllipse(SparkBrush, null, new Point(centerX, baseY), width * 0.20, 1.6);
        dc.Pop();

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
            dc.PushOpacity(alpha);
            dc.DrawEllipse(SparkBrush, null, new Point(px, y), _particleSize[i], _particleSize[i]);
            dc.Pop();
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

        // Every repaint re-runs the dock's glass and rim shaders on a layered window,
        // so 30 fps is plenty for a soft light and keeps hover animations smooth.
        _sinceRepaint += dt;
        if (_sinceRepaint < RepaintInterval)
        {
            return;
        }

        _sinceRepaint = 0;
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

    private static Pen[][] CreatePens(double thickness, double alphaScale)
    {
        var pens = new Pen[ColorFamilies][];
        for (int family = 0; family < ColorFamilies; family++)
        {
            pens[family] = new Pen[AlphaGrades];
            for (int grade = 0; grade < AlphaGrades; grade++)
            {
                double alpha = AlphaLadder[grade] * FamilyScale[family] * alphaScale;
                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    StartPoint = new Point(0.5, 1),
                    EndPoint = new Point(0.5, 0),
                    SpreadMethod = GradientSpreadMethod.Pad
                };
                brush.GradientStops.Add(new GradientStop(WithAlpha(FamilyColors[family], alpha), 0));
                brush.GradientStops.Add(new GradientStop(WithAlpha(FamilyColors[family], alpha * 0.68), 0.30));
                brush.GradientStops.Add(new GradientStop(WithAlpha(FamilyColors[family], alpha * 0.28), 0.62));
                brush.GradientStops.Add(new GradientStop(WithAlpha(FamilyColors[family], alpha * 0.07), 0.85));
                brush.GradientStops.Add(new GradientStop(WithAlpha(FamilyColors[family], 0), 1));
                brush.Freeze();

                var pen = new Pen(brush, thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                pens[family][grade] = pen;
            }
        }

        return pens;
    }

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
