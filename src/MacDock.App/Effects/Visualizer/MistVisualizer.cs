using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// A cold "mist" field of blue light: seven wandering hot spots, each a
/// flame-shaped bundle of thin strands that stay near vertical and bend at
/// most a few DIP at the tip. A dense carpet of short faint strands runs the
/// full width so the strip never looks empty, a few luminous wisps drift just
/// above the floor, and a soft glow sits behind every bundle.
/// <para>
/// Sampling follows the same lifecycle as <see cref="VisualizerGlow"/> and
/// <see cref="HologramVisualizer"/>: the render callback is hooked only while
/// <see cref="IsActive"/> is set or the ~400 ms fade-out is still running.
/// </para>
/// </summary>
public sealed class MistVisualizer : FrameworkElement
{
    // Audio shaping, matching VisualizerGlow/HologramVisualizer so all three
    // visualizers react alike.
    private const double GainDecaySeconds = 4.0;
    private const double GainFloor = 0.002;
    private const double SilenceFloor = 0.0003;
    private const double AttackSeconds = 0.05;
    private const double ReleaseSeconds = 0.30;
    private const double BalanceSeconds = 0.18;
    private const double FadeOutSeconds = 0.40;
    private const double FadeInSeconds = 0.22;

    // Seven hot spots across the width, each a flame-shaped bundle 40-90 DIP
    // wide with 18 strands. The remaining budget is a dense low carpet: at the
    // dock's ~1030 DIP width that is one strand every ~8 DIP.
    private const int BundleCount = 7;
    private const int StrandsPerBundle = 18;
    private const int CarpetCount = 134;
    private const int MaxStrands = BundleCount * StrandsPerBundle + CarpetCount;

    private const double BundleMinWidth = 40.0;
    private const double BundleWidthRange = 50.0;

    // Strands are near vertical: the tip may wander at most this far from the
    // root, so a bundle never fans out from a common point.
    private const double MaxTipBend = 4.0;

    private const int SparkCount = 24;
    private const int WispCount = 3;
    private const double WispSampleStep = 8.0;

    private const int OpacitySteps = 32;

    // Pen ladders: strand strokes are cached frozen pens, quantised by width
    // and alpha, so a frame never builds a brush or a pen.
    private const int PenAlphaSteps = 12;
    private const double CoreWidthMin = 0.30;
    private const double CoreWidthStep = 0.13;
    private const int CoreWidthSteps = 10;
    private const double HaloWidthMin = 1.8;
    private const double HaloWidthStep = 0.4;
    private const int HaloWidthSteps = 8;

    // The required blue ramp: pale icy root, cyan at 40%, deep blue at 80%,
    // transparent tip. The root is the only near-white part of a strand.
    private static readonly Color RootColor = Color.FromRgb(0xE6, 0xF6, 0xFF);
    private static readonly Color MidColor = Color.FromRgb(0x7C, 0xC8, 0xFF);
    private static readonly Color DeepColor = Color.FromRgb(0x2E, 0x8B, 0xFF);
    private static readonly Color GlowColor = Color.FromRgb(0x2F, 0x7D, 0xFF);

    // Frozen brush ladders. Every strand/wisp/glow picks one pre-built brush
    // per frame; no brushes are created inside OnRender.
    private static readonly Brush[] ReflectionBrushes = Ladder(CreateReflectionBrush());
    private static readonly Brush[] SparkBrushes = Ladder(new SolidColorBrush(RootColor));
    private static readonly Brush[] CoreLineBrushes = Ladder(new SolidColorBrush(Color.FromRgb(0xF6, 0xFC, 0xFF)));

    // ~8 DIP visible cyan bloom standing on the base line.
    private static readonly Brush[] BaseBloomBrushes = Ladder(CreateBaseBloomBrush());

    // Wide elliptical glow behind a bundle and the brighter pool it casts on
    // the floor.
    private static readonly Brush[] BundleGlowBrushes = Ladder(CreateBundleGlowBrush());
    private static readonly Brush[] PoolBrushes = Ladder(CreatePoolBrush());

    // Cached gradient pens for the strand cores: every pen restarts the ramp
    // at its own root, so a short carpet strand and a tall bundle strand both
    // read pale at the root and transparent at the tip.
    private static readonly Pen[][] CorePens =
        BuildGradientPens(CoreWidthMin, CoreWidthStep, CoreWidthSteps, PenAlphaSteps);

    // A single deep-blue halo pen ladder; halos never use a light colour.
    private static readonly Pen[][] HaloPens =
        BuildPens(DeepColor, HaloWidthMin, HaloWidthStep, HaloWidthSteps);

    // One smooth curve per wisp: round joins and round caps, no dots.
    private static readonly Pen WispCorePen = CreateWispPen(MidColor, 0.22, 1.05);
    private static readonly Pen WispHaloPen = CreateWispPen(DeepColor, 0.06, 4.6);

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Per-strand deterministic constants, allocated once. Bundle strands carry
    // a DIP offset from the bundle centre; carpet strands carry a normalised
    // position across the full width.
    private readonly int[] _strandBundle = new int[MaxStrands];
    private readonly double[] _offset = new double[MaxStrands];
    private readonly double[] _heightScale = new double[MaxStrands];
    private readonly double[] _widthScale = new double[MaxStrands];
    private readonly double[] _bright = new double[MaxStrands];
    private readonly double[] _bend = new double[MaxStrands];
    private readonly double[] _swayPhase = new double[MaxStrands];
    private readonly double[] _swaySpeed = new double[MaxStrands];
    private readonly double[] _seed = new double[MaxStrands];
    private readonly double[] _noiseSpeed = new double[MaxStrands];
    private readonly double[] _noisePhase = new double[MaxStrands];

    // Eased strand heights (0..1 of the strip height) and this frame's targets.
    private readonly double[] _heights = new double[MaxStrands];
    private readonly double[] _targets = new double[MaxStrands];

    // Bundle state, refreshed from the clock every frame.
    private readonly double[] _bundlePos = new double[BundleCount];
    private readonly double[] _bundleEnergy = new double[BundleCount];
    private readonly double[] _bundleSpeed = new double[BundleCount];
    private readonly double[] _bundlePhase = new double[BundleCount];
    private readonly double[] _bundleAmp = new double[BundleCount];
    private readonly double[] _bundleSeed = new double[BundleCount];
    private readonly double[] _bundleHeight = new double[BundleCount];
    private readonly double[] _bundleWidth = new double[BundleCount];

    // Three wisps, each cached as one reusable StreamGeometry. Open() clears
    // the previous content, so nothing is allocated after the first paint.
    private readonly StreamGeometry?[] _wispGeometry = new StreamGeometry?[WispCount];
    private readonly double[] _wispPhase = new double[WispCount];
    private readonly double[] _wispSpeed = new double[WispCount];

    // Strands tall enough to carry a sparkle or a reflection, gathered per frame.
    private readonly int[] _tallStrands = new int[MaxStrands];
    private int _tallCount;

    // Sparkles are a fixed pool animated from the clock; no state churn.
    private readonly double[] _sparkX = new double[SparkCount];
    private readonly double[] _sparkPhase = new double[SparkCount];
    private readonly double[] _sparkSpeed = new double[SparkCount];
    private readonly double[] _sparkDrift = new double[SparkCount];

    private readonly float[] _channelPeaks = new float[2];

    // Cached clip geometry and edge-fade mask, rebuilt only when the capsule
    // actually changes.
    private Geometry? _clipGeometry;
    private Rect _clipRect;
    private Brush? _edgeMask;
    private Rect _maskRect;

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

    public MistVisualizer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        int strand = 0;
        for (int b = 0; b < BundleCount; b++)
        {
            _bundleSpeed[b] = 0.055 + 0.055 * Hash01(b, 0x1D3F51);
            _bundlePhase[b] = 6.283 * (b / (double)BundleCount) + 1.2 * Hash01(b, 0x6C2A77);
            _bundleAmp[b] = 0.028 + 0.018 * Hash01(b, 0x40B1E3);
            _bundleSeed[b] = Hash01(b, 0x5E9C21);
            _bundleWidth[b] = Hash01(b, 0x73AD05);
            _bundleHeight[b] = Hash01(b, 0x2F91A3);

            double bundleWidth = BundleMinWidth + BundleWidthRange * _bundleSeed[b];
            for (int k = 0; k < StrandsPerBundle; k++, strand++)
            {
                _strandBundle[strand] = b;

                // Roots are spread across the bundle width with a little
                // jitter. There is no common root, and the tip stays within
                // MaxTipBend of its own root.
                double u = (k + 0.5) / StrandsPerBundle;
                double jitter = (Hash01(strand, 0x51ED27) - 0.5) * bundleWidth * 0.07;
                double off = (u - 0.5) * bundleWidth + jitter;
                _offset[strand] = off;

                // Flame envelope: tall in the middle, shorter at the edges.
                double edge = Math.Clamp(Math.Abs(off) / (bundleWidth * 0.5), 0, 1);
                double flame = Math.Pow(Math.Cos(edge * Math.PI * 0.5), 0.72);
                _heightScale[strand] = (0.30 + 0.68 * flame) *
                                       (0.84 + 0.20 * Hash01(strand, 0x2F91A3));
                _widthScale[strand] = 0.60 + 1.00 * Hash01(strand, 0x7C3B19);
                _bright[strand] = 0.65 + 0.40 * Hash01(strand, 0x19AF43);
                _bend[strand] = Hash01(strand, 0x2D6B11) * 2 - 1;
                _swayPhase[strand] = 6.283 * Hash01(strand, 0x58C3A7);
                _swaySpeed[strand] = 0.45 + 1.10 * Hash01(strand, 0x6F14D9);
                _seed[strand] = Hash01(strand, 0x77A31D);
                _noiseSpeed[strand] = 0.65 + 1.55 * Hash01(strand, 0x3B7E55);
                _noisePhase[strand] = 6.283 * Hash01(strand, 0x19AF43);
            }
        }

        for (int c = 0; c < CarpetCount; c++, strand++)
        {
            _strandBundle[strand] = -1;

            // Random positions at roughly one every width/CarpetCount DIP,
            // jittered so no two carpet strands line up.
            _offset[strand] = (c + 0.15 + 0.70 * Hash01(strand, 0x60D1B7)) / CarpetCount;
            _heightScale[strand] = 0.05 + 0.15 * Hash01(strand, 0x14C9E1);
            _widthScale[strand] = 0.45 + 0.55 * Hash01(strand, 0x7A21F5);
            _bright[strand] = 0.25 + 0.40 * Hash01(strand, 0x3720B9);
            _bend[strand] = Hash01(strand, 0x4C8D23) * 2 - 1;
            _swayPhase[strand] = 6.283 * Hash01(strand, 0x2E5F87);
            _swaySpeed[strand] = 0.40 + 1.00 * Hash01(strand, 0x0B3C6D);
            _seed[strand] = Hash01(strand, 0x71B2D9);
            _noiseSpeed[strand] = 0.65 + 1.55 * Hash01(strand, 0x5FA0C7);
            _noisePhase[strand] = 6.283 * Hash01(strand, 0x27C6E5);
        }

        for (int w = 0; w < WispCount; w++)
        {
            _wispPhase[w] = 6.283 * Hash01(w, 0x33D2A1);
            _wispSpeed[w] = 0.13 + 0.08 * Hash01(w, 0x6A14B3);
        }

        for (int i = 0; i < SparkCount; i++)
        {
            _sparkX[i] = Hash01(i, 0x71B2D9);
            _sparkPhase[i] = Hash01(i, 0x38E4A1);
            _sparkSpeed[i] = 0.045 + 0.055 * Hash01(i, 0x5FA0C7);
            _sparkDrift[i] = 0.14 + 0.40 * Hash01(i, 0x27C6E5);
        }

        _fade = 0;
    }

    /// <summary>
    /// Capsule bounds in this element's coordinates. The base line sits just
    /// above the bottom; the loudest strands reach 85-95% of the capsule.
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
        double stripHeight = capsule.Height;

        // A thin mirrored strip below the line; the mist itself owns almost
        // all of the height.
        double reflectionDepth = Math.Clamp(stripHeight * 0.055, 2.5, 5.0);
        double baseY = capsule.Bottom - reflectionDepth;
        double maxHeight = Math.Max(1, baseY - capsule.Top - 0.5);

        ComputeFrame(t, level);

        // The live path eases the heights in OnRendering; a preview paints the
        // targets directly because no rendering hook runs for it.
        double[] heights = debug >= 0 ? _targets : _heights;

        PushClipAndMask(dc, capsule);
        try
        {
            // --------------------------------------------------------------
            // Volumetric body: a wide soft glow behind every bundle, then the
            // luminous wisps flowing just above the floor.
            // --------------------------------------------------------------
            DrawBundleGlows(dc, capsule, baseY, stripHeight, level, fade);
            DrawWisps(dc, capsule, baseY, stripHeight, t, fade);

            // --------------------------------------------------------------
            // Glowing floor: cyan bloom with a brighter pool under each bundle.
            // --------------------------------------------------------------
            DrawFloor(dc, capsule, baseY, level, energy);

            DrawReflection(dc, capsule, baseY, reflectionDepth, heights, energy);

            // --------------------------------------------------------------
            // Strands: all halos first, then the bright cores, so a later halo
            // never dims an earlier core.
            // --------------------------------------------------------------
            DrawStrandHalos(dc, capsule, baseY, stripHeight, maxHeight, heights, energy, t);
            DrawStrandCores(dc, capsule, baseY, stripHeight, maxHeight, heights, energy, t);

            // --------------------------------------------------------------
            // Base line: 1.5 DIP white core with a soft cyan underglow.
            // --------------------------------------------------------------
            DrawBaseLine(dc, capsule, baseY, level, fade);

            // --------------------------------------------------------------
            // Tiny sparkles drifting up along the tall strands.
            // --------------------------------------------------------------
            DrawSparkles(dc, capsule, baseY, stripHeight, t, heights, energy);
        }
        finally
        {
            dc.Pop(); // edge mask
            dc.Pop(); // clip
        }
    }

    // ------------------------------------------------------------------
    // Volumetric mist
    // ------------------------------------------------------------------

    private void DrawBundleGlows(DrawingContext dc, Rect capsule, double baseY, double stripHeight,
        double level, double fade)
    {
        // 1.5x the old body, capped at 0.35 overall opacity at loud level.
        double alpha = Math.Min(0.35, (0.10 + 0.25 * level) * fade);
        if (alpha <= 0.004)
        {
            return;
        }

        for (int b = 0; b < BundleCount; b++)
        {
            double x = capsule.Left + _bundlePos[b] * capsule.Width;
            double w = 1.5 * (120 + 80 * _bundleWidth[b]);
            double h = 1.5 * stripHeight * (0.60 + 0.30 * _bundleHeight[b]);
            double a = alpha * _bundleEnergy[b];
            dc.DrawEllipse(Graded(BundleGlowBrushes, a), null,
                new Point(x, baseY - h * 0.5), w * 0.5, h * 0.5);
        }
    }

    private void DrawWisps(DrawingContext dc, Rect capsule, double baseY, double stripHeight,
        double t, double fade)
    {
        if (fade <= 0.01)
        {
            return;
        }

        double width = capsule.Width;
        if (width <= 1)
        {
            return;
        }

        int samples = Math.Max(2, (int)Math.Ceiling(width / WispSampleStep));
        double stepX = width / samples;

        // The pens carry the requested 0.22 core / 0.06 halo alpha; the fade
        // multiplies both without needing another pen ladder.
        dc.PushOpacity(fade);
        try
        {
            for (int w = 0; w < WispCount; w++)
            {
                StreamGeometry geometry = _wispGeometry[w] ??= new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    double startY = WispY(0, capsule, baseY, stripHeight, t, w);
                    ctx.BeginFigure(new Point(capsule.Left, startY), false, false);
                    for (int s = 1; s <= samples; s++)
                    {
                        double u = s / (double)samples;
                        double x = capsule.Left + stepX * s;
                        ctx.LineTo(new Point(x, WispY(u, capsule, baseY, stripHeight, t, w)),
                            true, false);
                    }
                }

                dc.DrawGeometry(null, WispHaloPen, geometry);
                dc.DrawGeometry(null, WispCorePen, geometry);
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private double WispY(double u, Rect capsule, double baseY, double stripHeight,
        double t, int wisp)
    {
        // The bundles only lift the wisp a little; it otherwise hugs the floor.
        double rise = 0;
        double width = capsule.Width;
        for (int b = 0; b < BundleCount; b++)
        {
            double dx = (u - _bundlePos[b]) * width;
            double near = Math.Exp(-(dx * dx) / (2 * 55.0 * 55.0));
            rise = Math.Max(rise, near * _bundleEnergy[b]);
        }

        // Two sine waves scrolling sideways plus a slow noise wobble.
        double s1 = Math.Sin((u * 4.2 - t * _wispSpeed[wisp]) * 6.283 + _wispPhase[wisp]);
        double s2 = Math.Sin((u * 9.1 + t * _wispSpeed[wisp] * 0.55) * 6.283 + _wispPhase[wisp] * 1.7);
        double n = ValueNoise(u * 7.0 + t * 0.24 + wisp * 13.0, 0x2C71) - 0.5;

        double lift = 0.080 + 0.045 * s1 + 0.022 * s2 + 0.035 * n + 0.085 * rise;
        lift = Math.Clamp(lift, 0.015, 0.25);
        double y = baseY - stripHeight * lift;
        return Math.Clamp(y, capsule.Top + 0.5, baseY - 0.5);
    }

    private void DrawFloor(DrawingContext dc, Rect capsule, double baseY, double level, double energy)
    {
        // Faint uniform wash under everything; the pools add the hot spots.
        const double washHeight = 11.0;
        dc.DrawRectangle(Graded(BaseBloomBrushes, (0.10 + 0.06 * level) * energy), null,
            new Rect(capsule.Left, baseY - washHeight, capsule.Width, washHeight));

        for (int b = 0; b < BundleCount; b++)
        {
            double x = capsule.Left + _bundlePos[b] * capsule.Width;
            double a = (0.16 + 0.24 * level) * _bundleEnergy[b] * energy;
            double rx = 36 + 14 * _bundleWidth[b];
            dc.DrawEllipse(Graded(PoolBrushes, a), null, new Point(x, baseY - 0.5), rx, 6.2);
        }
    }

    // ------------------------------------------------------------------
    // Strands
    // ------------------------------------------------------------------

    private void DrawReflection(DrawingContext dc, Rect capsule, double baseY, double reflectionDepth,
        double[] heights, double energy)
    {
        double alpha = Math.Min(0.10, 0.10 * energy);
        for (int i = 0; i < MaxStrands; i++)
        {
            double heightRatio = Math.Clamp(heights[i], 0, 1);
            if (heightRatio < 0.40 || _strandBundle[i] < 0)
            {
                continue;
            }

            double x = StrandX(capsule, i);
            double w = _widthScale[i] * 2.0;
            double h = reflectionDepth * Math.Clamp(heightRatio * 1.6, 0.35, 1.0);
            double a = alpha * (0.45 + 0.55 * heightRatio);
            dc.DrawRectangle(Graded(ReflectionBrushes, a), null,
                new Rect(x - w * 0.5, baseY, w, h));
        }
    }

    private void DrawStrandHalos(DrawingContext dc, Rect capsule, double baseY, double stripHeight,
        double maxHeight, double[] heights, double energy, double t)
    {
        _tallCount = 0;
        for (int i = 0; i < MaxStrands; i++)
        {
            double heightRatio = Math.Clamp(heights[i], 0, 1);
            double h = Math.Min(maxHeight, heightRatio * stripHeight);
            if (h < 1.0)
            {
                continue;
            }

            double x = StrandX(capsule, i);
            StrandTip(i, x, baseY, h, t, out double tipX, out double tipY);

            if (heightRatio > 0.42)
            {
                // One deep-blue stroke behind the core; never a second light
                // strand.
                double width = 2.2 + 1.8 * _widthScale[i];
                double a = (0.07 + 0.10 * heightRatio) * energy * _bright[i];
                dc.DrawLine(HaloPen(width, a), new Point(x, baseY), new Point(tipX, tipY));
            }

            if (heightRatio > 0.38 && _tallCount < _tallStrands.Length)
            {
                _tallStrands[_tallCount++] = i;
            }
        }
    }

    private void DrawStrandCores(DrawingContext dc, Rect capsule, double baseY, double stripHeight,
        double maxHeight, double[] heights, double energy, double t)
    {
        for (int i = 0; i < MaxStrands; i++)
        {
            double heightRatio = Math.Clamp(heights[i], 0, 1);
            double h = Math.Min(maxHeight, heightRatio * stripHeight);
            if (h < 0.8)
            {
                continue;
            }

            double x = StrandX(capsule, i);
            StrandTip(i, x, baseY, h, t, out double tipX, out double tipY);

            double alpha = (0.55 + 0.35 * _bright[i]) * energy * (0.62 + 0.38 * heightRatio);
            if (_strandBundle[i] < 0)
            {
                alpha *= 0.70;
            }

            // A single straight root-to-tip stroke: near vertical, no visible
            // segment joints, and the cached gradient pen supplies the ramp.
            dc.DrawLine(CorePen(_widthScale[i], alpha),
                new Point(x, baseY), new Point(tipX, tipY));
        }
    }

    private void StrandTip(int i, double x, double baseY, double h, double t,
        out double tipX, out double tipY)
    {
        double sway = Math.Sin(t * _swaySpeed[i] + _swayPhase[i]);
        double wobble = ValueNoise(t * 0.85 + _seed[i] * 13.0, 0x77C3) - 0.5;
        double bend = _bend[i] * 2.2 + sway * 1.1 + wobble * 1.2;
        bend = Math.Clamp(bend, -MaxTipBend, MaxTipBend);

        tipX = x + bend;
        tipY = baseY - h;
    }

    private double StrandX(Rect capsule, int i)
    {
        double x = _strandBundle[i] >= 0
            ? capsule.Left + _bundlePos[_strandBundle[i]] * capsule.Width + _offset[i]
            : capsule.Left + _offset[i] * capsule.Width;
        return Math.Clamp(x, capsule.Left + 0.5, capsule.Right - 0.5);
    }

    private void DrawBaseLine(DrawingContext dc, Rect capsule, double baseY, double level, double fade)
    {
        // Cyan underglow hugging the line, wide but very faint.
        double underglow = (0.20 + 0.22 * Math.Sqrt(level)) * fade;
        dc.DrawRectangle(Graded(BaseBloomBrushes, underglow), null,
            new Rect(capsule.Left, baseY - 3.4, capsule.Width, 3.4));

        // The white-hot core, 1.5 DIP tall.
        double core = (0.55 + 0.33 * Math.Sqrt(level)) * fade;
        dc.DrawRectangle(Graded(CoreLineBrushes, core), null,
            new Rect(capsule.Left, baseY - 1.5, capsule.Width, 1.5));
    }

    private void DrawSparkles(DrawingContext dc, Rect capsule, double baseY, double stripHeight,
        double t, double[] heights, double energy)
    {
        if (_tallCount <= 0)
        {
            return;
        }

        for (int i = 0; i < SparkCount; i++)
        {
            int strand = _tallStrands[(i + (int)(t * 0.45)) % _tallCount];
            double heightRatio = Math.Clamp(heights[strand], 0, 1);
            if (heightRatio < 0.30)
            {
                continue;
            }

            double progress = _sparkPhase[i] + t * _sparkSpeed[i];
            progress -= Math.Floor(progress);

            // Start at mid height, climb along the strand and fade before the
            // tip; a sparkle is never drawn on the base line.
            double travel = 0.45 + 0.55 * progress;
            double alpha = Math.Pow(Math.Sin(Math.PI * progress), 1.5) * (0.55 + 0.35 * energy);
            if (alpha <= 0.01)
            {
                continue;
            }

            double x = StrandX(capsule, strand);
            double h = heightRatio * stripHeight;
            StrandTip(strand, x, baseY, h, t, out double tipX, out _);

            x += (tipX - x) * travel +
                 (_sparkX[i] - 0.5) * 1.6 +
                 Math.Sin(t * _sparkDrift[i] + _sparkPhase[i] * 9) * 0.7;
            double y = baseY - h * travel;

            dc.DrawEllipse(Graded(SparkBrushes, alpha), null, new Point(x, y), 0.8, 0.8);
        }
    }

    // ------------------------------------------------------------------
    // Frame state
    // ------------------------------------------------------------------

    private void ComputeFrame(double t, double level)
    {
        double lv = Math.Clamp(level, 0, 1);

        // Height scale: a calm idle floor plus the live level. sqrt keeps quiet
        // material visible while loud passages still open up the strip.
        double drive = 0.24 + 0.78 * Math.Pow(lv, 0.45);
        double pan = Math.Clamp(_balance, -1, 1) * 0.02;

        for (int b = 0; b < BundleCount; b++)
        {
            // Each bundle keeps its own slot across the width and only wanders
            // a couple of percent, so the seven hot spots stay spread out but
            // never sit on a perfect grid.
            double slot = (b + 0.5) / BundleCount + (_bundleSeed[b] - 0.5) * 0.055;
            double wander = _bundleAmp[b] *
                (0.62 * Math.Sin(t * _bundleSpeed[b] + _bundlePhase[b]) +
                 0.38 * Math.Sin(t * (_bundleSpeed[b] * 1.7 + 0.02) + _bundlePhase[b] * 1.73));
            _bundlePos[b] = Math.Clamp(slot + pan + wander, 0.045, 0.955);

            double pulse = 0.5 + 0.5 * Math.Sin(t * (0.75 + 0.18 * b) + _bundlePhase[b] * 2.1);
            double breathe = 0.80 + 0.20 * ValueNoise(t * 0.8 + b * 3.7, 0x2B17);
            _bundleEnergy[b] = (0.55 + 0.45 * pulse) * breathe;
        }

        for (int i = 0; i < MaxStrands; i++)
        {
            double flicker = 0.5 * (ValueNoise(t * _noiseSpeed[i] + _seed[i] * 71.0, 0x51ED27) +
                                    ValueNoise(t * (_noiseSpeed[i] * 1.7 + 0.11) + _noisePhase[i], 0x3D71B5));

            int bundle = _strandBundle[i];
            double target;
            if (bundle >= 0)
            {
                target = _heightScale[i] * drive *
                         (0.72 + 0.38 * flicker) *
                         (0.80 + 0.20 * _bundleEnergy[bundle]);
            }
            else
            {
                target = _heightScale[i] * drive * (0.70 + 0.45 * flicker);
            }

            _targets[i] = Math.Clamp(target, 0, 0.94);
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

        UpdateStrands(dt, _clock.Elapsed.TotalSeconds, _level);

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

    private void UpdateStrands(double dt, double t, double level)
    {
        ComputeFrame(t, level);

        // Fast attack, slow release: loud passages push tall strands at once,
        // quiet ones let them sink back over a few hundred milliseconds.
        for (int i = 0; i < MaxStrands; i++)
        {
            double target = _targets[i];
            double tau = target > _heights[i] ? 0.045 : 0.30;
            _heights[i] += (target - _heights[i]) * (1 - Math.Exp(-dt / tau));
        }
    }

    private void SampleAudio(double dt)
    {
        _meter ??= new AudioPeakMeter();

        // Auto gain: normalise the endpoint meter against a slowly decaying
        // recent peak so quiet apps still fill the strip.
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

    // ------------------------------------------------------------------
    // Clip / mask, cached per capsule
    // ------------------------------------------------------------------

    private void PushClipAndMask(DrawingContext dc, Rect capsule)
    {
        if (_clipGeometry is null || _clipRect != capsule)
        {
            var geometry = new RectangleGeometry(capsule);
            geometry.Freeze();
            _clipGeometry = geometry;
            _clipRect = capsule;
        }

        dc.PushClip(_clipGeometry);

        if (_edgeMask is null || _maskRect != capsule)
        {
            _edgeMask = CreateEdgeMask(capsule);
            _maskRect = capsule;
        }

        dc.PushOpacityMask(_edgeMask);
    }

    private static Brush CreateEdgeMask(Rect capsule)
    {
        // Outer 4% of the strip fades to nothing on both sides.
        const double inset = 0.04;
        var mask = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(capsule.Left, capsule.Top),
            EndPoint = new Point(capsule.Right, capsule.Top)
        };
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), inset));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 1 - inset));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
        mask.Freeze();
        return mask;
    }

    // ------------------------------------------------------------------
    // Geometry helpers
    // ------------------------------------------------------------------

    private static Pen[][] BuildPens(Color color, double minWidth, double step, int steps)
    {
        var pens = new Pen[steps][];
        for (int i = 0; i < steps; i++)
        {
            pens[i] = PenLadder(color, minWidth + i * step, PenAlphaSteps);
        }

        return pens;
    }

    // Builds pens whose brush is the blue root->tip ramp. Each strand is one
    // line, so RelativeToBoundingBox restarts the ramp over that strand's own
    // height: pale root, cyan middle, deep-blue shoulder, clear tip.
    private static Pen[][] BuildGradientPens(double minWidth, double step, int widthSteps, int alphaSteps)
    {
        var pens = new Pen[widthSteps][];
        for (int w = 0; w < widthSteps; w++)
        {
            pens[w] = new Pen[alphaSteps + 1];
            for (int a = 0; a <= alphaSteps; a++)
            {
                double alpha = (double)a / alphaSteps;
                var brush = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    StartPoint = new Point(0.5, 1),
                    EndPoint = new Point(0.5, 0)
                };
                brush.GradientStops.Add(new GradientStop(WithAlpha(RootColor, 0.95 * alpha), 0));
                brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0.95 * alpha), 0.4));
                brush.GradientStops.Add(new GradientStop(WithAlpha(DeepColor, 0.90 * alpha), 0.8));
                brush.GradientStops.Add(new GradientStop(WithAlpha(DeepColor, 0), 1));
                brush.Freeze();

                var pen = new Pen(brush, minWidth + w * step)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();
                pens[w][a] = pen;
            }
        }

        return pens;
    }

    private static Pen[] PenLadder(Color color, double width, int steps)
    {
        var pens = new Pen[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            var brush = new SolidColorBrush(WithAlpha(color, (double)i / steps));
            brush.Freeze();
            var pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            pens[i] = pen;
        }

        return pens;
    }

    private static Pen CreateWispPen(Color color, double alpha, double width)
    {
        var brush = new SolidColorBrush(WithAlpha(color, alpha));
        brush.Freeze();
        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CorePen(double width, double alpha)
    {
        int w = (int)Math.Round((width - CoreWidthMin) / CoreWidthStep);
        w = Math.Clamp(w, 0, CoreWidthSteps - 1);
        int a = (int)Math.Round(Math.Clamp(alpha, 0, 1) * PenAlphaSteps);
        return CorePens[w][a];
    }

    private static Pen HaloPen(double width, double alpha)
    {
        int w = (int)Math.Round((width - HaloWidthMin) / HaloWidthStep);
        w = Math.Clamp(w, 0, HaloWidthSteps - 1);
        int a = (int)Math.Round(Math.Clamp(alpha, 0, 1) * PenAlphaSteps);
        return HaloPens[w][a];
    }

    // ------------------------------------------------------------------
    // Frozen brushes
    // ------------------------------------------------------------------

    private static Brush CreateReflectionBrush()
    {
        // Top of the reflection is at the base line; the alpha ceiling here is
        // 0.12 so the mirror never outshines the strands.
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(DeepColor, 0.12), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(DeepColor, 0.05), 0.55));
        brush.GradientStops.Add(new GradientStop(WithAlpha(DeepColor, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBaseBloomBrush()
    {
        // Bottom (offset 1) meets the base line.
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1)
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0.22), 0.25));
        brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0.60), 0.60));
        brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0.85), 0.88));
        brush.GradientStops.Add(new GradientStop(WithAlpha(MidColor, 0.98), 1));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateBundleGlowBrush()
    {
        // An ellipse-sized soft blue body, biased toward the floor so the
        // bundle looks rooted rather than floating.
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.62),
            GradientOrigin = new Point(0.5, 0.62),
            RadiusX = 0.5,
            RadiusY = 0.62
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowColor, 0.90), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowColor, 0.55), 0.38));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowColor, 0.20), 0.68));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowColor, 0.05), 0.88));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowColor, 0), 1));
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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xD8, 0xF2, 0xFF), 0.30));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 0x7C, 0xC8, 0xFF), 0.68));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x2E, 0x8B, 0xFF), 1));
        brush.Freeze();
        return brush;
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
