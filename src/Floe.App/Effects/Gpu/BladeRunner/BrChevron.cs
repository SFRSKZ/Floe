namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 13 — the chevrons (4K take, 47.5–53.3 s): a dark cavity runs along the middle of the strip, mirrored up and down,
/// and the light lies round it in soft wide glowing bands. Along the strip the cavity opens out from a sharp arrow tip
/// pointing toward the screen edge, again and again: in stairs (green, with a yellow-olive lip along each step), as one
/// smooth V (red-orange, yellow at the lip), or in round waist-like lobes (red, with pink and magenta further out, on a
/// faint dark green). Measured on the take: lip 102 113 49, green 26 65 26, red 180 48 28, deep red 144 22 13, magenta
/// 90 18 46. Everything is soft: the film blurs the light, nothing is a crisp line.
/// Every step, V or lobe is a band: its lip burns brighter and its light spreads further as the band rises, and the
/// cavity closes in a little on it; a kick flashes the lips. The shapes breathe and the lobes roll slowly while the
/// music plays; in silence only the lips glow faintly.
/// </summary>
public sealed class BrChevron : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .05f, .1f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, 0f, 0f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.45f, .06f, .3f, .06f);
    public BrChevron() : base(Body, 0, Hex(0x3CE05A), Hex(0xF0E070), Hex(0xFF5A2A), Hex(0x2A6CFF), Hex(0x061830)) { }

    private const string Body = """
        // the light's colour at distance t out from the lip (0 the lip, 1 far out), for motif m: 0 stairs, 1 V, 2 lobes
        float3 Pal(float t, float m)
        {
            float3 g = lerp(float3(.55, .62, .1), float3(.1, .5, .06), smoothstep(.05, .35, t));
            g = lerp(g, float3(.01, .14, .02), smoothstep(.4, 1.0, t));
            float3 v = lerp(float3(1.0, .6, .1), float3(1.0, .2, .04), smoothstep(.04, .3, t));
            v = lerp(v, float3(.75, .04, .02), smoothstep(.3, .7, t));
            v = lerp(v, float3(.15, .01, .02), smoothstep(.7, 1.0, t));
            float3 r = lerp(float3(1.0, .25, .2), float3(.85, .05, .03), smoothstep(.04, .3, t));
            r = lerp(r, float3(.45, .02, .22), smoothstep(.35, .8, t));
            r = lerp(r, float3(.08, .005, .06), smoothstep(.8, 1.0, t));
            return m < .5 ? g : (m < 1.5 ? v : r);
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 800 : 0;
            float k = Kick();
            float L = WingPx(w.y);
            float s = w.z / D;
            float hh = H / D * .5;
            float ay = abs(p.y / D - hh);                                  // mirrored about the middle line
            // the take leans now toward the green stairs, now the red V, now the lobes
            float lean = Noise(float2(Clock / 30.0, side + 4.0));

            // one cavity to a cell, pointed at both ends: it opens from one tip in one motif and closes to the
            // other tip in another (green stairs into a red V, say), the tips joined by a thin line of light
            float P = 520.0;
            float sw = s + 40.0 * Noise(float2(s / 500.0, side + 1.0));
            float ci = floor(sw / P);
            float id = ci + side;
            float mA = floor(3.0 * frac(Hash(float2(id, 1)) + lean * 1.3));
            float mB = floor(3.0 * frac(Hash(float2(id, 11)) + lean * 1.3));
            float x0 = P * (.04 + .08 * Hash(float2(id, 4)));
            float x1 = P * (.88 + .08 * Hash(float2(id, 3)));
            float xl = sw - ci * P + 6.0 * sin(Clock * .3 + id);           // the cavity sways a little
            float xc = lerp(x0, x1, .5);
            xl = xc + (xl - xc) * (1.0 - .14 * Bass - .06 * k);          // and stretches out on the bass, like a lens pulling
            float t = (xl - x0) / (x1 - x0);
            float inCell = step(0, t) * step(t, 1);
            t = saturate(t);
            float pk = .4 + .25 * Hash(float2(id, 5));                     // where it is widest
            float hmax = hh * (.6 + .2 * Hash(float2(id, 2))) * (1 + .08 * sin(Clock * .4 + id * 1.7));
            float rise = t < pk ? 1 : 0;
            float u = rise > 0 ? t / pk : (1 - t) / (1 - pk);             // 0 at a tip, 1 at the widest
            float m = rise > 0 ? mA : mB;

            // the cavity's half-height: stairs, a straight V or round lobes from the tip to the widest point
            float hC;
            float seg;                                                     // which step or lobe: each is a band of its own
            if (m < .5)
            {
                float n = 3.0, i = floor(u * n * .999), f = frac(u * n * .999);
                float st = (i + smoothstep(.65, 1.0, f)) / n;
                hC = hmax * lerp(.18 + .82 * st, 1, step(.999, u)) * smoothstep(0, .07, u);
                seg = i;
            }
            else if (m < 1.5)
            {
                hC = hmax * pow(u, .85);
                seg = 1;
            }
            else
            {
                hC = hmax * lerp(.62 + .33 * sin(u * 7.5 - 1.6 + Clock * .5 + id), 1, smoothstep(.75, 1.0, u)) * smoothstep(0, .2, u);
                seg = floor(u * 2.0);
            }
            hC *= inCell;
            float useg = rise > 0 ? (seg + .5) / 3.0 * pk : 1 - (seg + .5) / 3.0 * (1 - pk);
            float us = saturate((ci * P + x0 + (x1 - x0) * useg) * D / max(L, 1));
            float b = saturate(BandW(us) * 1.35);
            hC *= 1.06 - .2 * b - .06 * k;                                // the light closes in on a loud band

            // the light: a soft bright lip on the cavity's edge and a broad glow out from it
            float d = ay - hC;                                             // >0 out in the light
            float reach = 7.0 + 26.0 * b + 8.0 * k;
            float tl = saturate(d / max(reach * 1.6, 1));
            float lip = exp(-pow(d / 11.0, 2)) * smoothstep(-12.0, 0, d) * step(.5, hC);
            float glow = exp(-max(d, 0) / reach) * smoothstep(-13.0, 4.0, d);
            float mix = smoothstep(pk - .05, pk + .05, t);                 // the two motifs meet softly at the widest point
            float3 col = lerp(Pal(tl, mA), Pal(tl, mB), mix);
            float outside = 1 - inCell;                                   // the line between tips: one shared amber, no seam
            col = lerp(col, lerp(float3(.7, .4, .06), float3(.3, .05, .02), smoothstep(.1, .6, tl)), outside);
            float lvl = .02 + .85 * b + .3 * k;
            float3 c = col * glow * lvl * .75 + lerp(lerp(Pal(0, mA), Pal(0, mB), mix), float3(1.0, .6, .1), outside) * lip * (.025 + .7 * b + .7 * k) * .55;
            // the step risers: a brighter curl where each step turns
            if (m < .5)
            {
                float f = frac(u * 3.0 * .999);
                c += Pal(.1, 0) * exp(-pow((f - .85) / .08, 2)) * exp(-pow(d / 10.0, 2)) * .3 * lvl;
            }
            // far out a faint field (in the lobes take a dark green ground)
            c += lerp(float3(.006, .02, .008), float3(.002, .03, .0), smoothstep(.55, .8, lean)) * smoothstep(0, hh * .5, d) * (.3 + Level);
            // the cavity itself: near black with a faint blue haze
            c += float3(.003, .006, .015) * smoothstep(0, -10.0, d) * (.3 + Level);
            return c;
        }
        """;
}
