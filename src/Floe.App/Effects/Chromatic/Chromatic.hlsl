// Chromatic button face: one fixed tint over a slowly rotating light field,
// projected through a rounded contour. Ported from the ReactDrops chromatic
// button material (chromatic-shaders.ts, reference only), rewritten for ps_2_0
// so WPF's software renderer draws it in RenderTargetBitmap too.
sampler2D Input : register(s0);
sampler2D Field : register(s1);
sampler2D Grain : register(s2);
float2 ElementSize : register(c0);
float Hover : register(c1);
float Intensity : register(c2);
float GrainStrength : register(c3);
float Feather : register(c4);
float4 Tint : register(c5);
float2 Spin : register(c6);

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 size = max(ElementSize, float2(4.0, 4.0));
    float2 p = (uv - 0.5) * size;
    float2 normalised = p / (size * 0.5);
    // 0 at the centre, 1 on the inscribed ellipse: the reference's contour ratio.
    float contour = saturate(length(normalised));

    // Plane-to-sphere projection: the field compresses into the rim, so the
    // reflections streak along the edge as they do in the reference material.
    float2 direction = p / max(length(p), 0.0001);
    float c2 = contour * contour;
    float2 source = direction * (c2 * contour);

    // Slow panoramic spin plus drift: only the light moves, the tint never does.
    source = float2((source.x * Spin.y) - (source.y * Spin.x),
                    (source.x * Spin.x) + (source.y * Spin.y));
    float2 uvField = float2(0.5, 0.5) + (source * float2(0.5, 0.4)) + float2(0.10 * Spin.x, 0.08 * Spin.y);
    // A little shear keeps the rim reflections off perfectly symmetric bands.
    uvField.y -= 0.045 * source.x;
    uvField.x = frac(uvField.x);
    uvField.y = saturate(uvField.y);

    // The field is pre-blurred on the CPU, where the projection would otherwise
    // magnify single texels into visible rings at the rim.
    float light = tex2D(Field, uvField).r;

    // Fixed colour mode: value only, so hue and saturation stay on one tint.
    // The face darkens toward the label while the projected rim keeps its glints.
    float mid = 1.0 - abs((2.0 * uv.y) - 1.0);
    float value = saturate(light * 1.75 * Intensity * (1.0 + (0.3 * Hover)))
        * (0.26 + (0.62 * c2)) * (1.0 - (0.24 * mid)) * (1.0 + (0.16 * Hover));
    float3 color = Tint.rgb * value;

    // Static per-pixel grain, like the reference's hash grain.
    color += (tex2D(Grain, uv).r - 0.5) * GrainStrength * 0.4;

    // The border supplies the rounded mask; feather softens the last few DIP.
    float2 edge = min(uv, 1.0 - uv);
    float alpha = saturate(min(edge.x * size.x, edge.y * size.y) / max(Feather, 0.001));
    alpha *= tex2D(Input, uv).a;
    return float4(color * alpha, alpha);
}
