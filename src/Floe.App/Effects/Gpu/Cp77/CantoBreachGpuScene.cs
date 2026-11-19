using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Canto Daemon (3D Mikoshi Neural Vault Point-Cloud): 30,720 GPU-instanced micro-particles crystalize in a
/// staggered 3D perspective geodesic hex-vault curving over black void. Calm concentric EMP waves ripple through
/// the lattice as localized neural pockets shift seamlessly across sapphire void, netrunner cyan, cobalt blue,
/// and rogue-daemon crimson-magenta.
/// </summary>
public sealed class CantoBreachGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 30720; // 2 wings * 160 cols * 96 geodesic rows
    private const int ConstantFloats = 64;

    private static readonly Color IceCyan = Color.FromRgb(0x00, 0xEA, 0xDC);
    private static readonly Color Cobalt = Color.FromRgb(0x16, 0x52, 0xFF);
    private static readonly Color DaemonCrimson = Color.FromRgb(0xFF, 0x10, 0x54);
    private static readonly Color DeepSapphire = Color.FromRgb(0x14, 0x22, 0x58);
    private static readonly Color PearlCyan = Color.FromRgb(0xDC, 0xFF, 0xFC);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public CantoBreachGpuScene() : base(ParticleCount, ConstantFloats, VertexShader)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        Smoother.Update(frame, attack: 9.0f, release: 2.6f, peakDecay: 0.45f);
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

        var lead = Lead(IceCyan);
        Put(44, lead);
        Put(48, Theme is null ? Cobalt : Mix(Cobalt, lead, .35f));
        Put(52, Second(DaemonCrimson));
        Put(56, Theme is null ? DeepSapphire : Mix(Colors.Black, lead, .20f));
        Put(60, Theme is null ? PearlCyan : Mix(lead, Colors.White, .78f));
    }

    private const string VertexShader = """
        cbuffer CantoCloud : register(b0)
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
            float4 CyanCol;
            float4 CobaltCol;
            float4 DaemonCol;
            float4 ShadowCol;
            float4 PearlCol;
        };

        """ + SharedHeader + """

        float VaultHeight(float u, float v, float band, float emp)
        {
            // Geodesic hexagonal wave folding across curved vault
            float hex1 = sin(u * 20.0 - Phase * 1.1 + v * 6.0) * cos(v * 14.0 + Phase * 0.85);
            float hex2 = sin(u * 11.0 + v * 9.0 - Phase * 0.7);
            float arch = sin(v * 3.14159);
            float n = Noise(float2(u * 6.0 + Phase * 0.25, v * 5.0 - Phase * 0.20));
            return saturate((0.24 + 0.54 * band + 0.20 * Bass + 0.24 * emp) * (0.45 + 0.35 * (0.5 + 0.5 * hex1) + 0.25 * n) * (0.4 + 0.6 * arch));
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
                cIdx = wingIdx * COLS + cIdx;
                totalCols = (float)(COLS * 2u);
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

            // Geodesic stagger creates crystalline diagonal/hexagonal point alignment
            float stagger = ((rIdx & 1u) != 0u) ? 0.5 : 0.0;
            float u = ((float)cIdx + stagger + 0.25) / totalCols;
            float v = ((float)rIdx + 0.5) / (float)ROWS;

            float acousticU = (CrownMode > 0.5) ? abs(u - 0.5) * 2.0 : (wingIdx == 0u ? (1.0 - u) : u);
            float band = Band(saturate(acousticU * 0.92 + 0.04));

            float empDist = length(float2(acousticU - KickAge * 0.85, (v - 0.48) * 0.55));
            float emp = exp(-pow(empDist * 8.5, 2.0)) * exp(-KickAge * 3.2);

            float h = VaultHeight(u, v, band, emp);
            float hBack = VaultHeight(u, saturate(v + 0.03), band, emp);
            float slope = saturate(0.5 + (h - hBack) * 5.2);

            float depthZ = lerp(0.95, 2.50, v);
            float persp = 1.0 / depthZ;
            float wingW = max(xMax - xMin, 10.0);
            float domeArc = 0.06 * sin(u * 3.14159);
            float pxX = 0.5 * (xMin + xMax) + (u - 0.5) * wingW * lerp(1.0, 0.72, v);
            float groundY = lerp(Size.y * 0.90, Size.y * (0.34 - domeArc), pow(v, 0.88));
            float pxY = groundY - h * Size.y * 0.74 * persp;

            // Localized neural breach color pockets shifting across sapphire, cyan, cobalt and daemon crimson
            float breachPatch = Noise(float2(u * 3.8 + Phase * 0.14 + (float)wingIdx * 4.7, v * 3.4 - Phase * 0.11));
            float isBreach = smoothstep(0.54, 0.80, breachPatch + emp * 0.35);
            float cobaltShift = smoothstep(0.32, 0.68, Noise(float2(u * 5.0 - Phase * 0.12, v * 4.2)));

            float3 baseCol = lerp(ShadowCol.rgb, lerp(CyanCol.rgb, CobaltCol.rgb, cobaltShift * 0.65), smoothstep(0.10, 0.54, h + slope * 0.32));
            baseCol = lerp(baseCol, DaemonCol.rgb, isBreach * 0.88);
            baseCol = lerp(baseCol, PearlCol.rgb, smoothstep(0.66, 0.94, h * slope + emp * 0.4) * 0.72);

            float energy = (0.14 + 0.42 * pow(slope, 1.25) + 0.28 * h + 0.24 * emp) * persp * lerp(1.25, 0.62, v);
            o.Color = baseCol * energy * (0.50 + 0.78 * Level);

            float radiusPx = lerp(1.75, 1.05, v) * Dip * (0.90 + 0.28 * h);
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;
}
