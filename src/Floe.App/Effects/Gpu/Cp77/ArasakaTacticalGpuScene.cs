using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Arasaka OS (Corporate Neo-Militarist Brutalism): cold matte-obsidian chassis with 1px chamfered borders,
/// crisp 48-channel high-density micro-segmented hardware needles driven by damped spectrum and peak-hold values,
/// calm optical crimson target sweeps, and readable left-to-right corporate telemetry typography.
/// </summary>
public sealed class ArasakaTacticalGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 88;
    private static readonly Color Crimson = Color.FromRgb(0xE8, 0x0C, 0x26);
    private static readonly Color Stark = Color.FromRgb(0xEB, 0xEE, 0xF5);
    private static readonly Color Steel = Color.FromRgb(0x34, 0x3C, 0x48);
    private static readonly Color Chassis = Color.FromRgb(0x09, 0x0B, 0x0F);

    private readonly Cp77AudioSmoother _smooth = new();
    private float _phase, _kickAge = 9, _snareAge = 9, _lock, _seed = 1;

    public ArasakaTacticalGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _smooth.Update(frame, attack: 14f, release: 3.8f, peakDecay: 0.50f);
        _kickAge += dt;
        _snareAge += dt;

        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _snareAge = 0;
            else
            {
                _kickAge = 0;
                _lock = 1f;
            }
        }

        _lock *= MathF.Exp(-dt * 4.8f);
        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.18f + (.48f * _smooth.Level) + (.25f * _smooth.Bass)))) % 10000;
        }
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
        d[6] = _smooth.Level;
        d[7] = _smooth.Bass;
        d[8] = _smooth.Treble;
        d[9] = _phase;
        d[10] = _kickAge;
        d[11] = _snareAge;
        d[12] = _lock;
        d[13] = _seed;
        d[14] = MathF.Pow(.72f, step);
        d[15] = crown ? 1f : 0f;
        d[16] = d[17] = d[18] = d[19] = 0f;
        _smooth.WriteBands(d, 20);
        _smooth.WritePeaks(d, 44);

        var lead = Lead(Crimson);
        Put(68, lead);
        Put(72, Theme is null ? Stark : Mix(lead, Colors.White, .82f));
        Put(76, Steel);
        Put(80, Chassis);
        Put(84, Second(Color.FromRgb(0xFF, 0xB8, 0x20)));
    }

    private static readonly string Shared = """
        cbuffer ArasakaTactical : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Bass;
            float Treble;
            float Phase;
            float KickAge;
            float SnareAge;
            float Lock;
            float Seed;
            float Decay;
            float CrownMode;
            float4 Spare;
            float4 Bands[6];
            float4 Peaks[6];
            float4 CrimsonCol;
            float4 StarkCol;
            float4 SteelCol;
            float4 ChassisCol;
            float4 WarnCol;
        };

        """ + ShaderHelpers + Cp77ShaderLib.CoreHelpers + Cp77ShaderLib.FontHlsl
        + Cp77ShaderLib.LabelFn("LeftTag", "ARASAKA // OS-4.7")
        + Cp77ShaderLib.LabelFn("RightTag", "BUS // TELEMETRY")
        + """

        float PeakAt(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = i + 1;
            return lerp(Peaks[i / 4][i % 4], Peaks[j / 4][j % 4], f - i);
        }
        """;

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            // Minimal crisp laser sweep memory only
            float4 prev = Last.Load(int3((int2)sp, 0));
            float active = step(0.015, Level);
            float3 wake = prev.rgb * (active > 0.5 ? Decay * 0.65 : 0.80);

            float sweepU = KickAge * 1.15;
            float sweepEnv = exp(-KickAge * 4.2);
            float xMin, xMax;
            WingBoundsPx(uv.x, xMin, xMax);
            float sweepX = side < 0.0 ? lerp(xMax, xMin, sweepU) : lerp(xMin, xMax, sweepU);
            float beam = StrokePx(sp.x - sweepX, 1.5 * Dip) * sweepEnv;

            float3 cur = CrimsonCol.rgb * beam * 0.95;
            return float4(max(cur, wake) * wingEdge, 1.0);
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
            float spanW = max(x1 - x0, 24.0);
            float topSoft = smoothstep(0.0, 0.14, uv.y);

            // Cold corporate matte-obsidian chassis + 1px micro-grid texture
            float3 col = ChassisCol.rgb * (0.88 + 0.16 * uv.y);
            float gStep = 6.0 * Dip;
            float grid = max(
                StrokePx(abs(frac((sp.x - x0) / gStep + 0.5) - 0.5) * gStep, 1.0),
                StrokePx(abs(frac((sp.y - topY) / gStep + 0.5) - 0.5) * gStep, 1.0));
            col += SteelCol.rgb * grid * 0.12;

            // Chamfered 1px outer architectural chassis hairline
            float2 fCen = float2((x0 + x1) * 0.5, (topY + botY) * 0.5);
            float2 fHalf = float2(spanW * 0.5, (botY - topY) * 0.5);
            float frameLine = StrokePx(ChamferBoxSdf(sp - fCen, fHalf, floor(6.0 * Dip)), 1.0);
            col += SteelCol.rgb * frameLine * 0.72;

            // Upper micro-register bus strip (header separator & crisp bit-status ticks)
            float fs = HudFs(Dip);
            float headerBot = topY + 7.0 * fs + 5.0 * Dip;
            float headerDiv = StrokePx(sp.y - headerBot, 1.0) * step(x0 + 4.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip);
            col += CrimsonCol.rgb * headerDiv * 0.65;

            // Crisp micro-typography (left-to-right on both wings)
            float tagInk = side < 0.0
                ? LeftTag(sp, float2(x0 + 5.0 * Dip, topY + 2.5 * Dip), fs)
                : RightTag(sp, float2(x0 + 5.0 * Dip, topY + 2.5 * Dip), fs);
            col = lerp(col, StarkCol.rgb, tagInk * 0.88);

            // Compact top-right memory address indicator ticks
            float regCell = floor((sp.x - (x1 - 48.0 * Dip)) / (3.0 * Dip));
            float inReg = step(0.0, regCell) * step(regCell, 13.0)
                        * step(topY + 2.5 * Dip, sp.y) * step(sp.y, topY + 5.0 * Dip)
                        * step(0.32, frac((sp.x - (x1 - 48.0 * Dip)) / (3.0 * Dip)));
            float regOn = step(0.45, Hash(float2(regCell, floor(Phase * 1.4) + side * 3.0)));
            col += lerp(SteelCol.rgb * 0.55, CrimsonCol.rgb, regOn * saturate(0.4 + Level)) * inReg;

            // High-density 48-channel precision hardware spectrum needles (replaces chunky Winamp blocks)
            float meterTop = headerBot + 4.0 * Dip;
            float meterBot = botY - 5.0 * Dip;
            float meterH = max(meterBot - meterTop, 8.0);
            float cols = 48.0;
            float nx = saturate((sp.x - (x0 + 4.0 * Dip)) / max(spanW - 8.0 * Dip, 1.0));
            float colW = (spanW - 8.0 * Dip) / cols;
            float cx = nx * cols;
            float colId = floor(cx);
            float colF = frac(cx);
            float sampleU = saturate((colId + 0.5) / cols * 0.94 + 0.03);
            // Smoothly sample wings from dock edge outward
            float bandU = side < 0.0 ? (1.0 - sampleU) : sampleU;
            float bVal = saturate(pow(Band(bandU) * 2.35, 0.85));
            float pVal = saturate(pow(PeakAt(bandU) * 2.35, 0.85));

            float needleHalfPx = max(0.85, colW * 0.28);
            float needleDistPx = abs(colF - 0.5) * colW;
            float inNeedleX = FillPx(needleDistPx - needleHalfPx);
            float inMeterArea = step(x0 + 4.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip)
                              * step(meterTop, sp.y) * step(sp.y, meterBot);

            float fillY = meterBot - meterH * lerp(0.06, 0.96, bVal);
            float peakY = meterBot - meterH * lerp(0.06, 0.96, pVal) - 1.5 * Dip;

            // Fine horizontal hardware rung subdivisions (every 3dip, 1px gap)
            float rungY = abs(frac((meterBot - sp.y) / (3.0 * Dip) + 0.5) - 0.5) * (3.0 * Dip);
            float rungGap = step(0.42 * Dip, rungY);
            float relY = saturate((meterBot - sp.y) / meterH);

            float lit = step(fillY, sp.y) * inNeedleX * inMeterArea * rungGap;
            float unlitSlot = (1.0 - step(fillY, sp.y)) * inNeedleX * inMeterArea * rungGap;
            float peakTick = StrokePx(sp.y - peakY, 1.2 * Dip) * inNeedleX * step(x0 + 4.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip);

            float3 barCol = lerp(StarkCol.rgb * 0.88, CrimsonCol.rgb * 1.45, smoothstep(0.58, 0.72, relY));
            col += SteelCol.rgb * 0.22 * unlitSlot;
            col += barCol * lit * (0.72 + 0.45 * bVal);
            col += CrimsonCol.rgb * 1.35 * peakTick;

            // Bottom calibrated millimeter axis
            float minorT, majorT;
            TickRulerPx(sp.x - x0, 5.0 * Dip, 5.0, minorT, majorT);
            float botAxis = StrokePx(sp.y - (botY - 2.0 * Dip), 1.0) * step(x0 + 4.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip);
            float botTicks = (minorT * step(botY - 4.0 * Dip, sp.y) + majorT * step(botY - 5.5 * Dip, sp.y)) * step(sp.y, botY - 2.0 * Dip);
            col += CrimsonCol.rgb * botAxis * 0.72 + StarkCol.rgb * botTicks * 0.50;

            // Crisp optical kick laser hairline from feedback pass (no white blotch)
            float3 wake = Last.Load(int3((int2)sp, 0)).rgb;
            col += wake;

            float3 graded = AcesToneClean(col * 1.15);
            float alpha = saturate(max(0.76, max(lit, max(tagInk, peakTick))) * wingEdge * topSoft) * Fade;
            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
