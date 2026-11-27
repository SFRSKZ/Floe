namespace Floe.App.Effects.Gpu.BladeRunner;

/// <summary>
/// 03 — the pulse line (4K take, 2.5–5.0 s): a thin red-orange line, beaded with small hot dots, runs along the strip.
/// By the dock tall blue-violet needles stand across it, mirrored up and down, each with a yellow-white swelling where it
/// crosses. Further out the beads swell into soft red-orange bells above the line, pink-white at their foot, and below
/// it, across a dark gap, blue drops hang, narrow at the top and round at the bottom, turning teal-green at the tip.
/// Toward the screen edge bells and drops grow and fuse: red-orange above, a pink-white band on the line, magenta,
/// blue and teal below. Every bell and drop is a band: its height is its level; a kick lifts them all and flares the line.
/// </summary>
public sealed class BrPulseLine : BrScene
{
    protected override (float Erode, float Grille, float Mottle, float Dust) Material => (0f, .04f, .1f, .04f);
    protected override (float Slip, float Mesh, float Burn, float Pitch) Material2 => (0f, 0f, 0f, 3.5f);
    protected override (float Bloom, float Streak, float Chroma, float Grain) Film => (.35f, .04f, .4f, .06f);

    public BrPulseLine() : base(Body, 0, Hex(0xFFE9C8), Hex(0x2D4BFF), Hex(0xFF5A24), Hex(0xFF3FA8), Hex(0x4FD0A0)) { }

    private const string Body = """
        float3 Scene(float2 p)
        {
            float3 w = Wing(p.x);
            if (w.y == 0) return (float3)0;
            float D = Dip / 1.5, H = Size.y;
            float side = w.y > 0 ? 50 : 0;
            float cy = H * .46;
            float dy = (p.y - cy) / D;                    // + below the line
            float ady = abs(dy);
            float k = Kick();
            float L = WingPx(w.y);
            float tt = floor(Clock * 6.0);
            float3 c = 0;

            // now and then the band jumps sideways for a few frames (a tape tear)
            float tear = smoothstep(.02, .12, Level) * step(.94, Hash(float2(tt, 71))) * (Hash(float2(tt, 72)) - .5) * 30.0 * D;
            float z = max(w.z + tear * step(.3, Hash(float2(floor(p.y / (6 * D)), tt))), 0);
            float u = saturate(z / L);
            float hA = cy / D, hB = (H - cy) / D;           // room above and below the line

            // ---- needles by the dock: tall, thin, blue-violet, a yellow-white swelling on the line ----
            float spN = 34.0 * D;
            float jN = floor(z / spN), fN = (frac(z / spN) - .5) * spN / D;
            float uN = saturate((jN + .5) * spN / L);
            float bN = saturate(BandW(uN) * 1.3);
            float nMask = (1 - smoothstep(.12, .26, uN)) * step(.2, Hash(float2(jN + side, floor(Clock * 2.0 + Hash(float2(jN, 4))))));
            float nH = (ady < 0 ? 0 : (dy < 0 ? hA : hB)) * (.45 + .55 * bN + .2 * k) * (.8 + .2 * Hash(float2(jN, 7)));
            float sN = ady / max(nH, 1);
            float nW = (1.3 + 1.6 * bN) * (1 + 2.2 * exp(-pow(ady / 5.0, 2)));        // the swelling where it crosses
            float needle = smoothstep(nW + 1.0, nW - .6, abs(fN)) * smoothstep(1, .6, sN);
            float nCore = exp(-pow(fN / max(nW * .5, .4), 2));
            float3 nCol = lerp(float3(.55, .2, 1.1), float3(.15, .3, 1.15), nCore);
            nCol = lerp(nCol, float3(1.0, .85, .45), exp(-pow(ady / 4.0, 2)) * nCore);
            c += nCol * needle * nMask * (.08 + 1.2 * bN + .5 * k) * (1 - .6 * sN);

            // ---- bells above, drops below, growing outward and fusing toward the screen edge ----
            float sp = lerp(14.0, 24.0, u) * D;
            float j = floor(z / sp), f = (frac(z / sp) - .5) * sp / D, spD = sp / D;
            float uj = saturate((j + .5) * sp / L);
            float b = saturate(BandW(uj) * 1.3);
            float grow = smoothstep(.22, .9, uj);              // how far out along the strip we are
            float fuse = smoothstep(.6, .92, uj);
            float wob = .85 + .3 * Noise(float2(j * .7 + side, Clock * .3));
            if (dy < 0)
            {
                // a bell: a soft rounded peak, red-orange, pink-white at its foot
                float h = hA * (.06 + (.22 + .8 * b) * grow + .15 * k) * wob;
                float s = ady / max(h, 1);
                float halfW = .5 * spD * lerp(.4, 1.15, grow) * pow(saturate(1 - s * s), .65) * (1 - .3 * s);
                float soft = 1.2 + .3 * halfW;
                float bell = smoothstep(halfW + soft, halfW - soft * .5, abs(f)) * smoothstep(1.05, .7, s);
                float3 col = lerp(float3(1.0, .6, .55), float3(1.0, .26, .08), smoothstep(.0, .25, s));
                col = lerp(col, float3(1.0, .12, .1), smoothstep(.5, 1.0, s));
                // a thin blue fringe sits above the small bells
                float fr = exp(-pow((s - 1.0) / .12, 2)) * smoothstep(halfW + 3.0, halfW, abs(f)) * (1 - fuse);
                c += col * bell * (.05 + .85 * b + .35 * k) * (1 - fuse) + float3(.2, .3, 1.0) * fr * .25 * b;
                // fused: a red wall up to the top with a rippled crest
                float hF = hA * (.15 + .8 * b + .15 * k) * (.92 + .08 * cos(f / spD * 6.2832));
                float sF = ady / max(hF, 1);
                float3 fc = lerp(float3(1.0, .62, .66), float3(1.0, .2, .1), smoothstep(.0, .14, sF));
                c += fc * smoothstep(1.0, .75, sF) * fuse * (.05 + .8 * b + .35 * k);
            }
            else
            {
                // a drop hangs below a dark gap: narrow at the top, round at the bottom, blue to teal-green
                float gap = 3.0 + 2.0 * (1 - grow);
                float h = hB * (.1 + (.15 + .75 * b) * grow + .12 * k) * wob;
                float t = (dy - gap) / max(h, 1);
                float prof = pow(saturate(sin(PI * pow(saturate(t), 2.2))), .75) * step(0, t) * step(t, 1);
                float halfW = .5 * spD * lerp(.3, .95, grow) * prof;
                float drop = saturate((halfW - abs(f)) / (1.4 + .35 * halfW) + .5) * step(.04, prof) * smoothstep(.0, .08, t);
                float3 col = lerp(float3(.1, .26, 1.1), float3(.1, .7, .85), smoothstep(.6, .95, t));
                col = lerp(col, float3(.3, .9, .45), smoothstep(.92, 1.0, t));
                c += col * drop * smoothstep(.2, .45, uj) * (.05 + .85 * b + .35 * k) * (1 - fuse);
                // fused: magenta on the line, then blue, teal and green toward the foot
                float hF = hB * (.2 + .75 * b + .12 * k) * (.9 + .1 * cos(f / spD * 6.2832 + 3.14));
                float sF = ady / max(hF, 1);
                float3 fc = lerp(float3(1.0, .25, .75), float3(.15, .38, 1.1), smoothstep(.05, .35, sF));
                fc = lerp(fc, float3(.1, .7, .8), smoothstep(.45, .8, sF));
                fc = lerp(fc, float3(.3, .85, .4), smoothstep(.8, 1.0, sF));
                c += fc * smoothstep(1.0, .7, sF) * fuse * (.05 + .75 * b + .3 * k) * (1 - .3 * smoothstep(.6, 1, sF));
            }

            // ---- the line: thin red-orange, beaded with small hot dots, a pink glow round its foot ----
            float jit = 1 + .25 * (Noise(float2(z * .05 / D, Clock * 3)) - .5);
            float lvl = .25 + .8 * Level + 1.0 * k;
            float coreL = exp(-pow(ady / (1.1 * jit), 2));
            float bead = exp(-pow(f / (1.6 + 2.0 * grow), 2)) * exp(-pow(ady / (1.8 + 1.5 * grow), 2)) * (1 - fuse * .5);
            c += float3(1.0, .3, .1) * coreL * lvl * .9 + float3(1.0, .7, .45) * bead * (.1 + .9 * b + .4 * k);
            c += float3(1.0, .35, .45) * exp(-ady / 5.0) * (.03 + .25 * Bass) * (.3 + grow);
            return c;
        }
        """;
}
