using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Blackwall Cinder (3D Cyberspace Point-Cloud Sculpture): 30,720 GPU-instanced micro-particles form an oblique
/// 3D perspective undulating cyberspace terrain &amp; digital veil receding into pure obsidian void. Steep folded
/// crests gather additively into luminous magenta-pearl highlights while localized spatial regions shift across
/// shadow burgundy, arterial crimson, magenta and cobalt-violet Blackwall interference pockets.
/// </summary>
public sealed class BlackwallCinderGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 30720; // 2 wings * 160 cols * 96 depth rows
    private const int ConstantFloats = 64;

    private static readonly Color Crimson = Color.FromRgb(0xEB, 0x08, 0x34);
    private static readonly Color Magenta = Color.FromRgb(0xFF, 0x1A, 0x7A);
    private static readonly Color CobaltViolet = Color.FromRgb(0x42, 0x28, 0xFF);
    private static readonly Color DeepShadow = Color.FromRgb(0x36, 0x04, 0x10);
    private static readonly Color PearlHot = Color.FromRgb(0xFF, 0xC8, 0xEA);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public BlackwallCinderGpuScene() : base(ParticleCount, ConstantFloats, VertexShader)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        Smoother.Update(frame, attack: 8.5f, release: 2.5f, peakDecay: 0.45f);
        _kickAge += dt;
        _snareAge += dt;

        if (BeatHit)
        {
            _seed = (_seed + 1f) % 997f;
            if (BeatSnare) _snareAge = 0f;
            else _kickAge = 0f;
        }

        if (frame.Audio.Level > .015f)
        {
            // Calm, majestic sculptural breathing — frozen completely during silence
            _phase = (_phase + (dt * (.14f + (.32f * Smoother.Level) + (.18f * Smoother.Bass)))) % 10000f;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        bool crown = frame.DockLeft >= frame.DockRight;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0f, 1f);
        d[4] = crown ? 1f : Math.Clamp(frame.DockLeft, .05f, .9f);
        d[5] = crown ? 0f : Math.Clamp(frame.DockRight, d[4] + .02f, .95f);
        d[6] = Smoother.Level;
        d[7] = crown ? 1f : 0f;
        d[8] = Smoother.Bass;
        d[9] = Smoother.Treble;
        d[10] = _phase;
        d[11] = BeatPulse;
        d[12] = _kickAge;
        d[13] = _snareAge;
        d[14] = Smoother.Mid;
        d[15] = _seed;
        d[16] = d[17] = d[18] = d[19] = 0f;
        Smoother.WriteBands(d, 20);

        var lead = Lead(Crimson);
        Put(44, lead);
        Put(48, Second(Magenta));
        Put(52, Theme is null ? CobaltViolet : Mix(CobaltViolet, lead, .35f));
        Put(56, Theme is null ? DeepShadow : Mix(Colors.Black, lead, .22f));
        Put(60, Theme is null ? PearlHot : Mix(lead, Colors.White, .78f));
    }

    private const string VertexShader = """
        cbuffer BlackwallCloud : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float CrownMode;
            float Bass;
            float Treble;
            float Phase;
            float BeatPulse;
            float KickAge;
            float SnareAge;
            float Mid;
            float Seed;
            float4 Params;
            float4 Bands[6];
            float4 CrimsonCol;
            float4 MagentaCol;
            float4 VioletCol;
            float4 ShadowCol;
            float4 PearlCol;
        };

        """ + SharedHeader + """

        float TerrainHeight(float u, float v, float band, float shock)
        {
            // Organic multi-scale undulating mountain folds + subtle terraced Blackwall ledges
            float n1 = Noise(float2(u * 6.5 - Phase * 0.45, v * 4.8 + Phase * 0.32));
            float n2 = Noise(float2(u * 13.0 + Phase * 0.30, v * 9.5 - Phase * 0.25));
            float wave = 0.5 + 0.5 * sin(u * 11.0 - v * 7.5 - Phase * 1.45);
            float ridge = pow(1.0 - abs(n1 * 2.0 - 1.0), 1.4) * 0.52 + n2 * 0.28 + wave * 0.20;
            float terrace = lerp(ridge, floor(ridge * 6.0) / 6.0, 0.22);
            return saturate(terrace * (0.34 + 0.72 * band + 0.28 * Bass + 0.22 * shock));
        }

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            const uint COLS = 160u;
            const uint ROWS = 96u;
            uint perWing = COLS * ROWS;
            uint wingIdx = min(iid / perWing, 1u);
            uint rem = iid - wingIdx * perWing;
            uint cIdx = rem % COLS;
            uint rIdx = rem / COLS;

            float xMin, xMax, side, totalCols;
            if (CrownMode > 0.5)
            {
                xMin = 0.0;
                xMax = Size.x;
                side = 1.0;
                uint gCol = wingIdx * COLS + cIdx;
                totalCols = (float)(COLS * 2u);
                cIdx = gCol;
            }
            else if (wingIdx == 0u)
            {
                xMin = 0.0;
                xMax = DockLeft * Size.x;
                side = -1.0;
                totalCols = (float)COLS;
            }
            else
            {
                xMin = DockRight * Size.x;
                xMax = Size.x;
                side = 1.0;
                totalCols = (float)COLS;
            }

            float u = ((float)cIdx + 0.5) / totalCols;
            float v = ((float)rIdx + 0.5) / (float)ROWS;

            // Radiate frequency symmetry outward from the dock capsule edges
            float acousticU = (CrownMode > 0.5) ? abs(u - 0.5) * 2.0 : (wingIdx == 0u ? (1.0 - u) : u);
            float band = Band(saturate(acousticU * 0.92 + 0.04));

            float shockFront = KickAge * 0.95;
            float shock = exp(-pow((acousticU - shockFront) * 10.0, 2.0)) * exp(-KickAge * 3.4);

            float h = TerrainHeight(u, v, band, shock);
            float hBack = TerrainHeight(u, saturate(v + 0.03), band, shock);
            float slope = saturate(0.5 + (h - hBack) * 5.2);

            // Oblique 3D perspective camera receding into deep dark cyberspace horizon
            float depthZ = lerp(0.95, 2.55, v);
            float perspScale = 1.0 / depthZ;
            float wingW = max(xMax - xMin, 10.0);
            float centerX = 0.5 * (xMin + xMax);
            float localX = (u - 0.5) * wingW * lerp(1.0, 0.72, v);
            float horizonShift = (CrownMode > 0.5) ? 0.0 : (-side * v * wingW * 0.14);
            float pxX = centerX + localX + horizonShift;

            float groundY = lerp(Size.y * 0.92, Size.y * 0.36, pow(v, 0.85));
            float liftPx = h * Size.y * 0.78 * perspScale;
            float pxY = groundY - liftPx;

            // Localized organic color shifting ("yer yer renk degisiyorlar")
            float patchNoise = Noise(float2(u * 4.2 + Phase * 0.18 + (float)wingIdx * 3.7, v * 3.5 - Phase * 0.14));
            float cobaltPocket = smoothstep(0.56, 0.80, patchNoise);
            float magZone = smoothstep(0.32, 0.68, h * 0.7 + slope * 0.45);

            float3 baseCol = lerp(ShadowCol.rgb, CrimsonCol.rgb, smoothstep(0.10, 0.55, h + slope * 0.35));
            baseCol = lerp(baseCol, MagentaCol.rgb, magZone * 0.78);
            baseCol = lerp(baseCol, VioletCol.rgb, cobaltPocket * (0.45 + 0.40 * (1.0 - slope)));
            baseCol = lerp(baseCol, PearlCol.rgb, smoothstep(0.74, 0.98, h * slope + shock * 0.35) * 0.65);

            // Chiaroscuro intensity: subtle dark-valley structure, luminous folded 3D ridge crests
            float energy = (0.11 + 0.30 * pow(slope, 1.30) + 0.22 * h + 0.16 * shock) * perspScale * lerp(1.22, 0.62, v);
            o.Color = baseCol * energy * (0.48 + 0.78 * Level + 0.32 * Band(saturate(acousticU)));

            float radiusPx = lerp(1.75, 1.05, v) * Dip * (0.90 + 0.30 * h);
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;
}
