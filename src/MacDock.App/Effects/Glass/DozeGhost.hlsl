sampler2D Input : register(s0);
float Line : register(c0);
float Time : register(c1); // Reserved; deliberately static to avoid idle redraws.
float2 Size : register(c2); // Physical pixels, not DIP.

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float4 source = tex2D(Input, uv);
    float3 color = source.rgb / max(source.a, 0.0001);
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    float3 ice = lerp(float3(0.08, 0.28, 0.40), float3(0.84, 0.97, 1.0), saturate(luminance * 1.12));
    ice = lerp(ice, color, 0.12);
    float scan = 1.0 - 0.055 * step(0.5, frac(uv.y * Size.y * 0.5));
    float4 ghost = float4(ice * scan * source.a * 0.35, source.a * 0.35);
    float distance = (Line - uv.y) * Size.y;
    float converted = saturate(distance + 0.5) * saturate(Line * Size.y);
    float4 result = lerp(source, ghost, converted);
    // One physical pixel core with a quiet six-pixel cyan skirt.
    float core = saturate(1.0 - abs(distance));
    float skirt = saturate(1.0 - abs(distance) / 3.0);
    float fade = saturate(Line * Size.y / 3.0) * saturate((1.0 - Line) * Size.y / 3.0);
    float light = (core * 0.72 + skirt * skirt * 0.16) * fade * source.a;
    float3 beam = lerp(float3(0.27, 0.77, 1.0), float3(0.88, 0.98, 1.0), core);
    return float4(result.rgb * (1.0 - light) + beam * light, result.a + (1.0 - result.a) * light);
}
