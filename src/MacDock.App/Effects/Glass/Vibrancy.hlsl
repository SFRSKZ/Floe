sampler2D Input : register(s0);
float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float4 source = tex2D(Input, uv);
    float3 color = source.rgb / max(source.a, 0.0001);
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    // Lift deep wallpaper tones, compress highlights, preserve hue.
    float lifted = 0.06 + luminance * 0.88;
    color = saturate(lifted + (color - luminance) * 1.8);
    return float4(color * source.a, source.a);
}
