// S09 Iron Man, the HUD. Rings in one plane that tilts and sways in 3D round the icon: a tick ring turning,
// a segmented band turning the other way with one amber segment, a fine dashed ring with a radar sweep, and
// data marks. The near half of the plane runs over the icon, the far half behind it. Corner brackets lock on.

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float incl = 0.95 + 0.12 * sin(t * 0.5);
    float node = 0.22 * sin(t * 0.33);
    float2 q = Rot(p, -node);
    q.y /= cos(incl);
    float r = length(q);
    float a = atan2(q.y, q.x);
    float front = step(0.0, q.y);
    float3 cyan = float3(0.45, 0.9, 1.0);
    float3 amber = float3(1.0, 0.68, 0.22);
    // the plane is foreshortened: line widths in plane units grow where it is squeezed
    float sq = lerp(1.0, 1.0 / cos(incl), abs(q.y) / max(r, 1e-3));
    float lw = 0.22 * sq;

    // tick ring
    float a1 = a - t * 0.25;
    float tickN = 72.0;
    float tf = frac(a1 * tickN / TAU) - 0.5;
    float major = step(abs(frac(a1 * 6.0 / TAU + 0.5 / tickN) - 0.5 / tickN), 0.5 / tickN);
    float tlen = lerp(1.1, 2.4, major);
    float ticks = Band(tf * TAU * r / tickN, lw * 0.9) * step(r, 37.2) * step(37.2 - tlen, r);
    float ring1 = Band(r - 37.3, lw * 0.8);

    // segmented band
    float a2 = a + t * 0.45;
    float seg = frac(a2 * 7.0 / TAU);
    float segOn = step(0.1, seg) * step(seg, 0.86);
    float segId = floor(a2 * 7.0 / TAU);
    float isAmber = step(abs(Ang(segId - 2.0 * floor(segId / 2.0))), 0.1) * step(0.7, H11(segId + 50.0));
    float bandOutline = (Band(r - 34.4, lw) + Band(r - 32.6, lw)) * segOn;
    float bandFill = step(32.6, r) * step(r, 34.4) * segOn * 0.22;

    // dashed ring and sweep
    float dash = Band(r - 30.3, lw * 0.8) * step(0.5, frac(a * 48.0 / TAU));
    float sw = frac((a - t * 1.6) / TAU);
    float sweep = step(28.5, r) * step(r, 37.0) * pow(1.0 - sw, 10.0) * 0.4;
    float sweepEdge = Band(sw * TAU * r, lw * 1.2) * step(28.5, r) * step(r, 37.0);

    // data marks: little bars of "text" at a few angles between the rings
    float da = frac((a + t * 0.1) * 5.0 / TAU);
    float block = step(0.04, da) * step(da, 0.16) * step(35.2, r) * step(r, 36.2);
    float glyphs = step(0.45, H21(float2(floor(da * 60.0), floor(t * 3.0)))) * block;

    float3 rgb = cyan * (ticks + ring1 + dash + bandOutline + bandFill + sweep + sweepEdge * 1.3 + glyphs);
    rgb = lerp(rgb, amber * (bandOutline + bandFill * 1.6), isAmber * step(0.01, bandOutline + bandFill));
    float alpha = saturate(ticks + ring1 + dash + bandOutline + bandFill + sweep + sweepEdge + glyphs);
    float4 hud = float4(rgb, alpha);

    // corner brackets on the icon's box, snapping in once per cycle
    float lock = 1.0 - pow(saturate(frac(t * 0.25) * 4.0), 0.4) * (1.0 - step(0.25, frac(t * 0.25)));
    float m = HALF + 3.0 + (1.0 - lock) * 3.0;
    float2 ap = abs(p);
    float br = (Band(ap.x - m, 0.28) * step(m - 7.0, ap.y) * step(ap.y, m + 0.28))
             + (Band(ap.y - m, 0.28) * step(m - 7.0, ap.x) * step(ap.x, m + 0.28));
    br = saturate(br) * (0.6 + 0.4 * lock);

    float4 c = Over(icon, hud * (1.0 - front));
    c = Over(hud * front * lerp(1.0, 0.75, icon.a), c);
    c = Add(c, cyan * br, br);
    return lerp(icon, c, Room(p));
}
