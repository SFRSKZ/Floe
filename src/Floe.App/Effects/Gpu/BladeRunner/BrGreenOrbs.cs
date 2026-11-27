namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 12 — the green orbs (4K take, 41.5–45.3 s): columns of big soft glowing blobs stacked one above the other on black.
/// A blob is a curled, coil-like lump of green light, pale yellow-green and hot at its lower rim, deep green at its
/// edge, wrapped in a grainy halo that sparkles on the tape's frames; some columns run mint and cyan, the upper blob a
/// little bluer. Measured on the take: hot (linear) .44 .66 .26, body .07 .34 .055, edge .01 .1 .004.
/// Each column is a band: its blobs swell and burn brighter with it and glow up on a kick; in silence they shrink to
/// dim green embers. The blobs drift and curl slowly while the music plays.
/// </summary>
public sealed class BrGreenOrbs : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .05f, .1f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, 0f, 0f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.35f, .05f, .3f, .07f);

    public BrGreenOrbs() : base(Body, 0, Hex(0x2CE04A), Hex(0xC8E030), Hex(0xD8FFE8), Hex(0x0E4A18)) { }

    private const string Body = """
        // one blob: centre c, radius r, mint 0..1, band b, id; returns its light at q (pixels)
        float3 Blob(float2 q, float2 c, float r, float mint, float b, float id, float tk, float k)
        {
            float2 d = (q - c) / r;
            float ang = atan2(d.y, d.x);
            // a lumpy, curling outline
            float rr = length(d) * (1 + .16 * (Noise(float2(ang * 1.6 + id, Clock * .2)) - .5) * 2);
            if (rr > 3.2) return 0;
            // inside: a coil, bright crescents winding round the core, hottest at the lower rim
            float coil = .5 + .5 * sin(rr * 6.0 - ang * 2.0 + d.y * 3.5 + id * 2.3 + Clock * .4);
            coil = lerp(.35, 1.15, coil) * (.6 + .8 * Noise(d * 2.6 + id + Clock * .1));
            float body = smoothstep(1.15, .55, rr);
            float rim = exp(-pow((rr - .78) / .16, 2)) * smoothstep(-.6, .7, d.y);
            float core = exp(-pow(rr / .45, 2));
            float3 edgeC = lerp(float3(.012, .1, .006), float3(.008, .1, .025), mint);
            float3 bodyC = lerp(float3(.07, .34, .055), float3(.045, .38, .1), mint);
            float3 hotC = lerp(float3(.44, .66, .26), float3(.32, .7, .38), mint);
            float3 col = lerp(edgeC, bodyC, smoothstep(1.05, .75, rr)) * body * coil
                       + hotC * (rim * .9 + core * .35) * body;
            // the grainy halo: sparkling grain falling off round the blob
            float g = Hash(floor(q * 1.0) + tk * 7.0 + id);
            float halo = exp(-max(rr - .9, 0) * 2.6) * smoothstep(.85, 1.1, rr) * (.25 + 1.5 * g * g);
            col += bodyC * halo * .5;
            return col * (.06 + 1.1 * b + .4 * k);
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 600 : 0;
            float tk = floor(Clock * 12.0);
            float k = Kick();
            float L = WingPx(w.y);
            float s = w.z / D;
            float y = p.y / D, Hd = H / D;
            // the whole take turns slowly between yellow-green and mint
            float mintG = smoothstep(.5, .8, Noise(float2(Clock / 25.0, side + 3.0)));

            float P = 140.0;
            float sw = s + 40.0 * Noise(float2(s / 300.0, side + 1.0));
            float ci = floor(sw / P);
            float3 c = 0;
            [unroll] for (int n = -1; n <= 1; n++)
            {
                float cc = ci + n;
                float id = cc + side;
                if (Hash(float2(id, 1)) < .22) continue;
                float xc = (cc + .5 + (Hash(float2(id, 2)) - .5) * .4) * P;
                float uc = saturate(xc * D / max(L, 1));
                float b = saturate(BandW(uc) * 1.3);
                float mint = saturate(mintG + (Hash(float2(id, 3)) - .5) * .5);
                float nb = 2.0 + floor(Hash(float2(id, 4)) * 1.99);    // two or three blobs in a column
                float r0 = Hd / (nb * 1.55) * (.75 + .5 * b + .12 * k) * (.85 + .3 * Hash(float2(id, 5)));
                [unroll] for (int m = 0; m < 3; m++)
                {
                    if (m >= nb) break;
                    float yc = Hd * (m + .5) / nb + (Hash(float2(id, 10 + m)) - .5) * 10.0 + 4.0 * sin(Clock * .35 + id + m);
                    float xb = xc + (Hash(float2(id, 20 + m)) - .5) * 14.0 + 5.0 * sin(Clock * .27 + id * 1.7 + m);
                    float rb = r0 * (.8 + .35 * Hash(float2(id, 30 + m)));
                    float mm = saturate(mint + (m == 0 ? .25 : 0));      // the upper blob runs a little bluer
                    c += Blob(float2(sw, y), float2(xb, yc), rb, mm, b * (m == nb - 1 ? 1.15 : .7), id * 3.0 + m, tk, k);   // the lowest burns hottest
                }
            }
            // a few green specks drifting in the dark
            float sp = step(.9985, Hash(floor(float2(sw / 2.0, y / 2.0)) + floor(Clock * 2.0)));
            c += float3(.1, .5, .1) * sp * (.1 + .5 * Level);
            return c;
        }
        """;
}
