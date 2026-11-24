using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Thermal Monowire (3D Braided Helical Plasma Particle Strands): 28,800 GPU-instanced micro-particles wind along
/// 16 intertwined 3D helical plasma filaments pinned tautly across each wing, swelling softly with harmonic bass
/// standing waves while shifting organically across arterial red, molten amber-gold, white-hot core beads and
/// localized electric violet-blue ionization pockets.
/// </summary>
public sealed class MonowireArcGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 28800; // 2 wings * 16 helical strands * 900 dots per strand
    private const int ConstantFloats = 64;

    private static readonly Color ThermalOrange = Color.FromRgb(0xFF, 0x52, 0x0C);
    private static readonly Color PlasmaGold = Color.FromRgb(0xFF, 0xD0, 0x38);
    private static readonly Color ArterialRed = Color.FromRgb(0xD8, 0x08, 0x26);
    private static readonly Color IonViolet = Color.FromRgb(0x78, 0x32, 0xFF);
    private static readonly Color WhiteHot = Color.FromRgb(0xFF, 0xFA, 0xEE);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public MonowireArcGpuScene() : base(ParticleCount, ConstantFloats, VertexShader)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        Smoother.Update(frame, attack: 10.5f, release: 3.0f, peakDecay: 0.45f);
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
            // Calm, taut plasma filament oscillation
            _phase = (_phase + (dt * (.22f + (.48f * Smoother.Level) + (.25f * Smoother.Bass)))) % 10000f;
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

        var lead = Lead(ThermalOrange);
        Put(44, lead);
        Put(48, Second(PlasmaGold));
        Put(52, ArterialRed);
        Put(56, IonViolet);
        Put(60, WhiteHot);
    }

    private const string VertexShader = """
        cbuffer MonowireCloud : register(b0)
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
            float4 OrangeCol;
            float4 GoldCol;
            float4 RedCol;
            float4 VioletCol;
            float4 WhiteCol;
        };

        """ + SharedHeader + """

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            const uint STRANDS = 16u;
            const uint DOTS = 900u;
            uint perWing = STRANDS * DOTS;
            uint wingIdx = min(iid / perWing, 1u);
            uint rem = iid - wingIdx * perWing;
            uint strandIdx = rem / DOTS;
            uint dotIdx = rem - strandIdx * DOTS;

            float xMin, xMax, side;
            if (CrownMode > 0.5)
            {
                xMin = wingIdx == 0u ? 0.0 : Size.x * 0.5;
                xMax = wingIdx == 0u ? Size.x * 0.5 : Size.x;
                side = wingIdx == 0u ? -1.0 : 1.0;
            }
            else if (wingIdx == 0u)
            {
                xMin = 0.0;
                xMax = DockLeft * Size.x;
                side = -1.0;
            }
            else
            {
                xMin = DockRight * Size.x;
                xMax = Size.x;
                side = 1.0;
            }

            float u = ((float)dotIdx + 0.5) / (float)DOTS;
            float wingW = max(xMax - xMin, 10.0);
            float acousticU = wingIdx == 0u ? (1.0 - u) : u;
            float band = Band(saturate(acousticU * 0.92 + 0.04));

            float sNorm = (float)strandIdx / (float)STRANDS; // 0..1
            float isCore = 1.0 - saturate((float)strandIdx / 6.0);
            float env = sin(u * 3.14159265); // taut anchor pinning at wing ends

            // a kick flares the wire at once wherever its band is loud, as the Blackwall does, instead of a whip
            // running down it: what lights up is where the song is
            float whipPulse = exp(-KickAge * 5.0) * saturate(0.25 + band * 1.3);
            float spineY = 0.52 * Size.y
                + (0.20 * Size.y * env * sin(u * 5.2 - Phase * 1.35 + side * 0.7) * (0.25 + 0.75 * Bass))
                + (0.10 * Size.y * env * sin(u * 13.0 + Phase * 1.9) * (0.20 + 0.80 * band))
                - (0.10 * Size.y * whipPulse);

            // 3D helical strand radius & perspective projection
            float helixAngle = sNorm * 6.2831853 * 2.0 + u * (14.0 + 6.0 * isCore) - Phase * (1.8 + 0.8 * sNorm);
            // the braid opens where the song is loud and lies close where it is quiet; a kick throws it wider there
            float spreadR = lerp(3.2, 22.0, pow(1.0 - isCore, 1.35)) * Dip * env * (0.30 + 1.10 * band + 1.10 * whipPulse);
            float zDepth = cos(helixAngle);
            float pxX = lerp(xMin, xMax, u) + zDepth * 3.2 * Dip;
            float pxY = spineY + sin(helixAngle) * spreadR;

            // Localized electric violet-cobalt ionization pockets along high-energy segments
            float ionPatch = smoothstep(0.55, 0.82, Noise(float2(u * 5.0 + Phase * 0.16 + (float)wingIdx * 3.3, sNorm * 3.2 - Phase * 0.12)));

            float3 col = lerp(RedCol.rgb, OrangeCol.rgb, 0.45 + 0.55 * isCore);
            col = lerp(col, GoldCol.rgb, smoothstep(0.35, 0.85, isCore + band * 0.35));
            col = lerp(col, VioletCol.rgb, ionPatch * (0.55 + 0.35 * (1.0 - isCore)));
            col = lerp(col, WhiteCol.rgb, smoothstep(0.68, 0.98, isCore * (0.55 + 0.45 * zDepth) + whipPulse * 0.5) * 0.72);

            float depthFade = lerp(0.58, 1.15, 0.5 + 0.5 * zDepth);
            float energy = lerp(0.08, 0.22, isCore) * (0.25 + 0.30 * Level + 1.10 * band + 2.60 * whipPulse) * depthFade;
            o.Color = col * energy;

            float radiusPx = lerp(1.00, 1.45, isCore) * Dip * lerp(0.85, 1.15, 0.5 + 0.5 * zDepth);
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;
}
