using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// A slab of liquid glass hanging in the dark, a neon street burning behind it: the strip is a thick, rounded bar of
/// glass across the middle of the frame (about 70% of its height), and the picture is what the neon becomes on the
/// way through. The slab bends its own background — a broad warp through the body, a hard one at the rounded edges —
/// and splits that bend into red, green and blue, because each wavelength gets its own index of refraction. Inside
/// there is a cool blue-green absorption, a banded specular sheen on the top face, three crisp bubbles and a couple
/// of scratches; outside there are bokeh lights behind and a wet mirror under the bar.
/// <para>
/// The spectrum works the material: the low end thickens the glass (wider warp, stronger absorption, more frost), the
/// kick is a light wave that runs along the surface, the snare opens a fresh bubble and cracks the bevel, the hats
/// leave a fine sparkle on the face. The clock that drives the wave and the grain is fed by the level alone, so
/// silence freezes every phase and the slab simply hangs there, still and lit.
/// </para>
/// <para>
/// The four traps it does not fall into: not a still illustration (warp, frost and wave are the music and they stop
/// dead with it); not blind to the dock gap (the slab, its bevel, the bokeh and the mirror run the whole width, and
/// the bubbles sit outside the middle where the dock covers them); not unmeasured (mass = thickness and bend, kick =
/// the surface wave, snare = a bubble and a crack, hat = sparkle, band = how hard the neon behind burns); not
/// decorated (glass, neon, bubbles, scratches, wet ground: the material and nothing else).
/// </para>
/// <para>
/// Port note: the shader's y runs down from the top row, so <c>main</c> converts twice — <c>g.y = 1 - uv.y</c> for the
/// design space and <c>px.y = Size.y - position.y</c> for the pixel-sized bend, the bevel and the grain. If the
/// shipping target hands over a y-up position, delete both flips; nothing else assumes a direction. The chromatic
/// aberration lives in the show pass, which resamples the field three times, so the field stays a texture in the port.
/// </para>
/// </summary>
[SceneDoc("Liquid glass: a thick slab of neon-lit glass with real dispersion, bokeh behind and a wet mirror under")]
public sealed class LiquidGlass : FeedbackGpuScene
{
    private const int ConstantFloats = 68; // a multiple of 4: the constant buffer is 16-byte aligned

    private float _mass;   // the low end, eased: how thick the glass is
    private float _wave;   // the kick, dying away: the light wave running along the surface
    private float _spark;  // the hats, eased: surface sparkle
    private float _flow;   // the level, eased: how much neon is behind the slab
    private float _clock;  // the surface clock: only ever advanced by the level
    private float _crack;  // on a snare: the fresh bubble and the crack at the bevel, fading out
    private float _seam;   // where the last snare left its seam

    public LiquidGlass() : base(ConstantFloats, Shared + FieldPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        var audio = frame.Audio;
        _mass += (Math.Clamp(audio.Bass, 0, 1) - _mass) * (1 - MathF.Exp(-frame.Dt * 8f));
        _flow += (Math.Clamp(audio.Level, 0, 1) - _flow) * (1 - MathF.Exp(-frame.Dt * 7f));
        _spark += (Math.Clamp(audio.Hat, 0, 1) - _spark) * (1 - MathF.Exp(-frame.Dt * 14f));

        // the kick is a wave with a tail: it runs along the surface for about ten frames, then dies
        _wave = Math.Max(_wave * MathF.Exp(-frame.Dt * 8f), audio.Kick);

        // a snare opens a new bubble and cracks the bevel; the crack cools off over about half a second
        if (audio.Snare > 0.45f)
        {
            _crack = Math.Max(_crack, audio.Snare);
            _seam += frame.Dt * 0.6f;
        }
        _crack *= MathF.Exp(-frame.Dt * 5f);

        // the surface wave and the grain run off this clock, fed by the level alone: silence freezes them
        _clock += frame.Dt * 2.0f * Math.Clamp(audio.Level, 0, 1);

        if (_mass < 0.001f) _mass = 0;
        if (_wave < 0.001f) _wave = 0;
        if (_spark < 0.001f) _spark = 0;
        if (_flow < 0.001f) _flow = 0;
        if (_crack < 0.001f) _crack = 0;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = frame.Audio.Level;
        d[6] = _mass;
        d[7] = frame.Audio.Treble;
        d[8] = _wave;
        d[9] = _crack;
        d[10] = _spark;
        d[11] = frame.Audio.Mid;
        PutBands(12, frame);                                  // 24 bands: 12..35
        Put(36, Lead(Color.FromRgb(0xFF, 0xD4, 0x00)));       // amber neon
        Put(40, Color.FromRgb(0x00, 0xE5, 0xFF));             // cyan neon
        Put(44, Color.FromRgb(0xFF, 0x2E, 0x88));             // magenta neon
        Put(48, Color.FromRgb(0xFF, 0x2E, 0x40));             // red: a warning tube
        Put(52, Color.FromRgb(0x0A, 0x14, 0x1C));             // the glass body, teal
        Put(56, Color.FromRgb(0x05, 0x08, 0x12));             // the dark behind the glass
        Put(60, Color.FromRgb(0xFF, 0x8A, 0x36));             // the sodium wash
        d[64] = _clock;
        d[65] = _seam;
        d[66] = _flow;
        d[67] = 0;
    }

    private const string Shared = """
        cbuffer Glass : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Mass;
            float Treble;
            float Wave;
            float Crack;
            float Spark;
            float Mid;
            float4 Bands[6];
            float4 Core;
            float4 Cyan;
            float4 Magic;
            float4 Alert;
            float4 Body;
            float4 Night;
            float4 Warm;
            float Clock;
            float Seam;
            float Flow;
            float Pad2;
        };

        """ + ShaderHelpers;

    private const string FieldPs = """

        // =============================================================================================
        // The slab: a rounded bar of glass across the middle of the strip. Its body warps the neon behind it,
        // its edges warp it hard, and the three channels are bent by different amounts so the light splits
        // into colour. Everything is linear light until the ACES curve at the very end.
        // =============================================================================================
        static const float Y0 = 0.15;   // the bottom of the slab
        static const float CY = 0.50;   // its centre line
        static const float HH = 0.35;   // its half height
        static const float RAD = 0.075; // the rounding, in strip heights

        float3 Aces(float3 x)
        {
            x = max(x, 0);
            return saturate((x * ((2.51 * x) + 0.03)) / ((x * ((2.43 * x) + 0.59)) + 0.14));
        }

        // the signed distance to the slab's rounded box, in strip heights
        float SlabDistance(float2 g)
        {
            float2 p = float2((g.x - 0.5) * (Size.x / Size.y), g.y - CY);
            float2 half = float2(((Size.x / Size.y) * 0.5) - RAD, HH - RAD);
            float2 d = abs(p) - half;
            return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - RAD;
        }

        // the neon street behind the slab: tubes, bokeh lights and a dim LED wall
        float3 Neon(float2 q, float frost)
        {
            float3 col = Night.rgb * 0.55;

            // the air: a cold haze high up, a warm pollution band low down
            col += lerp(float3(0.004, 0.006, 0.014), float3(0.010, 0.008, 0.020), smoothstep(0.2, 0.9, q.y));
            col += Magic.rgb * 0.020 * exp(-pow((q.y - 0.58) / 0.20, 2.0));

            // vertical neon tubes: the signs the glass has to bend
            for (int i = 0; i < 2; i++)
            {
                float si = (float)i;
                float x = 0.13 + (0.74 * Hash(float2(si, 3.3)));
                float w = 0.0009 + (0.0011 * Hash(float2(si, 7.1)));
                float lean = 0.035 * (Hash(float2(si, 11.7)) - 0.5);
                float3 tube = lerp(lerp(Magic.rgb, Core.rgb, step(0.4, Hash(float2(si, 13.1)))), Cyan.rgb,
                                   step(0.75, Hash(float2(si, 17.9))));
                float energy = (1.6 + (1.9 * Band(saturate((si + 0.5) / 3.0)))) * (0.6 + (0.9 * Flow));
                float across = 1.0 - saturate(abs(q.x - (x + ((q.y - 0.5) * lean))) / (w + frost));
                col += tube * energy * across * across;
                col += tube * energy * 0.14 * exp(-abs(q.x - (x + ((q.y - 0.5) * lean))) * 90.0);
            }

            // bokeh: the street's own lights, far behind, soft discs that the refraction can split
            for (int j = 0; j < 4; j++)
            {
                float sj = (float)j;
                float2 c = float2(Hash(float2(sj, 21.1)), 0.10 + (0.80 * Hash(float2(sj, 27.7))));
                float r = (0.018 + (0.034 * Hash(float2(sj, 31.3)))) * (1.0 + (0.6 * frost));
                float2 dp = float2((q.x - c.x) * (Size.x / Size.y), q.y - c.y) * 0.10;
                float disc = saturate(1.0 - (length(dp) / r));
                float3 tint = lerp(lerp(Magic.rgb, Core.rgb, step(0.45, Hash(float2(sj, 37.1)))), Cyan.rgb,
                                   step(0.76, Hash(float2(sj, 41.9))));
                col += tint * disc * disc * (0.12 + (0.20 * Flow)) * (1.0 - (0.35 * frost));
            }

            // an LED wall far behind, dim: something for the warp to drag
            float2 cellQ = q * float2(300.0, 17.0);
            float2 cel = floor(cellQ);
            float2 cfr = frac(cellQ) - 0.5;
            float dots = (1.0 - saturate(length(cfr) / 0.42)) * step(0.60, Hash(cel + 5.0));
            col += lerp(Cyan.rgb * 0.35, Warm.rgb * 0.30, step(0.58, Hash(cel + 9.0))) * dots * dots * 0.10 * (1.0 - frost);

            return col;
        }

        // the warp: a broad bend through the body, a hard one at the faces, plus the bubbles' own lenses
        float2 Bend(float2 g, float ior)
        {
            float2 p = float2((g.x - 0.5) * (Size.x / Size.y), g.y - CY);
            float wave = sin((g.x * 4.5) + (Clock * 1.6)) * (0.5 + (1.4 * Mass));
            float rim = exp(-abs(p.y) / 0.10);                       // the slab's own curvature at its faces

            float bendY = (sin((p.y * 7.0) + wave) * (1.4 + (3.2 * Mass))) + (wave * 0.8 * rim);
            float bendX = sin((p.y * 3.0) - (Clock * 0.9)) * (0.5 + (1.3 * Mass));
            float2 push = float2(bendX, bendY) * (1.0 + (2.6 * rim));

            // bubbles bend harder than the body does
            for (int i = 0; i < 2; i++)
            {
                float bi = (float)i;
                float2 c = float2(0.10 + (0.80 * Hash(float2(bi, 51.3))), 0.24 + (0.52 * Hash(float2(bi, 57.7))));
                float r = 0.055 + (0.075 * Hash(float2(bi, 63.1)));
                float2 dp = float2((g.x - c.x) * (Size.x / Size.y), g.y - c.y);
                float d = length(dp);
                push += (dp / max(d, 1e-4)) * (1.0 - saturate(d / r)) * (4.5 + (5.0 * Mass));
            }

            // the snare's fresh bubble
            float2 q = float2(0.08 + (0.84 * Hash(float2(Seam, 5.5))), 0.26 + (0.46 * Hash(float2(Seam, 9.9))));
            float2 dp2 = float2((g.x - q.x) * (Size.x / Size.y), g.y - q.y);
            float d2 = length(dp2);
            push += (dp2 / max(d2, 1e-4)) * (1.0 - saturate(d2 / 0.09)) * (9.0 * Crack);

            return push * ior;
        }

        float3 Film(float3 light, float2 g)
        {
            float3 c = pow(Aces(light * 1.10), 1.0 / 2.2);
            float l = dot(c, float3(0.2126, 0.7152, 0.0722));
            c = lerp(c * float3(0.76, 0.97, 1.15), c * float3(1.09, 1.01, 0.90), smoothstep(0.08, 0.55, l));
            float2 d = (g - 0.5) * float2(0.85, 2.0);
            c *= 1.0 - (0.38 * dot(d, d));
            float grain = Hash((g * Size) + (Clock * 61.3)) - 0.5;
            c += grain * 0.022 * (1.0 - (0.5 * l));
            return saturate(c);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;                        // y runs down from the top row
            float2 g = float2(uv.x, 1.0 - uv.y);                   // the design space: y up from the bottom
            float2 px = float2(position.x, Size.y - position.y);   // pixels, y up
            float fx = 1.0 / Size.x;
            float fy = 1.0 / Size.y;

            float sd = SlabDistance(g) * Size.y;                    // in pixels, negative inside
            float inside = 1.0 - smoothstep(-0.9, 0.9, sd);

            // --- the three channels see three different slabs: dispersion, visible wherever the neon bends
            float2 pG = Bend(g, 1.00);
            float2 pR = Bend(g, 1.26);
            float2 pB = Bend(g, 0.74);
            float2 qG = g - (pG * float2(fx, fy));
            float2 qR = g - (pR * float2(fx, fy));
            float2 qB = g - (pB * float2(fx, fy));

            // frost: read through the thickness of the glass, so the lights behind are wider and softer
            float frost = (0.20 + (0.55 * Mass)) * inside;
            float3 behind = float3(Neon(qR, frost).r, Neon(qG, frost).g, Neon(qB, frost).b);

            // --- the world outside the slab: the same street, colder and dimmer, with a wet mirror underneath
            float3 air = behind * 0.30;
            if (g.y < Y0)
            {
                float mirrorY = (2.0 * Y0) - g.y;
                float2 qm = float2(g.x + (0.002 * sin((g.y * 90.0) + Clock)), mirrorY);
                float3 mirror = Neon(qm, 0.25) * float3(1.10, 0.85, 1.05) * 0.60;
                air = lerp(mirror * 0.55, air, smoothstep(0.0, Y0 * 0.35, g.y));
                air += Magic.rgb * 0.05 * exp(-abs(g.y - Y0) * 60.0) * (0.6 + (0.6 * Flow));
            }

            // --- the slab itself: cool absorption, a bevel that catches the light, a specular band on top
            float2 pp = float2((g.x - 0.5) * (Size.x / Size.y), g.y - CY);
            float lens = saturate(1.0 - (abs(pp.y) / HH));                       // thickest on the centre line
            float3 absorb = exp(-float3(0.55, 0.22, 0.30) * (0.8 + (2.2 * Mass)) * (0.40 + (0.90 * lens)));
            float3 glass = behind * absorb * (0.85 + (0.35 * lens));

            // the body of the glass is a little lit from inside, teal, and never fully black
            glass += Body.rgb * (0.10 + (0.20 * lens)) * (0.5 + (0.5 * Flow));
            glass += Core.rgb * 0.045 * lens * Wave;

            // the bevel: a bright 1.5 px lip just inside the edge, with its own colour split
            float bevel = 1.0 - saturate(abs(sd + 1.4) / 1.9);
            glass += (float3(0.85, 0.95, 1.0) * 1.5 + Core.rgb * 0.35) * bevel * bevel * (1.1 + (0.7 * Flow));
            // and a dark contact line just outside it, so the silhouette of the slab reads on any wallpaper
            float contact = 1.0 - saturate(abs(sd - 1.8) / 2.0);

            // the specular sheen lying along the top face of the slab
            float spec = exp(-pow((pp.y - (0.20 + (0.03 * sin((g.x * 2.4) + (Clock * 0.5))))) / 0.055, 2.0));
            glass += float3(0.55, 0.70, 0.85) * spec * (0.18 + (0.30 * Flow)) * inside;

            // --- bubbles: crisp rims, a bright catch light, and a little lift in the middle
            float bubbles = 0;
            float catchLight = 0;
            for (int i = 0; i < 3; i++)
            {
                float bi = (float)i;
                float2 c = float2(0.06 + (0.88 * Hash(float2(bi, 51.3))), 0.24 + (0.52 * Hash(float2(bi, 57.7))));
                float r = 0.055 + (0.075 * Hash(float2(bi, 63.1)));
                float2 dp = float2((g.x - c.x) * (Size.x / Size.y), g.y - c.y);
                float d = length(dp) / r;
                bubbles += (1.0 - saturate(abs(d - 0.90) / 0.10)) * (0.55 + (0.45 * sin((bi * 2.1) + Clock)));
                catchLight += pow(saturate(1.0 - (length(dp - (float2(-0.018, 0.020) * r)) / (r * 0.30))), 3.0) * 1.6;
            }
            float2 qc = float2(0.08 + (0.84 * Hash(float2(Seam, 5.5))), 0.26 + (0.46 * Hash(float2(Seam, 9.9))));
            float2 dpc = float2((g.x - qc.x) * (Size.x / Size.y), g.y - qc.y);
            bubbles += (1.0 - saturate(abs((length(dpc) / 0.09) - 0.90) / 0.12)) * Crack;
            catchLight += pow(saturate(1.0 - (length(dpc) / 0.030)), 2.0) * 2.2 * Crack;
            glass += (Cyan.rgb * 0.45 + float3(0.45, 0.50, 0.55)) * bubbles * 0.9 * inside;
            glass += float3(1.0, 1.0, 1.0) * catchLight * inside;

            // --- scratches on the face: two thin bright lines, and the hats' sparkle
            float2 sp = float2(g.x * (Size.x / Size.y), g.y);
            float scratch = 0;
            for (int k = 0; k < 2; k++)
            {
                float sk = (float)k;
                float a = (0.26 + (0.22 * Hash(float2(sk, 71.3)))) * 3.14159;
                float2 dir = float2(cos(a), sin(a));
                float along = dot(sp, dir) - (Hash(float2(sk, 77.7)) * 9.0);
                float across = abs(dot(sp, float2(-dir.y, dir.x)));
                scratch += (1.0 - saturate(across / 0.012)) * (1.0 - smoothstep(0.4, 1.1, abs(along)));
            }
            glass += float3(0.55, 0.60, 0.68) * saturate(scratch) * 0.30 * inside;
            float sparkle = step(0.9975 - (0.0015 * Spark), Hash(floor(px / float2(3.0, 2.0)) + 17.0)) * Spark;
            glass += (Cyan.rgb * 0.6 + float3(0.4, 0.4, 0.4)) * sparkle * inside;

            // --- composite: the air outside, the slab inside, the contact shadow, the film
            float3 light = lerp(air, glass, inside);
            light *= 1.0 - (contact * 0.45 * (1.0 - inside));
            float3 graded = Film(light, g);
            float a = 0.985 * Fade;
            float edge = saturate(g.y / 0.05) * saturate((1.0 - g.y) / 0.05);
            a *= 0.35 + (0.65 * edge);
            return float4(graded * a, a);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // the picture is the field; here it only gets its lens: a little chromatic aberration towards the
            // edges, growing with the distance from the middle of the strip
            float2 uv = position.xy / Size;
            float2 bend = (uv - 0.5) * float2(0.0009, 0.0032);
            float r = Last.SampleLevel(Linear, uv + bend, 0).r;
            float4 m = Last.SampleLevel(Linear, uv, 0);
            float b = Last.SampleLevel(Linear, uv - bend, 0).b;
            return float4(r, m.g, b, m.a);
        }
        """;
}
