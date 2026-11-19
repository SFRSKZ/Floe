// S10 Prometheus, the star map. The icon is the star: three tilted orbits round it, drawn as dotted lines, each
// with a shaded planet lit from the icon's side, a hard terminator, banded clouds and a thin atmosphere rim.
// Planets and the near arcs of their orbits pass in front of the icon; far ones go behind it. Faint stars twinkle.

float4 Planet(float2 p, float3 c3, float rad, float3 col, float band, float t)
{
    float s = rad * (1.0 + c3.z / 90.0);
    float2 l = (p - c3.xy) / s;
    float ll = dot(l, l);
    if (ll > 1.0) return float4(0, 0, 0, 0);
    float3 n = float3(l, sqrt(1.0 - ll));
    float3 L = normalize(-c3 + float3(0, 0, 18.0));
    float ndl = dot(n, L);
    float lit = smoothstep(-0.05, 0.12, ndl);
    float bands = 0.8 + 0.2 * sin((l.y + 0.15 * sin(l.x * 3.0 + band)) * 9.0 + band);
    float3 rgb = col * bands * (0.12 + 0.95 * lit);
    float3 h = normalize(L + float3(0, 0, 1));
    rgb += pow(saturate(dot(n, h)), 40.0) * 0.6 * lit;
    float rim = smoothstep(0.75, 1.0, sqrt(ll)) * (0.3 + 0.7 * lit);
    rgb += col * rim * 0.6 + 0.2 * rim;
    float a = 1.0 - smoothstep(1.0 - 2.0 * AA / s, 1.0, sqrt(ll));
    return float4(rgb * a, a);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float4 back = float4(0, 0, 0, 0);
    float4 front = float4(0, 0, 0, 0);

    [unroll] for (int i = 0; i < 3; i++)
    {
        float fi = i;
        float R = 30.0 + fi * 3.6;
        float incl = 1.05 + fi * 0.18;
        float node = -0.5 + fi * 0.62 + t * 0.03;
        float3 u, v;
        Basis(incl, node, u, v);
        float3 o = Orbit(p, u, v, R);
        float dots = step(0.45, frac(o.y * R / 1.4));
        float line1 = Band(o.x, 0.16) * dots * 0.55;
        float3 col = i == 0 ? float3(0.95, 0.8, 0.5) : (i == 1 ? float3(0.45, 0.9, 0.85) : float3(0.95, 0.55, 0.6));
        float4 ln = Lit(lerp(col, float3(1, 1, 1), 0.3), line1);
        float isFront = step(0.0, o.z);
        front = Over(ln * isFront * 0.7, front);
        back = Over(ln * (1.0 - isFront), back);

        float th = t * (0.7 - fi * 0.17) + fi * 2.1;
        float3 c3 = OnOrbit(u, v, R, th);
        float4 pl = Planet(p, c3, 4.1 - fi * 0.6, col, fi * 3.0 + t * 0.4, t);
        float pf = step(0.0, c3.z);
        front = Over(pl * pf, front);
        back = Over(pl * (1.0 - pf), back);
    }

    // the star: a hairline corona on the icon's edge and fine rays
    float a = atan2(p.y, p.x);
    float corona = Band(d - 0.6, 0.18) * 0.8 + Band(d - 1.4, 0.12) * 0.25 * step(0.6, frac(a * 20.0 / TAU + t * 0.05));
    float2 sc = floor(p / 4.0);
    float2 sr = H22(sc + 9.0);
    float star = Star(p - (sc + sr) * 4.0, 0.8) * step(0.86, H21(sc + 1.0)) * (0.4 + 0.6 * sin(t * 1.7 + sr.y * 20.0)) * smoothstep(3.0, 6.0, d);

    float4 c = Lit(float3(0.85, 0.9, 1.0), star * 0.8);
    c = Over(back, c);
    c = Add(c, float3(1.0, 0.9, 0.7) * corona, corona * smoothstep(0.0, 0.5, d));
    c = Over(icon, c);
    c = Over(front, c);
    return lerp(icon, c, Room(p));
}
