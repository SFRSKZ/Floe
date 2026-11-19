using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Night City Rain (3D Holographic Skyline Curtains &amp; Wet Asphalt Point Reflections): 30,000 GPU-instanced
/// micro-particles sculpt vertical holographic neon towers above a dark wet-asphalt reflection plane with calm
/// horizontal puddle ripples and sparse slanted micro-rain glints, shifting organically across Kabuki magenta,
/// cyber cyan, ultramarine blue and izakaya amber.
/// </summary>
public sealed class NightCityRainGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 30000; // 2 wings * 150 cols * 100 vertical/depth samples
    private const int ConstantFloats = 64;

    private static readonly Color NeonCyan = Color.FromRgb(0x00, 0xDA, 0xFF);
    private static readonly Color KabukiMagenta = Color.FromRgb(0xFF, 0x16, 0x76);
    private static readonly Color Ultramarine = Color.FromRgb(0x18, 0x4C, 0xFF);
    private static readonly Color IzakayaAmber = Color.FromRgb(0xFF, 0x94, 0x1E);
    private static readonly Color PearlNeon = Color.FromRgb(0xFF, 0xEC, 0xF8);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public NightCityRainGpuScene() : base(ParticleCount, ConstantFloats, VertexShader)
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
            _phase = (_phase + (dt * (.15f + (.34f * Smoother.Level) + (.16f * Smoother.Bass)))) % 10000f;
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

        Put(44, Lead(NeonCyan));
        Put(48, Second(KabukiMagenta));
        Put(52, Ultramarine);
        Put(56, IzakayaAmber);
        Put(60, PearlNeon);
    }

    private const string VertexShader = """
        cbuffer NightCityCloud : register(b0)
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
            float4 MagentaCol;
            float4 BlueCol;
            float4 AmberCol;
            float4 PearlCol;
        };

        """ + SharedHeader + """

        float3 StreetPalette(float p1, float p2)
        {
            float3 c = lerp(CyanCol.rgb, BlueCol.rgb, smoothstep(0.35, 0.70, p1));
            c = lerp(c, MagentaCol.rgb, smoothstep(0.32, 0.72, p2));
            c = lerp(c, AmberCol.rgb, smoothstep(0.72, 0.94, p1 * (1.0 - p2 * 0.4)));
            return c;
        }

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            const uint COLS = 150u;
            const uint ROWS = 100u;
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

            float u = ((float)cIdx + 0.5) / totalCols;
            float acousticU = (CrownMode > 0.5) ? abs(u - 0.5) * 2.0 : (wingIdx == 0u ? (1.0 - u) : u);
            float band = saturate(Band(saturate(acousticU * 0.92 + 0.04)) * 2.1);

            float wingW = max(xMax - xMin, 10.0);
            float horizonY = Size.y * 0.54;

            // Architectural skyline block quantizes adjacent columns into distinct neon towers & kanji curtains
            float colRand = Hash(float2(floor(u * 26.0), 11.0 + (float)wingIdx * 5.0));
            float towerMaxH = (0.34 + 0.48 * band + 0.24 * colRand);
            float curtainWave = 0.5 + 0.5 * sin(u * 18.0 - Phase * 1.15);

            float p1 = Noise(float2(u * 4.5 + Phase * 0.12 + (float)wingIdx * 3.1, ((float)rIdx / 100.0) * 2.2));
            float p2 = Noise(float2(u * 3.2 - Phase * 0.10, ((float)rIdx / 100.0) * 3.0 + Phase * 0.08));
            float3 hue = StreetPalette(p1, p2);

            float pxX = lerp(xMin, xMax, u);
            float pxY = horizonY;
            float radiusPx = 1.35 * Dip;
            float bri = 0.0;

            if (rIdx < 60u)
            {
                // Upper 3D holographic neon signage curtains
                float v = ((float)rIdx + 0.5) / 60.0;
                float swayX = sin(v * 6.28 + u * 16.0 - Phase * 1.25) * (wingW / totalCols) * 0.52;
                pxX += swayX;
                float yFrac = v * towerMaxH;
                pxY = horizonY - yFrac * (horizonY - 5.0 * Dip);

                float strata = 0.58 + 0.42 * sin(v * 18.0 + floor(u * 26.0) * 1.7);
                float densWave = 0.45 + 0.55 * sin(v * 9.5 - Phase * 1.4 + u * 12.0);
                bri = (0.20 + 0.40 * band + 0.20 * densWave) * strata * smoothstep(0.04, 0.30, colRand + band * 0.45);
                hue = lerp(hue, PearlCol.rgb, smoothstep(0.55, 0.90, densWave * band) * 0.56);
                radiusPx = lerp(1.30, 1.80, band) * Dip;
            }
            else if (rIdx < 92u)
            {
                // Lower 3D wet-asphalt reflection plane with gentle horizontal water ripple drift
                float v = ((float)(rIdx - 60u) + 0.5) / 32.0;
                float ripple = sin(v * 26.0 - Phase * 2.1 + colRand * 6.28) * (3.2 * Dip) * (0.3 + 0.7 * v);
                pxX += ripple;
                pxY = lerp(horizonY + 1.5 * Dip, Size.y - 4.0 * Dip, pow(v, 1.15));

                float puddleMask = 0.52 + 0.48 * sin(v * 18.0 - Phase * 1.6 + u * 9.0);
                bri = (0.16 + 0.36 * band) * exp(-v * 0.78) * puddleMask * (0.45 + 0.55 * curtainWave);
                radiusPx = lerp(1.25, 1.65, v) * Dip;
            }
            else
            {
                // Sparse delicate slanted micro-rain glint dots drifting calmly downward
                float rSub = ((float)(rIdx - 92u) + Hash(float2(u * 90.0, (float)rIdx))) / 8.0;
                float fall = frac(rSub + Phase * (0.45 + 0.15 * colRand) + colRand);
                pxX += (0.5 - fall) * 7.0 * Dip; // calm diagonal slant
                pxY = fall * (Size.y - 4.0 * Dip) + 2.0 * Dip;
                float glint = step(0.42, Hash(float2((float)cIdx, (float)rIdx))) * (0.4 + 0.6 * Treble);
                bri = 0.36 * glint * (1.0 + 0.6 * BeatPulse);
                hue = lerp(CyanCol.rgb, PearlCol.rgb, 0.55);
                radiusPx = 1.10 * Dip;
            }

            o.Color = hue * bri * (0.54 + 0.78 * Level);
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;
}
