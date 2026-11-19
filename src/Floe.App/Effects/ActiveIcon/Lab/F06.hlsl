// F06 How to Train Your Dragon, plasma fire. Flames rise off the icon in hard toon bands, white core to cyan
// to violet to a deep purple lip, tallest over the top, licking up the sides; the icon's edge takes a blue
// rim light from them. Embers spin up and wink out.

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float h = HALF;
    // how tall flames may be here: tall over the top, short underneath
    float top = saturate(-p.y / h * 0.5 + 0.5);
    float tall = lerp(2.5, 10.5, top * top);
    // noise rising, stretched upward
    float2 nq = float2(p.x * 0.24, p.y * 0.14 + t * 2.6);
    float n = Fbm(nq) + 0.35 * VNoise(float2(p.x * 0.6, p.y * 0.3 + t * 4.5));
    float f = 1.0 - d / tall + (n - 0.62) * 1.25;
    f *= smoothstep(-0.5, 0.8, d);
    float b1 = smoothstep(0.0 - 0.03, 0.0 + 0.03, f);
    float b2 = smoothstep(0.28 - 0.03, 0.28 + 0.03, f);
    float b3 = smoothstep(0.55 - 0.03, 0.55 + 0.03, f);
    float b4 = smoothstep(0.8 - 0.03, 0.8 + 0.03, f);
    float3 rgb = float3(0.28, 0.06, 0.45) * b1;
    rgb = lerp(rgb, float3(0.55, 0.25, 1.0), b2);
    rgb = lerp(rgb, float3(0.35, 0.8, 1.0), b3);
    rgb = lerp(rgb, float3(0.92, 0.98, 1.0), b4);
    float alpha = b1 * 0.9;

    // embers spinning up
    float em = 0;
    [unroll] for (int i = 0; i < 9; i++)
    {
        float fi = i;
        float life = frac(t * 0.6 + H11(fi * 1.9));
        float born = floor(t * 0.6 + H11(fi * 1.9));
        float x0 = (H11(fi * 3.7 + born) - 0.5) * (2.0 * h + 6.0);
        float2 c = float2(x0 + sin(life * 9.0 + fi) * 2.5, -h * 0.6 - life * 18.0);
        em += Fill(length(p - c) - 0.5 * (1.0 - life)) * (1.0 - life) * step(0.5, frac(t * 8.0 + fi * 0.37) + 0.3);
    }

    float rim = Band(d + 0.5, 0.6) * icon.a * (0.4 + 0.4 * top);
    float4 c = Over(icon, Lit(rgb, alpha));
    c = Add(c, float3(0.45, 0.8, 1.0) * rim * 0.6, 0);
    c = Add(c, float3(0.85, 0.75, 1.0) * em * 1.3, em);
    return lerp(icon, c, Room(p));
}
