namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 07 — tall glass panels etched like a silicon die: dense crisp flecks of pale dust and faint stains, over a blue → violet → red wash; ragged dark gaps between panels carry a chain of dots; the panel feet
/// dissolve into loose dots. Bands light more of the lattice, a kick brightens the panels, a snare makes dots spark.
/// </summary>
public sealed class BrFrostPanels : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (.4f, .2f, .25f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, .2f, .2f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.22f, .05f, .8f, .05f);

    public BrFrostPanels() : base(Body, 0, Hex(0x1630C8), Hex(0x3AA0FF), Hex(0xEAF4FF), Hex(0xC81A12), Hex(0x7A3AD8)) { }

    private const string Body = """
        // a 3x5 pixel font for the die's code labels: 0-9, A-F, a dash (rows top to bottom, 3 bits each)
        static const float Font[17] = { 31599, 11415, 29671, 29647, 23497, 31183, 31215, 29257, 31727, 31695,
                                        11245, 27566, 14627, 27502, 31143, 31140, 448 };
        float Glyph(float code, float2 g)
        {
            if (g.x < 0 || g.x >= 3 || g.y < 0 || g.y >= 5) return 0;
            float idx = (4 - floor(g.y)) * 3 + (2 - floor(g.x));
            return fmod(floor(code / exp2(idx)), 2.0);
        }

        // frost needles: short crisp 1 px needles in three families 60 degrees apart, like ice on a window
        float Needles(float2 q, float seed)
        {
            float best = 0;
            [unroll] for (int j = 0; j < 3; j++)
            {
                float a = j * 1.0472 + .25;
                float2 r = float2(cos(a) * q.x + sin(a) * q.y, -sin(a) * q.x + cos(a) * q.y);
                float2 cs = float2(11.0, 4.0);
                float2 ci = floor(r / cs);
                float2 cf = r - ci * cs;
                float len = cs.x * (.35 + .6 * Hash(ci + seed + j * 7.0 + 3.0));
                float x0 = (cs.x - len) * Hash(ci + seed + j * 5.0 + 9.0);
                float yc = cs.y * (.3 + .4 * Hash(ci + seed + j * 3.0 + 1.0));
                float on = step(.45, Hash(ci + seed + j * 17.0)) * step(x0, cf.x) * step(cf.x, x0 + len);
                best = max(best, smoothstep(.75, .25, abs(cf.y - yc)) * on);
            }
            return best;
        }

        float3 Scene(float2 p)
        {
            // the tape tears now and then: a band of rows jumps sideways for a few frames
            float tb = floor(p.y / (9.0 * Dip)), tt = floor(Clock * 7.0);
            p.x += smoothstep(.02, .12, Level) * step(.9, Hash(float2(tb, tt))) * (Hash(float2(tb + 3.0, tt)) - .5) * 16.0 * Dip;
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float H = Size.y;
            float side = w.y > 0 ? 300 : 0;
            float k = Kick(), sn = Snr();
            float s = w.z;
            float yn = p.y / H;
            float e = saturate(BandW(w.x) * 1.3 + .15 * Level);

            // free shapes across the whole wing: wide and narrow, swelling and tapering down their height, no two alike
            // and the wings not mirrored; louder bands grow them
            float n = Noise(float2(s / (95.0 * Dip), side + 1.3 + Clock * .06)) * .55
                    + Noise(float2(s / (30.0 * Dip), p.y / (90.0 * Dip) + side + 7.1 + Clock * .14)) * .33
                    + Noise(float2(s / (6.0 * Dip), p.y / (14.0 * Dip) + side)) * .12;
            float thr = .58 - .12 * e - .06 * k + .12 * smoothstep(.55, 1.0, yn) * Noise(float2(s / (20.0 * Dip), side + 9.0));
            float sh = n - thr;
            float inPanel = smoothstep(.0, .02, sh);
            float whole = smoothstep(.01, .07, sh);
            float loose = smoothstep(-.07, .0, sh);              // just outside: the shape dissolves into loose dots

            float hz = Noise(float2(s / (140.0 * Dip), side + 4.0));
            float3 wash = lerp(Lin(C0), Lin(C1), smoothstep(.0, .35, yn) * .6);
            wash = lerp(wash, Lin(C4), smoothstep(.5, .8, yn - .25 * hz + .1));
            wash = lerp(wash, Lin(C3), smoothstep(.72, 1.0, yn - .3 * hz + .12));
            float3 c = wash * (.06 + .2 * e + .2 * k) * inPanel * (.4 + .6 * whole);

            // dust: irregular crisp flecks of pale blue, 1-4 px, thick inside the shape and thinning out past its edge;
            // between them faint stains, so the surface is never clean
            float2 dq = float2(s, p.y);
            float fn = Noise(dq / (1.4 * Dip)) * .72 + Noise(dq / (4.0 * Dip) + 7.0) * .28;
            float fth = lerp(.82, .7 - .06 * e - .04 * k, inPanel) + .1 * (1 - loose);
            float fleck = smoothstep(fth, fth + .03, fn);
            float nb = Noise(dq / (10.0 * Dip) + 3.0);
            float fb = .5 + .8 * nb;
            float spark = step(.9, Hash(floor(dq / (3.0 * Dip)) + floor(Clock * 6.0))) * sn;
            float3 dotCol = lerp(lerp(Lin(C1), Lin(C2), .55), lerp(Lin(C2), Lin(C3), .55) * 1.2, smoothstep(.6, .95, yn - .3 * hz + .1));
            c += dotCol * fleck * fb * (.45 + .8 * e + .5 * k + 1.5 * spark) * loose;
            float stain = smoothstep(.55, .85, nb) * whole;
            c += lerp(Lin(C1), Lin(C2), .3) * stain * (.08 + .2 * e);

            // cyan patches high up in the wash, so the blue is never one flat colour
            float cy = smoothstep(.55, .8, Noise(dq / (26.0 * Dip) + side + 21.0)) * (1 - smoothstep(.2, .6, yn));
            c += float3(.02, .2, .35) * cy * whole * (.05 + .5 * e);

            // the die: fine etched traces on a cell grid, broken here and there, small pads where they meet;
            // packets of light run along the traces with the band
            float2 cp = float2(13.0, 9.0) * Dip / 1.5;
            float2 gi = floor(dq / cp), gf = dq - gi * cp;
            float eH = step(.48, Hash(gi + side + 31.0)) * smoothstep(1.1, .35, gf.y);
            float eV = step(.64, Hash(gi + side + 57.0)) * smoothstep(1.1, .35, gf.x);
            float pad = step(.82, Hash(gi + side + 71.0)) * step(gf.x, 2.2) * step(gf.y, 2.2);
            float etch = max(max(eH, eV), pad);
            float packet = smoothstep(.88, 1.0, frac(s / (90.0 * Dip) - Clock * .7 + Hash(float2(gi.y, side + 5.0)))) * eH;
            float3 etchCol = lerp(wash, lerp(Lin(C1), Lin(C2), .5), .55);
            c += etchCol * etch * whole * (.02 + .45 * e + .3 * k) * (.6 + .8 * Noise(gi * .37 + Clock * .2));
            c += float3(.75, .92, 1.1) * packet * whole * (.5 * e + .4 * k);

            // cracks through the ice: thin dark veins, each with a pale hairline along it
            float vn = Noise(dq / (17.0 * Dip) + side + 11.0) * .7 + Noise(dq / (5.0 * Dip) + 2.0) * .3;
            float vd = abs(vn - .5);
            c *= 1 - .6 * smoothstep(.035, .008, vd) * whole;
            c += lerp(Lin(C1), Lin(C2), .7) * smoothstep(.012, .0, abs(vd - .03)) * whole * (.02 + .2 * e);

            // rare hot flecks: a crisp bright core with a short horizontal glint
            float2 hcell = 6.0 * Dip;
            float2 hq = floor(dq / hcell);
            float hh = Hash(hq + side + 91.0);
            float2 hd = dq - (hq + .25 + .5 * float2(Hash(hq + 3.1), Hash(hq + 8.3))) * hcell;
            float hotCore = step(abs(hd.x), .9) * step(abs(hd.y), .9);
            float glint = exp(-abs(hd.x) / (3.0 * Dip)) * step(abs(hd.y), .5);
            float hot = step(.975 - .04 * e - .03 * k, hh) * loose;
            c += float3(.8, .95, 1.1) * (hotCore + .45 * glint) * hot * (.08 + 1.1 * e + .6 * k);

            // the shapes' edge: a broken pale frost rim
            float rim = exp(-pow((sh - .012) / .006, 2)) * step(.42, Noise(dq / (3.0 * Dip) + side));
            c += float3(.4, .65, 1.1) * rim * (.03 + .4 * e + .3 * k);

            // frost grows in from the shapes' edges as the band rises: crisp needles, thickest by the edge;
            // in silence only a thin fringe of it stays
            float px = Dip / 1.5;
            float grow = (.012 + .06 * e + .025 * k) * (.7 + .6 * Noise(dq / (6.0 * Dip) + side + 61.0));
            float frostM = smoothstep(.0, .006, sh) * smoothstep(grow, grow * .55, sh);
            float needle = Needles(dq / px, side + 77.0);
            c += float3(.6, .85, 1.1) * needle * frostM * (.03 + .9 * e + .4 * k) * (.5 + .5 * saturate(1 - sh / max(grow, .001)));

            // a kick cracks the ice: a bright fracture runs out from a point and cools into a dark vein
            float seedK = floor(Clock / 6.0);
            float ccell = 280.0 * Dip;
            float cix = floor(s / ccell);
            float cseed = cix * 1.37 + side + seedK * 2.11;
            float ox = (cix + .2 + .6 * Hash(float2(cseed + .5, 3.7))) * ccell;
            float dist = abs(s - ox);
            float front = min(KickAge * 700.0 * Dip, 150.0 * Dip);
            // its path: short straight jagged segments, wandering up and down as it runs along the strip
            float seg = 7.0 * px;
            float sj = (s - ox) / seg;
            float j = floor(sj), fj = sj - j;
            float y0 = H * (.25 + .5 * Hash(float2(cseed, 8.1)));
            float ya = y0 + 26.0 * px * (Noise(float2(j * .09, cseed)) - .5) + 5.0 * px * (Hash(float2(j, cseed + 4.4)) - .5);
            float yb = y0 + 26.0 * px * (Noise(float2((j + 1.0) * .09, cseed)) - .5) + 5.0 * px * (Hash(float2(j + 1.0, cseed + 4.4)) - .5);
            float slope = (yb - ya) / seg;
            float yc = lerp(ya, yb, fj);
            float cw = abs(p.y - yc) / sqrt(1 + slope * slope);           // in pixels from the crack
            // a fork splits off partway out and runs off at a slant for a while
            float fk = (20.0 + 40.0 * Hash(float2(cseed, 6.6))) * px;
            float fs = (Hash(float2(cseed, 7.7)) < .5 ? -1 : 1) * (.25 + .35 * Hash(float2(cseed, 9.9)));
            float cw2 = abs(p.y - yc - fs * (dist - fk)) / sqrt(1 + fs * fs);
            cw = min(cw, cw2 + 99.0 * (step(dist, fk) + step(fk + 50.0 * px, dist)));
            float crack = smoothstep(1.4, .4, cw);
            float rev = step(dist, front) * step(.2, Hash(float2(cix * 1.91 + side + .3, seedK * 1.73 + 9.3))) * whole;
            float tip = exp(-pow((front - dist) / (6.0 * Dip), 2));
            float fresh = exp(-KickAge * 2.5);
            // fresh: a dark groove cut through the flecks with a white-hot line in it and a cyan glow, hottest at the tip
            c *= 1 - .85 * smoothstep(4.5, 1.5, cw) * rev * fresh;
            c += (float3(.9, .97, 1.1) * crack * (1.0 + 3.0 * tip) * 1.6 + float3(.15, .5, 1.1) * exp(-cw / 2.0) * .5) * rev * fresh;
            // cooled: it stays a while as a dark vein
            c *= 1 - .55 * crack * rev * smoothstep(0, .3, KickAge) * exp(-KickAge * .4);

            // out in the gaps the shapes echo as dotted contour lines
            float ring = exp(-pow((sh + .055) / .004, 2));
            float dotted = step(.6, Hash(floor(dq / (2.2 * Dip)) + side));
            c += float3(.1, .25, .8) * ring * dotted * (1 - loose) * (.01 + .22 * e + .3 * k);

            // the die's markings, etched where the glass is lit: code labels, alignment crosses, corner brackets
            float2 mc = float2(104.0, 42.0) * px;
            float2 mq = float2(w.y > 0 ? s : -s, p.y);                    // read left to right on both wings
            float2 mi = floor(mq / mc);
            float2 mf = mq - mi * mc;
            float mh = Hash(mi + side + 131.0);
            float2 anchor = floor(float2(5.0 + 50.0 * Hash(mi + 3.7), 5.0 + 22.0 * Hash(mi + 9.1)));
            float2 lq = floor(mf / px) - anchor;
            float mark = 0;
            if (mh < .5)
            {
                float ch = floor(lq.x / 4.0);
                if (ch >= 0 && ch < 5)
                {
                    float r = Hash(mi + ch * 3.3 + 17.0);
                    float gi2 = ch == 2 ? 16 : (ch == 3 ? 10 + floor(r * 5.99) : floor(r * 9.99));
                    mark = Glyph(Font[(int)gi2], float2(lq.x - ch * 4.0, lq.y));
                }
            }
            else if (mh < .78)
            {
                float2 ac = abs(lq - 4.0);
                mark = saturate(step(ac.x, .5) * step(ac.y, 4.5) + step(ac.y, .5) * step(ac.x, 4.5));
            }
            else if (mh < .9)
            {
                float inL = step(0, lq.x) * step(0, lq.y);
                mark = inL * saturate(step(lq.x, 6) * step(lq.y, .5) + step(lq.y, 6) * step(lq.x, .5));
            }
            c += float3(.45, .7, 1.0) * mark * (.2 + .8 * inPanel) * (.05 + .4 * e + .25 * k);

            return c;
        }
        """;
}
