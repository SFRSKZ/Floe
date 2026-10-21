using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// The hologram's cold-flame filaments spread across the whole strip: flame
/// clusters of different widths and heights drift and breathe over a low
/// carpet of short strands, with smoky veils flowing along the floor.
/// <see cref="Style"/> picks how the strands are drawn (thin lines or dust),
/// the density, the fog and the colours.
/// <para>
/// Sampling follows the same lifecycle as <see cref="VisualizerGlow"/>: the
/// render callback is hooked only while <see cref="IsActive"/> is set or the
/// ~400 ms fade-out is still running.
/// </para>
/// </summary>
public sealed class MistVisualizer : FrameworkElement
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
    private const int MaxSparks = 120;
    private const int AlphaGrades = 8;
    private const int DustFrames = 4;

    private static readonly double[] FamilyScale = { 1.0, 0.90, 0.62 };

    // Pre-frozen alpha ladder: a strand picks one grade per frame, the sprite never changes.
    private static readonly double[] AlphaLadder = { 0.06, 0.09, 0.13, 0.18, 0.25, 0.33, 0.43, 0.56 };

    // A strand sprite is the hologram's pen stack (bloom, glow, core), scaled by the style's
    // strand width, composited once with the base-to-tip fade baked in.
    private const double SpriteWidth = 10;
    private const double SpriteHeight = 72;
    private static readonly (double Thickness, double Alpha)[] StrandLayers = { (8.5, 0.07), (4.6, 0.24), (1.15, 1.0) };
    private static readonly (double Thickness, double Alpha)[] HeavyLayers = { (8.5, 0.07), (4.6, 0.24), (2.4, 0.55), (1.15, 1.0) };

    // Opacity ladders for the soft shapes, so nothing needs PushOpacity (an offscreen layer each).
    private const int OpacitySteps = 24;

    // Flame clusters across the strip: centre (0..1 of the width), half-width (DIP), peak height (0..1).
    private static readonly (double Center, double HalfWidth, double Peak)[] Clusters =
    {
        (0.05, 70, 0.90),
        (0.14, 90, 0.55),
        (0.24, 80, 0.72),
        (0.34, 70, 0.48),
        (0.44, 100, 0.66),
        (0.55, 90, 0.52),
        (0.65, 80, 0.70),
        (0.75, 100, 0.50),
        (0.85, 90, 0.86),
        (0.95, 70, 0.60),
    };

    private const double CarpetHeight = 0.16;
    private const double VeilStep = 8;

    private static readonly Dictionary<MistStyle, Assets> AssetCache = new();

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Per-strand deterministic constants, allocated once.
    private readonly double[] _jitter = new double[MaxFilaments];
    private readonly double[] _tipSeed = new double[MaxFilaments];
    private readonly double[] _swaySpeed = new double[MaxFilaments];
    private readonly double[] _swayPhase = new double[MaxFilaments];
    private readonly double[] _flickerSpeed = new double[MaxFilaments];

    // Sparks are a fixed pool animated from the clock; no state churn.
    private readonly double[] _particleX = new double[MaxSparks];
    private readonly double[] _particlePhase = new double[MaxSparks];
    private readonly double[] _particleSpeed = new double[MaxSparks];
    private readonly double[] _particleDrift = new double[MaxSparks];
    private readonly double[] _particleSize = new double[MaxSparks];

    // Where each cluster sits this frame, refreshed once per paint.
    private readonly double[] _clusterX = new double[Clusters.Length];
    private readonly double[] _clusterPeak = new double[Clusters.Length];

    private readonly float[] _channelPeaks = new float[2];

    private AudioPeakMeter? _meter;
    private MistStyle _style = MistStyle.All[0];
    private Assets _assets = AssetsFor(MistStyle.All[0]);
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
    private double _debugTime = -1;

    public MistVisualizer()
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

        for (int i = 0; i < MaxSparks; i++)
        {
            _particleX[i] = Hash01(i, 0x71B2D9);
            _particlePhase[i] = Hash01(i, 0x38E4A1);
            _particleSpeed[i] = 0.018 + 0.030 * Hash01(i, 0x5FA0C7);
            _particleDrift[i] = 0.12 + 0.38 * Hash01(i, 0x27C6E5);
            _particleSize[i] = 0.45 + 0.75 * Hash01(i, 0x6A14B3);
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
    /// Turns sampling on or off. Off fades the mist out (~400 ms) and then
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

    internal MistStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            _assets = AssetsFor(value);
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

    /// <summary>Diagnostic override: paint this clock time instead of the live one (-1 = live).</summary>
    internal double DebugTime
    {
        get => _debugTime;
        set
        {
            _debugTime = value;
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

        var style = _style;
        var assets = _assets;
        double t = _debugTime >= 0 ? _debugTime : _clock.Elapsed.TotalSeconds;
        double width = capsule.Width;
        double baseY = capsule.Bottom - 4;
        double maxHeight = Math.Max(1, capsule.Height * 0.95);
        double shift = Math.Clamp(_balance, -1, 1) * width * 0.02;

        int grade = Math.Clamp((int)Math.Round(energy * (AlphaGrades - 1)), 0, AlphaGrades - 1);

        // Clusters drift slowly sideways and breathe on their own beat.
        for (int k = 0; k < Clusters.Length; k++)
        {
            var c = Clusters[k];
            _clusterX[k] = capsule.Left + c.Center * width + shift +
                           Math.Sin(t * (0.07 + 0.03 * k) + k * 1.7) * width * 0.018;
            _clusterPeak[k] = c.Peak * (0.70 + 0.30 * ValueNoise(t * 0.45 + k * 3.1, 0x5A17 + k));
        }

        // ------------------------------------------------------------------
        // Atmospheric bloom behind every cluster.
        // ------------------------------------------------------------------
        for (int k = 0; k < Clusters.Length; k++)
        {
            double peak = _clusterPeak[k];
            double half = Clusters[k].HalfWidth;
            double x = _clusterX[k];
            dc.DrawEllipse(Graded(assets.HazeBody, (0.11 + 0.27 * energy) * (0.5 + 0.5 * peak) * style.Haze), null,
                new Point(x, baseY - maxHeight * 0.30 * peak), half * 3.2, maxHeight * 0.70 * peak);
            dc.DrawEllipse(Graded(assets.HazeCore, (0.08 + 0.20 * energy) * peak * style.Haze), null,
                new Point(x, baseY - maxHeight * 0.36 * peak), half * 1.1, maxHeight * 0.62 * peak);
        }

        // ------------------------------------------------------------------
        // Smoky veils just above the floor, behind the strands.
        // ------------------------------------------------------------------
        double amplitude = 0.12 + 1.10 * Math.Pow(level, 0.66);
        for (int v = 0; v < style.Veils; v++)
        {
            var veil = new StreamGeometry();
            using (var g = veil.Open())
            {
                g.BeginFigure(new Point(capsule.Left, baseY), true, true);
                for (double x = capsule.Left; x < capsule.Right + VeilStep; x += VeilStep)
                {
                    double px = Math.Min(x, capsule.Right);
                    double xn = (px - capsule.Left) / width;
                    double wave = (0.5 + 0.5 * Math.Sin(xn * (9 + 4 * v) + t * (0.35 + 0.12 * v) + v * 2.1)) *
                                  (0.5 + 0.5 * Math.Sin(xn * (23 - 5 * v) - t * (0.22 + 0.07 * v) + v));
                    double lift = 0.06 + (0.20 + 0.05 * (v % 5)) * wave + 0.22 * Envelope(px) * amplitude;
                    double y = baseY - Math.Min(0.55, lift * (0.6 + 0.4 * energy)) * maxHeight * EdgeFade(xn);
                    g.LineTo(new Point(px, y), true, true);
                }

                g.LineTo(new Point(capsule.Right, baseY), false, true);
            }

            veil.Freeze();
            dc.DrawGeometry(assets.VeilFill, assets.VeilHalo, veil);
            dc.DrawGeometry(null, assets.VeilCore, veil);
        }

        // ------------------------------------------------------------------
        // Filaments: one sprite per strand (a thin line or a column of dust),
        // shaped by the cluster envelope.
        // ------------------------------------------------------------------
        int count = Math.Clamp((int)Math.Round(width / (FilamentSpacing * style.Spacing)), 8, MaxFilaments);
        double spacing = width / count;
        int dustFrame = (int)(t * 7);

        for (int i = 0; i < count; i++)
        {
            double xn = (i + 0.5) / count;
            double x = capsule.Left + (i + 0.5) * spacing + _jitter[i] * spacing * 0.70;
            x = Math.Clamp(x, capsule.Left + 0.75, capsule.Right - 0.75);

            double envelope = Envelope(x, out double nearest);

            double seed = _tipSeed[i];
            double noise = 0.62 * ValueNoise(t * _flickerSpeed[i] + seed * 63.0, (int)(seed * 100003)) +
                           0.38 * ValueNoise(t * (_flickerSpeed[i] * 2.3 + 0.4) + seed * 17.0, (int)(seed * 70001));
            noise = Math.Clamp(noise, 0, 1);

            // the strip is shallow, so the clusters run taller than the hologram's single bell
            double h = envelope * maxHeight * amplitude * (0.45 + 0.55 * noise) * style.Height * EdgeFade(xn);

            double clump = 0.72 + 0.50 * ValueNoise(xn * 60.0 + t * 0.18 + 4.7, 0x1F3D);
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
            int family = nearest < 0.35 ? 0 : nearest < 1.2 ? 1 : 2;

            BitmapSource sprite;
            if (Hash01(i, 0x2D55) < style.Dust)
            {
                // dust glitters: each column steps through its frames out of phase with its neighbours
                sprite = assets.Dust[(dustFrame + i) % DustFrames][family][grade];
            }
            else
            {
                bool heavy = Hash01(i, 0x33A7) > 0.86;
                sprite = (heavy ? assets.Heavy : assets.Strands)[family][grade];
            }

            double cx = x + sway * 0.5;
            dc.DrawImage(sprite, new Rect(cx - (SpriteWidth / 2), y0 - h - 1, SpriteWidth, h + 1));

            double tipAlpha = (0.06 + 0.24 * noise) * energy * (0.35 + 0.65 * heightRatio);
            if (tipAlpha > 0.15)
            {
                dc.DrawEllipse(Graded(assets.Spark, tipAlpha), null, new Point(x + sway, y0 - h), 0.8, 0.8);
            }
        }

        // ------------------------------------------------------------------
        // Floor: a faint line of light across the whole strip, pooled under each cluster.
        // ------------------------------------------------------------------
        double poolAlpha = Math.Clamp((0.30 + 0.45 * Math.Sqrt(level)) * fade, 0, 1);
        double mid = capsule.Left + width * 0.5;
        dc.DrawEllipse(Graded(assets.HazeBody, poolAlpha * 0.40), null, new Point(mid, baseY + 1.5), width * 0.52, 7);
        dc.DrawEllipse(Graded(assets.Spark, poolAlpha * 0.22), null, new Point(mid, baseY), width * 0.50, 1.4);
        for (int k = 0; k < Clusters.Length; k++)
        {
            double peak = _clusterPeak[k];
            double half = Clusters[k].HalfWidth;
            dc.DrawEllipse(Graded(assets.Pool, poolAlpha * 0.72 * (0.5 + 0.5 * peak)), null,
                new Point(_clusterX[k], baseY + 0.5), half * 2.4, 4.2 + 3.4 * level);
            dc.DrawEllipse(Graded(assets.Spark, poolAlpha * 0.36 * peak), null,
                new Point(_clusterX[k], baseY), half * 1.2, 1.6);
        }

        // ------------------------------------------------------------------
        // Sparks drifting up out of the clusters.
        // ------------------------------------------------------------------
        for (int i = 0; i < style.Sparks; i++)
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

            int k = i % Clusters.Length;
            double px = _clusterX[k] + (_particleX[i] - 0.5) * Clusters[k].HalfWidth * 2.4 +
                        Math.Sin(t * _particleDrift[i] + _particlePhase[i] * 9) * 3.0;
            dc.DrawEllipse(Graded(assets.Spark, alpha), null, new Point(px, y), _particleSize[i], _particleSize[i]);
        }
    }

    private double Envelope(double x) => Envelope(x, out _);

    /// <summary>
    /// Height envelope at <paramref name="x"/>: the tallest cluster bell there, over a low carpet.
    /// <paramref name="nearest"/> is the distance to the closest cluster in its half-widths.
    /// </summary>
    private double Envelope(double x, out double nearest)
    {
        double best = 0;
        nearest = double.MaxValue;
        for (int k = 0; k < Clusters.Length; k++)
        {
            double d = (x - _clusterX[k]) / Clusters[k].HalfWidth;
            nearest = Math.Min(nearest, Math.Abs(d));
            best = Math.Max(best, _clusterPeak[k] * Math.Exp(-0.5 * d * d));
        }

        return Math.Min(1.12, best + CarpetHeight * (1 - best));
    }

    // The outer 3% of the strip fades out so nothing ends in a hard edge.
    private static double EdgeFade(double xn)
    {
        double e = Math.Clamp(Math.Min(xn, 1 - xn) / 0.03, 0, 1);
        return e * e * (3 - 2 * e);
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

        // See VisualizerPacing: the strip shares the render thread with the dock.
        // Sampling and the envelopes only need to run once per painted frame,
        // so accumulate dt and do that work inside the gate.
        _sinceRepaint += dt;
        if (_sinceRepaint < VisualizerPacing.RepaintInterval)
        {
            return;
        }

        dt = _sinceRepaint;
        _sinceRepaint = 0;

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

        // Below the render threshold OnRender draws nothing, so a repaint would
        // only burn the shared render thread.
        double energy = _fade * (0.30 + 0.70 * Math.Sqrt(Math.Clamp(_level, 0, 1)));
        if (_active && _debugLevel < 0 && energy <= 0.001)
        {
            return;
        }

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

        var channels = _channelPeaks;
        int count = _meter.GetChannelPeaks(channels);
        double left = 0;
        double right = 0;
        if (count >= 2)
        {
            left = channels[0];
            right = channels[1];
        }
        else if (count == 1)
        {
            left = right = channels[0];
        }

        double sum = left + right;
        double balance = sum > 0.0001 ? Math.Clamp((right - left) / sum, -1, 1) : 0;
        _balance += (balance - _balance) * (1 - Math.Exp(-dt / BalanceSeconds));
    }

    /// <summary>Everything a style draws with, built once per style and shared by every instance.</summary>
    private sealed class Assets
    {
        public required BitmapSource[][] Strands { get; init; }
        public required BitmapSource[][] Heavy { get; init; }
        public required BitmapSource[][][] Dust { get; init; }
        public required Brush[] Spark { get; init; }
        public required Brush[] HazeBody { get; init; }
        public required Brush[] HazeCore { get; init; }
        public required Brush[] Pool { get; init; }
        public required Brush VeilFill { get; init; }
        public required Pen VeilHalo { get; init; }
        public required Pen VeilCore { get; init; }
    }

    private static Assets AssetsFor(MistStyle style)
    {
        lock (AssetCache)
        {
            if (AssetCache.TryGetValue(style, out var cached))
            {
                return cached;
            }

            var colors = new[] { style.Core, style.Body, style.Edge };
            var dust = new BitmapSource[DustFrames][][];
            for (int frame = 0; frame < DustFrames; frame++)
            {
                dust[frame] = CreateDustSprites(colors, style.StrandWidth, frame);
            }

            double veil = style.VeilAlpha;
            var assets = new Assets
            {
                Strands = CreateSprites(StrandLayers, colors, style.StrandWidth),
                Heavy = CreateSprites(HeavyLayers, colors, style.StrandWidth),
                Dust = dust,
                Spark = Ladder(CreateSparkBrush(style.Core, style.Body)),
                HazeBody = Ladder(CreateHazeBrush(style.Body)),
                HazeCore = Ladder(CreateHazeBrush(Mix(style.Core, style.Body, 0.3))),
                Pool = Ladder(CreatePoolBrush(style)),
                VeilFill = CreateVeilFill(style.Body, style.Edge, veil),
                VeilHalo = FrozenPen(WithAlpha(style.Body, 0.063 * veil), 7),
                VeilCore = FrozenPen(WithAlpha(Mix(style.Core, style.Body, 0.4), 0.19 * veil), 0.8),
            };
            AssetCache[style] = assets;
            return assets;
        }
    }

    private static BitmapSource[][] CreateSprites((double Thickness, double Alpha)[] layers, Color[] colors, double strandWidth)
    {
        const int scale = 3;
        int width = (int)(SpriteWidth * scale);
        int height = (int)(SpriteHeight * scale);
        var sprites = new BitmapSource[ColorFamilies][];
        for (int family = 0; family < ColorFamilies; family++)
        {
            sprites[family] = new BitmapSource[AlphaGrades];
            var color = colors[family];
            for (int grade = 0; grade < AlphaGrades; grade++)
            {
                double baseAlpha = AlphaLadder[grade] * FamilyScale[family];
                var pixels = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    // 0 at the base (bottom row) to 1 at the tip
                    double v = 1 - ((y + 0.5) / height);
                    double fade = Ramp(v, (0, 1), (0.30, 0.68), (0.62, 0.28), (0.85, 0.07), (1, 0));
                    for (int x = 0; x < width; x++)
                    {
                        double dx = Math.Abs(((x + 0.5) / scale) - (SpriteWidth / 2));
                        double clear = 1;
                        foreach (var (thickness, alpha) in layers)
                        {
                            double coverage = Math.Clamp((thickness * strandWidth / 2) - dx + 0.5, 0, 1);
                            clear *= 1 - (Math.Clamp(baseAlpha * alpha * fade, 0, 1) * coverage);
                        }

                        Put(pixels, ((y * width) + x) * 4, color, 1 - clear);
                    }
                }

                sprites[family][grade] = Freeze(width, height, scale, pixels);
            }
        }

        return sprites;
    }

    /// <summary>
    /// A column of fine glowing dust: sharp specks scattered up the strand, denser
    /// and brighter at the base, each with a faint halo.
    /// </summary>
    private static BitmapSource[][] CreateDustSprites(Color[] colors, double strandWidth, int frame)
    {
        const int scale = 3;
        const int specks = 26;
        int width = (int)(SpriteWidth * scale);
        int height = (int)(SpriteHeight * scale);

        var sx = new double[specks];
        var sy = new double[specks];
        var sr = new double[specks];
        var sa = new double[specks];
        for (int i = 0; i < specks; i++)
        {
            int seed = frame * 131 + i;
            // squaring biases the specks toward the base
            double v = Math.Pow(Hash01(seed, 0x6E11), 1.6);
            sx[i] = (SpriteWidth / 2 + (Hash01(seed, 0x3C07) - 0.5) * 2.6 * strandWidth / 0.45) * scale;
            sy[i] = (1 - v) * height;
            sr[i] = (0.32 + 0.30 * Hash01(seed, 0x19D3)) * scale;
            sa[i] = (0.55 + 0.45 * Hash01(seed, 0x7A21)) * Ramp(v, (0, 1), (0.5, 0.6), (0.85, 0.2), (1, 0));
        }

        var sprites = new BitmapSource[ColorFamilies][];
        for (int family = 0; family < ColorFamilies; family++)
        {
            sprites[family] = new BitmapSource[AlphaGrades];
            var color = colors[family];
            for (int grade = 0; grade < AlphaGrades; grade++)
            {
                double baseAlpha = Math.Min(1, AlphaLadder[grade] * FamilyScale[family] * 1.8);
                var pixels = new byte[width * height * 4];
                for (int i = 0; i < specks; i++)
                {
                    double halo = sr[i] * 3.2;
                    int x0 = Math.Max(0, (int)(sx[i] - halo));
                    int x1 = Math.Min(width - 1, (int)(sx[i] + halo));
                    int y0 = Math.Max(0, (int)(sy[i] - halo));
                    int y1 = Math.Min(height - 1, (int)(sy[i] + halo));
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            double d = Math.Sqrt(Math.Pow(x + 0.5 - sx[i], 2) + Math.Pow(y + 0.5 - sy[i], 2));
                            double core = Math.Clamp(sr[i] - d + 0.5, 0, 1);
                            double glow = Math.Max(0, 1 - d / halo) * 0.22;
                            double a = Math.Clamp(baseAlpha * sa[i] * Math.Max(core, glow * glow * 4), 0, 1);
                            int p = ((y * width) + x) * 4;
                            // screen-blend specks that overlap
                            double prior = pixels[p + 3] / 255.0;
                            Put(pixels, p, color, 1 - (1 - prior) * (1 - a));
                        }
                    }
                }

                sprites[family][grade] = Freeze(width, height, scale, pixels);
            }
        }

        return sprites;
    }

    private static void Put(byte[] pixels, int i, Color color, double a)
    {
        pixels[i] = (byte)Math.Round(color.B * a);
        pixels[i + 1] = (byte)Math.Round(color.G * a);
        pixels[i + 2] = (byte)Math.Round(color.R * a);
        pixels[i + 3] = (byte)Math.Round(255 * a);
    }

    private static BitmapSource Freeze(int width, int height, int scale, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
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

    private static RadialGradientBrush Radial()
    {
        return new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
    }

    private static Brush CreateSparkBrush(Color core, Color body)
    {
        var brush = Radial();
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(Colors.White, core, 0.5), 0.85), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(core, 0.47), 0.30));
        brush.GradientStops.Add(new GradientStop(WithAlpha(body, 0.14), 0.64));
        brush.GradientStops.Add(new GradientStop(WithAlpha(body, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateHazeBrush(Color color)
    {
        var brush = Radial();
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.34), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.18), 0.34));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.06), 0.68));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreatePoolBrush(MistStyle style)
    {
        var brush = Radial();
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(Colors.White, style.Core, 0.5), 0.95), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(style.Core, style.Body, 0.5), 0.66), 0.22));
        brush.GradientStops.Add(new GradientStop(WithAlpha(style.Body, 0.31), 0.52));
        brush.GradientStops.Add(new GradientStop(WithAlpha(style.Edge, 0.06), 0.80));
        brush.GradientStops.Add(new GradientStop(WithAlpha(style.Edge, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateVeilFill(Color body, Color edge, double strength)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(body, Colors.White, 0.2), 0.20 * strength), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(body, edge, 0.5), 0.086 * strength), 0.55));
        brush.GradientStops.Add(new GradientStop(WithAlpha(edge, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
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

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 255),
            color.R, color.G, color.B);
}
