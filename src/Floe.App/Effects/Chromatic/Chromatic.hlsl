// Chromatic button face: one fixed tint under a slowly drifting light field,
// shaded as a shallow dome. The Border supplies the rounded silhouette; this
// shader only colours inside it (its alpha is the border's coverage), so the
// edge stays crisp and anti-aliased no matter what the field does.

sampler2D Input : register(s0);
sampler2D Field : register(s1);
sampler2D Grain : register(s2);
float2 ElementSize : register(c0);
float Hover : register(c1);
float Brightness : register(c2);
float GrainStrength : register(c3);
float RimDepth : register(c4);
float4 Tint : register(c5);
float2 Spin : register(c6);
float2 Flow : register(c7);

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 size = max(ElementSize, float2(8.0, 8.0));
    float2 halfSize = size * 0.5;
    float2 p = (uv - 0.5) * size;
    float2 n = p / halfSize;

    // Screen-space field: one texture covers 160 DIP, so the reflections keep
    // their size on wide toggles and narrow close buttons alike. Rotating and
    // drifting the lookup moves the light; the tint itself never changes.
    float2 plane = p * (1.0 / 120.0);
    float2 rotated = float2(
        (plane.x * Spin.y) - (plane.y * Spin.x),
        (plane.x * Spin.x) + (plane.y * Spin.y));
    float2 fieldUv = frac(float2(0.5, 0.5) + rotated + Flow);
    float light = tex2D(Field, fieldUv).r;

    // The face sits on the tint almost everywhere: the field only lifts or
    // deepens it by a few percent, so there is no dark hole in the middle.
    float value = 0.84 + (0.18 * light);

    // Shallow dome lit from the top-left: lighter across the upper left, softly
    // deeper toward the lower-right rim. One smooth gradient plus a short rim
    // falloff, never a ring.
    float tilt = -((0.55 * n.x) + (0.83 * n.y));
    value *= 1.0 + (0.10 * tilt);

    float edgeDistance = min(halfSize.x - abs(p.x), halfSize.y - abs(p.y));
    float rim = saturate(1.0 - (edgeDistance / max(RimDepth, 0.001)));
    rim = rim * rim * (3.0 - (2.0 * rim));
    value *= 1.0 - (0.06 * rim);

    value *= 1.0 + (0.12 * Hover);
    value *= Brightness;

    // Clamp the shading, not each channel: the brightest point is exactly the
    // tint, so bright bands never wash out toward white.
    float3 color = Tint.rgb * saturate(value);
    color += (tex2D(Grain, uv).r - 0.5) * GrainStrength * 0.08;

    // WPF already anti-aliased the rounded rectangle into the input's alpha.
    // Reuse it untouched: grain and shading must never reach the edge alpha.
    float alpha = tex2D(Input, uv).a;
    return float4(saturate(color) * alpha, alpha);
}
