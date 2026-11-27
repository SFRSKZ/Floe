namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 06 — the scan-line blocks (4K take, 13.0–14.8 s): big blocks standing side by side on black, each a stack of
/// fine horizontal lines. A line is a lumpy, beaded band of light that wavers along its length and ends in a frayed
/// point; between the lines a dark maroon seam. Most blocks are ice-blue and white, some orange and yellow, some deep
/// blue or red-violet. A thin cyan-green fringe runs down a block's torn left edge, a row of little rings sits on top of
/// some of them, and toward the foot the lines break up into a dim speckle of their own colour.
/// Each block is one band: the louder it is, the further down its lines stay whole and the brighter they burn; quiet,
/// it crumbles from the foot up. A kick jolts groups of lines sideways for a moment; every few seconds of music the
/// blocks are dealt anew.
/// </summary>
public sealed class BrGlitchBlocks : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .08f, .1f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (.4f, .1f, 0f, 3f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.2f, .05f, .35f, .06f);
    public BrGlitchBlocks() : base(Body, 0, Hex(0xFF8A1E), Hex(0xDDE8FF), Hex(0xC8207A), Hex(0xFFC830), Hex(0x2A3AD0)) { }

    private const string Body = """
        // the block kinds: line colour (dim end, hot end) and seam colour
        // 0 ice-white, 1 orange-yellow, 2 deep blue, 3 red-violet
        static const float3 LA[4] = { float3(.2, .55, 1.1), float3(1.0, .32, .03), float3(.08, .2, .9), float3(.75, .08, .35) };
        static const float3 LB[4] = { float3(.62, .9, 1.1), float3(1.0, .78, .12), float3(.3, .55, 1.1), float3(1.0, .35, .6) };
        static const float3 SM[4] = { float3(.035, .002, .012), float3(.14, .012, .003), float3(.006, .004, .035), float3(.04, .0, .03) };

        float Kind(float h) { return h < .55 ? 0 : h < .78 ? 1 : h < .9 ? 2 : 3; }

        float3 Scene(float2 p)
        {
            float D = Dip / 1.5;
            float3 w0 = Wing(p.x);
            if (w0.y == 0) return (float3)0;
            float H = Size.y, yn = p.y / H;
            float side = w0.y > 0 ? 500 : 0;
            float tk = floor(Clock * 12.0);
            float deal = floor(Clock / 4.0);                         // the layout is dealt anew every ~4 s of music
            float k = Kick();

            // the lines: ~30 to the strip's height
            float pitch = 4.2 * D;
            float row = floor(p.y / pitch);
            // a kick jolts groups of lines sideways for a moment
            float grp = floor(row / 3.0);
            float jolt = (Hash(float2(grp + side, floor(Clock * 9.0))) - .5) * 22.0 * D * smoothstep(.25, 0, KickAge) * step(.55, Hash(float2(grp, deal + 3)));
            float s = (w0.z + jolt) / D;

            // the blocks: one to a cell of ~150-260 px, the strip warped so no two cells match
            float sw = s + 60.0 * Noise(float2(s / 300.0, side + deal * .37));
            float P = 200.0;
            float ci = floor(sw / P), cx = sw - ci * P;
            float id = ci + side + deal * 13.0;
            float bw = P * (.5 + .42 * Hash(float2(id, 1)));
            float bl = (P - bw) * Hash(float2(id, 2));
            float kd = Kind(Hash(float2(id, 3)));
            float exists = step(.12, Hash(float2(id, 4)));
            float uc = saturate((ci * P + bl + bw * .5) * D / max(WingPx(w0.y), 1));
            float b = saturate(BandW(uc) * 1.35);

            // every line tears to its own length at both ends; the right end runs out in a frayed point
            float rgL = (Hash(float2(row + side, id + tk * .37)) - .5) * 8.0 + (Noise(float2(row * .4, id)) - .5) * 10.0;
            float rgR = (Hash(float2(row - side, id * 1.3 + tk * .37)) - .5) * 10.0 + (Noise(float2(row * .4, id + 9)) - .5) * 8.0;
            float xL = bl + rgL, xR = bl + bw + rgR;
            float lx = cx - xL;
            float top = H * (.02 + .12 * step(.6, Hash(float2(id, 5))) * Hash(float2(id, 6)));

            // the line itself: lumpy, beaded, wavering
            float wv = (Noise(float2(cx / 40.0 + id, row * .7 + Clock * .2)) - .5) * 1.4 * D;
            float fy = frac((p.y + wv) / pitch) - .5;
            float hw = .07 + .28 * pow(Noise(float2(cx / 4.0 + row * 3.1 + id, tk * .15)), 1.5) * (.5 + .7 * Noise(float2(cx / 17.0 + row, id)));
            float taper = smoothstep(xR + 1.0, xR - 14.0, cx);        // the pointed right end
            hw *= lerp(.25, 1.0, taper);
            float core = smoothstep(hw + .07, hw - .05, abs(fy)) * (.75 + .5 * smoothstep(.12, .3, hw));
            float inRow = smoothstep(xL - 1.0, xL + 1.0, cx) * step(cx, xR + 1.0) * step(top, p.y);
            // a few wisps run on past the end
            float wisp = exp(-pow(fy / .07, 2)) * smoothstep(xR + 14.0, xR, cx) * step(xR, cx) * step(.6, Hash(float2(row, id + 21)));

            float3 la = LA[(int)kd], lb = LB[(int)kd], sm = SM[(int)kd];
            float heat = Noise(float2(cx / 30.0 + id, p.y / (16.0 * D) + id * .3)) * .75 + .25 * Hash(float2(row, id + 8));
            float3 lineC = lerp(la, lb, saturate(heat * 1.2 - .1));
            // the light in a line is pitted with small dark flecks
            lineC *= .68 + .42 * Noise(float2(cx / 2.5, p.y / (1.6 * D) + tk * .3));
            // the torn left edge has a thin cyan-green fringe
            float fringe = exp(-pow((lx - 1.5) / 2.2, 2)) * inRow;

            // the meter: the louder the block's band, the further down its lines stay whole; below that they crumble
            float whole = .4 + .6 * b + (Noise(float2(cx / 30.0 + id, 3.3)) - .5) * .25;
            float crumbleT = smoothstep(whole - .05, whole + .2, yn);
            float2 gq = floor(float2(cx / 1.6, p.y / (1.4 * D)));
            float speck = step(.6 + .3 * crumbleT, Hash(gq + id + tk * .21));
            // crumbled: a dim speckle of the line's own colour drifting to teal, the seams gone dark
            float3 crumbC = lerp(lineC, float3(.08, .45, .55), .45) * core * speck * .3;
            float3 whole3 = lerp(sm, lineC, core);
            float3 c = (lerp(whole3, crumbC, crumbleT) + float3(.1, 1.0, .65) * fringe * .35 * (1 - crumbleT)) * inRow
                     + lb * wisp * .5 * (1 - crumbleT);

            // a row of little rings on top of some ice blocks
            float rings = 0;
            if (kd < .5 && Hash(float2(id, 7)) > .4)
            {
                float rp = 15.0;
                float rx = frac((cx - bl) / rp) * rp - rp * .5, ry = p.y - (top + 6.0 * D);
                float rr = length(float2(rx, ry * 1.1));
                rings = exp(-pow((rr - 4.3 * D) / (.9 * D), 2)) * step(bl, cx) * step(cx, bl + bw);
            }
            c += lerp(float3(1.0, .25, .3), float3(.4, .7, 1.1), Hash(float2(floor((cx - bl) / 15.0), id))) * rings * .6;

            c *= exists * (.05 + 1.15 * b + .4 * k);
            // the black between the blocks: faint ghosts of the lines run on
            float ghost = exp(-pow(fy / .12, 2)) * .006 * (.4 + Level) * Noise(float2(s / 50.0, row));
            c += float3(.4, .6, 1.0) * ghost;
            return c;
        }
        """;
}
