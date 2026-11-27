namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 04 — the hanging flames (4K take, 5.0–7.3 s): a molten band along the top of the strip, salmon-pink with hot beads,
/// magenta at its lower edge, scratched with dust. Below it a field of blue light, deep blue high up, brightening to blue
/// and cyan toward the foot, and standing in it a row of dark silhouettes hanging from the band: a rounded head pushing up
/// into the band, a long body, a narrow neck low down, a foot that widens again, with a thin green fringe round the foot.
/// The blue light is the sound: each stretch of it rises from the foot as high as its band and burns brighter; the band
/// glows with the level and flares on a kick. In silence the blue sinks and the silhouettes stand in the dark.
/// </summary>
public sealed class BrHangingFlames : BrScene
{
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.3f, .06f, .35f, .07f);
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .08f, .15f, .04f);

    public BrHangingFlames() : base(Body, 0, Hex(0xFF6A2A), Hex(0xFF5FA8), Hex(0x1C2EFF), Hex(0x6FA8FF), Hex(0xFFF0E0)) { }

    private const string Body = """
        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 400 : 0;
            float tk = floor(Clock * 12.0);
            float k = Kick();
            float L = WingPx(w.y);
            float s = w.z / D;
            float y = p.y / D, Hd = H / D;

            // the band's lower edge, lumpy, and how far down the field below runs
            float bandB = Hd * .24 + (Noise(float2(s / 45.0 + side, Clock * .15)) - .5) * 8.0;
            float below = Hd - bandB;

            // ---- the silhouettes: one to a cell of ~60-100 px, swaying a little ----
            float P = 112.0;
            float sw = s + 25.0 * Noise(float2(s / 200.0, side + 1.0));
            float ci = floor(sw / P);
            float sil = 0, fringe = 0;
            [unroll] for (int n = -1; n <= 1; n++)
            {
                float cc = ci + n;
                float id = cc + side;
                if (Hash(float2(id, 1)) < .18) continue;
                float xc = (cc + .5 + (Hash(float2(id, 2)) - .5) * .35) * P + 5.0 * sin(Clock * .3 + id * 2.1);
                float wS = P * (.6 + .35 * Hash(float2(id, 3)));
                float dx = abs(sw - xc);
                float t = (y - bandB) / max(below, 1);                 // 0 at the band's edge, 1 at the foot
                float neck = .62 + .15 * Hash(float2(id, 4));
                // body: widest high up, narrowing to the neck, widening again into the foot; the edge wavers
                float hw = t < neck ? lerp(.3, .09, smoothstep(.2, 1.0, t / neck))
                                    : lerp(.09, .22, pow(saturate((t - neck) / (1 - neck)), .8));
                hw *= wS * (1 + .18 * (Noise(float2(y / 14.0, id * 3.3 + Clock * .2)) - .5));
                // the head: the body's top is rounded and pushes up into the band
                float r = .3 * wS;
                float capY = bandB - .85 * r + r;
                float d = y < capY ? length(float2(dx, (y - capY) * 1.15)) - r : dx - hw;
                float sh = smoothstep(6.0, -5.0, d);                  // soft: the film blurs everything
                sil = max(sil, sh);
                fringe = max(fringe, exp(-pow((dx - hw - 1.5) / 2.0, 2)) * smoothstep(.65, 1.0, t));
            }

            // ---- the blue field: it rises from the foot as high as its band ----
            float u = saturate(w.z / L);
            float b = saturate(BandW(u) * 1.3);
            float lh = below * (.18 + .9 * b + .1 * k) * (.85 + .3 * Noise(float2(s / 70.0 + side, Clock * .2)));
            float top = Hd - lh;
            float lt = saturate((Hd - y) / max(lh, 1));               // 0 at the foot, 1 at the light's top
            float lightM = smoothstep(top - 10.0, top + 14.0, y) * step(bandB - 6.0, y);
            float3 blue = lerp(float3(.03, .32, .7), float3(.03, .1, .75), smoothstep(.0, .55, lt));
            blue = lerp(blue, float3(.01, .02, .3), smoothstep(.55, 1.0, lt));
            float3 field = blue * lightM * (.05 + 1.0 * b + .35 * k) * (.8 + .3 * Noise(float2(s / 30.0, y / 12.0 + Clock * .1)));
            // a deep blue wash under the band even where the light has not reached
            field += float3(.004, .004, .06) * smoothstep(Hd, bandB, y) * step(bandB - 4.0, y) * (.15 + Level);

            float3 c = field * (1 - sil) + float3(.2, 1.0, .5) * fringe * (1 - sil) * lightM * (.1 + .5 * b);

            // ---- the molten band: salmon-pink, hot beads, magenta along its lower edge ----
            float bt = saturate(y / max(bandB, 1));
            float inBand = smoothstep(bandB + 3.0, bandB - 3.0, y);
            float beads = .7 + .6 * pow(Noise(float2(s / 26.0 + side, Clock * .25)), 2.0);
            // measured on the take: salmon-red (linear 1 : .2 : .1), crimson toward the edge, dark magenta on it
            float3 bc = lerp(float3(1.0, .17, .09), float3(1.0, .34, .22), exp(-pow((bt - .4) / .3, 2)) * saturate(beads - .3));
            bc = lerp(bc, float3(.75, .02, .07), smoothstep(.62, .9, bt));
            bc = lerp(bc, float3(.3, .0, .1), smoothstep(.9, 1.05, bt));
            // dust and scratches on the band: dark specks and short bright hairs
            float2 g = floor(float2(s / 1.5, y / 1.5));
            float dark = step(.988, Hash(g + side));
            float hair = step(.992, Hash(floor(float2(s / 6.0, y / 1.2)) + tk)) * step(.5, Treble + k);
            float lvl = .1 + .7 * Level + .4 * k;
            c = lerp(c, bc * beads * lvl * (1 - .7 * dark) + float3(1, .85, .7) * hair * .5, inBand * (1 - sil));
            return c;
        }
        """;
}
