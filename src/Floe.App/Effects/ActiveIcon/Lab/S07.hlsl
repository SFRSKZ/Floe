// S07 Arrival, the logogram. A ring of ink round the icon is written in one sweep, thick and thin by turns
// like calligraphy, with tendrils reaching out at a few places and fine splatter; it holds, then breaks into
// smoke threads and fades, and a new one, different, is written. Pale ink on the dark dock, hard edges.

#define RING 3.2
#define CYCLE 9.0

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    // the logogram wants a circle: the ring follows the union of the icon and a circle round it
    float dr = min(d, length(p) - (HALF + 3.0));
    float a = atan2(p.y, p.x);
    float k = floor(t / CYCLE);
    float ct = frac(t / CYCLE) * CYCLE;
    float2 seed = float2(k * 3.1, k * 1.7);

    // the brush: width along the ring, from noise on a circle so it closes on itself
    float2 cir = float2(cos(a), sin(a));
    float w = 0.25 + 3.6 * pow(Fbm(cir * 2.1 + seed), 2.6);
    // tendrils: three reaching out, each a narrow spike in angle
    float tend = 0;
    [unroll] for (int i = 0; i < 3; i++)
    {
        float ai = H11(k * 7.0 + i) * TAU;
        float sp = saturate(1.0 - abs(Ang(a - ai)) / (0.09 + 0.05 * H11(k + i * 2.0)));
        tend = max(tend, sp * sp * (3.5 + 3.0 * H11(k * 3.0 + i)));
    }
    float rough = (Fbm(p * 0.7 + seed * 5.0) - 0.5) * 2.0;
    float off = dr - RING - tend * 0.55;
    float body = w + tend * 0.45 + rough - abs(off);

    // writing: a sweep round from the start angle over the first 2.6 s
    float start = H11(k * 9.1) * TAU;
    float along = frac((a - start) / TAU);
    float sweep = smoothstep(0.0, 2.6, ct) * 1.04;
    float written = smoothstep(0.0, 0.015, sweep - along);
    // dissolving: from 6 s on the ink thins from the outside in and drifts outward
    float dis = smoothstep(6.0, 8.6, ct);
    float drift = Fbm(p * 0.35 + float2(t * 0.2, -t * 0.15) + seed) ;
    body -= dis * (drift * 6.0 + 0.5);
    float ink = smoothstep(-AA, AA, body) * written;
    // inside the stroke: wet centre darker, dry edge brighter
    float edge = smoothstep(0.0, 0.9, body);
    float3 inkRgb = lerp(float3(0.96, 0.95, 0.9), float3(0.62, 0.62, 0.6), edge * 0.6);

    // splatter dots near the ring
    float2 sc = floor(p / 1.6);
    float2 sr = H22(sc + seed);
    float splat = Fill(length(p - (sc + sr) * 1.6) - 0.35 * sr.y) * step(0.9, H21(sc + seed * 2.0)) * Glow(dr - RING, 4.0) * written * (1.0 - dis);

    // smoke threads: crisp isolines of a drifting field near the ink as it leaves
    float smokeField = Fbm(p * 0.28 + float2(0.0, -t * 0.25) + seed);
    float threads = Band(frac(smokeField * 7.0) - 0.5, 0.05) * Glow(dr - RING - dis * 3.0, 3.5) * (0.12 + dis * 0.55) * written;

    float alpha = saturate(ink + splat) * smoothstep(0.4, 1.2, d);
    float4 c = Over(Lit(inkRgb, alpha), icon);
    c = Add(c, float3(0.85, 0.85, 0.82) * threads, threads * 0.8);
    return lerp(icon, c, Room(p));
}
