namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 11 — the light lances (4K take, 45.5–47.3 s, and the red capsule at 19.0–20.9 s): three thick horizontal lances
/// thrown from the screen edge toward the dock. Each has a flat, frayed cyan head, the brightest and widest part; its
/// body runs blue, violet and magenta with a crimson lip along its lower edge, and tapers to a red needle with a hot
/// pink line in it. The lowest is huge and half out of the strip, a lavender-pink haze full of frost-like specks.
/// Around them a purple-black ground with fine grain, and a dark scratched band at the screen edge.
/// Each lance's length is its band (top: treble, middle: mids, bottom: bass), a kick swells and brightens its head.
/// Now and then the lances burn out into the red capsule: a big soft red glow with a dark slot ringed in red light,
/// a hard red line through its end and a dimmer mirror image toward the dock.
/// </summary>
public sealed class BrLances : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .05f, .12f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.3f, .1f, 0f, 3f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.3f, .04f, .25f, .08f);
    public BrLances() : base(Body, .55f, Hex(0x9FC8FF), Hex(0xFF3A6A), Hex(0xFF2A24), Hex(0xF0F6FF), Hex(0x6A5CFF)) { }

    private const string Body = """
        // the colour down a lance, head to tail: cyan, blue, violet, magenta, crimson
        float3 LanceCol(float t)
        {
            // measured along the 4K take's lances: cyan for a moment, a long blue, then violet, magenta, crimson
            float3 c = lerp(float3(.04, .72, 1.1), float3(.14, .36, 1.15), smoothstep(.03, .16, t));
            c = lerp(c, float3(.5, .3, 1.1), smoothstep(.38, .58, t));
            c = lerp(c, float3(.95, .2, .85), smoothstep(.58, .8, t));
            return lerp(c, float3(1.0, .08, .38), smoothstep(.82, 1.0, t));
        }

        float3 Lance(float2 p, float3 w, float cy, float th, float e, float id, float fat, float tk)
        {
            float D = Dip / 1.5;
            float k = Kick();
            float L = WingPx(w.y);
            float ex = L - w.z;                                   // pixels from the screen edge
            float h0 = 16.0 * D;                                  // the head stands a little in from the edge
            float len = (.16 + .55 * e + .1 * k) * L * (.9 + .2 * Hash(float2(id, 3)));
            // the head is flat and frayed: every row starts a few pixels off
            float fray = (Hash(float2(floor(p.y / D) + id * 31.0, tk)) - .5) * 5.0 * D + (Noise(float2(p.y / (4.0 * D), id + tk * .1)) - .5) * 6.0 * D;
            float hx = ex - h0 - fray;
            float t = hx / max(len, 1.0);                         // 0 at the head, 1 at the tail tip
            if (t > 1.05 || hx < -12.0 * D) return 0;
            // the lance sways gently along its length while the music plays
            float cyw = cy + (Noise(float2(hx / (180.0 * D) + id * 5.0, Clock * .25)) - .5) * th * .5;
            float dy = p.y - cyw, ady = abs(dy);
            // thickness: the head widest, a slight neck, a long body, then the taper to a needle
            float prof = lerp(1.0, .85, smoothstep(0, .15, t)) * lerp(1.0, .12, pow(smoothstep(.4, 1.0, t), .8));
            float r = th * D * prof * (1 + .18 * k * (1 - smoothstep(0, .3, t))) * (1 + .4 * fat * (1 - t));
            float start = smoothstep(-6.0 * D, 4.0 * D, hx);
            float end = 1 - smoothstep(.92, 1.05, t);
            // soft but full: a plateau with a soft fall-off, not a thin glowing line
            float dens = smoothstep(r * 1.2, r * .4, ady) * .75 + exp(-pow(ady / max(r, .5), 2) * 1.6) * .3;
            float3 col = LanceCol(saturate(t));
            // a crimson lip along the lower edge, stronger down the body
            float lip = exp(-pow((dy - r * .8) / max(r * .28, .8), 2)) * smoothstep(.0, .3, t);
            col = lerp(col, float3(1.0, .06, .2), lip * .85);
            // the head runs whiter at its core
            float headC = exp(-pow(hx / (r * 2.2 + 1), 2)) * exp(-pow(ady / max(r * .6, .5), 2));
            // toward the tail a hot pink line runs down the middle
            float tailC = exp(-pow(ady / max(r * .18, .45), 2)) * smoothstep(.45, .85, t) * end;
            // frost: fine specks of light all over the body, thickest in the fat lance
            float g = Hash(float2(floor(p.x / D) + id * 17.0, floor(p.y / D) + tk * 13.0));
            float frost = step(.98 - .03 * fat, g) * (.4 + .6 * fat);
            float tex = .6 + .5 * Noise(float2(hx / (7.0 * D), p.y / (2.0 * D) + id)) + frost;
            float lum = lerp(.8, .45, saturate(t)) * (.07 + .8 * e + .35 * k);
            float3 c = col * dens * tex * lum * start * end
                     + float3(.1, .6, .9) * headC * start * (.03 + .3 * e + .35 * k)
                     + float3(1.0, .35, .8) * tailC * (.05 + .6 * e);
            // the fat lance spills a lavender-pink haze below itself, and a violet mist hangs around every lance
            float haze = exp(-max(ady - r, 0) / ((7.0 + 14.0 * fat) * D)) * start * (1 - smoothstep(.3, 1.0, t));
            c += lerp(float3(.2, .3, 1.0), float3(.7, .25, .85), smoothstep(0, .35, t)) * haze * (.04 + .22 * fat) * (.1 + e);
            return c;
        }

        // the red capsule: a soft red glow round a dark slot ringed in red light, a hard red line at its end
        float3 Capsule(float2 p, float3 w, float tk)
        {
            float D = Dip / 1.5, H = Size.y;
            float L = WingPx(w.y);
            float ex = L - w.z;
            float k = Kick();
            float cx = .3 * L, cy = H * .5;
            float3 c = 0;
            // the slot, and its dimmer, blurrier mirror image further toward the dock: all of it out of focus
            [unroll] for (int m = 0; m < 2; m++)
            {
                float mx = m == 0 ? cx : cx + 250.0 * D;
                float a = m == 0 ? 1.0 : .35;
                float soft = m == 0 ? 1.0 : 1.8;
                float2 q = float2(ex - mx, p.y - cy);
                q.y += (Noise(float2(q.x / (60.0 * D), tk * .04 + m)) - .5) * 4.0 * D;
                float2 hs = float2(105.0 * D, 12.0 * D);
                float2 dq = abs(q) - hs;
                float sd = length(max(dq, 0)) + min(max(dq.x, dq.y), 0) - 5.0 * D;
                float halo = (exp(-pow(length(q / (float2(150.0, 46.0) * D * soft)), 2) * 1.4) + .5 * exp(-max(sd, 0) / (10.0 * D * soft))) * smoothstep(-12.0 * D, 5.0 * D * soft, sd);
                float ring = exp(-pow(sd / (4.5 * D * soft), 2)) * (m == 0 ? 1.0 : .4);
                float wob = .75 + .5 * Noise(float2(q.x / (30.0 * D), q.y / (14.0 * D) + tk * .05));
                float3 hc = lerp(float3(1.0, .07, .04), float3(.45, .1, .6), smoothstep(1.0, 1.8, length(q / (float2(150.0, 46.0) * D * soft))));
                c += a * (hc * halo * .45 * wob + float3(1.0, .12, .07) * ring * .55) * (.1 + .7 * Bass + .5 * k);
            }
            // the hard line through the slot's end, full height, flaring on a kick
            float lx = ex - (cx + 112.0 * D);
            c += float3(1.0, .1, .07) * exp(-pow(lx / (1.4 * D), 2)) * (.15 + .6 * Mid + 1.0 * k)
               + float3(.6, .3, 1.0) * exp(-pow((lx - 3.0 * D) / (1.2 * D), 2)) * .15;
            return c;
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float tk = floor(Clock * 12.0);
            float L = WingPx(w.y);
            float ex = L - w.z;
            // now and then (every ~26 s of music, for ~4 s) the lances burn out into the red capsule
            float ph = frac(Clock / 26.0 + .3);
            float cap = smoothstep(.8, .83, ph) * (1 - smoothstep(.95, .98, ph));

            float3 c = 0;
            if (cap < .999)
                c = (Lance(p, w, H * .2, 8.0, saturate(Treble * .7 + BandW(.8) * .7), 1, 0, tk)
                   + Lance(p, w, H * .52, 9.0, saturate(Mid * .7 + BandW(.45) * .7), 2, 0, tk)
                   + Lance(p, w, H * .92, 15.0, saturate(Bass * .7 + BandW(.1) * .6), 3, 1, tk)) * (1 - cap);
            if (cap > .001) c += Capsule(p, w, tk) * cap;

            // the ground: purple-black with fine grain
            c += float3(.004, .002, .008) * (.6 + .8 * Hash(float2(floor(p.x / D), floor(p.y / D) + tk)));
            // the dark scratched band at the screen edge: teal and blue streaks flickering row by row
            float band = smoothstep(14.0 * D, 4.0 * D, ex);
            float scr = step(.6, Hash(float2(floor(p.y / (2.0 * D)), tk))) * (.4 + .6 * Hash(float2(floor(p.y / D), tk + 7)));
            c = c * (1 - band * .85) + float3(.02, .12, .16) * band * scr * (.15 + .8 * Level);
            return c;
        }
        """;
}
