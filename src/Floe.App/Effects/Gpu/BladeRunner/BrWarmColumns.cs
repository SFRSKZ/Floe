namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 05 — warm light masses through wet glass (4K take, 7.5–9.9 s): big soft masses of light with dark gaps between them.
/// A mass is an L lying on its side: a broad horizontal band of light high up that runs out softly toward its far side,
/// and a narrow column standing by its one hard edge, down to a rounded foot. The hard edge is a bright cream line with
/// a red fringe outside it; the column beside it runs pale cream-olive, or blue and violet in the colder takes; the band
/// runs cream by the edge, then orange, then red as it fades out (measured on the take: red 151 37 22, orange 190 80 41,
/// cream 179 138 63, blue 26 34 123, violet 102 60 137). The column's foot turns green or cyan. Under the band's far side
/// the mass is dark. Drops of water on the glass in front catch the light.
/// Each mass is a band: its light burns brighter and reaches further out from the hard edge as the band rises, the
/// column climbs with it, a kick flashes the edge; in silence the masses sink to embers and only the edges stay.
/// </summary>
public sealed class BrWarmColumns : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .05f, .1f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, 0f, 0f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.4f, .05f, .3f, .06f);
    public BrWarmColumns() : base(Body, 0, Hex(0xFFD23C), Hex(0xFF5A18), Hex(0x6BFF3A), Hex(0x3C8BFF), Hex(0xFFFAE8)) { }

    private const string Body = """
        // one mass: q in px, cell-local x and y; returns its light
        float3 Mass(float x, float y, float P, float id, float b, float k, float cold, float tk)
        {
            float H = Size.y / (Dip / 1.5);
            if (Hash(float2(id, 1)) < .12) return 0;
            float len = P * (.74 + .18 * Hash(float2(id, 2)));             // from the hard edge to where the band runs out
            float x0 = (P - len) * (.25 + .5 * Hash(float2(id, 3)));
            float hardL = Hash(float2(id, 4)) < .5 ? 1 : 0;                // the hard edge on the left or the right
            float e = hardL > 0 ? x - x0 : x0 + len - x;                   // 0 on the hard edge, growing inward
            if (e < -14.0) return 0;
            float wob = Noise(float2(y / 18.0 + id, Clock * .25));

            // the shape: a high band and a column by the hard edge, joined by a broad soft curve (an L on its side)
            float yT = H * (.05 + .07 * Hash(float2(id, 5)));
            float yB = H * (.48 + .14 * Hash(float2(id, 6)) + .04 * sin(Clock * .35 + id)) + .06 * e;   // sags outward
            float cwid = 34.0 + 40.0 * Hash(float2(id, 7));
            float foot = H * (.62 + .32 * saturate(.3 + .8 * b));            // the column climbs with the band
            float r = 14.0;
            float dB = max(yT - y, y - yB);
            float2 qf = float2(max(r - e, 0), max(y - (foot - r), 0));
            float dC = max(max(e - cwid, y - foot), yT - y);
            dC = y > foot - r && e < r ? length(qf) - r : dC;
            float kk = 26.0;
            float hsm = saturate(.5 + .5 * (dC - dB) / kk);
            float d = lerp(dC, dB, hsm) - kk * hsm * (1 - hsm);          // a smooth union: the inner corner rounds
            float sf = lerp(4.0, 18.0, saturate(e / (len * .6)));             // crisp by the hard edge, blurred far out
            float shape = smoothstep(sf, -sf, d);
            float colness = smoothstep(cwid + 30.0, cwid - 6.0, e);
            float reach = len * (.35 + .65 * b + .1 * k) * (.9 + .2 * wob);
            float fade = max(smoothstep(reach, reach * .2, e), colness);
            float hard = smoothstep(-1.0, 1.0, e);
            float m = shape * fade * hard;

            // colour: cream by the edge, orange, red as the band fades out; the column olive or blue-violet,
            // in the cold takes a red stripe between the edge line and the blue
            float t = saturate(e / max(reach, 1));
            float3 bc = lerp(float3(.75, .4, .12), float3(1.0, .22, .04), smoothstep(.1, .4, t));
            bc = lerp(bc, float3(.8, .04, .02), smoothstep(.4, .85, t));
            float3 colC = lerp(float3(.3, .22, .05), float3(.03, .05, .55), cold);
            colC = lerp(colC, lerp(float3(.16, .13, .04), float3(.25, .07, .5), cold), smoothstep(.4, 1.0, e / cwid));
            colC = lerp(colC, float3(.9, .05, .04), exp(-pow((e - 9.0) / 4.0, 2)) * cold);
            float green = Hash(float2(id, 8)) < .5 ? 1 : 0;
            colC = lerp(colC, lerp(float3(.03, .4, .05), float3(.02, .3, .7), green), smoothstep(foot - 30.0, foot, y) * .8);
            float inBand = smoothstep(yB + 10.0, yB - 6.0, y);
            float3 c = lerp(colC, bc, inBand * (1 - colness * (.6 + .4 * cold))) * m;
            // shimmer inside the light
            c *= .75 + .5 * Noise(float2(x / 40.0 + id, y / 26.0 + Clock * .15));

            // the hard edge: a cream line, a red fringe just outside it, a thin blue one just inside
            float vis = step(y, foot) * smoothstep(yT - 6.0, yT + 6.0, y);
            float scan = exp(-pow((e - 1.5) / 1.6, 2)) * vis;
            float red = exp(-pow((e + 3.5) / 2.5, 2)) * vis;
            float blu = exp(-pow((e - 6.0) / 2.2, 2)) * vis * (.4 + .6 * cold);
            float lvl = .04 + .9 * b + .35 * k;
            float3 o = c * lvl * 1.05;
            o += float3(1.0, .82, .55) * scan * (.12 + .9 * b + 1.2 * k);
            o += float3(1.0, .08, .04) * red * (.05 + .6 * b + .6 * k);
            o += float3(.1, .2, 1.0) * blu * (.03 + .4 * b + .4 * k);

            // drops of water on the glass: each one a small lens catching the light behind it
            float2 dp = float2(x + id * 37.0, y - Clock * 1.5) / 7.0;
            float2 di = floor(dp);
            float2 df = frac(dp) - (.5 + (float2(Hash(di + id), Hash(di + id + 3.3)) - .5) * .5);
            float dr = .14 + .18 * Hash(di + id + 1.7);
            float dd = length(df);
            float dOn = step(.6, Hash(di + id + 9.0));
            float spec = exp(-pow(length(df + float2(.08, .08)) / (dr * .4), 2));
            float rim = exp(-pow((dd - dr) / .05, 2));
            o *= 1 - dOn * .18 * smoothstep(dr, dr - .05, dd) + dOn * (1.2 * spec + .4 * rim) * smoothstep(.05, .3, m);
            return o;
        }

        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5;
            float side = w.y > 0 ? 500 : 0;
            float tk = floor(Clock * 12.0);
            float k = Kick();
            float L = WingPx(w.y);
            float s = w.z / D + Clock * 3.0;
            float y = p.y / D;
            // the take turns now and then from the warm masses to the cold ones, with blue and violet columns
            float coldG = smoothstep(.5, .7, Noise(float2(Clock / 26.0, side + 2.0)));

            float P = 640.0;
            float sw = s + 40.0 * Noise(float2(s / 600.0, side + 1.0));
            float ci = floor(sw / P);
            float3 c = 0;
            [unroll] for (int n = -1; n <= 0; n++)
            {
                float cc = ci + n;
                float id = cc + side;
                float uc = saturate(((cc + .5) * P - Clock * 3.0) * D / max(L, 1));
                float b = saturate(BandW(uc) * 1.3);
                float cold = saturate(coldG + (Hash(float2(id, 9)) < .35 ? .8 : 0));
                c += Mass(sw - cc * P, y, P, id, b, k, cold, tk);
            }
            return c;
        }
        """;
}
