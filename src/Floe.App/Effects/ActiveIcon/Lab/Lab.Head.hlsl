// Icon lab: shared head for the active-icon design studies (film-inspired, 3D-looking).
// Each study file defines   float4 Shade(float2 uv, float2 p, float4 icon)
// p is the pixel in DIP from the icon centre (y down), icon is the premultiplied icon pixel.
// The same source compiles for WPF ps_3_0 (dock) and for D3D11 ps_4_0 (WARP preview, LAB_D3D11).
#ifdef LAB_D3D11
Texture2D InputT : register(t0);
Texture2D FieldT : register(t1);
SamplerState Smp : register(s0);
cbuffer Parameters : register(b0) { float4 C0; float4 C1; float4 C2; float4 C3; float4 C4; float4 C5; };
#define ICON(q) InputT.SampleLevel(Smp, (q), 0)
#define FIELDR(q) FieldT.SampleLevel(Smp, (q), 0).r
#else
sampler2D Input : register(s0);
sampler2D Field : register(s1);
float4 C0 : register(c0);
float4 C1 : register(c1);
float4 C2 : register(c2);
float4 C3 : register(c3);
float4 C4 : register(c4);
float4 C5 : register(c5);
#define ICON(q) tex2Dlod(Input, float4((q), 0, 0))
#define FIELDR(q) tex2Dlod(Field, float4((q), 0, 0)).r
#endif

#define Time C0.x
// the same registers as ActiveIcon.hlsl, so one effect class drives both (c1 is its Mode)
#define Strength C4.x
#define ColorA C2
#define ColorB C3
#define TOTAL max(C5.x, 1.0)
#define HALF (C5.y * 0.5)
#define PI 3.14159265
#define TAU 6.2831853
// edge softness of a crisp line, in DIP: about half a device pixel at 150 %
#define AA 0.4

float2 ToUv(float2 p) { return p / TOTAL + 0.5; }
// signed distance to the icon's silhouette in DIP, positive outside
float Dist(float2 uv) { return (FIELDR(uv) - 0.5) * 32.0; }
float DistP(float2 p) { return Dist(ToUv(p)); }
float2 Grad(float2 uv)
{
    float e = 2.0 / TOTAL;
    float2 g = float2(Dist(uv + float2(e, 0)) - Dist(uv - float2(e, 0)), Dist(uv + float2(0, e)) - Dist(uv - float2(0, e)));
    return g / max(length(g), 1e-4);
}

// sine-free hashes (Dave Hoskins): sin() of large arguments collapses on the GPU, and the dock's clock
// and cell indices do get large; these stay random
float H11(float n) { n = frac(n * 0.1031); n *= n + 33.33; n *= n + n; return frac(n); }
float H21(float2 q) { float3 p3 = frac(q.xyx * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
float2 H22(float2 q) { float3 p3 = frac(q.xyx * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.xx + p3.yz) * p3.zy); }
float VNoise(float2 q)
{
    float2 i = floor(q);
    float2 f = frac(q);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + 1.0), f.x), f.y);
}
float Fbm(float2 q) { return VNoise(q) * 0.55 + VNoise(q * 2.03 + 7.1) * 0.3 + VNoise(q * 4.11 + 3.7) * 0.15; }

// a crisp line of half-width w around x = 0
float Band(float x, float w) { return 1.0 - smoothstep(w - AA, w + AA, abs(x)); }
// a hard-edged filled side: 1 where x < 0
float Fill(float x) { return 1.0 - smoothstep(-AA, AA, x); }
// a tight light falloff; kept narrow on purpose, the look is crisp, not hazy
float Glow(float x, float w) { return exp(-x * x / (w * w)); }
float Ang(float a) { return a - TAU * floor(a / TAU + 0.5); }
float2 Rot(float2 q, float a) { float c = cos(a), s = sin(a); return float2(c * q.x - s * q.y, s * q.x + c * q.y); }
// fades everything out before the edge of the padded box the effect may draw in
float Room(float2 p) { return smoothstep(0.0, 2.5, 0.5 * TOTAL - max(abs(p.x), abs(p.y))); }
// premultiplied over
float4 Over(float4 top, float4 under) { return top + under * (1.0 - top.a); }
float4 Lit(float3 rgb, float a) { a = saturate(a); return float4(rgb * a, a); }
// light that adds (premultiplied with less alpha than colour)
float4 Add(float4 under, float3 light, float a) { return float4(under.rgb * (1.0 - saturate(a)) + light, saturate(under.a + a * (1.0 - under.a))); }
float3 Hue(float x) { return saturate(abs(frac(x + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0); }
float3 Unpremul(float4 c) { return c.rgb / max(c.a, 1e-3); }

// a four-ray sparkle of radius r, hard core
float Star(float2 q, float r)
{
    q = abs(q);
    float rays = max(saturate(1.0 - q.x / r) * saturate(1.0 - q.y * 5.0 / r), saturate(1.0 - q.y / r) * saturate(1.0 - q.x * 5.0 / r));
    float core = saturate(1.6 - length(q) * 4.0 / r);
    return saturate(max(rays * rays * 1.3, core));
}

// a circle of radius R in the plane spanned by the 3D unit axes u, v (x right, y down, z toward the viewer),
// projected flat. Returns x: signed screen distance to the drawn ellipse, y: angle on the circle, z: depth there.
float3 Orbit(float2 p, float3 u, float3 v, float R)
{
    float det = u.x * v.y - v.x * u.y;
    det = (det < 0 ? -1.0 : 1.0) * max(abs(det), 0.03);
    float2x2 inv = float2x2(v.y, -v.x, -u.y, u.x) / det;
    float2 c = mul(inv, p) / R;
    float lc = max(length(c), 1e-4);
    float2 g = mul(transpose(inv), c / lc) / R;
    float th = atan2(c.y, c.x);
    return float3((lc - 1.0) / max(length(g), 1e-4), th, R * (cos(th) * u.z + sin(th) * v.z));
}
// the axes of a circle tilted back by incl (0 = facing the viewer) and turned by node in the screen plane
void Basis(float incl, float node, out float3 u, out float3 v)
{
    float ci = cos(incl), si = sin(incl), cn = cos(node), sn = sin(node);
    u = float3(cn, sn, 0);
    v = float3(-sn * ci, cn * ci, si);
}
float3 OnOrbit(float3 u, float3 v, float R, float th) { return R * (cos(th) * u + sin(th) * v); }
