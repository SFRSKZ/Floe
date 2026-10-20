sampler2D Input : register(s0);
float2 Size : register(c0);
float Hover : register(c1);
float Bulge : register(c2);
float2 Stretch : register(c3);
float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    // The padded host has 24px on each side and 32px of bulge headroom.
    float2 extent = Size + float2(48, 80);
    float2 p = uv * extent - float2(24, 56);
    float vertical = saturate((Size.y - p.y) / Size.y);
    vertical = vertical * vertical * (3 - 2 * vertical);
    p.x += (p.x < Size.x * 0.5 ? Stretch.x : -Stretch.y) * vertical;
    float x = clamp(p.x, 22, Size.x - 22);
    float d = x - Hover;
    float lift = Bulge * exp(-d * d / (2 * 34 * 34));
    // Translate the top corners intact; smoothly reconnect to the fixed bottom.
    float weight = saturate((Size.y - 22 - p.y) / (Size.y - 44 + lift));
    p.y += lift * weight;
    return tex2D(Input, (p + float2(24, 56)) / extent);
}
