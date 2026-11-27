namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 08 — the LED columns (4K take, 17.0–18.8 s red and 25–29.3 s yellow): tall columns of thick horizontal slats with
/// narrow black slits between them, standing on black with wide gaps. Down its height a column runs pale cream at the
/// top, yellow, orange, red at the foot (measured on the take, linear: .38 .33 .18 / .35 .31 .03 / .43 .14 .006); across
/// its width the light is split as through a prism: red-orange on one side, yellow in the middle, green, teal and blue
/// on the other. Now and then the take turns red: thinner slats like light streaks, red-orange with a hot cream middle
/// and cyan at the ends. The columns bend in slow S curves like light seen through a lens; every slat's end is torn to
/// its own length.
/// Every slat is a lamp of its own: it comes on when its column's band passes its mark (the marks run in loose runs),
/// the rest glow dimly; a kick knocks groups of slats sideways for a moment.
/// </summary>
public sealed class BrLedColumns : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .06f, .12f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.25f, 0f, 0f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.3f, .06f, .3f, .06f);
    public BrLedColumns() : base(Body, 0, Hex(0xFF5A14), Hex(0xFFC81E), Hex(0x5A5CFF), Hex(0x6BD23A), Hex(0xFFFFFF)) { }

    private const string Body = """
        // a column's colour down its height g (0 top, 1 foot): pale cream, yellow, orange, red at the foot; some columns
        // run pink-red or green-cyan; mode turns them all toward the red take
        float3 Down(float g, float mode, float fam)
        {
            float3 warm = lerp(float3(.42, .38, .2), float3(.4, .33, .03), smoothstep(.1, .4, g));
            warm = lerp(warm, float3(.48, .17, .008), smoothstep(.55, .78, g));
            warm = lerp(warm, float3(.48, .045, .01), smoothstep(.84, 1.0, g));
            float3 grn = lerp(float3(.06, .36, .32), float3(.12, .4, .05), smoothstep(.3, .9, g));
            float3 pnk = lerp(float3(.55, .2, .26), float3(.5, .03, .12), smoothstep(.3, 1.0, g));
            float3 c = fam < .5 ? warm : (fam < 1.5 ? pnk : grn);
            float3 red = lerp(float3(.5, .07, .02), float3(.55, .22, .04), exp(-pow((g - .45) / .3, 2)));
            return lerp(c, red, mode);
        }

        // the prism split across a column: 0 the warm side, 1 the cool side
        float3 Spec(float d)
        {
            float3 c = lerp(float3(1.0, .07, .02), float3(1.0, .32, .02), smoothstep(.0, .25, d));
            c = lerp(c, float3(1.0, .72, .06), smoothstep(.22, .45, d));
            c = lerp(c, float3(.4, .9, .06), smoothstep(.5, .7, d));
            c = lerp(c, float3(.04, .8, .55), smoothstep(.7, .86, d));
            c = lerp(c, float3(.04, .3, 1.0), smoothstep(.86, 1.0, d));
            return c;
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 300 : 0;
            float k = Kick();
            float L = WingPx(w.y);
            float s = w.z / D;
            float y = p.y / D, g = p.y / H;
            // the take turns now and then between the yellow columns and the red ones
            float mode = smoothstep(.45, .62, Noise(float2(Clock / 28.0, side + 5.0)));

            // the columns: one to a cell, 46-90 px wide, bending in slow S curves like light through a lens
            float P = 128.0;
            float sw = s + 30.0 * Noise(float2(s / 260.0, side + 1.0));
            float ci = floor(sw / P);
            float id = ci + side;
            float cw = 46.0 + 44.0 * Hash(float2(id, 1));
            float cl = 6.0 + (P - cw - 12.0) * Hash(float2(id, 2));
            float bend = (.12 + .1 * Bass) * cw;
            float ph = Hash(float2(id, 5)) * 6.28 + Clock * .12;
            float xl = cl + bend * (Hash(float2(id, 3)) - .5) * 2.0 * sin(g * 2.6 + ph);
            float xr = cl + cw + bend * (Hash(float2(id, 4)) - .5) * 2.0 * sin(g * 2.2 + ph + 1.3);
            float cx = sw - ci * P;
            float exists = step(.12, Hash(float2(id, 6)));
            float fh = Hash(float2(id, 7));
            float fam = fh < .65 ? 0 : (fh < .83 ? 1 : 2);
            float dir = Hash(float2(id, 8)) < .75 ? 1 : -1;               // which side runs warm, which cool
            float uc = saturate((ci * P + cl + cw * .5) * D / max(L, 1));
            float b = saturate(BandW(uc) * 1.35);

            // the slats: thick, a narrow black slit between (the red take: thinner streaks, wider slits)
            float pitch = lerp(17.0, 15.0, mode);
            float si = floor(y / pitch), sf = frac(y / pitch);
            float duty = lerp(.84, .6, mode);
            float slit = smoothstep(0, .08, sf) * smoothstep(duty, duty - .12, sf) * (.72 + .28 * sin(3.14159 * saturate(sf / duty)));
            // a kick knocks groups of slats sideways for a moment
            float grp = floor(si / 2.0);
            float jolt = (Hash(float2(grp + id, floor(Clock * 9.0))) - .5) * 16.0 * smoothstep(.25, 0, KickAge) * step(.5, Hash(float2(grp, id + 9)));
            float tear = lerp(3.0, 12.0, mode);
            float a0 = xl + jolt + (Hash(float2(si, id + 11)) - .5) * tear, a1 = xr + jolt + (Hash(float2(si, id + 12)) - .5) * tear * 1.3;
            float soft = lerp(2.5, 5.0, mode);                             // the film blurs the ends, the streaks most
            float inC = smoothstep(a0 - soft, a0 + soft, cx) * smoothstep(a1 + soft, a1 - soft, cx);
            float lx = saturate((cx - a0) / max(a1 - a0, 1));
            float ld = dir > 0 ? lx : 1 - lx;                              // 0 on the warm side, 1 on the cool

            // each slat is a lamp of its own: it comes on when the band passes its mark; marks run in loose runs
            float th = max(.05, .02 + .5 * Noise(float2(si / 1.6 + id * 3.1, id * .7)) + .16 * (Hash(float2(si, id + 20)) - .5));
            float on = smoothstep(th, th + .07, b);
            float lit = lerp(.11, 1.0, on);

            // the colour: down the height, split across the width like light through a prism; the top stays pale,
            // the foot red; the red take keeps its red with cyan only at the far end
            float3 dn = Down(g, mode, fam);
            float lum = dot(dn, float3(.3, .55, .15)) + .06;
            float a = lerp(.7, 0, mode) * (fam < .5 ? 1 : .55) * (1 - .55 * smoothstep(.8, 1.0, g)) * (1 - .45 * smoothstep(.3, 0, g));
            float3 col = lerp(dn, Spec(ld) * lum * 1.5, a);
            col = lerp(col, float3(.04, .55, 1.0) * lum * 1.6, smoothstep(.85, 1.0, ld) * mode * .8);
            col = lerp(col, float3(1.0, .82, .5) * .55, exp(-pow((lx - .5) / lerp(.2, .14, mode), 2)) * on * (.1 + .3 * b) * (.4 + .6 * mode));
            float across = .62 + .38 * sin(lx * 3.14159);
            float3 c = col * across * slit * inC * lit * (.45 + .8 * b + .4 * k) * exists;

            // a soft haze bleeds out of each edge into the dark: warm on one side, cool on the other
            float dxL = max(a0 - cx, 0), dxR = max(cx - a1, 0);
            float3 hzL = dir > 0 ? float3(.5, .08, .02) : float3(.03, .2, .5);
            float3 hzR = dir > 0 ? float3(.03, .2, .5) : float3(.5, .08, .02);
            c += (hzL * exp(-dxL / 4.0) * step(.01, dxL) + hzR * exp(-dxR / 4.0) * step(.01, dxR)) * slit * (.01 + .2 * b + .12 * k) * exists;
            return c;
        }
        """;
}
