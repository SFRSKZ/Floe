using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Braindance Edit Suite (Judy's BD Forensic Analyzer): surgical 3-lane DAW/forensic editor rendered at exact
/// physical pixel coordinates. Features labeled track gutters (THM / AUD / VIS), 1px thermal contour density curves,
/// crisp high-density sample audio waveforms with cue brackets, millimeter frame rulers with diamond anomaly pins,
/// and a razor-sharp triangular-capped amber playhead needle.
/// </summary>
public sealed class BraindanceTimelineGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color BdCyan = Color.FromRgb(0x16, 0xDF, 0xEA);
    private static readonly Color BdAmber = Color.FromRgb(0xFC, 0xC4, 0x1A);
    private static readonly Color BdThermal = Color.FromRgb(0xFF, 0x2A, 0x48);
    private static readonly Color BdSlate = Color.FromRgb(0x07, 0x11, 0x18);

    private readonly Cp77AudioSmoother _smooth = new();
    private float _phase, _playhead, _kickAge = 9, _snareAge = 9, _scrub, _seed = 1;

    public BraindanceTimelineGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _smooth.Update(frame, attack: 12f, release: 3.5f, peakDecay: 0.48f);
        _kickAge += dt;
        _snareAge += dt;

        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _snareAge = 0;
            else
            {
                _kickAge = 0;
                _scrub = 1f;
            }
        }

        _scrub *= MathF.Exp(-dt * 4.5f);
        if (audio.Level > .015f)
        {
            _phase = (_phase + (dt * (.18f + (.45f * _smooth.Level)))) % 10000;
            _playhead = (_playhead + (dt * (.045f + (.045f * _smooth.Level)))) % 1f;
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
        d[10] = _playhead;
        d[11] = _scrub;
        d[12] = _snareAge;
        d[13] = _seed;
        d[14] = MathF.Pow(.74f, step);
        d[15] = crown ? 1f : 0f;
        d[16] = d[17] = d[18] = d[19] = 0f;
        _smooth.WriteBands(d, 20);

        var lead = Lead(BdCyan);
        Put(44, lead);
        Put(48, Second(BdAmber));
        Put(52, BdThermal);
        Put(56, Theme is null ? BdSlate : Mix(Colors.Black, lead, .14f));
        Put(60, Color.FromRgb(0xF6, 0xFF, 0xFF));
    }

    private static readonly string Shared = """
        cbuffer Braindance : register(b0)
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
            float Playhead;
            float Scrub;
            float SnareAge;
            float Seed;
            float Decay;
            float CrownMode;
            float4 Spare;
            float4 Bands[6];
            float4 CyanCol;
            float4 AmberCol;
            float4 ThermalCol;
            float4 SlateCol;
            float4 HotCol;
        };

        """ + ShaderHelpers + Cp77ShaderLib.CoreHelpers + Cp77ShaderLib.FontHlsl
        + Cp77ShaderLib.LabelFn("TagThm", "THM")
        + Cp77ShaderLib.LabelFn("TagAud", "AUD")
        + Cp77ShaderLib.LabelFn("TagVis", "VIS")
        + Cp77ShaderLib.LabelFn("TagSuite", "BD // 4K");

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            return float4(0, 0, 0, 0);
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

            float fs = HudFs(Dip);
            float gutterW = 24.0 * fs + 8.0 * Dip;
            float trackX0 = min(x1 - 24.0 * Dip, x0 + gutterW);
            float trackX1 = x1 - 4.0 * Dip;
            float trackW = max(trackX1 - trackX0, 16.0);

            float hTotal = max(botY - topY, 24.0);
            float yDiv1 = floor(topY + hTotal * 0.34);
            float yDiv2 = floor(topY + hTotal * 0.69);

            // Deep forensic slate monitor chassis
            float3 col = SlateCol.rgb * 0.94;

            // Subtle vertical timecode ruler gridlines across all 3 tracks
            float vGrid = StrokePx(abs(frac((sp.x - trackX0) / (20.0 * Dip) + 0.5) - 0.5) * (20.0 * Dip), 1.0)
                        * step(trackX0, sp.x) * step(sp.x, trackX1);
            col += CyanCol.rgb * vGrid * 0.06;

            // Track gutter separator & crisp horizontal lane dividers
            float gutLine = StrokePx(sp.x - trackX0, 1.0) * step(topY, sp.y) * step(sp.y, botY);
            float divLine = (StrokePx(sp.y - yDiv1, 1.0) + StrokePx(sp.y - yDiv2, 1.0)) * step(x0, sp.x) * step(sp.x, trackX1);
            col += CyanCol.rgb * saturate(gutLine * 0.45 + divLine * 0.38);

            // Left-aligned 5x7 track header tags (THM, AUD, VIS)
            float thmInk = TagThm(sp, float2(x0 + 4.0 * Dip, floor((topY + yDiv1) * 0.5 - 3.5 * fs)), fs);
            float audInk = TagAud(sp, float2(x0 + 4.0 * Dip, floor((yDiv1 + yDiv2) * 0.5 - 3.5 * fs)), fs);
            float visInk = TagVis(sp, float2(x0 + 4.0 * Dip, floor((yDiv2 + botY) * 0.5 - 3.5 * fs)), fs);
            col = lerp(col, ThermalCol.rgb, thmInk * 0.85);
            col = lerp(col, CyanCol.rgb, audInk * 0.90);
            col = lerp(col, AmberCol.rgb, visInk * 0.85);

            float inTrackX = step(trackX0 + 2.0 * Dip, sp.x) * step(sp.x, trackX1);
            float tx = saturate((sp.x - trackX0) / trackW);
            float bandU = side < 0.0 ? (1.0 - tx) : tx;
            float band = saturate(Band(saturate(bandU * 0.92 + 0.04)) * 2.25);

            // --- LANE 1: THERMAL LAYER (smooth thermal density curve + 1px peak contour trace) ---
            if (sp.y >= topY && sp.y < yDiv1 && inTrackX > 0.5)
            {
                float laneH = max(yDiv1 - topY, 6.0);
                // a kick heaves the heat curve where its band is loud, at once, like the Blackwall's flash
                float heatVal = saturate(band * 0.72 + Noise(float2(tx * 10.0 - Phase * 0.4, 1.5)) * 0.28 + Scrub * band * 0.35);
                float curveY = yDiv1 - 2.0 * Dip - heatVal * (laneH - 5.0 * Dip);
                float underCurve = smoothstep(curveY + 8.0 * Dip, curveY, sp.y) * step(curveY, sp.y);
                float lineTrace = StrokePx(sp.y - curveY, 1.15 * Dip);

                float3 thmFill = lerp(CyanCol.rgb * 0.45, lerp(AmberCol.rgb, ThermalCol.rgb, smoothstep(0.55, 0.9, heatVal)), heatVal);
                col += thmFill * underCurve * 0.35;
                col += lerp(AmberCol.rgb, ThermalCol.rgb, smoothstep(0.5, 0.85, heatVal)) * lineTrace * 0.95;

                // Top-right concise BD suite identifier inside Track 1 header (padded clear of corner bracket)
                float suiteInk = TagSuite(sp, float2(trackX1 - 44.0 * fs - 6.0 * Dip, topY + 2.5 * Dip), fs);
                col = lerp(col, HotCol.rgb, suiteInk * 0.72);
            }

            // --- LANE 2: AUDIO LAYER (crisp high-density DAW sample needles + 1px cue brackets) ---
            if (sp.y >= yDiv1 && sp.y < yDiv2 && inTrackX > 0.5)
            {
                float midY = floor((yDiv1 + yDiv2) * 0.5);
                float halfH = max((yDiv2 - yDiv1) * 0.46, 3.0);
                float axis = StrokePx(sp.y - midY, 1.0);
                col += CyanCol.rgb * axis * 0.30;

                float sampleStep = 2.0 * Dip;
                float sId = floor(sp.x / sampleStep);
                float sampleDist = abs(frac(sp.x / sampleStep + 0.5) - 0.5) * sampleStep;
                float isBar = StrokePx(sampleDist, 1.0);
                float mod = 0.42 + 0.58 * sin(sId * 0.72 - Phase * 3.5);
                float amp = saturate(0.08 + 0.80 * band * mod + 0.45 * Scrub * band) * halfH;
                float inNeedle = step(abs(sp.y - midY), amp) * isBar;
                col += CyanCol.rgb * inNeedle * (0.45 + 0.55 * band + 1.1 * Scrub * band);
                col += HotCol.rgb * inNeedle * smoothstep(0.65, 0.95, band) * 0.55;

                // Subtle 1px forensic cue clip region bracket
                float cueU = frac(tx * 3.0 - Phase * 0.03);
                float inCue = step(0.18, cueU) * step(cueU, 0.42);
                float cueTop = StrokePx(abs(sp.y - midY) - (halfH - 1.5 * Dip), 1.0) * inCue;
                col += AmberCol.rgb * (inCue * 0.06 + cueTop * 0.78);
            }

            // --- LANE 3: VISUAL TIMECODE RULER (millimeter ticks & 3px diamond anomaly pins) ---
            if (sp.y >= yDiv2 && sp.y <= botY && inTrackX > 0.5)
            {
                float minorT, majorT;
                TickRulerPx((sp.x - trackX0) - Phase * 6.0 * Dip, 4.0 * Dip, 5.0, minorT, majorT);
                float rulerTicks = minorT * step(yDiv2 + 5.0 * Dip, sp.y) + majorT * step(yDiv2 + 2.0 * Dip, sp.y);
                col += CyanCol.rgb * rulerTicks * step(sp.y, botY - 1.0 * Dip) * 0.45;

                // Small 2.5dip rotated diamond markers aligned along centerline
                float mSpacing = 28.0 * Dip;
                float mCoord = (sp.x - trackX0) / mSpacing;
                float mId = floor(mCoord);
                float mLit = step(0.68, Hash(float2(mId, side * 11.0 + 3.0)));
                float2 mD = float2((frac(mCoord) - 0.5) * mSpacing, sp.y - floor((yDiv2 + botY) * 0.5));
                float dDiamond = (abs(mD.x) + abs(mD.y)) - 2.6 * Dip;
                float diamond = FillPx(dDiamond) * mLit;
                // the anomaly pins blink on the snare
                col += lerp(AmberCol.rgb, ThermalCol.rgb, step(0.85, Hash(float2(mId, 7.0)))) * diamond * (0.55 + 1.2 * exp(-SnareAge * 6.0));
            }

            // Surgical 1.5px amber-white playhead needle with small crisp cap triangle
            float playX = floor(lerp(trackX0, trackX1, Playhead));
            float needle = StrokePx(sp.x - playX, 1.25 * Dip) * step(topY, sp.y) * step(sp.y, botY);
            float capY = saturate((sp.y - topY) / (4.5 * Dip));
            float capTri = step(topY, sp.y) * step(sp.y, topY + 4.5 * Dip)
                         * step(abs(sp.x - playX), (1.0 - capY) * 3.5 * Dip);
            col = lerp(col, AmberCol.rgb, saturate(needle * 0.88 + capTri * 0.95));
            col += HotCol.rgb * needle * (0.30 + 0.9 * Scrub);

            // Crisp 1px outer frame corner brackets
            float brackets = CornerBracketsPx(sp, float2(x0, topY), float2(x1, botY), floor(8.0 * Dip), 1.0);
            col = lerp(col, CyanCol.rgb, brackets * 0.55);

            float3 graded = AcesToneClean(col * 1.14);
            float alpha = saturate(max(0.76, max(needle, max(brackets, thmInk + audInk + visInk))) * wingEdge * topSoft) * Fade;
            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
