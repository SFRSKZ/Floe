// S05 2001, the Star Gate. A slit-scan corridor whose vanishing point is the icon: thin hard streaks of light
// shoot out of the icon's edge toward the viewer, each a bright head with a long tail, faster and wider as
// they come near, in the film's shifting saturated colours. The upper and lower planes carry the most light;
// a white hairline at the icon's edge is the light at the end of the corridor.

float Streak(float a, float zz, float n, float seed, float t, out float hue)
{
    float id = floor(a * n / TAU);
    float f = frac(a * n / TAU) - 0.5;
    float r = H11(id * 1.37 + seed);
    float r2 = H11(id * 4.11 + seed);
    hue = r2;
    // the lane narrows toward the vanishing point
    float w = (0.05 + 0.16 * r) * (0.4 + 0.6 * saturate(zz));
    float lane = Band(f, w) * step(0.2, r);
    // one packet per lane per period: head at s = 0, a tail behind it toward the icon
    float s = frac(zz * 0.55 - t * (0.35 + 0.45 * r) + r2 * 7.0);
    float tail = (1.0 - smoothstep(0.0, 0.42, s)) * step(0.0, s);
    float head = 1.0 - smoothstep(0.0, 0.05, s);
    return lane * (tail * 0.75 + head * 0.9) * (0.55 + 0.45 * r);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float a = atan2(p.y, p.x);
    float zz = saturate(d / 12.0);
    float walls = 0.3 + 0.7 * smoothstep(0.3, 0.8, abs(p.y) / (abs(p.x) + abs(p.y) + 1e-3));
    float h1, h2;
    float s1 = Streak(a, zz, 110.0, 1.0, t, h1);
    float s2 = Streak(a + 0.013, zz * 0.8, 64.0, 7.0, t * 0.85, h2);
    // the film's palette: magenta, cyan, amber, violet, green; drifting
    float3 c1 = Hue(h1 * 0.9 + t * 0.04);
    float3 c2 = Hue(h2 * 0.9 + t * 0.04 + 0.5);
    float3 rgb = (c1 * s1 + c2 * s2) * walls * 1.4;
    float outside = smoothstep(0.5, 1.6, d);
    rgb *= outside * (0.35 + 0.9 * zz);
    float edge = Band(d - 0.7, 0.18) * (0.7 + 0.3 * sin(t * 3.0 + a * 4.0));
    rgb += float3(1, 1, 1) * edge;
    float alpha = saturate(max(rgb.r, max(rgb.g, rgb.b)));
    float4 c = Add(icon, rgb, alpha);
    return lerp(icon, c, Room(p));
}
