// F09 Maleficent, thorns and green fire. Two thorny vines grow from under the icon up both sides and meet
// over the top, weaving over and under each other; they are shaded as round stems (dark, a hard highlight
// on the lit side) with sharp thorns. Green hellfire licks up off them in hard bands, sparks lift off.
// After a while the vines wither back and grow again.

#define CYCLE 10.0
#define LANE 4.6

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float a = atan2(p.y, p.x);
    float ct = frac(t / CYCLE) * CYCLE;
    float grow = smoothstep(0.0, 2.8, ct) * (1.0 - smoothstep(8.6, 9.9, ct));
    // angle from the bottom, 0 .. 1 at the top, the same on both sides
    float fromBottom = abs(Ang(a - PI * 0.5)) / PI;
    float shown = step(fromBottom, grow * 1.02);

    float4 vines = float4(0, 0, 0, 0);
    float cover = 0;
    float freq = 7.0;
    [unroll] for (int k = 0; k < 2; k++)
    {
        float sgn = k == 0 ? 1.0 : -1.0;
        float wv = sin(a * freq + t * 0.0);
        float center = LANE + 1.8 * wv * sgn;
        float w = 1.05 * (1.0 - fromBottom * 0.35);
        float s = (d - center) / w;
        float stem = Fill((abs(s) - 1.0) * w);
        float3 n = float3(s, 0, sqrt(saturate(1.0 - s * s)));
        float lit = saturate(dot(n, normalize(float3(-0.6, 0, 0.8))));
        float3 rgb = float3(0.04, 0.09, 0.05) + float3(0.18, 0.32, 0.14) * lit + Band((s + 0.45) * w, 0.12) * float3(0.4, 0.75, 0.35);
        // thorns: pointing out from the stem every so often
        float N = 34.0;
        float cell = a * N / TAU + k * 0.5;
        float tx = (frac(cell) - 0.5) * TAU * (HALF + LANE) / N;
        float side = H11(floor(cell) * 3.1 + k) > 0.5 ? 1.0 : -1.0;
        float ty = (d - center) * side - w;
        float thorn = step(0.0, ty) * Fill(abs(tx) - 0.75 * saturate(1.0 - ty / 2.4)) * step(ty, 2.4) * step(0.3, H11(floor(cell) + k * 9.0));
        float3 thornRgb = float3(0.05, 0.1, 0.06) + float3(0.3, 0.5, 0.25) * step(0.0, tx);
        float m = saturate(stem + thorn) * shown;
        float3 col = lerp(thornRgb, rgb, stem);
        // weave: this vine is on top where its wave is rising
        float onTop = step(0.0, cos(a * freq) * sgn);
        float4 layer = float4(col * m, m);
        vines = onTop > 0.5 ? Over(layer, vines) : Over(vines, layer);
        cover = max(cover, m);
    }

    // green fire off the vines: hard bands, mostly upward
    float up = saturate(-p.y / HALF * 0.6 + 0.6);
    float n = Fbm(float2(p.x * 0.3, p.y * 0.18 + t * 2.4));
    float f = 1.0 - abs(d - LANE) / (3.0 + 6.0 * up) + (n - 0.6) * 1.3;
    f *= shown * grow;
    float b1 = smoothstep(0.32, 0.36, f), b2 = smoothstep(0.6, 0.64, f), b3 = smoothstep(0.82, 0.86, f);
    float3 fireRgb = lerp(lerp(float3(0.05, 0.35, 0.08), float3(0.25, 0.95, 0.2), b2), float3(0.85, 1.0, 0.6), b3);
    float fireA = b1 * 0.85;

    float spk = 0;
    [unroll] for (int i = 0; i < 7; i++)
    {
        float fi = i;
        float life = frac(t * 0.7 + H11(fi * 2.7));
        float born = floor(t * 0.7 + H11(fi * 2.7));
        float sa = -PI * 0.5 + (H11(fi + born * 5.0) - 0.5) * 2.6;
        float2 c = float2(cos(sa), sin(sa)) * (HALF + LANE) + float2(sin(life * 8.0 + fi) * 1.5, -life * 12.0);
        spk += Fill(length(p - c) - 0.45 * (1.0 - life)) * (1.0 - life);
    }
    spk *= grow;

    float4 c = Over(icon, Lit(fireRgb, fireA * 0.6));
    c = Over(vines, c);
    c = Add(c, fireRgb * fireA * 0.5 * (1.0 - cover), 0);
    c = Add(c, float3(0.6, 1.0, 0.45) * spk * 1.3, spk);
    return lerp(icon, c, Room(p));
}
