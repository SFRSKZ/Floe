// Ported from metal-fx by Jakub Antalik (MIT), https://github.com/Jakubantalik/Libraries.dev
// Analytic rounded-rectangle ring. No source sampling: use on a transparent
// overlay above the dock fill. Coordinates outside [0,1] cover effect padding.
float Time : register(c0);
float CursorX : register(c1);
float CursorY : register(c2);
float BendStrength : register(c3);
float Strength : register(c4);
float Thickness : register(c5);
float CornerRadius : register(c6);
float2 Size : register(c7);
float GlowGain : register(c8);
float4 TintColor : register(c9);
float4 HighlightColor : register(c10);
float4 ShadowColor : register(c11);
float Speed : register(c12);
float Repetition : register(c13);
float Softness : register(c14);
float ShiftRed : register(c15);
float ShiftBlue : register(c16);
float Distortion : register(c17);
float Contour : register(c18);
float ShaderOpacity : register(c19);
float4 GlowColor : register(c20);
float FlashX : register(c21);
float FlashAge : register(c22);

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 size = max(Size, 1.0);
    // WPF hands the shader the padded glow area; map uv back onto the element itself.
    uv = (uv * (size + 48) - 24) / size;
    float2 p = uv * size;
    float2 centered = p - size * 0.5;
    float radius = min(CornerRadius < 0 ? size.y * 0.5 : CornerRadius, min(size.x, size.y) * 0.5);
    float2 q = abs(centered) - (size * 0.5 - radius);
    float2 outside = max(q, 0);
    float len = length(outside);
    float d = len + min(max(q.x, q.y), 0) - radius;
    float2 flatNormal = q.x > q.y ? float2(1, 0) : float2(0, 1);
    float2 normal = (len > 0.001 ? outside / max(len, 0.001) : flatNormal) * sign(centered);

    // The reference's 13px bend blob and 9px displacement cap, without its
    // velocity/spring history. Zero strength disables both dent and catch-light.
    float2 delta = p - saturate(float2(CursorX, CursorY)) * size;
    float contact = exp2(-dot(delta, delta) / (2.0 * 13.0 * 13.0) * 1.442695);
    float bend = saturate(BendStrength) * contact;
    d += 9.0 * bend;
    float thick = clamp(Thickness, 0.1, min(size.x, size.y) * 0.5);
    float band = smoothstep(-thick - 0.6, -thick + 0.6, d) * (1 - smoothstep(-0.6, 0.6, d));
    float crossSection = saturate(-d / thick);
    float top = saturate(0.5 - 0.5 * normal.y);
    float time = Time * Speed;

    // Folded reflection stripes replace Paper's simplex noise / image modes.
    // Keep the reference repetition, softness, dispersion and colour burn.
    float phase = uv.x * Repetition + uv.y * 0.32 + time * 0.025
        + Distortion * sin(uv.x * 11 + uv.y * 6 - time * 0.13)
        + Contour * (crossSection * 0.65 + normal.x * 0.22);
    float3 field = abs(frac(phase + float3(ShiftRed, 0, -ShiftBlue) * 0.16) * 2 - 1);
    float3 stripe = smoothstep(0.22 - Softness, 0.75 + Softness, field);
    float3 metal = lerp(ShadowColor.rgb, HighlightColor.rgb, stripe);
    metal *= 0.62 + 0.38 * sin(crossSection * 3.141593);
    float3 burn = 1 - saturate((1 - metal) / max(TintColor.rgb, 0.001));
    metal = lerp(metal, burn, TintColor.a);

    // White 90% / ~1px top inner rim, plus the thin outer bevel reflection.
    float inner = exp2(-pow((d + thick - 0.45) / 0.65, 2)) * top * 0.9;
    float outer = exp2(-pow((d + 0.35) / 0.5, 2)) * (0.18 + 0.48 * top);
    metal = lerp(metal, HighlightColor.rgb, saturate(inner + outer));

    // Slowly drifting top halo and a weaker counter-reflection underneath.
    float hx = size.x * (0.5 + 0.38 * sin(time * 0.19));
    float2 haloDelta = (p - float2(hx, thick * 0.5)) / float2(42, 12);
    float halo = exp2(-dot(haloDelta, haloDelta) * 1.442695);
    float2 echoDelta = (p - float2(size.x - hx, size.y - thick * 0.5)) / float2(65, 10);
    halo += 0.35 * exp2(-dot(echoDelta, echoDelta) * 1.442695);
    metal = lerp(metal, HighlightColor.rgb, saturate(halo * 0.8 + bend * 0.9));

    // Drop impact: a bloom where it landed, then two glints racing apart along the rim.
    float flash = 0;
    if (FlashAge >= 0)
    {
        float spread = 10 + 110 * FlashAge;
        float bloom = exp2(-pow((p.x - FlashX) / spread, 2) * 1.442695) * exp2(-FlashAge * 6.5);
        float travel = 300 * FlashAge;
        float fronts = exp2(-pow((p.x - FlashX - travel) / 16, 2) * 1.442695)
            + exp2(-pow((p.x - FlashX + travel) / 16, 2) * 1.442695);
        flash = (bloom * 1.6 + fronts * 1.3 * exp2(-FlashAge * 2.6)) * (0.25 + 0.75 * top);
        metal = lerp(metal, HighlightColor.rgb, saturate(flash));
    }

    // Two analytic lobes approximate the four blurred glow strokes (sigma 8.4/2.1).
    // Explicit support cutoff leaves the distant exterior and centre transparent.
    float exterior = smoothstep(-0.5, 0.6, d);
    float glow = (0.16 * exp2(-d * d / (2 * 8.4 * 8.4) * 1.442695)
        + 0.24 * exp2(-d * d / (2 * 2.1 * 2.1) * 1.442695))
        * exterior * (0.12 + halo * 2 + bend * 1.5)
        * (1 - smoothstep(20, 24, d)) * max(GlowGain, 0) * GlowColor.a;
    float ringAlpha = band * saturate(ShaderOpacity);
    float glowAlpha = saturate(glow) * (1 - ringAlpha);
    float alpha = (ringAlpha + glowAlpha) * saturate(Strength);
    float3 rgb = (saturate(metal) * ringAlpha + GlowColor.rgb * glowAlpha) * saturate(Strength);

    // the rim is already near white, so the impact shows as ice-blue light spilling off it
    float flashLight = saturate(flash) * (0.85 * exp2(-d * d / (2 * 2.5 * 2.5) * 1.442695)
        + 0.55 * exp2(-d * d / (2 * 9.0 * 9.0) * 1.442695)) * (1 - smoothstep(18, 24, abs(d)));
    flashLight = saturate(flashLight) * saturate(Strength);
    rgb = rgb * (1 - flashLight) + float3(0.78, 0.93, 1.0) * flashLight;
    alpha = alpha * (1 - flashLight) + flashLight;
    return float4(rgb, alpha); // WPF requires premultiplied alpha.
}
