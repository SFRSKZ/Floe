sampler2D Input : register(s0);
float2 Size : register(c0);
// capsule at rest: left, top, width, height (window DIPs)
float4 Capsule : register(c1);
float Radius : register(c2);
float Hover : register(c3);
float Bulge : register(c4);
float Plateau : register(c5);
float Sigma : register(c6);
// how far the rim reaches out for the scene behind it, and how much red and blue part there
float Refraction : register(c7);
float Dispersion : register(c8);
float Bevel : register(c9);

// the top edge rises under the cursor the same way the rim warp lifts it
float Top(float x)
{
    float d = max(0, abs(x - Hover) - Plateau);
    return Capsule.y - Bulge * exp(-d * d / (2 * Sigma * Sigma));
}

// signed distance to the rounded capsule, negative inside
float Distance(float2 p)
{
    float top = Top(p.x);
    float bottom = Capsule.y + Capsule.w;
    float2 centre = float2(Capsule.x + Capsule.z * 0.5, (top + bottom) * 0.5);
    float2 extent = float2(Capsule.z * 0.5, (bottom - top) * 0.5);
    float2 q = abs(p - centre) - extent + Radius;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - Radius;
}

float3 Vibrant(float3 color)
{
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    // lift deep wallpaper tones, compress highlights, keep the hue
    return saturate(0.06 + luminance * 0.88 + (color - luminance) * 1.8);
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * Size;
    float d = Distance(p);
    float2 normal = float2(Distance(p + float2(1, 0)) - Distance(p - float2(1, 0)),
                           Distance(p + float2(0, 1)) - Distance(p - float2(0, 1)));
    normal /= max(length(normal), 0.0001);

    // a convex bevel: flat in the middle, steepening towards the rim, where it pulls in the scene from just outside;
    // the last couple of pixels stay unbent, so the metal outline lies on a clean edge, not a smeared copy of it
    float rim = 1 - saturate(-d / Bevel);
    float bend = rim * rim * Refraction * saturate(-d / 2.5);
    float2 shift = normal * bend / Size;
    float2 spread = shift * Dispersion;
    float3 color;
    color.r = tex2D(Input, saturate(uv + shift + spread)).r;
    color.g = tex2D(Input, saturate(uv + shift)).g;
    color.b = tex2D(Input, saturate(uv + shift - spread)).b;
    float alpha = tex2D(Input, uv).a;
    color /= max(alpha, 0.0001);

    color = Vibrant(color);
    // the bevel catches a little more light than the flat middle
    color = saturate(color + rim * rim * 0.06);
    return float4(color * alpha, alpha);
}
