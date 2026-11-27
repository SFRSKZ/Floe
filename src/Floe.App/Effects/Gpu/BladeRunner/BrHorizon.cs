namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 01 — the skyline on the horizon (4K take: the opening 0.5–2.5 s and the credit strips 64–72 s, which are almost
/// exactly a taskbar): clusters of lit windows standing on a low horizon, far apart along the strip. A cluster is a
/// thin top band of windows, a dark gap, and a taller block below; every window a narrow vertical bar, hot yellow-white
/// at the core and orange at the edges, a few green-tinged. Thin horizontal streaks run out from the window rows, the
/// clusters glow, and from some of them broad soft shafts of pink-orange light rise at a shallow slant across the dark
/// sky, which holds a few faint stars. Below the horizon a dim smeared reflection.
/// Each cluster is one band: its windows come on one by one as the band rises past each window's mark. A kick fans
/// the shafts out and flashes a starburst at their source. Every ~30 s of music the city turns from warm to a cold
/// blue-white with pink-violet shafts and back.
/// </summary>
public sealed class BrHorizon : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .05f, .08f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.2f, 0f, 0f, 3f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.35f, .12f, .3f, .05f);

    public BrHorizon() : base(Body, 0, Hex(0xFFF2D8), Hex(0xFF7A22), Hex(0xFFC9A0), Hex(0x3E6BFF), Hex(0xE8F2FF)) { }

    private const string Body = """
        // one cluster's light at (cx, up): windows (own cell only), glow and the row streaks (seen from the neighbours too)
        // ci: cell index; cx: x inside the cell; up: px above the horizon; cool: 0 warm, 1 cold
        float3 Cluster(float ci, float cx, float up, float P, float uc, float cool, float own, float tk)
        {
            if (Hash(float2(ci, 1)) < .3) return 0;
            float cw = 28.0 + 95.0 * Hash(float2(ci, 2));
            float cl = (P - cw) * Hash(float2(ci, 3));
            float bh = 12.0 + 24.0 * pow(Hash(float2(ci, 4)), 1.2);       // the main block's height
            float gap = 3.0 + 3.0 * Hash(float2(ci, 5));
            float tb = 3.5 + 2.0 * Hash(float2(ci, 6));                  // the thin top band
            float b = saturate(BandW(uc) * 1.3);
            float lx = cx - cl;

            float3 warmA = float3(1.0, .5, .12), warmB = float3(1.0, .9, .62);
            float3 coolA = float3(.35, .55, 1.1), coolB = float3(.88, .93, 1.1);
            float3 ca = lerp(warmA, coolA, cool), cb = lerp(warmB, coolB, cool);
            float3 c = 0;

            // the windows: narrow vertical bars on a ~4.6 px pitch, in the block and in the top band
            if (own > .5 && lx > 0 && lx < cw && up > 0)
            {
                float colI = floor(lx / 3.3), fx = frac(lx / 3.3);
                // the block's top is uneven: some columns stand a little taller or shorter
                float bhc = bh * (.8 + .3 * Hash(float2(ci, floor(colI / 3.0) + 40)));
                float inBlock = step(up, bhc);
                float inBand = step(bhc + gap, up) * step(up, bhc + gap + tb);
                float rowI = inBlock > 0 ? floor(up / 7.0) : 99.0;
                float wid = Hash(float2(colI + ci * 7.0, rowI + 3.0));
                float bar = smoothstep(.08, .28, fx) * smoothstep(1.0, .8, fx);
                float vrow = inBlock > 0 ? smoothstep(0, .14, frac(up / 7.0)) * smoothstep(1, .88, frac(up / 7.0)) * .75 + .25 : 1;
                // lit when the band passes the window's mark; unlit ones keep a faint glow
                // windows light in runs: a run's mark comes from a coarse noise, each window nudges it a little
                float th = .05 + .65 * Noise(float2(colI / 4.0 + ci * 3.7, rowI * 1.3 + ci)) + .12 * (wid - .5);
                float lit = lerp(.012, 1, smoothstep(th, th + .06, b));
                float hot = (.35 + .65 * smoothstep(.2, .9, Hash(float2(colI + ci, rowI + 9.0)))) * lit * (1 - .5 * smoothstep(.2, .5, abs(lx / cw - .5)));
                float3 wc = lerp(ca, cb, hot);
                wc = lerp(wc, float3(.5, 1.0, .5), step(.975, Hash(float2(colI, rowI + ci))) * .5);   // a few green-tinged
                float flick = 1 + .5 * Treble * (Hash(float2(colI + ci, tk)) - .5);
                c += wc * bar * vrow * (inBlock + inBand * .8) * lit * (.75 + .45 * Hash(float2(colI, rowI + 5.0 + ci))) * flick * 1.3;
            }
            // the cluster glows, wider than tall (the film's anamorphic blur)
            float dxo = max(max(cl - cx, cx - (cl + cw)), 0);
            float hgt = bh + gap + tb;
            float glow = exp(-dxo / 26.0) * exp(-abs(up - hgt * .5) / (hgt * .5 + 5.0));
            c += ca * glow * .2 * (.05 + b);
            // thin horizontal streaks run out from the window rows, far past the cluster
            float sr = frac(up / 6.0);
            float srow = floor(up / 6.0);
            float sOn = step(.55, Hash(float2(ci, srow + 50.0)));
            float streak = exp(-pow((sr - .5) / .08, 2)) * step(0, up) * step(up, bh) * exp(-dxo / (25.0 + 70.0 * Hash(float2(ci, srow + 8.0)))) * sOn;
            c += lerp(ca, cb, .4) * streak * step(.001, dxo) * .08 * b;
            return c;
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 700 : 0;
            float tk = floor(Clock * 12.0);
            float k = Kick();
            float hy = H * .86;
            float up = (hy - p.y) / D;
            // the camera drifts along the skyline while the music plays
            float s = w.z / D + Clock * 6.0;
            float cool = smoothstep(.55, .75, Noise(float2(Clock / 30.0, side + .5)));
            float L = WingPx(w.y) / D;

            // the reflection below the horizon: the city again, squashed and smeared
            float refl = up < 0 ? 1 : 0;
            float upR = up < 0 ? -up * 1.8 : up;
            float sR = s + (up < 0 ? (Noise(float2(p.y / (1.5 * D), tk * .2)) - .5) * 6.0 : 0);

            float P = 150.0;
            float sw = sR + 50.0 * Noise(float2(sR / 400.0, side + 2.0));
            float ci = floor(sw / P), cx = sw - ci * P;
            float3 c = 0;
            [unroll] for (int n = -1; n <= 1; n++)
            {
                float cc = ci + n + side;
                float uc = saturate(((ci + n) * P + P * .5 - Clock * 6.0) / max(L, 1));
                c += Cluster(cc, cx - n * P, upR, P, uc, cool, n == 0 ? 1 : 0, tk);
            }
            if (refl > 0) c *= .22 * exp(up / 9.0);

            // the shafts: from a few beacons along the skyline, broad soft beams rising at a shallow slant, striped
            // like light through blinds; a kick fans them out and flashes a starburst at the source
            float3 rayC = lerp(float3(1.0, .42, .28), float3(.85, .4, 1.0), cool);
            float B = 560.0;
            float bi0 = floor(s / B);
            float rays = 0, burst = 0;
            [unroll] for (int m = -1; m <= 1; m++)
            {
                float bi = bi0 + m + side;
                if (Hash(float2(bi, 31)) < .35) continue;
                float bx = (bi0 + m + .2 + .6 * Hash(float2(bi, 32))) * B;
                float2 d = float2(s - bx, up - 10.0);
                float r = length(d);
                float th = atan2(max(d.y, 0), d.x);                         // 0 to the right, pi to the left
                float dir = Hash(float2(bi, 33)) < .5 ? 0 : 3.14159;
                float fan = .1 + .18 * Hash(float2(bi, 34)) + .1 * k + .04 * sin(Clock * .3 + bi);
                float a = abs(th - dir);
                a = min(a, 6.28318 - a);
                float lobe = exp(-pow(a / fan, 2));
                // blinds: soft parallel stripes across the beam
                float stripes = .55 + .45 * Noise(float2(th * 40.0 + bi, Clock * .1));
                float fall = exp(-r / (260.0 + 300.0 * Hash(float2(bi, 35)))) * smoothstep(4.0, 30.0, r) * smoothstep(1.2 * B, .9 * B, abs(d.x));
                rays += lobe * stripes * fall * step(0, d.y);
                float sp = pow(abs(cos(th * 6.0 + bi)), 60.0);
                burst += (exp(-r / 7.0) + sp * exp(-r / 16.0) * step(0, d.y)) * (.15 * Bass + k);
            }
            float energy = .06 + .7 * Bass + .6 * k;
            c += rayC * rays * .38 * energy * (1 - refl * .7);
            c += lerp(float3(1.0, .6, .35), float3(.75, .6, 1.1), cool) * burst * .2 * (1 - refl);

            // the sky: near black, a warm or cold haze low over the horizon, a few faint stars
            float upA = max(up, 0);
            c += lerp(float3(.03, .008, .002), float3(.004, .01, .045), cool) * exp(-upA / 30.0) * (.15 + .8 * Bass) * (1 - refl);
            float st = step(.9993, Hash(floor(float2(s * 1.0, p.y / D)))) * step(8.0, up);
            c += float3(.7, .75, 1.0) * st * (.15 + .3 * Hash(float2(floor(s), tk)));
            // the horizon line itself
            c += lerp(float3(.6, .3, .1), float3(.2, .3, .8), cool) * exp(-abs(up) / 1.2) * .02 * (.3 + Level);
            return c;
        }
        """;
}
