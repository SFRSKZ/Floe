using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Trauma Team Biometric (Clinical Med-HUD): ultra-sharp surgical-teal, stark-white and arterial-crimson medical
/// OLED telemetry monitor. Renders crisp 1px millimeter graph gridlines, Lead II P-QRS-T cardiac complexes, continuous
/// smooth SpO2 plethysmograph trace, precision 24-channel O2 micro-bars with peak-hold ticks, and readable left-to-right
/// clinical typography.
/// </summary>
public sealed class TraumaBiometricGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 92;
    private const int MaxPulses = 6;
    private static readonly Color SurgicalTeal = Color.FromRgb(0x00, 0xE6, 0xBC);
    private static readonly Color SurgicalWhite = Color.FromRgb(0xEE, 0xFA, 0xF8);
    private static readonly Color CriticalRed = Color.FromRgb(0xFF, 0x18, 0x32);
    private static readonly Color MedObsidian = Color.FromRgb(0x04, 0x0E, 0x12);
    private const float Speed = .34f;

    private readonly Cp77AudioSmoother _smooth = new();
    private readonly float[] _u = new float[MaxPulses], _amp = new float[MaxPulses];
    private readonly bool[] _live = new bool[MaxPulses];
    private int _next;
    private float _sinceBeat, _alert, _phase;

    public TraumaBiometricGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _smooth.Update(frame, attack: 12f, release: 3.5f, peakDecay: 0.50f);
        _sinceBeat += dt;

        bool transientBeat = (audio.Bass > 0.72f || audio.Mid > 0.78f) && _sinceBeat > 0.42f;
        bool beat = Beat.Hit || transientBeat || (_sinceBeat > 1.15f && audio.Level > .08f);
        if (beat)
        {
            float amp = Beat.Snare
                ? Math.Clamp(.36f + (.42f * Math.Max(_smooth.Mid, _smooth.Treble)), 0, 1)
                : Math.Clamp(.46f + (.50f * Math.Max(_smooth.Bass, Beat.Pulse)), 0, 1);
            _u[_next] = 0f;
            _amp[_next] = amp;
            _live[_next] = true;
            _next = (_next + 1) % MaxPulses;
            _sinceBeat = 0;
            if (!Beat.Snare) _alert = 1f;
            else _alert = Math.Max(_alert, 0.45f);
        }

        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.32f + (.55f * _smooth.Level) + (.25f * _smooth.Bass)))) % 10000;
            for (int i = 0; i < MaxPulses; i++)
            {
                if (!_live[i]) continue;
                _u[i] += Speed * dt;
                if (_u[i] > 1.15f) _live[i] = false;
            }
        }

        _alert *= MathF.Exp(-dt * 4.0f);
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        float step = frame.Dt * 60f;
        bool crown = frame.DockLeft >= frame.DockRight;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = crown ? 1f : Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = crown ? 0f : Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = _alert;
        d[7] = _phase;
        d[8] = _smooth.Level;
        d[9] = _smooth.Bass;
        d[10] = _smooth.Treble;
        d[11] = MathF.Pow(.62f, step);

        for (int i = 0; i < MaxPulses; i++)
        {
            d[12 + (i * 2)] = _live[i] ? _u[i] : -10f;
            d[13 + (i * 2)] = _live[i] ? _amp[i] : 0f;
        }

        _smooth.WriteBands(d, 24);
        _smooth.WritePeaks(d, 48);
        var lead = Lead(SurgicalTeal);
        Put(72, lead);
        Put(76, SurgicalWhite);
        Put(80, Second(CriticalRed));
        Put(84, Theme is null ? MedObsidian : Mix(Colors.Black, lead, .14f));
        d[88] = crown ? 1f : 0f;
        d[89] = _smooth.Mid;
        d[90] = d[91] = 0f;
    }

    private static readonly string Shared = """
        cbuffer TraumaBiometric : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Alert;
            float Phase;
            float Level;
            float Bass;
            float Treble;
            float Decay;
            float4 Pulses01;
            float4 Pulses23;
            float4 Pulses45;
            float4 Bands[6];
            float4 Peaks[6];
            float4 TealCol;
            float4 WhiteCol;
            float4 RedCol;
            float4 ObsidianCol;
            float4 Flags;
        };

        #define CrownMode Flags.x

        """ + ShaderHelpers + Cp77ShaderLib.CoreHelpers + Cp77ShaderLib.FontHlsl
        + Cp77ShaderLib.LabelFn("LeftTag", "TTI // ECG-II")
        + Cp77ShaderLib.LabelFn("RightTag", "VITALS // SPO2")
        + """

        float PeakAt(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = i + 1;
            return lerp(Peaks[i / 4][i % 4], Peaks[j / 4][j % 4], f - i);
        }

        float EcgWave(float dx)
        {
            float p = 0.14 * exp(-pow((dx + 0.032) / 0.010, 2.0));
            float q = -0.22 * exp(-pow((dx + 0.010) / 0.0042, 2.0));
            float r = 1.00 * exp(-pow(dx / 0.0055, 2.0));
            float s = -0.38 * exp(-pow((dx - 0.011) / 0.0048, 2.0));
            float t = 0.26 * exp(-pow((dx - 0.040) / 0.016, 2.0));
            return p + q + r + s + t;
        }

        void EvaluateTraces(float wingU, float spY, out float ecgY, out float plethY, out float spikeHot)
        {
            float band = Band(saturate(wingU * 0.92 + 0.04));

            float pu[6] = { Pulses01.x, Pulses01.z, Pulses23.x, Pulses23.z, Pulses45.x, Pulses45.z };
            float pa[6] = { Pulses01.y, Pulses01.w, Pulses23.y, Pulses23.w, Pulses45.y, Pulses45.w };

            float ecg = 0.0;
            spikeHot = 0.0;
            [unroll]
            for (int i = 0; i < 6; i++)
            {
                if (pu[i] > -1.0)
                {
                    float dx = wingU - pu[i];
                    float fadeU = exp(-max(0.0, pu[i]) * 0.40);
                    ecg += EcgWave(dx) * pa[i] * fadeU;
                    spikeHot = max(spikeHot, exp(-pow(dx / 0.025, 2.0)) * pa[i] * fadeU);
                }
            }

            float liveRipple = 0.038 * band * sin(wingU * 32.0 - Phase * 2.4)
                             + 0.014 * Treble * cos(wingU * 68.0 + Phase * 3.0);
            ecgY = (0.50 - ecg * 0.36 - liveRipple) * Size.y;
            plethY = (0.76 - 0.34 * band - 0.014 * sin(wingU * 14.0 - Phase * 1.4) * (0.3 + 0.7 * band)) * Size.y;
        }
        """;

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            // Pixel-exact phosphor memory ONLY for the moving waveforms (zero blurry offset!)
            float4 prev = Last.Load(int3((int2)sp, 0));
            float active = step(0.015, Level);
            float3 phosphorPrev = prev.rgb * (active > 0.5 ? Decay : 0.85);

            float ecgY, plethY, spikeHot;
            EvaluateTraces(wingU, sp.y, ecgY, plethY, spikeHot);

            float dEcgPx = abs(sp.y - ecgY);
            float dPlethPx = abs(sp.y - plethY);
            float ecgHalo = exp(-dEcgPx / (3.2 * Dip));
            float plethHalo = exp(-dPlethPx / (2.5 * Dip));

            float critical = saturate(Alert * 0.80 + smoothstep(0.62, 0.92, Bass) * 0.55);
            float3 glowCol = lerp(TealCol.rgb, RedCol.rgb, critical * 0.65) * ecgHalo * (0.48 + 0.65 * spikeHot);
            glowCol += TealCol.rgb * plethHalo * 0.28;

            float3 outCol = max(glowCol, phosphorPrev);
            return float4(outCol * wingEdge, 1.0);
        }
        """;

    private const string ShowPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            float xMin, xMax;
            WingBoundsPx(uv.x, xMin, xMax);
            float padX = floor(8.0 * Dip);
            float x0 = xMin + padX;
            float x1 = xMax - padX;
            float topY = floor(Size.y * 0.14);
            float botY = floor(Size.y - 5.0 * Dip);
            float topSoft = smoothstep(0.0, 0.14, uv.y);

            float band = Band(saturate(wingU * 0.92 + 0.04));
            float critical = saturate(Alert * 0.80 + smoothstep(0.62, 0.92, Bass) * 0.55);

            // Clean surgical dark glass background + 1px calibrated ECG millimeter grid
            float3 col = ObsidianCol.rgb * 0.92;
            float minorStep = 4.0 * Dip;
            float majorStep = 16.0 * Dip;
            float gxMin = StrokePx(abs(frac((sp.x - x0) / minorStep + 0.5) - 0.5) * minorStep, 1.0);
            float gyMin = StrokePx(abs(frac((sp.y - topY) / minorStep + 0.5) - 0.5) * minorStep, 1.0);
            float gxMaj = StrokePx(abs(frac((sp.x - x0) / majorStep + 0.5) - 0.5) * majorStep, 1.0);
            float gyMaj = StrokePx(abs(frac((sp.y - topY) / majorStep + 0.5) - 0.5) * majorStep, 1.0);

            float3 gridTint = lerp(TealCol.rgb, RedCol.rgb, critical * 0.35);
            col += gridTint * ((gxMin + gyMin) * 0.032 + (gxMaj + gyMaj) * 0.095 + (gxMaj * gyMaj) * 0.22);

            // Subtle phosphor trace under-glow from feedback pass
            float3 phosphor = Last.Load(int3((int2)sp, 0)).rgb;
            col += phosphor * 0.72;

            // Razor-crisp vector ECG + SpO2 waveforms evaluated at exact pixel height
            float ecgY, plethY, spikeHot;
            EvaluateTraces(wingU, sp.y, ecgY, plethY, spikeHot);
            float plethLine = StrokePx(sp.y - plethY, 1.15 * Dip);
            float plethFill = smoothstep(plethY + 14.0 * Dip, plethY, sp.y) * step(plethY, sp.y) * step(sp.y, botY - 8.0 * Dip);
            col += TealCol.rgb * (plethFill * 0.14 + plethLine * 0.78);
            col += WhiteCol.rgb * plethLine * 0.25;

            float ecgLine = StrokePx(sp.y - ecgY, 1.55 * Dip);
            float3 ecgCol = lerp(TealCol.rgb, RedCol.rgb, critical * 0.65);
            col += ecgCol * ecgLine * 0.85;
            col += WhiteCol.rgb * ecgLine * (0.72 + 0.65 * spikeHot);

            // Precision 24-channel micro-O2 bar & 1px peak-hold dashes along bottom bezel
            float normX = saturate((sp.x - x0) / max(x1 - x0, 1.0));
            float bx = normX * 24.0;
            float bId = floor(bx);
            float bF = frac(bx);
            float colGap = step(0.14, bF) * step(bF, 0.86);
            float bVal = Band(saturate((bId + 0.5) / 24.0 * 0.94 + 0.03));
            float pVal = PeakAt(saturate((bId + 0.5) / 24.0 * 0.94 + 0.03));
            float o2Base = botY - 2.0 * Dip;
            float o2MaxH = 8.0 * Dip;
            float o2Top = o2Base - o2MaxH * lerp(0.08, 1.0, bVal);
            float pTop = o2Base - o2MaxH * lerp(0.08, 1.0, pVal) - 1.5 * Dip;
            float inBar = step(o2Top, sp.y) * step(sp.y, o2Base) * colGap;
            float inPeak = StrokePx(sp.y - pTop, 1.0) * colGap;
            col += lerp(TealCol.rgb, RedCol.rgb, smoothstep(0.60, 0.88, bVal)) * inBar * (0.48 + 0.52 * bVal);
            col += WhiteCol.rgb * inPeak * 0.82;

            // Crisp 1px architectural HUD border & critical status accent bar (replaces chunky stripe)
            float brackets = CornerBracketsPx(sp, float2(x0, topY), float2(x1, botY), floor(8.0 * Dip), 1.0);
            float statusBar = step(x1 - 30.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip)
                            * step(topY + 3.0 * Dip, sp.y) * step(sp.y, topY + 5.5 * Dip);
            float fs = HudFs(Dip);
            float tagInk = side < 0.0
                ? LeftTag(sp, float2(x0 + 4.0 * Dip, topY + 2.5 * Dip), fs)
                : RightTag(sp, float2(x0 + 4.0 * Dip, topY + 2.5 * Dip), fs);

            col = lerp(col, TealCol.rgb, brackets * 0.65);
            col = lerp(col, lerp(TealCol.rgb * 0.45, RedCol.rgb, critical), statusBar * (0.65 + 0.35 * critical));
            col = lerp(col, WhiteCol.rgb, tagInk * 0.88);

            float3 graded = AcesToneClean(col * 1.14);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(0.76, max(ecgLine, max(plethLine, tagInk))) * wingEdge * topSoft) * Fade;
            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
