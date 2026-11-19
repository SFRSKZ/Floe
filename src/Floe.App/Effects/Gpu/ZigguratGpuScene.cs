using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Netspace data ziggurats: stepped obsidian-and-cyan data pyramids rise along a luminous cyber horizon on each wing,
/// reflected in a wet perspective net-floor below. Each stepped monolith breathes with its frequency band and carries
/// glowing horizontal data strata and cyan Fresnel rim light. A kick sends a blazing magenta ICE shockwave across the
/// floor; a snare strikes a golden vertical data spire with expanding concentric rings. In silence the world freezes
/// into a poised, reflective Netspace skyline.
/// </summary>
public sealed class ZigguratGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color Face = Color.FromRgb(0x07, 0x16, 0x1E);
    private static readonly Color Rain = Color.FromRgb(0x18, 0xDF, 0xC8);
    private static readonly Color Hot = Color.FromRgb(0xDC, 0xFF, 0xF8);
    private static readonly Color Core = Color.FromRgb(0xFF, 0x22, 0x98);
    private static readonly Color Needle = Color.FromRgb(0xFC, 0xE6, 0x38);

    private float _travel, _rain, _kickAge = 9, _needleAge = 9, _needleSeed = 1, _flare;

    public ZigguratGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _kickAge += dt;
        _needleAge += dt;
        if (Beat.Hit)
        {
            if (Beat.Snare)
            {
                _needleAge = 0;
                _needleSeed = (_needleSeed + 1) % 997;
            }
            else
            {
                _kickAge = 0;
            }
        }

        _flare = Math.Max(_flare * MathF.Exp(-dt * 3.8f), Math.Max(_kickAge < .02f ? 1f : 0f, audio.Bass * .55f));
        if (audio.Level > .015f)
        {
            _travel = (_travel + (dt * (.85f + (2.8f * audio.Level) + (2.0f * _flare)))) % 10000;
            _rain = (_rain + (dt * (.35f + (1.25f * audio.Level)))) % 1000;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        float step = frame.Dt * 60f;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = frame.Audio.Level;
        d[7] = _flare;
        d[8] = _travel;
        d[9] = _rain;
        d[10] = _kickAge;
        d[11] = _needleAge;
        d[12] = _needleSeed;
        d[13] = MathF.Pow(.84f, step);
        d[14] = frame.Audio.Bass;
        d[15] = frame.Audio.Treble;
        PutBands(20, frame);

        var rain = Lead(Rain);
        Put(44, Theme is null ? Face : Mix(Colors.Black, rain, .16f));
        Put(48, rain);
        Put(52, Theme is null ? Hot : Mix(rain, Colors.White, .78f));
        Put(56, Second(Core));
        Put(60, Needle);
    }

    private const string Shared = """
        cbuffer Ziggurat : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Flare;
            float Travel;
            float RainPhase;
            float KickAge;
            float NeedleAge;
            float NeedleSeed;
            float Decay;
            float Bass;
            float Treble;
            float4 Spare;
            float4 Bands[6];
            float4 FaceColor;
            float4 RainColor;
            float4 HotColor;
            float4 CoreColor;
            float4 NeedleColor;
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

        // Evaluates stepped Netspace ziggurat skyline at (wingU, hAboveHorizon [0..1])
        float3 SampleZiggurats(float wingU, float hAbove, float side, out float mask)
        {
            mask = 0.0;
            float3 col = 0;

            // Back parallax monolith row (dimmer, taller silhouettes)
            float backCells = 9.0;
            float bScroll = wingU * backCells + Travel * 0.14;
            float bId = floor(bScroll);
            float bLocal = abs(frac(bScroll) - 0.5) * 2.0;
            float bH = 0.28 + 0.36 * Hash(float2(bId, side * 5.0 + 1.0));
            float bStep = floor(hAbove * 6.0) / 6.0;
            float bHalfW = saturate(0.92 - bStep * 0.65);
            float bInside = step(hAbove, bH) * smoothstep(bHalfW + 0.04, bHalfW - 0.02, bLocal);
            if (bInside > 0.01)
            {
                float rim = exp(-abs(bLocal - bHalfW) * 24.0);
                col = FaceColor.rgb * 1.15 + RainColor.rgb * (0.14 + 0.45 * rim);
                mask = bInside * 0.72;
            }

            // Foreground sculpted stepped ziggurats driven directly by spectrum bands
            float cells = 6.0;
            float scroll = wingU * cells + Travel * 0.32;
            float id = floor(scroll);
            float localX = (frac(scroll) - 0.5) * 2.0; // -1..1
            float ax = abs(localX);
            float hRand = Hash(float2(id, side * 11.3 + 4.7));
            float band = Band(saturate(wingU * 0.92 + 0.04));

            float peakH = 0.34 + 0.24 * hRand + 0.38 * band;
            float tiers = 5.0;
            float tierIdx = floor(saturate(hAbove / max(peakH, 0.05)) * tiers);
            float tierFrac = frac(saturate(hAbove / max(peakH, 0.05)) * tiers);
            float halfW = lerp(0.84, 0.18, tierIdx / tiers);

            float inside = smoothstep(peakH + 0.02, peakH - 0.01, hAbove) * smoothstep(halfW + 0.035, halfW - 0.015, ax);
            if (inside > 0.01)
            {
                // Left/right sunlit bevel face shading
                float faceShade = localX < 0.0 ? 0.58 : 1.05;
                float edgeRim = exp(-abs(ax - halfW) * 28.0);
                float tierLedge = exp(-abs(tierFrac - 0.92) * 22.0);
                float coreSlit = exp(-abs(localX) * 26.0) * smoothstep(0.05, 0.85, hAbove / max(peakH, 0.05));

                float3 body = FaceColor.rgb * (0.75 + 0.75 * faceShade);
                body += RainColor.rgb * (tierLedge * (0.55 + 0.75 * band) + edgeRim * (0.75 + 0.95 * band));
                body += CoreColor.rgb * coreSlit * (0.45 + 1.15 * band + 0.95 * Flare);
                body += HotColor.rgb * (edgeRim * tierLedge * 1.15 + coreSlit * Flare * 0.85);

                col = lerp(col, body, inside);
                mask = max(mask, inside * 0.96);
            }

            return col;
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
            float horizon = 0.58;
            float band = Band(saturate(wingU * 0.92 + 0.04));
            float topSoft = smoothstep(0.0, 0.16, y);

            // Kick magenta ICE shockwave racing outward
            float shockPos = KickAge * 1.35;
            float shockEnv = exp(-KickAge * 3.5);
            float shock = exp(-pow((wingU - shockPos) * 14.0, 2.0)) * shockEnv;

            // Snare golden spire location
            float spireU = 0.18 + 0.64 * Hash(float2(NeedleSeed, side * 7.1 + 2.3));
            float spireEnv = exp(-NeedleAge * 4.2);
            float spireBeam = exp(-abs(wingU - spireU) * 110.0) * spireEnv;
            float spireGlow = exp(-abs(wingU - spireU) * 20.0) * spireEnv;

            // Atmospheric Netspace sky & horizon glow
            float hDist = abs(y - horizon);
            float horizonGlow = exp(-hDist * 8.5);
            float3 col = FaceColor.rgb * (0.45 + 0.75 * horizonGlow);
            col += lerp(RainColor.rgb, CoreColor.rgb, 0.25 + 0.35 * Flare) * horizonGlow * (0.32 + 0.45 * Bass + 0.55 * shock);

            float alpha = 0.68 * horizonGlow;

            if (y < horizon)
            {
                // Above horizon: stepped 3D ziggurat monoliths
                float hAbove = saturate((horizon - y) / horizon);
                float zigMask;
                float3 zigCol = SampleZiggurats(wingU, hAbove, side, zigMask);
                col = lerp(col, zigCol, zigMask);
                alpha = max(alpha, zigMask * 0.95);
            }
            else
            {
                // Below horizon: reflective perspective netspace floor + mirrored ziggurats
                float floorT = saturate((y - horizon) / (1.0 - horizon));
                float zDepth = 0.16 / max(floorT + 0.035, 0.035);

                // Mirrored ziggurat reflection softened by wet ripple
                float ripple = sin(zDepth * 8.0 - Travel * 3.5) * 0.004 * floorT;
                float refMask;
                float3 refCol = SampleZiggurats(wingU + ripple, floorT * 0.85, side, refMask);
                col = lerp(col, refCol * (0.48 - 0.18 * floorT), refMask * 0.72);

                // Crisp perspective grid lanes
                float laneX = abs(frac(wingU * 14.0 * (0.55 + 0.45 * floorT) + 0.5) - 0.5);
                float laneZ = abs(frac(zDepth * 0.85 - Travel * 0.65) - 0.5);
                float gridX = exp(-laneX * (28.0 - 12.0 * floorT));
                float gridZ = exp(-laneZ * 26.0);
                float fog = smoothstep(0.0, 0.32, floorT);

                col += RainColor.rgb * (gridX * 0.36 + gridZ * 0.42) * fog * (0.45 + 0.75 * band);
                col += CoreColor.rgb * gridZ * shock * 1.45 * fog;

                // Snare expanding golden ripple ring on the reflective floor
                float ringR = NeedleAge * 0.55;
                float ringD = length(float2((wingU - spireU) * 3.2, (floorT - 0.38) * 1.1));
                float ring = exp(-abs(ringD - ringR) * 36.0) * spireEnv;
                col += NeedleColor.rgb * ring * 1.85;

                alpha = max(alpha, 0.72 + 0.20 * fog);
            }

            // Crisp horizon neon seam
            float seam = exp(-hDist * 75.0);
            col += lerp(RainColor.rgb, HotColor.rgb, 0.5 + 0.5 * shock) * seam * (0.75 + 0.95 * band + 1.1 * shock);

            // Snare golden vertical data spire
            col += NeedleColor.rgb * (spireGlow * 0.45 + spireBeam * 1.85);
            col += HotColor.rgb * spireBeam * 1.35;

            col *= wingEdge * topSoft;
            float3 outCol = active > 0.5 ? max(col, trail * 0.78) : col;
            return float4(outCol, active > 0.5 ? max(alpha * wingEdge * topSoft, prev.a * 0.75) : alpha * wingEdge * topSoft);
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

            float ca = (0.4 + 2.6 * exp(-KickAge * 5.0)) * Dip / Size.x;
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
            hdr += max(bloom - 0.44, 0.0) * 0.55;

            float grain = (Hash(floor(position.xy) + floor(Travel * 18.0)) - 0.5) * 0.014;
            float3 graded = AcesTone(pow(max(hdr, 0.0), 1.32) * 1.48) + grain;

            float topSoft = smoothstep(0.0, 0.18, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.58, smoothstep(0.02, 0.28, luma)) * wingEdge * topSoft) * Fade;

            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
