
#ifdef LAB_D3D11
float4 main(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
#else
float4 main(float2 uv : TEXCOORD0) : COLOR0
#endif
{
    float2 p = (uv - 0.5) * TOTAL;
    float4 icon = ICON(uv);
    float4 c = Shade(uv, p, icon);
    c = float4(min(c.rgb, c.a + 0.6), saturate(c.a));
    // Strength fades the study in and out, back to the plain icon
    return lerp(icon, c, saturate(Strength));
}
