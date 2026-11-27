namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 02 — the light bands (4K take, 33.5–37.3 s): three bands of light across the strip with black between them, a partial
/// one along the top, the main one in the middle, a partial one at the foot. Each is cut into cells by thin dark posts;
/// behind them one continuous picture of long horizontal streaks, amber-gold in some stretches, ice blue or violet in
/// others; a hot white-gold line runs through the main band and washes the posts out.
/// Each cell lights on its own when its band passes its mark and rests dim below it, so a loud stretch is a burning
/// row of windows and a quiet one a few embers; mids feed the hot line, a kick flares it.
/// </summary>
public sealed class BrLightBands : BrScene
{
    public BrLightBands() : base(Body, 0, Hex(0x8CC4FF), Hex(0xFFB24A), Hex(0x2A5CE0), Hex(0xE07A20), Hex(0xFFF4DC)) { }

    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.3f, .1f, .5f, .06f);
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .06f, .12f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.3f, 0f, 0f, 3.0f);

    private const string Body = """
        // the light behind the grille: one continuous picture of long horizontal light streaks (a long-exposure city),
        // amber in some stretches and ice blue in others; the posts only cut windows into it
        float3 Behind(float x, float y, float id, float b)
        {
            float H = Size.y;
            float region = smoothstep(.5, .75, Noise(float2(x / (260 * Dip) + id, Clock * .02)));
            // measured palette, linear: amber-gold, ice blue, and now and then violet
            float3 amber = float3(1.0, .42, .08), ice = float3(.1, .42, 1.1), vio = float3(.42, .26, 1.0);
            float vr = step(.78, Noise(float2(x / (200 * Dip) + id * 2.0, 5.5)));
            float3 tint = lerp(lerp(ice, vio, vr), amber, region);
            // streaks: thin in y, long in x; a few wide soft glows under them
            float st1 = Noise(float2(x / (180 * Dip) + id * 3.1, y / (3.2 * Dip)));
            float st2 = Noise(float2(x / (90 * Dip) + 7.3, y / (1.8 * Dip) + 4.1));
            float streak = pow(saturate(st1 * 1.25 - .15), 3.0) * 1.8 + pow(saturate(st2 * 1.2 - .2), 4.0) * 1.2;
            float glow = Noise(float2(x / (140 * Dip) + 2.2, y / (16 * Dip) + id));
            float breath = .75 + .25 * sin(Clock * .7 + y / (9 * Dip) + region * 3.0);
            float3 hotT = lerp(float3(.75, .9, 1.1), float3(1.0, .82, .5), region);
            float3 c = tint * (.05 + .35 * glow * glow) + lerp(tint, hotT, saturate(streak - .6)) * streak * 1.2 * breath;
            return c;
        }

        // posts: rounded dark pillars with soft edges; pp the pitch, returns 0 on a post, 1 in a window
        float Post(float s, float pp, float wpx)
        {
            float f = frac(s / pp);
            float d = min(f, 1 - f) * pp;
            return smoothstep(wpx * .5 - .9 * Dip, wpx * .5 + 1.3 * Dip, d);
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float H = Size.y;
            float id = w.y > 0 ? 500 : 0;
            float b = BandW(w.x);
            // a row now and then tears sideways for a frame (only while sound plays)
            float tb = floor(p.y / (7 * Dip)), tt = floor(Clock * 7.0);
            float s = w.z + smoothstep(.02, .12, Level) * step(.93, Hash(float2(tb + id, tt))) * (Hash(float2(tb + 3, tt)) - .5) * 16 * Dip;

            // three bands with black between: a partial one along the top, the main one, a partial one at the foot;
            // the main band's top and bottom step from one run of windows to the next
            float yn = p.y / H;
            float tier = yn < .25 ? 0 : (yn < .875 ? 1 : 2);
            float pp = (15 + 7 * Hash(float2(floor(s / (150 * Dip)), id + tier * 7 + 1))) * Dip;
            float sp = s + tier * pp * .37;
            float cell = floor(sp / pp);
            float grp = floor(cell / (2 + floor(Hash(float2(floor(cell / 6), id + tier)) * 4))) + id + tier * 31;
            float edgeA = tier == 0 ? -4 * Dip : (tier == 1 ? H * (.3 + .06 * Hash(float2(grp, 3))) : H * (.9 + .03 * Hash(float2(grp, 3))));
            float edgeB = tier == 0 ? H * (.17 + .05 * Hash(float2(grp, 4))) : (tier == 1 ? H * (.84 - .06 * Hash(float2(grp, 4))) : H + 4 * Dip);
            float inTier = smoothstep(edgeA - 1.0 * Dip, edgeA + 1.5 * Dip, p.y) * smoothstep(edgeB + 1.0 * Dip, edgeB - 1.5 * Dip, p.y);

            // the picture, each colour channel seeing the posts a hair apart (blue and orange fringes)
            float3 Lc = Behind(s, p.y, id + tier * 3.0, b);
            float wpx = (3.2 + 1.6 * Hash(float2(grp, 8))) * Dip;
            float3 post = float3(Post(sp - .6 * Dip, pp, wpx), Post(sp, pp, wpx), Post(sp + .6 * Dip, pp, wpx));
            // the glass of each window: fine vertical ribs, a brighter lid at the top
            float rib = .82 + .18 * sin(s / (1.3 * Dip) * 3.14159);
            float yb = saturate((p.y - edgeA) / max(edgeB - edgeA, 1.0));
            float lid = 1 + .5 * smoothstep(.3, .0, yb);
            // the meter: each cell lights when its band passes its own mark and rests dim below it
            float uc = saturate(((cell + .5) * pp - tier * pp * .37) / max(WingPx(w.y), 1));
            float bc = saturate(BandW(uc) * 1.6 * (.85 + .3 * Kick()));
            float th = .05 + .7 * Hash(float2(cell, grp + 11));
            float act = smoothstep(th, th + .08, bc);
            float lvl = lerp(.035, 1.0, act) * (.75 + .35 * Hash(float2(cell, grp + 7))) * (tier == 1 ? 1.0 : .7);
            float3 c = Lc * post * rib * lid * lvl * inTier;
            // the posts are not black: a faint lit edge where they meet the light
            c += Lc * .05 * (1 - post.g) * inTier * lvl;

            // the hot line through the main band: white-gold, it washes the posts out
            float hy = H * .57 + (Noise(float2(s / (90 * Dip), Clock * .2 + id)) - .5) * 2.0 * Dip;
            float hl = exp(-pow((p.y - hy) / (1.3 * Dip), 2)) + .35 * exp(-abs(p.y - hy) / (4.0 * Dip));
            float run = 1.6 * pow(Noise(float2(s / (70 * Dip) + id, Clock * .3)), 2.0);
            c += float3(1.0, .78, .45) * hl * run * (.02 + .8 * Mid + .6 * Kick()) * step(.5, tier) * step(tier, 1.5);

            // behind the bands: near-black blue with fine dust
            float2 dq = float2(s * .6, p.y);
            float2 dg = floor(dq / (.9 * Dip));
            float dh = Hash(dg + id);
            float dd = length(frac(dq / (.9 * Dip)) - .5);
            float dust = step(.93, dh) * saturate((.5 - dd) * 3.0) * (.15 + .5 * Hash(dg + 5.1)) * (.5 + Noise(dq / (25.0 * Dip)));
            float3 sky = float3(.002, .004, .012) * (.5 + Noise(dq / (30.0 * Dip))) + float3(.3, .4, .8) * dust * (.02 + .06 * b);
            c += sky * (1 - inTier * post.g);
            return c;
        }
        """;
}
