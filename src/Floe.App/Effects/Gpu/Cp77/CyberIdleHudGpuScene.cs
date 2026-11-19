using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Minimal diegetic Cyberpunk 2077 Kiroshi / Arasaka standby HUD displayed softly over the free strip glass when
/// no music is playing. Everything renders analytically in physical screen coordinates for 1px crispness without
/// feedback trail blur.
/// </summary>
public sealed class CyberIdleHudGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 24;
    private static readonly Color Cyan = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Crimson = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);

    private float _clock;

    public CyberIdleHudGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        _clock = (_clock + frame.Dt) % 10000f;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        bool crown = frame.DockLeft >= frame.DockRight;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = crown ? 1f : Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = crown ? 0f : Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = _clock;
        d[7] = crown ? 1f : 0f;

        var lead = Lead(Cyan);
        Put(8, lead);
        Put(12, Second(Crimson));
        Put(16, Theme is null ? Yellow : Mix(lead, Colors.White, .65f));
        d[20] = d[21] = d[22] = d[23] = 0f;
    }

    private static readonly string Shared = """
        cbuffer CyberIdleHud : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Clock;
            float CrownMode;
            float4 PrimaryCol;
            float4 AccentCol;
            float4 HotCol;
            float4 Params;
        };

        """ + ShaderHelpers + Cp77ShaderLib.CoreHelpers + Cp77ShaderLib.FontHlsl
        + Cp77ShaderLib.LabelFn("LeftTag", "KIROSHI // STBY")
        + Cp77ShaderLib.LabelFn("RightTag", "NC-77 // LINK");

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
            float padX = floor(10.0 * Dip);
            float x0 = xMin + padX;
            float x1 = xMax - padX;
            if (x1 <= x0 + 20.0 * Dip) return float4(0, 0, 0, 0);

            float topY = floor(Size.y * 0.18);
            float botY = floor(Size.y - 7.0 * Dip);
            float railY = floor(Size.y - 14.0 * Dip);

            // Calm 5-second respiratory standby cycle
            float breathe = 0.72 + 0.28 * sin(Clock * 1.2566);

            // Crisp 1px L-shaped optical corner brackets framing each wing
            float brackets = CornerBracketsPx(sp, float2(x0, topY), float2(x1, botY), floor(10.0 * Dip), 1.0);

            // Razor-sharp 1px lower millimeter calibration rail + 6dip/30dip graduated ticks
            float inRailSpan = step(x0 + 6.0 * Dip, sp.x) * step(sp.x, x1 - 6.0 * Dip);
            float rail = StrokePx(sp.y - railY, 1.0) * inRailSpan;
            float minorT, majorT;
            TickRulerPx(sp.x - x0, 6.0 * Dip, 5.0, minorT, majorT);
            float minorBand = step(railY, sp.y) * step(sp.y, railY + 3.0 * Dip);
            float majorBand = step(railY - 2.0 * Dip, sp.y) * step(sp.y, railY + 5.0 * Dip);
            float ticks = (minorT * minorBand * 0.55 + majorT * majorBand) * inRailSpan;

            // Left-to-right crisp 5x7 micro-labels inside top corner of each wing (never mirrored)
            float fs = HudFs(Dip);
            float2 tagPos = float2(x0 + 6.0 * Dip, topY + 4.0 * Dip);
            float tagInk = side < 0.0
                ? LeftTag(sp, tagPos, fs)
                : RightTag(sp, tagPos, fs);

            // Three crisp 2dip square optical status pips quietly indexing in sequence
            float pips = 0.0;
            float pipY = topY + 4.0 * Dip + 3.5 * fs;
            float pipStartX = x1 - 24.0 * Dip;
            [unroll]
            for (int k = 0; k < 3; k++)
            {
                float2 pc = float2(pipStartX + (float)k * 6.0 * Dip, pipY);
                float dPip = SdfBox(sp - pc, float2(1.2 * Dip, 1.2 * Dip), 0.0);
                float pulse = 0.35 + 0.65 * (0.5 + 0.5 * sin(Clock * 1.2 + (float)k * 1.4));
                pips += FillPx(dPip) * pulse;
            }

            // Whisper-thin 1px Kiroshi calibration scan line gliding calmly once every 8 seconds
            float scanPhase = frac(Clock / 8.0);
            float scanVis = smoothstep(0.0, 0.06, scanPhase) * smoothstep(0.35, 0.24, scanPhase);
            float scanX = lerp(x0, x1, saturate(scanPhase / 0.32));
            float scanLine = StrokePx(sp.x - scanX, 1.0) * step(topY + 2.0 * Dip, sp.y) * step(sp.y, botY - 2.0 * Dip) * scanVis;

            float3 col = PrimaryCol.rgb * ((brackets * 0.55 + rail * 0.45 + ticks * 0.50 + tagInk * 0.70) * breathe);
            col += AccentCol.rgb * pips * 0.65;
            col += lerp(PrimaryCol.rgb, HotCol.rgb, 0.45) * scanLine * 0.85;

            float rawAlpha = saturate((brackets * 0.45 + rail * 0.35 + ticks * 0.40 + tagInk * 0.62 + pips * 0.55 + scanLine * 0.72) * breathe);
            float topSoft = smoothstep(0.0, 0.14, uv.y);
            float alpha = rawAlpha * 0.54 * wingEdge * topSoft * Fade;
            return float4(saturate(col) * alpha, alpha);
        }
        """;
}
