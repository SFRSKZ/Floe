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

    // a breathing glass band over the edge that refracts the icon with a little dispersion
    float d0 = -4.0;
    float d1 = 4.5 + 1.2 * sin(Time * 1.6 + sin(a * 2.0 + Time) * 0.8);
    float t = (d - d0) / (d1 - d0);
    float ring = smoothstep(-0.04, 0.04, t) * smoothstep(1.04, 0.96, t) * k;
    float c = 2 * t - 1;
    // a soft lens profile: steep edges scatter the dispersed samples into speckles
    float h = sqrt(max(1 - c * c, 0.2));
    float slope = clamp(-c / h, -2, 2) * ring;
    float2 off = g * slope * 2.5 * px;
    float4 r0 = tex2D(Input, uv - off);
    float4 r1 = tex2D(Input, uv - off * 1.12);
    float4 r2 = tex2D(Input, uv - off * 1.24);
    float4 lens = float4(r0.r, r1.g, r2.b, r1.a);
    lens += float4(0.05, 0.055, 0.065, 0.10) * (1 - lens.a);
    lens.rgb = lens.rgb * 1.06 + 0.03 * lens.a;

    float2 light = normalize(float2(-0.6, -0.8));
    float lit = max(dot(g, light), 0);
    float shade = max(dot(-g, light), 0);
    float qo = (t - 0.85) / 0.08;
    float qi = (t - 0.15) / 0.08;
    float rim = lit * lit * exp(-qo * qo) + shade * shade * exp(-qi * qi) * 0.6;
    lens += saturate(rim * 1.1);

    float4 o = lerp(icon, lens, ring);
    float edge = exp(-abs(d - d1) * 2.4) * 0.25 * k;
    o.rgb += edge;
    o.a = saturate(o.a + edge);
    return o;
}
