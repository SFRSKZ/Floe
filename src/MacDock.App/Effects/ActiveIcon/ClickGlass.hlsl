sampler2D Input : register(s0);
sampler2D Field : register(s1);
float Progress : register(c0);
float Tone : register(c1);
float2 Box : register(c2);
float Palette : register(c3);
float4 ColorA : register(c4);
float4 ColorB : register(c5);

float Dist(float2 uv) { return (tex2D(Field, uv).r - 0.5) * 32.0; }

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float t = saturate(Progress);
    float total = max(Box.x, 1.0);
    float px = 1.0 / total;
    float2 p = (uv - 0.5) * total;
    float d = Dist(uv);

    // a round drop settles into the icon's outline, overshoots, then the shell swells outward
    float morph = smoothstep(0.0, 0.32, t);
    float settle = sin(saturate(t / 0.4) * 3.141593) * 3.0;
    float grow = 6.0 * smoothstep(0.4, 0.8, t);
    float circle = length(p) - (total - 24.0) * 0.36;
    float edge = lerp(circle, d, morph) - settle - grow;

    float2 g = float2(Dist(uv + float2(2*px, 0)) - Dist(uv - float2(2*px, 0)),
                      Dist(uv + float2(0, 2*px)) - Dist(uv - float2(0, 2*px)));
    g = lerp(p / max(length(p), 0.001), g / max(length(g), 0.001), morph);
    g /= max(length(g), 0.001);

    float fade = smoothstep(0.0, 0.06, t) * (1.0 - smoothstep(0.72, 1.0, t));
    float inside = 1.0 - smoothstep(-1.0, 1.0, edge);
    float rim = exp(-edge * edge / 3.0);
    float band = exp(-pow(edge + 2.2, 2.0) / 4.0);
    float rings = smoothstep(0.38, 0.5, t) *
        (exp(-pow(edge + 3.0, 2.0) / 0.5) * 0.9 + exp(-pow(edge + 6.0, 2.0) / 0.5) * 0.55);

    // thick lens: content magnified in the middle, pulled hard toward the rim
    float lensDepth = fade * (1.0 - 0.5 * smoothstep(0.5, 0.9, t));
    float2 zoom = -p * 0.12 * inside * lensDepth;
    float bend = (exp(-abs(edge) / 3.0) * 7.5 + inside * 1.2) * lensDepth;
    float2 off = (g * bend + zoom) * px;
    // 0 rainbow, 1 colourless, 2 the icon's own colours
    float mono = Palette > 0.5 && Palette < 1.5 ? 1.0 : 0.0;
    float dispersion = lerp(0.45, 0.8, Tone) * (1.0 - mono * 0.75);
    float4 r = tex2D(Input, uv + off * (1.0 + dispersion));
    float4 m = tex2D(Input, uv + off);
    float4 b = tex2D(Input, uv + off * (1.0 - dispersion));
    float3 lens = float3(r.r, m.g, b.b);
    float alpha = max(r.a, max(m.a, b.a));

    // rotating rainbow fringe on the rim and a white specular arc chasing it
    float angle = atan2(p.y, p.x) - t * 9.0;
    float spot = pow(saturate(0.5 + 0.5 * cos(angle)), 4.0);
    float arc = pow(saturate(0.5 + 0.5 * cos(atan2(p.y, p.x) + 2.3 - t * 3.0)), 10.0);
    float3 fringe = 0.5 + 0.5 * cos(angle * 2.0 + edge * 0.9 + float3(0.0, 2.1, 4.2));
    fringe = saturate(fringe * 1.4 - 0.1);
    float3 iconTint = lerp(ColorA.rgb, ColorB.rgb, 0.5 + 0.5 * cos(angle * 2.0 + edge * 0.9));
    fringe = Palette > 1.5 ? saturate(iconTint * 1.25 + 0.1) : lerp(fringe, float3(0.92, 0.95, 1.0), mono);

    float frost = Tone * 0.55 * inside * lensDepth;
    lens = lerp(lens, float3(0.96, 0.97, 1.0), frost);
    alpha = max(alpha, frost);
    lens *= 1.0 - Tone * band * 0.35;

    float3 color = lens;
    color += fringe * band * (0.55 + spot * 0.9) * lerp(0.8, 1.15, Tone);
    color += lerp(float3(1, 1, 1), fringe, 0.35) * rings * (0.5 + spot * 0.8);
    color += float3(1, 1, 1) * rim * (arc * 1.4 + spot * 0.5) * lerp(1.0, 0.7, Tone);
    float shine = saturate(band * (0.4 + spot * 0.6) + rings * 0.7 + rim * arc);

    float mask = saturate(inside + rim + band + rings) * fade;
    float a = saturate(alpha + shine) * mask;
    // fade back to the untouched icon outside the effect window
    float3 rgb = color * mask;
    float4 icon = tex2D(Input, uv);
    return lerp(icon, float4(min(rgb, a), a), fade);
}
