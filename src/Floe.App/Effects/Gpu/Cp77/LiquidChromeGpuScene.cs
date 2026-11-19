using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Liquid Chrome (Obsidian-Mirror Cyberware &amp; Molten Crimson Core): sculpts smoothly curved dark obsidian metallic
/// lobes punctured by crisp silver light-bank reflections and a saturated arterial-crimson fracture seam along the
/// center spine, framed by 1px etched cyberware blueprint hairlines in ShowPs.
/// </summary>
public sealed class LiquidChromeGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color Chrome = Color.FromRgb(0xE8, 0xEE, 0xF6);
    private static readonly Color Crimson = Color.FromRgb(0xFF, 0x0A, 0x38);
    private static readonly Color Hot = Color.FromRgb(0xFF, 0xF4, 0xF0);
    private static readonly Color Gunmetal = Color.FromRgb(0x0D, 0x11, 0x18);
    private static readonly Color EmberCore = Color.FromRgb(0xFF, 0x4E, 0x14);

    private readonly Cp77AudioSmoother _smooth = new();
    private float _phase, _kickAge = 9, _snareAge = 9, _surge, _seed = 1;

    public LiquidChromeGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _smooth.Update(frame, attack: 12f, release: 3.2f, peakDecay: 0.48f);
        _kickAge += dt;
        _snareAge += dt;

        if (Beat.Hit)
        {
            _seed = (_seed + 1) % 997;
            if (Beat.Snare) _snareAge = 0;
            else
            {
                _kickAge = 0;
                _surge = 1f;
            }
        }

        _surge *= MathF.Exp(-dt * 4.2f);
        if (audio.Level > .015f)
        {
            // Calm, heavy viscous liquid-metal flow
            _phase = (_phase + (dt * (.20f + (.65f * _smooth.Level) + (.35f * _smooth.Bass)))) % 10000;
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
        d[12] = _surge;
        d[13] = _seed;
        d[14] = MathF.Pow(.76f, step);
        d[15] = crown ? 1f : 0f;
        d[16] = d[17] = d[18] = d[19] = 0f;
        _smooth.WriteBands(d, 20);

        Put(44, Lead(Chrome));
        Put(48, Second(Crimson));
        Put(52, Hot);
        Put(56, Gunmetal);
        Put(60, EmberCore);
    }

    private static readonly string Shared = """
        cbuffer LiquidChrome : register(b0)
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
            float Surge;
            float Seed;
            float Decay;
            float CrownMode;
            float4 Spare;
            float4 Bands[6];
            float4 ChromeCol;
            float4 CrimsonCol;
            float4 HotCol;
            float4 GunmetalCol;
            float4 EmberCol;
        };

        """ + ShaderHelpers + Cp77ShaderLib.CoreHelpers + Cp77ShaderLib.FontHlsl
        + Cp77ShaderLib.LabelFn("LeftTag", "CHROME // 77-AG")
        + Cp77ShaderLib.LabelFn("RightTag", "AG-CORE // REV.4")
        + """

        float ChromeShardField(float u, float y, float band, float shock, out float coreGap)
        {
            float spineY = 0.51
                + 0.075 * sin(u * 8.5 - Phase * 0.95) * (0.35 + 0.65 * band)
                + 0.032 * cos(u * 18.0 + Phase * 1.45) * (0.25 + 0.75 * Treble);

            float dy = y - spineY;
            float halfW = 0.19 + 0.12 * band + 0.05 * Bass + 0.05 * shock;

            float skewU = u * 7.2 - dy * 1.65 * sign(dy + 0.0001) - Phase * 0.52;
            float n1 = Noise(float2(skewU, y * 3.1));
            float n2 = Noise(float2(u * 13.5 + dy * 2.2, y * 5.2 - Phase * 0.28));

            float fold1 = 1.0 - abs(n1 * 2.0 - 1.0);
            float fold2 = 1.0 - abs(n2 * 2.0 - 1.0);
            float swell = 0.5 + 0.5 * sin(u * 11.0 - dy * 3.1 - Phase * 0.95);

            float shardReach = halfW * (0.58 + 0.54 * pow(fold1, 0.85) + 0.26 * fold2);
            float distNorm = abs(dy) / max(shardReach, 0.04);
            float envelope = saturate(1.0 - pow(distNorm, 1.55));

            float h = pow(envelope, 0.72) * (0.44 + 0.38 * fold1 + 0.22 * fold2 + 0.16 * swell);

            float crackWave = (n1 - 0.5) * 0.052;
            float spineDist = abs(dy - crackWave);
            float crackWidth = 0.024 + 0.044 * band + 0.032 * shock;
            coreGap = smoothstep(crackWidth + 0.018, crackWidth * 0.18, spineDist) * smoothstep(0.18, 0.36, envelope);

            return max(0.0, h * (1.0 - coreGap * 0.55));
        }

        float3 StudioChrome(float3 n, float coreProximity, float shock)
        {
            float3 v = float3(0, 0, 1);
            float3 r = reflect(-v, n);
            float fresnel = pow(1.0 - saturate(dot(n, v)), 4.0);

            float bankTop = smoothstep(0.34, 0.44, r.y) * smoothstep(0.72, 0.54, r.y);
            float horizonBlade = exp(-pow((r.y + 0.12 * r.x - 0.10) * 11.0, 2.0));
            float coolBounce = exp(-pow((r.y + 0.58) * 4.5, 2.0)) * 0.24;
            float keySpec = pow(saturate(dot(r, normalize(float3(0.28, 0.72, 0.62)))), 42.0);
            float rimSpec = pow(saturate(dot(r, normalize(float3(-0.38, 0.56, 0.73)))), 22.0);

            float3 obsidian = float3(0.022, 0.026, 0.036);
            float3 mirrorSilver = ChromeCol.rgb * 1.15;
            float lightStrip = saturate(bankTop * 0.92 + horizonBlade * 0.78 + coolBounce);

            float3 metal = lerp(obsidian, mirrorSilver, lightStrip);
            metal += HotCol.rgb * (keySpec * 1.35 + rimSpec * 0.48) * (1.0 + 0.38 * shock);
            metal += ChromeCol.rgb * fresnel * 0.15;

            float innerLip = smoothstep(-0.08, -0.62, r.y) * coreProximity;
            metal += float3(0.92, 0.03, 0.06) * innerLip * (0.68 + 0.65 * shock);
            return metal;
        }
        """;

    private const string FeedbackPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            float active = step(0.015, Level);
            float4 prev = Last.Load(int3((int2)sp, 0));
            float3 trail = prev.rgb * (active > 0.5 ? Decay : 0.92);

            float y = uv.y;
            float topSoft = smoothstep(0.0, 0.14, y);
            float band = Band(saturate(wingU * 0.92 + 0.04));

            float shockPos = KickAge * 1.25;
            float shockEnv = exp(-KickAge * 3.8);
            float shock = exp(-pow((wingU - shockPos) * 14.0, 2.0)) * shockEnv;

            float epsU = 0.0024;
            float epsY = 0.012;
            float coreGap = 0, g1 = 0, g2 = 0;
            float h = ChromeShardField(wingU, y, band, shock, coreGap);
            float hx = ChromeShardField(wingU + epsU, y, band, shock, g1);
            float hy = ChromeShardField(wingU, y + epsY, band, shock, g2);

            float mask = smoothstep(0.18, 0.27, h) * (1.0 - smoothstep(0.24, 0.62, coreGap));
            float3 col = float3(0, 0, 0);

            if (coreGap > 0.001)
            {
                float coreVein = pow(coreGap, 1.55);
                float3 crimsonDeep = lerp(float3(0.65, 0.015, 0.035), CrimsonCol.rgb * 0.85, 0.25);
                float3 crimsonHot = lerp(float3(1.50, 0.10, 0.04), EmberCol.rgb * 1.25, 0.25);
                float3 molten = lerp(crimsonDeep, crimsonHot, saturate(coreVein * 0.72 + shock * 0.30 + band * 0.25));
                molten += float3(1.65, 0.25, 0.07) * pow(coreGap, 4.5) * (0.40 + 0.78 * band + 0.75 * shock);
                col = lerp(col, molten, saturate(coreGap * 0.98));
            }

            if (mask > 0.001)
            {
                float3 n = normalize(float3((h - hx) * 18.0, (h - hy) * 14.0, 0.36));
                float coreProximity = smoothstep(0.16, 0.72, coreGap);
                float3 chrome = StudioChrome(n, coreProximity, shock);

                float bevelRim = exp(-abs(h - 0.24) * 32.0);
                chrome += ChromeCol.rgb * bevelRim * (0.36 + 0.45 * shock);
                chrome += float3(0.88, 0.02, 0.04) * smoothstep(0.14, 0.52, coreGap) * (0.62 + 0.75 * band);

                col = lerp(col, chrome, mask);
            }

            col *= wingEdge * topSoft;
            float body = saturate(max(mask, coreGap));
            float3 outCol = active > 0.5 ? lerp(max(col, trail * 0.35), col, body * 0.96) : col;
            float alpha = saturate(max(mask * 0.98, coreGap * 0.98) * wingEdge * topSoft);
            return float4(outCol, active > 0.5 ? max(alpha, prev.a * 0.28) : alpha);
        }
        """;

    private const string ShowPs = """
        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            float wingU, wingEdge, side;
            if (WingInfo(uv.x, wingU, wingEdge, side) < 0.5) return float4(0, 0, 0, 0);

            // Mirror-crisp sample with zero blur bloom fog or always-on CA
            float4 c0 = Last.Load(int3((int2)sp, 0));
            float2 px = float2(Dip / Size.x, Dip / Size.y);
            float3 bloom = (Last.Sample(Linear, saturate(uv + float2(px.x * 1.5, 0))).rgb
                          + Last.Sample(Linear, saturate(uv - float2(px.x * 1.5, 0))).rgb) * 0.5;
            float3 hdr = c0.rgb + max(bloom - 0.95, 0.0) * 0.16;
            float3 graded = AcesToneClean(max(hdr, 0.0) * 1.15);

            // Ultra-subtle 1px lower blueprint micro-ticks only (pure gallery-clean liquid chrome without corner text clutter)
            float xMin, xMax;
            WingBoundsPx(uv.x, xMin, xMax);
            float padX = floor(8.0 * Dip);
            float x0 = xMin + padX;
            float x1 = xMax - padX;
            float botY = floor(Size.y - 5.0 * Dip);

            float minorT, majorT;
            TickRulerPx(sp.x - x0, 6.0 * Dip, 5.0, minorT, majorT);
            float inSpan = step(x0 + 4.0 * Dip, sp.x) * step(sp.x, x1 - 4.0 * Dip);
            float ticks = (minorT * step(botY - 3.0 * Dip, sp.y) + majorT * step(botY - 5.0 * Dip, sp.y)) * step(sp.y, botY) * inSpan;

            float hudMask = saturate(ticks * 0.18);
            graded = lerp(graded, ChromeCol.rgb, hudMask);

            float topSoft = smoothstep(0.0, 0.14, uv.y);
            float luma = dot(graded, float3(0.299, 0.587, 0.114));
            float alpha = saturate(max(c0.a * 0.94, max(smoothstep(0.02, 0.24, luma), hudMask * 0.35)) * wingEdge * topSoft) * Fade;
            return float4(saturate(graded) * alpha, alpha);
        }
        """;
}
