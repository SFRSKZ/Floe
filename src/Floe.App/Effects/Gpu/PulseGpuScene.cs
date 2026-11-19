using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Trauma Team biometric telemetry monitor: a backlit medical-HUD glass strip with calibrated ECG grid, a primary
/// glowing magenta-crimson cardiac trace carrying travelling P-QRS-T heartbeat complexes launched on every beat,
/// a secondary cyan neural-sync harmonic wave, and a crisp lower vitals bar. Phosphor persistence trails glow in
/// linear HDR and freeze cleanly in silence.
/// </summary>
public sealed class PulseGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 76;
    private const int MaxPulses = 6;
    private static readonly Color Core = Color.FromRgb(0xFF, 0x8A, 0xB8);
    private static readonly Color Body = Color.FromRgb(0xF0, 0x16, 0x64);
    private static readonly Color Neural = Color.FromRgb(0x18, 0xDF, 0xF0);
    private static readonly Color Deep = Color.FromRgb(0x1C, 0x05, 0x10);
    private const float Speed = .62f;

    private readonly float[] _u = new float[MaxPulses], _amp = new float[MaxPulses];
    private readonly bool[] _live = new bool[MaxPulses];
    private int _next;
    private float _sinceBeat, _flash, _phase;
    private float _dockLeft = .27f, _dockRight = .73f;

    public PulseGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _dockLeft = Math.Clamp(frame.DockLeft, .05f, .9f);
        _dockRight = Math.Clamp(frame.DockRight, _dockLeft + .02f, .95f);
        _sinceBeat += dt;

        bool beat = Beat.Hit || (_sinceBeat > 1.15f && audio.Level > .10f);
        if (beat)
        {
            float amp = Math.Clamp(.42f + (.58f * Math.Max(audio.Bass, Beat.Pulse)), 0, 1);
            _u[_next] = 0f;
            _amp[_next] = amp;
            _live[_next] = true;
            _next = (_next + 1) % MaxPulses;
            _sinceBeat = 0;
            _flash = 1f;
        }

        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.85f + (2.1f * audio.Level)))) % 10000;
            for (int i = 0; i < MaxPulses; i++)
            {
                if (!_live[i]) continue;
                _u[i] += Speed * dt;
                if (_u[i] > 1.15f) _live[i] = false;
            }
        }

        _flash *= MathF.Exp(-dt * 5.5f);
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        float step = frame.Dt * 60f;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _dockLeft;
        d[5] = _dockRight;
        d[6] = _flash;
        d[7] = _phase;
        d[8] = audio.Level;
        d[9] = audio.Bass;
        d[10] = audio.Treble;
        d[11] = MathF.Pow(.84f, step);

        for (int i = 0; i < MaxPulses; i++)
        {
            d[12 + (i * 2)] = _live[i] ? _u[i] : -10f;
            d[13 + (i * 2)] = _live[i] ? _amp[i] : 0f;
        }

        PutBands(24, frame);
        var body = Lead(Body);
        Put(48, Theme is null ? Core : Mix(body, Colors.White, .55f));
        Put(52, body);
        Put(56, Second(Neural));
        Put(60, Theme is null ? Deep : Mix(Colors.Black, body, .22f));
        Put(64, Color.FromRgb(0xFF, 0xF6, 0xFA));
    }

    private const string Shared = """
        cbuffer Pulse : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Flash;
            float Phase;
            float Level;
            float Bass;
            float Treble;
            float Decay;
            float4 Pulses01; // (u0, amp0, u1, amp1)
            float4 Pulses23;
            float4 Pulses45;
            float4 Bands[6];
            float4 CoreCol;
            float4 BodyCol;
            float4 NeuralCol;
            float4 DeepCol;
            float4 HotCol;
        };

        """ + ShaderHelpers + """

        float3 AcesTone(float3 x)
        {
            return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
        }

        float WingInfo(float u, out float wingU, out float wingEdge, out float side)
        {
            float leftW = max(DockLeft, 0.001);
            float rightW = max(1.0 - DockRight, 0.001);
            if (u < DockLeft)
            {
                side = -1.0;
                wingU = saturate((DockLeft - u) / leftW);
                wingEdge = smoothstep(0.0, 0.032, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            if (u > DockRight)
            {
                side = 1.0;
                wingU = saturate((u - DockRight) / rightW);
                wingEdge = smoothstep(0.0, 0.032, wingU) * smoothstep(1.0, 0.94, wingU);
                return 1.0;
            }
            side = 0.0;
            wingU = 0.0;
            wingEdge = 0.0;
            return 0.0;
        }

        // Authentic P-QRS-T cardiac waveform centered at dx = 0
        float EcgComplex(float dx)
        {
            float p = 0.12 * exp(-pow((dx + 0.032) / 0.011, 2.0));
            float q = -0.18 * exp(-pow((dx + 0.010) / 0.0045, 2.0));
            float r = 1.00 * exp(-pow(dx / 0.0055, 2.0));
            float s = -0.34 * exp(-pow((dx - 0.011) / 0.005, 2.0));
            float t = 0.24 * exp(-pow((dx - 0.040) / 0.016, 2.0));
            return p + q + r + s + t;
        }
        """;

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5)
            {
                return float4(0, 0, 0, 0);
            }

            float active = step(0.015, Level);
            float4 prev = Last.Sample(Linear, uv);
            float3 trail = prev.rgb * (active > 0.5 ? Decay : 0.94);

            float y = uv.y;
            float band = Band(saturate(wingU * 0.92 + 0.04));
            float topSoft = smoothstep(0.0, 0.16, y);

            // Backlit medical-HUD glass substrate + calibrated ECG millimeter grid
            float centerGlow = exp(-pow((y - 0.52) * 2.1, 2.0));
            float3 col = DeepCol.rgb * (0.55 + 0.65 * centerGlow);

            float gx = exp(-abs(frac(wingU * 26.0) - 0.5) * 42.0);
            float gy = exp(-abs(frac(y * 5.0) - 0.5) * 34.0);
            float majorGx = exp(-abs(frac(wingU * 6.5) - 0.5) * 52.0);
            col += BodyCol.rgb * ((gx + gy) * 0.08 + majorGx * 0.14) * (0.65 + 0.55 * centerGlow + 0.45 * Flash);

            // Evaluate travelling P-QRS-T heartbeat complexes along the entire wing
            float pu[6] = { Pulses01.x, Pulses01.z, Pulses23.x, Pulses23.z, Pulses45.x, Pulses45.z };
            float pa[6] = { Pulses01.y, Pulses01.w, Pulses23.y, Pulses23.w, Pulses45.y, Pulses45.w };

            float ecg = 0.0;
            float penHot = 0.0;
            [unroll]
            for (int i = 0; i < 6; i++)
            {
                if (pu[i] > -1.0)
                {
                    float dx = wingU - pu[i];
                    float decayU = exp(-max(0.0, pu[i]) * 0.45);
                    ecg += EcgComplex(dx) * pa[i] * decayU;
                    penHot = max(penHot, exp(-pow(dx / 0.022, 2.0)) * pa[i] * decayU);
                }
            }

            // Subtle spectrum-modulated live baseline ripple so the whole monitor breathes
            float baseRipple = (0.045 * sin(wingU * 24.0 - Phase * 3.4) * band)
                + (0.025 * sin(wingU * 52.0 + Phase * 2.1) * Treble);
            float traceY = 0.54 - ecg * 0.34 - baseRipple;

            // Secondary cyan neural-sync harmonic waveform
            float neuralY = 0.56
                + 0.13 * sin(wingU * 15.0 - Phase * 2.6 + side * 0.8) * (0.25 + 0.75 * band)
                + 0.05 * cos(wingU * 34.0 + Phase * 3.8) * (0.2 + 0.8 * Treble);

            float dNeural = abs(y - neuralY);
            float neuralCore = exp(-dNeural * 68.0);
            float neuralHalo = exp(-dNeural * 14.0);
            col += NeuralCol.rgb * (neuralHalo * 0.24 + neuralCore * 0.88) * (0.45 + 0.75 * band);

            // Primary crimson/magenta cardiac phosphor trace
            float dTrace = abs(y - traceY);
            float traceCore = exp(-dTrace * 62.0);
            float traceHalo = exp(-dTrace * 12.0);
            float traceWide = exp(-dTrace * 4.5);

            // Illuminated area wash under the cardiac curve
            float underWash = smoothstep(traceY + 0.14, traceY, y) * smoothstep(0.95, 0.65, y) * (0.12 + 0.35 * abs(ecg) + 0.18 * band);
            col += BodyCol.rgb * underWash;

            col += BodyCol.rgb * (traceWide * 0.28 + traceHalo * 0.72 + traceCore * 1.45) * (0.65 + 0.65 * Level + 0.75 * Flash);
            col += CoreCol.rgb * traceCore * (0.75 + 1.15 * penHot + 0.65 * Flash);
            col += HotCol.rgb * (traceCore * (0.35 + 1.45 * penHot) + exp(-dTrace * 20.0) * penHot * 0.95);

            // Lower vitals telemetry bar (segmented spectrum meter along bottom bezel)
            float barZone = smoothstep(0.84, 0.89, y) * smoothstep(0.98, 0.93, y);
            float seg = smoothstep(0.22, 0.32, abs(frac(wingU * 42.0) - 0.5));
            float barFill = step(0.10, band) * (0.35 + 0.85 * band);
            col += lerp(BodyCol.rgb, NeuralCol.rgb, 0.35) * barZone * (1.0 - seg) * barFill * 0.85;

            col *= wingEdge * topSoft;
            float3 outCol = active > 0.5 ? max(col, trail * 0.84) : col;
            float alpha = saturate(max(centerGlow * 0.74, max(traceHalo, max(neuralCore, barZone * 0.6))) * wingEdge * topSoft);
            return float4(outCol, active > 0.5 ? max(alpha, prev.a * 0.78) : alpha);
        }
        """;

    private const string ShowPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5)
            {
                return float4(0, 0, 0, 0);
            }

            float ca = (0.35 + 2.4 * Flash) * Dip / Size.x;
            float4 c0 = Last.Sample(Linear, uv);
            float r = Last.Sample(Linear, saturate(uv + float2(ca, 0))).r;
            float b = Last.Sample(Linear, saturate(uv - float2(ca, 0))).b;
            float3 hdr = float3(r, c0.g, b);

            float2 px = float2(Dip / Size.x, Dip / Size.y);
            float3 bloom = 0;
            bloom += Last.Sample(Linear, saturate(uv + float2(px.x * 4.0, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(px.x * 4.0, 0))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv + float2(0, px.y * 2.5))).rgb * 0.25;
            bloom += Last.Sample(Linear, saturate(uv - float2(0, px.y * 2.5))).rgb * 0.25;
            hdr += max(bloom - 0.46, 0.0) * 0.54;

            float grain = (Hash(floor(position.xy) + floor(Phase * 20.0)) - 0.5) * 0.014;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.44) * 1.46) + grain;

            float topSoft = smoothstep(0.0, 0.20, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.45, smoothstep(0.02, 0.30, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
