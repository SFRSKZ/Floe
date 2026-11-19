// F10 Peter Pan, fairy dust. A golden comet loops round the icon on a 3D path that slowly changes its tilt: an
// eight-rayed star for a head, behind it a fine gold ribbon that sags under gravity and frays into sparkles in
// gold, pink, mint and sky that fall away and twinkle out. Behind the icon it is hidden; dust that settled on
// the icon's edge glints.

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float3 u, v;
    Basis(0.9 + 0.3 * sin(t * 0.31), t * 0.17, u, v);
    float R = 33.0;
    float head = t * 1.5;
    float4 back = float4(0, 0, 0, 0);
    float4 front = float4(0, 0, 0, 0);

    // the ribbon: the path behind the head, sagging more the older it is
    float3 o = Orbit(p, u, v, R);
    float behind = Ang(head - o.y);
    behind = behind < 0.0 ? behind + TAU : behind;
    float age = behind / 1.5;
    float sag = age * age * 3.0;
    float3 o2 = Orbit(p - float2(0, sag), u, v, R);
    float rib = Band(o2.x, lerp(0.75, 0.12, saturate(age / 1.4))) * (1.0 - smoothstep(0.6, 1.4, age));
    float sparkleCut = step(0.35, H21(floor(float2(o2.y * 60.0, t * 6.0))));
    rib *= lerp(1.0, sparkleCut, saturate(age * 1.5));
    float4 r4 = float4(float3(1.0, 0.86, 0.5) * rib * 1.2, rib * 0.9);
    if (o2.z > 0.0) front = r4; else back = r4;

    [loop] for (int j = 0; j < 34; j++)
    {
        float fj = j;
        float ag = fj * 0.06;
        float th = head - ag * 1.5;
        float3 c3 = OnOrbit(u, v, R, th);
        float2 jit = (H22(float2(fj, floor(t * 2.0 - fj * 0.12))) - 0.5) * (1.0 + ag * 6.0);
        float2 c = c3.xy + jit + float2(0, ag * ag * 3.0 + ag * 4.0);
        float tw = 0.5 + 0.5 * sin(t * 17.0 + fj * 2.3);
        float size = lerp(2.2, 0.7, ag / 2.04) * (0.4 + 0.6 * tw);
        float k = Star(Rot(p - c, fj), size) * (1.0 - ag / 2.1);
        float hsel = H11(fj * 3.7);
        float3 col = hsel < 0.45 ? float3(1.0, 0.85, 0.45) : (hsel < 0.65 ? float3(1.0, 0.6, 0.85) : (hsel < 0.82 ? float3(0.6, 1.0, 0.8) : float3(0.6, 0.85, 1.0)));
        float4 s = float4(col * k * 1.3, k * 0.9);
        if (c3.z > 0.0) front = Over(s, front);
        else back = Over(s, back);
    }

    // the head: an eight-rayed star with a hot core
    float3 h3 = OnOrbit(u, v, R, head);
    float2 hq = p - h3.xy;
    float hs = 5.0 * (1.0 + h3.z / 90.0);
    float star8 = max(Star(hq, hs), Star(Rot(hq, 0.785), hs * 0.55));
    star8 = max(star8, Fill(length(hq) - 1.0));
    float4 hd = float4(float3(1.0, 0.95, 0.78) * star8 * 1.4, star8);
    if (h3.z > 0.0) front = Over(hd, front);
    else back = Over(hd, back);

    // dust settled on the edge glints now and then
    float2 ec = floor(p / 2.0);
    float2 ep = (ec + 0.2 + H22(ec) * 0.6) * 2.0;
    float edm = DistP(ep);
    float glint = Star(p - ep, 1.4) * step(-0.8, edm) * step(edm, 0.8) * pow(saturate(sin(t * 2.5 + H21(ec) * 40.0)), 10.0);

    float4 c = Over(icon, back);
    c = Add(c, float3(1.0, 0.9, 0.6) * glint, glint);
    c = Over(front, c);
    return lerp(icon, c, Room(p));
}
