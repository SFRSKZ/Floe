// F05 Avatar, seeds of the sacred tree. Bioluminescent seeds drift round the icon like tiny jellyfish: a bright
// core with a crown of fine filaments, each tipped with a dot, gently pulsing; near ones larger and in front,
// far ones smaller and behind. Along the icon's lower edge glowing moss lights up in waves that travel along it.

float4 Seed(float2 p, float2 c, float s, float pulse, float3 col)
{
    float2 q = (p - c) / s;
    float r = length(q);
    if (r > 1.6) return float4(0, 0, 0, 0);
    float a = atan2(q.y, q.x);
    // filaments in an umbrella: mostly upward, swaying
    float L = 1.0 + 0.25 * pulse;
    float n = 9.0;
    float sway = sin(a * 3.0 + pulse * 3.0) * 0.08;
    float arc = (frac((a + sway) * n / TAU) - 0.5) * TAU / n * r;
    float up = smoothstep(0.6, -0.2, q.y / max(r, 1e-3));
    float fil = Band(arc * s, 0.11) * step(0.25, r) * step(r, L) * (0.35 + 0.65 * up);
    float tipD = length(float2(arc, r - L)) * s;
    float tips = Fill(tipD - 0.32) * (0.4 + 0.6 * up);
    float core = Fill(r * s - 0.55 * s);
    float ring = Band(r * s - 0.75 * s, 0.12);
    float k = saturate(fil * 0.7 + tips + core + ring * 0.6);
    float3 rgb = col * (fil * 0.8 + tips + ring * 0.7) + float3(0.9, 1.0, 1.0) * core;
    return float4(rgb, k);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float4 back = float4(0, 0, 0, 0);
    float4 front = float4(0, 0, 0, 0);
    [unroll] for (int i = 0; i < 7; i++)
    {
        float fi = i;
        float ang = fi * TAU / 7.0 + t * (0.12 + 0.05 * H11(fi)) + sin(t * 0.3 + fi) * 0.3;
        float z = sin(ang * 1.0 + fi);
        float rad = 31.5 + 4.0 * H11(fi * 3.0);
        float2 c = float2(cos(ang) * rad, sin(ang) * rad * 0.95 + sin(t * 0.9 + fi * 2.0) * 2.0);
        float s = (2.9 + 1.0 * H11(fi * 5.0)) * (1.0 + 0.22 * z);
        float pulse = 0.5 + 0.5 * sin(t * 2.2 + fi * 1.7);
        float3 col = lerp(float3(0.25, 0.95, 1.0), float3(0.65, 0.5, 1.0), H11(fi * 7.0));
        float4 sd = Seed(p, c, s, pulse, col) * (0.55 + 0.45 * (0.5 + 0.5 * z));
        if (z > 0.0) front = Over(sd, front);
        else back = Over(sd, back);
    }

    // glowing moss along the lower edge: dots that light in a travelling wave
    float2 mc = floor(p / 1.5);
    float2 mr = H22(mc + 2.0);
    float2 mp = (mc + 0.2 + mr * 0.6) * 1.5;
    float md = DistP(mp);
    float wave = pow(saturate(0.5 + 0.5 * sin(mp.x * 0.25 - t * 2.4)), 4.0);
    float moss = Fill(length(p - mp) - 0.32 - 0.2 * mr.y) * step(-0.4, md) * step(md, 3.0) * smoothstep(-HALF * 0.1, HALF * 0.5, mp.y) * (0.25 + 0.95 * wave);
    float3 mossRgb = lerp(float3(0.3, 0.75, 1.0), float3(0.95, 0.4, 1.0), step(0.75, mr.x));

    float4 c = Over(icon, back);
    c = Add(c, mossRgb * moss, moss * 0.9);
    c = Over(front, c);
    return lerp(icon, c, Room(p));
}
