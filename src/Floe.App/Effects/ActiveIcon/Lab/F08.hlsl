// F08 Pirates of the Caribbean, cursed gold. Five embossed coins orbit the icon on a tilted path, each
// flipping over as it goes: a raised rim, a ring, an eight-pointed star in relief that catches the light on
// one side, the coin's edge showing as a thick band when it turns edge-on, a glint when its face swings to
// the light. Coins in front are warm gold; behind the icon they pass into moonlight and turn cursed silver.

float4 Coin(float2 p, float3 c3, float spin, float3 gold, float id)
{
    float s = 4.6 * (1.0 + c3.z / 100.0);
    float cs = cos(spin);
    float sx = max(abs(cs), 0.12);
    float2 l = (p - c3.xy) / s;
    // the edge band: the face shifted sideways by the coin's thickness
    float th = 0.18 * sign(sin(spin));
    float2 le = float2((l.x - th * sqrt(saturate(1.0 - cs * cs))) / sx, l.y);
    float edge = Fill(length(le) - 1.0);
    float2 lf = float2(l.x / sx, l.y);
    float rf = length(lf);
    float face = Fill((rf - 1.0) * s);
    if (edge + face <= 0.0) return float4(0, 0, 0, 0);
    // relief: rim, ring, star; lit from the upper left, the light crossing as the coin turns
    float ang = atan2(lf.y, lf.x);
    float starR = 0.45 + 0.17 * abs(frac(ang * 8.0 / TAU) - 0.5) * 2.0;
    float relief = step(0.84, rf) + Band((rf - 0.7) * s, 0.18) + Fill((rf - starR) * s) * 0.9;
    float2 grad = normalize(lf + 1e-4);
    float light = dot(grad, normalize(float2(-0.6, -0.8)) * sign(cs));
    float3 rgb = gold * (0.55 + 0.25 * cs * cs) * (0.85 + 0.35 * relief * light);
    rgb += gold * Band((rf - 0.84) * s, 0.12) * 0.6;
    float glint = pow(saturate(abs(cs)), 30.0) * Star(l * s - float2(-1.0, -1.0), 3.5);
    float3 edgeRgb = gold * 0.45 * (0.8 + 0.2 * sin(le.y * 30.0));
    float3 col = lerp(edgeRgb, rgb, face);
    float a = saturate(edge + face);
    return float4(col * a + glint, a);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float3 u, v;
    Basis(1.0 + 0.12 * sin(t * 0.4), 0.18 * sin(t * 0.27) - 0.12, u, v);
    float4 back = float4(0, 0, 0, 0);
    float4 front = float4(0, 0, 0, 0);
    float3 warm = float3(1.0, 0.76, 0.3);
    float3 moon = float3(0.62, 0.72, 0.85);
    [unroll] for (int i = 0; i < 6; i++)
    {
        float fi = i;
        float th = t * 0.55 + fi * TAU / 6.0;
        float3 c3 = OnOrbit(u, v, 32.5, th);
        float cursed = smoothstep(4.0, -4.0, c3.z);
        float4 coin = Coin(p, c3, t * 2.4 + fi * 1.3, lerp(warm, moon, cursed), fi);
        if (c3.z > 0.0) front = Over(coin, front);
        else back = Over(coin, back);
    }
    // moonlight along the icon's upper edge
    float2 g = Grad(uv);
    float moonRim = Band(d - 0.4, 0.3) * saturate(-g.y * 1.4 - 0.2);
    float4 c = Over(icon, back);
    c = Add(c, moon * moonRim, moonRim * 0.7);
    c = Over(front, c);
    return lerp(icon, c, Room(p));
}
