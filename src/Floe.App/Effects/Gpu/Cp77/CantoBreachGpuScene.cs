using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Canto Daemon: 30,720 GPU-instanced grains on a desert of dunes seen in 3D perspective. The grains are really there:
/// each has its own depth and is blown out from the middle to its edge across the sand, near ones faster than far
/// ones, the lit windward faces carrying the colour and the lee sides falling into sapphire shade. A kick throws the
/// grains of the loud bands into the air and they fall back onto the dunes. A focal plane drifts through the depth: grains in it are hard points, the rest open into small, dimmer
/// discs. The field runs the whole strip, behind the dock too.
/// </summary>
public sealed class CantoBreachGpuScene : PointCloudGpuScene
{
    private const int ParticleCount = 30720;
    private const int ConstantFloats = 64;

    private static readonly Color IceCyan = Color.FromRgb(0x00, 0xEA, 0xDC);
    private static readonly Color Cobalt = Color.FromRgb(0x16, 0x52, 0xFF);
    private static readonly Color DaemonCrimson = Color.FromRgb(0xFF, 0x10, 0x54);
    private static readonly Color DeepSapphire = Color.FromRgb(0x14, 0x22, 0x58);
    private static readonly Color PearlCyan = Color.FromRgb(0xDC, 0xFF, 0xFC);

    private float _phase, _kickAge = 9f, _snareAge = 9f, _seed = 1f;

    public CantoBreachGpuScene() : base(ParticleCount, ConstantFloats, VertexShader, ParticlePs, FullEdgeCompositePs)
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

        // the cloud keeps drifting slowly in silence; music drives it on
        float drive = frame.Audio.Level > .015f ? (.30f * Smoother.Level) + (.22f * Smoother.Bass) : 0f;
        _phase = (_phase + (dt * (.10f + drive))) % 10000f;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0f, 1f);
        // no gap for the dock: these are only kept for the shared constant layout
        d[4] = 1f;
        d[5] = 0f;
        d[6] = Smoother.Level;
        d[7] = 1f;
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

        struct VSOut
        {
            float4 Pos : SV_Position;
            float2 LocalUv : TEXCOORD0;
            float3 Color : COLOR0;
            float Blur : TEXCOORD1;
        };

        float Hash(float2 p)
        {
            float3 q = frac(p.xyx * .1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }

        float Band(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = i + 1;
            return lerp(Bands[i / 4][i % 4], Bands[j / 4][j % 4], f - i);
        }

        float3 Palette(float t)
        {
            // cyan -> cobalt -> daemon crimson -> cyan again: a closed loop, so the run never jumps
            t = frac(t) * 3.0;
            float3 a = lerp(CyanCol.rgb, CobaltCol.rgb, smoothstep(0.0, 1.0, saturate(t)));
            float3 b = lerp(CobaltCol.rgb, DaemonCol.rgb, smoothstep(0.0, 1.0, saturate(t - 1.0)));
            float3 c = lerp(DaemonCol.rgb, CyanCol.rgb, smoothstep(0.0, 1.0, saturate(t - 2.0)));
            return t < 1.0 ? a : (t < 2.0 ? b : c);
        }

        static const float2 Corners[6] =
        {
            float2(-1.0, -1.0), float2( 1.0, -1.0), float2(-1.0,  1.0),
            float2(-1.0,  1.0), float2( 1.0, -1.0), float2( 1.0,  1.0),
        };

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3.0 - (2.0 * f));
            return lerp(lerp(Hash(i), Hash(i + float2(1.0, 0.0)), f.x),
                        lerp(Hash(i + float2(0.0, 1.0)), Hash(i + float2(1.0, 1.0)), f.x), f.y);
        }

        // desert dunes: a long gentle windward climb, a short steep lee, fine ripples over both
        float Dune(float u, float v, out float lit)
        {
            float warp = Noise(float2(u * 3.0 + Phase * 0.02, v * 2.5)) * 0.9;
            float r = frac(u * 6.0 + v * 1.4 + warp - Phase * 0.015);
            float crest = r < 0.78 ? r / 0.78 : (1.0 - r) / 0.22;
            crest = crest * crest * (3.0 - 2.0 * crest);
            float ripple = 0.5 + 0.5 * sin(u * 120.0 + v * 30.0 + warp * 7.0 - Phase * 0.6);
            // the windward face takes the light, brightest just under the crest; the lee falls into shadow
            lit = r < 0.78 ? 0.25 + 0.75 * pow(r / 0.78, 1.6) : 0.10;
            return 0.82 * crest + 0.18 * ripple;
        }

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            VSOut o;
            float fi = (float)iid;
            float h1 = Hash(float2(fi * 0.137 + 0.5, 1.7));
            float h2 = Hash(float2(fi * 0.291 + 3.1, 7.3));
            float h3 = Hash(float2(fi * 0.073 + 11.0, 2.9));
            float h4 = Hash(float2(fi * 0.417 + 5.5, 13.1));
            float h5 = Hash(float2(fi * 0.191 + 9.2, 4.4));
            float h6 = Hash(float2(fi * 0.353 + 1.3, 17.7));

            // depth: 0 at the glass, 1 at the far ridge; more sand far back where the view is compressed
            float z = sqrt(h1);
            float near = 1.0 - z;
            float side = h3 < 0.5 ? -1.0 : 1.0;

            // every grain is blown out from the middle to its edge, the near ones faster (parallax)
            float speed = (0.012 + 0.040 * near) * (0.75 + 0.5 * h6);
            float s = frac(h2 + Phase * speed);
            float u = 0.5 + side * 0.5 * s;
            float band = Band(saturate(s * 0.92 + 0.04));

            float a = KickAge * 7.0;
            float burst = a * exp(1.0 - a);
            float kick = burst * saturate(0.20 + band * 1.5);
            // a kick swells the dunes (shape only: no flash of light)
            float swell = exp(-KickAge * 6.0) * saturate(0.25 + band * 1.6);

            // --- the dunes: a 3D field seen in perspective, grains resting on it
            float lit;
            float dune = Dune(u, z, lit);
            float arch = sin(z * 3.14159);
            // the dunes breathe with the music: quiet bands lie low, loud ones rise steep
            float hgt = saturate((0.26 + 0.85 * band + 0.30 * Bass + 0.35 * swell) * dune * (0.45 + 0.55 * arch));
            // a kick throws grains off the sand; they fall back onto it
            float hop = kick * saturate(0.30 + band * 1.5) * (0.25 + 0.75 * h5) * 0.95;
            float depthZ = lerp(0.95, 2.50, z);
            float persp = 1.0 / depthZ;
            float groundY = lerp(1.0, 0.30, pow(z, 0.88));
            float anchor = smoothstep(0.0, 0.22, z);
            float duneY = groundY - (hgt + hop) * 0.74 * persp * anchor - (h5 - 0.5) * 0.012;

            float y01 = duneY;
            float x01 = u;

            // colour runs out along the strip through cyan, cobalt and daemon crimson
            float hueT = s * 0.9 - Phase * 0.07 + z * 0.25;
            float3 tint = Palette(hueT);
            // on the dunes it is light and shade: lit faces carry the colour, lee sides sink to sapphire
            float3 duneCol = lerp(ShadowCol.rgb, tint, lit);
            duneCol = lerp(duneCol, PearlCol.rgb, smoothstep(0.80, 1.0, dune) * lit * 0.55);
            float3 col = duneCol;

            float life = smoothstep(0.0, 0.004, s) * smoothstep(1.0, 0.90, s);
            float duneE = (0.10 + 0.70 * lit * (0.3 + hgt) * (0.55 + 0.9 * band) + 0.3 * hop) * lerp(1.30, 0.55, z) * 1.5;
            float energy = duneE
                * (0.55 + 0.75 * h6) * life * (0.55 + 0.70 * Level);

            // focus: a plane drifting through the depth; a kick pulls it forward
            float focus = 0.22 + 0.36 * (0.5 + 0.5 * sin(Phase * 0.23)) - 0.14 * exp(-KickAge * 3.0);
            float coc = saturate((abs(z - focus) - 0.12) * 2.2);
            float baseR = lerp(1.9, 0.85, z);
            float blurR = coc * coc * 1.5;
            float r = baseR + blurR;
            energy *= (baseR * baseR) / (r * r);
            o.Color = col * energy;
            o.Blur = coc;

            float2 c = float2(x01 * Size.x, y01 * Size.y);
            float radiusPx = r * Dip;
            o.LocalUv = Corners[min(vid, 5u)];
            if (energy <= 0.0005 || c.y < -radiusPx || c.y > Size.y + radiusPx)
            {
                o.Pos = float4(2.0, 2.0, 2.0, 1.0);
                return o;
            }

            float2 p = c + o.LocalUv * radiusPx;
            o.Pos = float4((p.x / Size.x) * 2.0 - 1.0, 1.0 - (p.y / Size.y) * 2.0, 0.0, 1.0);
            return o;
        }
        """;

    private const string ParticlePs = """
        struct VSOut
        {
            float4 Pos : SV_Position;
            float2 LocalUv : TEXCOORD0;
            float3 Color : COLOR0;
            float Blur : TEXCOORD1;
        };

        float4 main(VSOut inp) : SV_Target
        {
            float r2 = dot(inp.LocalUv, inp.LocalUv);
            if (r2 > 1.0) discard;
            // in focus: a hard point; off it: a flat disc with a soft rim
            float sharp = pow(1.0 - r2, 1.85);
            float disc = saturate((1.0 - r2) * 2.2) * 0.85;
            float core = lerp(sharp, disc, inp.Blur);
            // coverage follows the light, so a dim disc never lays a dark ring over the desktop
            float lum = max(inp.Color.r, max(inp.Color.g, inp.Color.b));
            return float4(inp.Color * core, core * saturate(lum * 2.5) * 0.65);
        }
        """;

    private const string FullEdgeCompositePs = """
        cbuffer PointCompositeCb : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float CrownMode;
        };

        Texture2D HdrTex : register(t0);
        SamplerState Linear : register(s0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 sp = position.xy;
            float2 uv = sp / Size;
            // the cloud runs the whole strip, behind the dock too; only the screen's ends fade
            float edge = smoothstep(0.0, 0.01, uv.x) * smoothstep(1.0, 0.99, uv.x);

            float4 c0 = HdrTex.Load(int3((int2)sp, 0));
            float3 hdr = c0.rgb;
            float peak = max(hdr.r, max(hdr.g, hdr.b));

            float3 graded = saturate((hdr * (2.51 * hdr + 0.03)) / (hdr * (2.43 * hdr + 0.59) + 0.14));
            graded = saturate(graded + smoothstep(1.15, 3.6, peak) * float3(0.32, 0.24, 0.30));

            float topSoft = smoothstep(0.0, 0.12, uv.y);
            float colPeak = max(graded.r, max(graded.g, graded.b));
            float alpha = saturate(max(smoothstep(0.004, 0.14, colPeak), smoothstep(0.01, 0.25, c0.a) * 0.72)) * edge * topSoft * Fade;
            return float4(graded * alpha, alpha);
        }
        """;
}
