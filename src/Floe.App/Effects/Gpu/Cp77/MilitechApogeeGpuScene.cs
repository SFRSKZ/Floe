using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Militech Apogee Sandevistan (3D Parallel Kinetic Particle Curtains): 30,240 crisp GPU-instanced micro-dots
/// arranged in ordered parallel 3D vertical/depth ribbon slices separated by clean obsidian space, undulating like
/// a breathing kinetic wave sculpture with localized spatial color shifts between ultramarine blue, arterial crimson,
/// electric violet-magenta and pearl-pink crests.
/// </summary>
public sealed class MilitechApogeeGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 30240; // 2 wings * 36 ribbon slices * 420 micro-dots per slice
    private const int ConstantFloats = 64;

    private static readonly Color Ultramarine = Color.FromRgb(0x14, 0x48, 0xFF);
    private static readonly Color Crimson = Color.FromRgb(0xFF, 0x10, 0x42);
    private static readonly Color VioletMag = Color.FromRgb(0xCE, 0x22, 0xFF);
    private static readonly Color CyanAccent = Color.FromRgb(0x00, 0xDC, 0xFF);
    private static readonly Color PearlWhite = Color.FromRgb(0xFF, 0xEA, 0xF8);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public MilitechApogeeGpuScene() : base(ParticleCount, ConstantFloats, VertexShader)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        Smoother.Update(frame, attack: 9.5f, release: 2.8f, peakDecay: 0.45f);
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
            // Majestic calm orbital curtain flow
            _phase = (_phase + (dt * (.16f + (.36f * Smoother.Level) + (.20f * Smoother.Bass)))) % 10000f;
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

        var lead = Lead(Ultramarine);
        Put(44, lead);
        Put(48, Second(Crimson));
        Put(52, VioletMag);
        Put(56, Theme is null ? CyanAccent : Mix(CyanAccent, lead, .45f));
        Put(60, Theme is null ? PearlWhite : Mix(lead, Colors.White, .80f));
    }

    private const string VertexShader = """
        cbuffer ApogeeSlices : register(b0)
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
            float4 BlueCol;
            float4 CrimsonCol;
            float4 VioletCol;
            float4 CyanCol;
            float4 PearlCol;
        };

        """ + SharedHeader + """

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            const uint SLICES = 36u;
            const uint DOTS = 420u;
            uint perWing = SLICES * DOTS;
            uint wingIdx = min(iid / perWing, 1u);
            uint rem = iid - wingIdx * perWing;
            uint sliceIdx = rem / DOTS;
            uint dotIdx = rem - sliceIdx * DOTS;

            float xMin, xMax, totalSlices;
            uint gSlice;
            if (CrownMode > 0.5)
            {
                xMin = 0.0;
                xMax = Size.x;
                gSlice = wingIdx * SLICES + sliceIdx;
                totalSlices = (float)(SLICES * 2u);
            }
            else if (wingIdx == 0u)
            {
                xMin = 0.0;
                xMax = DockLeft * Size.x;
                gSlice = sliceIdx;
                totalSlices = (float)SLICES;
            }
            else
            {
                xMin = DockRight * Size.x;
                xMax = Size.x;
                gSlice = sliceIdx;
                totalSlices = (float)SLICES;
            }

            float su = ((float)gSlice + 0.5) / totalSlices;
            // Subdivide each vertical slice into 35 Y levels x 12 depth-curtain filaments so slices show crisp 3D depth ribbons
            uint depthSub = dotIdx % 12u;
            uint vertRow = dotIdx / 12u;
            float dv = ((float)depthSub + 0.5) / 12.0;
            float yNorm = ((float)vertRow + 0.5) / 35.0;

            float acousticU = (CrownMode > 0.5) ? abs(su - 0.5) * 2.0 : (wingIdx == 0u ? (1.0 - su) : su);
            float band = Band(saturate(acousticU * 0.92 + 0.04));
            float kickWave = exp(-pow((acousticU - KickAge * 0.90) * 9.0, 2.0)) * exp(-KickAge * 3.2);

            float wingW = max(xMax - xMin, 20.0);
            float sliceGap = wingW / totalSlices;

            // Ordered vertical ribbon wave bending gently in X and compressing vertically into glowing ridges
            float wave1 = sin(su * 10.5 - Phase * 1.35 + dv * 2.2);
            float wave2 = cos(yNorm * 5.2 - Phase * 1.1 + su * 6.5);
            float bendX = (wave1 * 0.24 + (dv - 0.5) * 0.42 * cos(yNorm * 3.14159 - Phase * 0.7)) * sliceGap;
            float pxX = lerp(xMin, xMax, su) + bendX;

            float rippleY = sin(su * 8.0 + dv * 3.0 - Phase * 1.5) * (0.08 + 0.14 * band);
            float ampCompress = lerp(0.88, 0.36 + 0.46 * band, 0.5 + 0.5 * wave2);
            float yCurved = 0.52 + (yNorm - 0.5) * ampCompress + rippleY;
            float pxY = yCurved * Size.y;

            // Rib density concentration brightness: where vertical wave folds bunch dots together, brightness blooms
            float bunch = saturate(0.45 + 0.55 * abs(sin(yNorm * 6.283 + su * 5.0 - Phase * 1.2)));
            float centerEnvelope = sin(yNorm * 3.14159);

            // Localized multi-hue shifting across blue, arterial red, violet-magenta & cyan pockets (match 00:14..00:20 video)
            float regionNoise1 = Noise(float2(su * 4.5 + Phase * 0.12 + (float)wingIdx * 5.1, yNorm * 2.8 - Phase * 0.09));
            float regionNoise2 = Noise(float2(su * 3.0 - Phase * 0.10, dv * 2.5 + yNorm * 2.2));
            float sliceAlt = smoothstep(0.25, 0.75, regionNoise1);
            float warmShift = smoothstep(0.32, 0.76, regionNoise2);

            float3 coolHue = lerp(BlueCol.rgb, CyanCol.rgb, smoothstep(0.58, 0.85, regionNoise1) * 0.45);
            float3 warmHue = lerp(CrimsonCol.rgb, VioletCol.rgb, sliceAlt * 0.62);
            float3 col = lerp(coolHue, warmHue, warmShift);
            col = lerp(col, PearlCol.rgb, smoothstep(0.66, 0.96, band * bunch + kickWave * 0.35) * 0.65);

            float brightness = (0.09 + 0.26 * band + 0.14 * bunch + 0.12 * kickWave) * centerEnvelope * (0.45 + 0.70 * Level);
            o.Color = col * brightness;

            float radiusPx = lerp(1.05, 1.55, bunch) * Dip * (0.85 + 0.25 * band);
            o.Pos = ProjectParticle(float2(pxX, pxY), radiusPx, vid, o.LocalUv);
            return o;
        }
        """;
}
