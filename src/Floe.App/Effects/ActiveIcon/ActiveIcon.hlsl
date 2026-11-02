// Effects for the active dock icon, drawn on the icon itself with padding around it.
// Field holds the icon's signed distance in DIP ((r - 0.5) * 32; positive outside),
// so the neon and the glass follow each icon's real silhouette, round or square.
sampler2D Input : register(s0);
sampler2D Field : register(s1);
float Time : register(c0);
float Mode : register(c1);      // 0 neon tube, 1 liquid glass frame
float4 ColorA : register(c2);
float4 ColorB : register(c3);
float Strength : register(c4);
float2 Box : register(c5);      // x: padded size in DIP

float Dist(float2 uv)
{
    return (tex2D(Field, uv).r - 0.5) * 32.0;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float total = max(Box.x, 1.0);
    float px = 1.0 / total;
    float4 icon = tex2D(Input, uv);
    float d = Dist(uv);
    float2 p = (uv - 0.5) * total;
    float a = atan2(p.y, p.x);
    // 2 DIP apart: the 8-bit field is too coarse for a smooth normal over 1 DIP
    float2 g = float2(Dist(uv + float2(2 * px, 0)) - Dist(uv - float2(2 * px, 0)),
                      Dist(uv + float2(0, 2 * px)) - Dist(uv - float2(0, 2 * px)));
    g = g / max(length(g), 1e-4);
    float k = saturate(Strength);

    if (Mode < 0.5)
    {
        // a tube hugging the edge 3 DIP out, colour flowing round it, the odd flicker
        float tb = d - 3.2;
        float core = exp(-tb * tb / 0.5);
        float glow = exp(-abs(tb) / 2.0) * 0.5 + exp(-abs(tb) / 5.0) * 0.14;
        float3 tint = lerp(ColorA.rgb, ColorB.rgb, 0.5 + 0.5 * sin(a + Time * 1.5));
        float flick = 0.88 + 0.12 * step(0.9, frac(sin(floor(Time * 18.0) * 12.9898) * 43758.5453));
        float outside = smoothstep(-0.5, 1.0, d) * k;
        float3 neon = (core * 0.45 + tint * (core * 1.1 + glow * 2.2)) * flick * outside;
        float alpha = saturate(max(core * 0.9, glow * 1.2)) * outside;
        // premultiplied: rgb above alpha adds light, which is what a neon should do
        return float4(icon.rgb * (1 - alpha) + neon, saturate(icon.a * (1 - alpha) + alpha));
    }

    // the click's glass rings, carried on: light ripples run out from the icon's edge and come back, again
    // and again. They only light the space around the icon; the icon itself is never bent or covered.
    float2 key = normalize(float2(-0.486, -0.874));
    float lit = pow(saturate(0.5 + 0.5 * dot(g, key)), 5.0);
    float fill = pow(saturate(0.5 + 0.5 * dot(g, float2(0.8, 0.6))), 5.0);
    float waves = 0;
    [unroll] for (int i = 0; i < 3; i++)
    {
        // each ripple swings out and back on its own beat, the outer ones later and fainter
        float swing = 0.5 - 0.5 * cos(Time * 1.7 - i * 0.95);
        float at = 1.6 + i * 2.3 + swing * 2.6;
        float x = (d - at - 0.12 * sin(a * 3.0 + Time * 1.3)) / (0.32 + 0.08 * i);
        waves += exp(-x * x) * pow(0.62, i) * (0.55 + 0.45 * swing);
    }

    // outside the icon only, fading out before the edge of the room the effect may draw in
    float room = 0.5 * total - max(abs(p.x), abs(p.y));
    float outside = smoothstep(0.2, 1.4, d) * smoothstep(0.0, 3.0, room) * k;
    float3 tint = lerp(float3(0.65, 0.80, 0.94), lerp(ColorA.rgb, ColorB.rgb, 0.5 + 0.5 * sin(a + Time * 0.8)), 0.4);
    float3 glow = (tint * (0.35 + lit * 0.9 + fill * 0.35) + float3(1, 1, 1) * lit * 0.25) * waves * outside * 0.6;
    float alpha = saturate(max(glow.r, max(glow.g, glow.b)));
    // inside the icon the click's own glass drop keeps going (DockWindow.ClickGlass); this effect only
    // lights the space around it
    float4 inner = icon;

    // premultiplied: the light adds over whatever is behind
    return float4(inner.rgb * (1 - alpha * (1 - inner.a)) + glow * (1 - inner.a), saturate(inner.a + alpha * (1 - inner.a)));
}
