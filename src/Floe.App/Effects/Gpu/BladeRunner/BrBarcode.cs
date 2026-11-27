namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 09 — the barcode take (19.9–21.7 s), built from its parts: tall stripes on a ~19 px pitch, each a hot core with
/// a red fringe on one side and a blue one on the other, beaded and nicked by short dark breaks; every scan row shakes
/// sideways on the tape's frames and the stripes bend in slow S-curves; between them dark olive, teal or bronze,
/// or a lit, meshed gold panel where the stripes go soft; soft pale-blue blocks at some seams, and a dark shade that
/// dips in from the top. The camera pans along the take with the music. Each stripe lights on its own when its band
/// passes its own mark, rests dim below it, and in a silent stretch crumbles into loose horizontal dashes.
/// </summary>
public sealed class BrBarcode : BrScene
{
    public BrBarcode() : base(Body, 0, Hex(0xFF9A2A), Hex(0xFFF0C8), Hex(0xC83A0C), Hex(0x3A1204), Hex(0x6A5CFF)) { }

    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.14f, .03f, .5f, .07f);
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, 0f, .12f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.35f, 0f, 0f, 3f);

    private const string Body = """
        // the take's kinds of stretch: the stripes' colour, what lies between them, whether the gap is a lit panel,
        // and how strongly the stripes show there
        // measured on the 4K take: mostly lemon-yellow and orange stripes, a little ice-blue and a teal seam, all
        // strongly saturated, over a dark warm ground (brown-red, olive), with golden-brown lit panels
        // kinds: 0 ice, 1 yellow, 2 gold panel, 3 bronze, 4 orange-red, 5 dark
        static const float3 SC[6] = { float3(.55, .85, 1.1), float3(1.0, .92, .05), float3(1.0, .6, .1), float3(.8, .42, .1), float3(1.0, .46, .09), float3(.9, .6, .2) };
        static const float3 BG[6] = { float3(.006, .016, .018), float3(.018, .016, .002), float3(.2, .11, .025), float3(.07, .036, .009), float3(.03, .011, .003), float3(.012, .008, .002) };
        static const float PN[6] = { 0, 0, 1, .35, 0, 0 };
        static const float SV[6] = { 1, 1, .75, .55, 1, .3 };

        // how often each kind turns up along the take: 8% ice, 42% yellow, 14% gold, 10% bronze, 16% orange, 10% dark
        float Kind(float seg, float id)
        {
            float h = Hash(float2(seg + id, 31));
            return h < .08 ? 0 : h < .50 ? 1 : h < .64 ? 2 : h < .74 ? 3 : h < .90 ? 4 : 5;
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float H = Size.y, yn = p.y / H;
            float id = w.y > 0 ? 300 : 0;
            float tk = floor(Clock * 15.0);                         // the tape's frames: the shake and the grain step on them
            // the take hardly slides: it drifts, and on each kick it is pulled, stretched and springs back (measured
            // in the 4K shot: ~1-2 px a frame, then surges of 5-7 for a third of a second). The pull is uneven along
            // the strip, so the stripes' spacing opens in some stretches and squeezes in others
            float ka = KickAge;
            float spring = (1 - exp(-ka * 14.0)) * exp(-ka * 2.2) * 1.4;     // pulled fast, let go slowly, no shake
            float field = Noise(float2(w.z / 260.0 + id, Clock * .05)) * 2 - 1;
            float elastic = 34.0 * spring * (.35 + .65 * field) * (.4 + .6 * Bass);
            float breathe = 14.0 * (Noise(float2(w.z / 340.0 + id, Clock * .09 + 3.3)) - .5);
            float x = w.z + Clock * 14.0 + id * 7.0 + elastic + breathe;
            float row = floor(p.y);
            float xz = w.z + Clock * 8.0 + id * 7.0 + elastic * .5;  // the coloured ground lies further back: it moves less

            // stretches of one kind each, ~110-260 px, melting into the next over their second half
            float L = 170.0;
            float xb = xz + 70.0 * Noise(float2(xz / 230.0, 4.1 + id + Clock * .03));
            float seg = floor(xb / L), sf = frac(xb / L);
            float k0 = Kind(seg, id), k1 = Kind(seg + 1, id);
            float mixK = smoothstep(.4, 1, sf);
            float3 sc = lerp(SC[(int)k0], SC[(int)k1], mixK);
            float3 bg = lerp(BG[(int)k0], BG[(int)k1], mixK);
            float pn = lerp(PN[(int)k0], PN[(int)k1], mixK);
            float sv = lerp(SV[(int)k0], SV[(int)k1], mixK) * (.6 + .7 * Noise(float2(x / 90.0, 6.6 + id + Clock * .15)));
            // each stretch warms and cools on its own over the take: red-orange, then yellow, then back
            float wv = Noise(float2(seg * .7 + id, Clock * .12));
            sc = lerp(sc, sc * float3(1.05, .86, .6), smoothstep(.35, .8, wv) * .5);
            bg = lerp(bg, bg * float3(1.1, .88, .6), smoothstep(.35, .8, wv) * .4);

            // the stripes: every scan row shakes a little on the tape's frames, the stripes bend slowly, the pitch breathes
            float jRow = (Hash(float2(row, tk)) - .5) * 1.0 + (Noise(float2(row * .35, tk * .17)) - .5) * 1.8;
            float bend = (Noise(float2(x / 70.0, yn * 2.2 + Clock * .45)) - .5) * 8.0;
            // the glass in front: lenses drifting along the strip, each with a chevron-shaped edge. Inside one the
            // stripes are magnified and kinked sideways, at its rim they squeeze and split into colour
            float lz = w.z + Clock * 5.0 + id * 3.0;
            float lc = floor(lz / 420.0);
            float lon = step(.3, Hash(float2(lc + id, 61)));
            float lr = 55.0 + 85.0 * Hash(float2(lc + id, 62));
            float ly = .25 + .5 * Hash(float2(lc + id, 63));
            float lcx = (lc + .5 + (Hash(float2(lc + id, 64)) - .5) * .3) * 420.0
                      + (18.0 + 22.0 * Hash(float2(lc + id, 65))) * (1 - abs(yn - ly) * 2.2) * sign(Hash(float2(lc + id, 66)) - .5);
            float lq = (lz - lcx) / lr;
            float lens = lon * saturate(1 - lq * lq);
            float lk = (.35 + .3 * Hash(float2(lc + id, 67))) * (.6 + .5 * Bass + .4 * Kick());
            float lensDisp = -(lz - lcx) * lk * lens * lens;
            float rim = lon * exp(-pow((abs(lq) - .95) / .12, 2.0));
            float xw = x + jRow + bend + 30.0 * Noise(float2(x / 180.0, 2.7)) + lensDisp;
            float xs = xw / 19.0;
            float sid = floor(xs);
            float d = frac(xs) - .5 - (Hash(float2(sid, 1)) - .5) * .16 + (Hash(float2(row * 1.3 + id, sid + tk * 5.7)) - .5) * .1;
            float wd = .12 + .08 * Hash(float2(sid, 2));
            // each edge frays on its own, row by row: a fine saw on both sides of every stripe
            float eL = (Hash(float2(row + id, sid * 2.0 + tk * 3.1)) - .5) * .26 + (Noise(float2(row * .5, sid * 3.0 + tk * .3)) - .5) * .1;
            float eR = (Hash(float2(row - id, sid * 2.0 + 1 + tk * 3.1)) - .5) * .26 + (Noise(float2(row * .5, sid * 3.0 + 7 + tk * .3)) - .5) * .1;
            float we = max(wd + (d < 0 ? eL : eR), .03);

            // a hot core, red fringe on one side and blue on the other, a little glow
            float ch = .065 + .16 * rim + .05 * lens;
            float3 dd = float3(d + ch, d, d - ch);
            float3 core = exp(-pow(abs(dd) / we, 3.0));
            float3 glow = exp(-dd * dd / (wd * wd * 7.0));
            float3 prof = core + .2 * glow;

            // fine grain inside the stripe: dark specks, uneven light, brighter at the top and speckled lower down
            float g = Hash(float2(floor(xw) + id, row + tk * 7.3));
            float speck = lerp(1, .3, step(g, .12 + .22 * yn));
            float grainL = .65 + .55 * Noise(float2(xw / 2.0, p.y / 1.6 + tk * .5));
            float top = H * (.07 + .06 * Hash(float2(sid, 4)));
            float env = smoothstep(top - 2, top + 5, p.y) * smoothstep(H - 1, H - 6, p.y);
            float vgrad = lerp(1.15, .75, yn);

            // sound, one stripe at a time: lit past its own mark, dim below it, crumbled to grains when its stretch goes quiet
            // the band fills its stretch the way a meter does: the louder it is, the more of its stripes are lit, so
            // a quiet stretch reads as mostly dark and a loud one as mostly lit
            float b = saturate(BandW(w.x) * 1.35 * (.8 + .35 * Kick()));
            float th = .1 + .8 * Hash(float2(sid, 11));
            float act = smoothstep(th, th + .08, b);
            float thr = .03 + .14 * Hash(float2(sid, 7));
            float dis = saturate((thr - b) / .06);

            float3 hot = sc * prof * 1.6 + .05 * pow(core.g, 4.0);
            float3 full = hot * speck * grainL * vgrad * env * sv * lerp(.024, 1, act);

            // dissolved: the stripe falls apart into loose 3-px grains around where it stood, fewer as it goes quiet
            float2 gc = float2(floor((d + .5) * 19.0 / 3.0 + Hash(float2(sid, row)) * 2.0), row);
            float gh = Hash(gc + float2(sid * 7.1 + id, tk * .0));
            float near = smoothstep(wd * 2.6, wd * 1.2, abs(d));
            float crumbM = step(.5 + .45 * dis, gh) * near;
            float3 crumb = sc * crumbM * env * (.05 + .4 * (1 - dis)) * (.7 + .6 * Hash(gc + 3.3));
            float3 stripe = lerp(full, crumb, dis);

            // between the stripes: a dark mottled ground with stray sparks, or a lit panel under a fine screen mesh
            float mott = .6 + .75 * Noise(float2(xz / 6.0, p.y / 4.0)) * (.65 + .35 * Noise(float2(xz / 23.0, p.y / 17.0 + Clock * .2)));
            float mesh = 1 - .24 * pn * step(.5, frac(floor(xz) * .5)) * step(.5, frac(row * .5));
            float3 ground = bg * mott * mesh * lerp(1, .45 + 1.1 * glow.g, pn);
            ground += sc * step(.997, Hash(float2(floor(xw) * 1.3, row + tk * 5.1))) * .4 * (1 - pn) * yn;
            ground *= lerp(.015, 1, smoothstep(.05, .7, b));         // a quieter stretch lets shadow in between its stripes

            // a soft pale-blue block at some seams
            float sx = sf * L;
            float blue = smoothstep(0, 10, sx) * smoothstep(34, 18, sx) * step(.6, Hash(float2(seg + id, 13)));
            ground = lerp(ground, float3(.03, .24, .2) * (.6 + .3 * Noise(float2(xz / 5.0, p.y / 9.0)) + .5 * glow.g) * lerp(.01, 1, smoothstep(.02, .5, b)), blue * .55);

            float3 c = (ground * env + stripe) * (1 + .25 * lens) + sc * rim * env * .08 * (.3 + b);

            // a dark shade dipping in from the top, and the dark lip of the frame
            float sl = H * (.03 + .32 * pow(Noise(float2(w.z / 160.0 + Clock * .35, 21 + id + Clock * .07)), 3.0));
            c *= .04 + .96 * smoothstep(sl - 2, sl + 8, p.y);
            return c * (1 + .3 * Kick());
        }
        """;
}
