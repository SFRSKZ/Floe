// S06 Star Wars, the hologram message. A small emitter under the icon throws a fan of blue rays up into it;
// the icon is shown as the projection: tinted toward blue, cut by scanlines, a brighter band rolling up,
// edges split in colour, now and then rows shear sideways. A faint second image floats just above.

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float h = HALF;
    float d = Dist(uv);
    float3 holo = float3(0.38, 0.72, 1.0);

    // glitches: a short burst every so often, rows sliding
    float burst = step(0.86, H11(floor(t * 5.0)));
    float row = floor(p.y / 2.2);
    float shift = burst * (H21(float2(row, floor(t * 24.0))) - 0.5) * 3.0 * step(0.5, H21(float2(row, floor(t * 5.0)) + 3.0));
    float2 su = uv + float2(shift / TOTAL, 0);
    float4 base = ICON(su);
    float r = ICON(su + float2(0.7 / TOTAL, 0)).r;
    float b = ICON(su - float2(0.7 / TOTAL, 0)).b;
    float3 rgb = Unpremul(base);
    rgb = float3(Unpremul(float4(r, 0, 0, max(base.a, 0.001))).r, rgb.g, Unpremul(float4(0, 0, b, max(base.a, 0.001))).b);
    float lum = dot(rgb, float3(0.3, 0.55, 0.15));
    float3 tinted = lerp(rgb, holo * (0.35 + lum * 1.2), 0.5);
    float scan = 0.62 + 0.38 * step(0.42, frac(p.y / 1.15));
    float roll = Glow(p.y - (frac(-t * 0.35) * (TOTAL + 20.0) - TOTAL * 0.5 - 10.0), 3.0) * 0.55;
    float flick = 0.9 + 0.1 * H11(floor(t * 22.0));
    float3 shown = (tinted * scan + holo * roll) * flick;
    float alpha = base.a * (0.82 + 0.18 * scan) * flick;
    float4 body = float4(shown * alpha, alpha);
    // a hairline at the projection's outline
    float outline = Band(d - 0.25, 0.22) * 0.8 * flick;
    body = Add(body, holo * outline * 1.2, outline);

    // the ghost: the same image lifted 5 DIP, faint, only outside the icon
    float4 ghost = ICON(uv + float2(0, 5.0 / TOTAL));
    float gk = ghost.a * 0.16 * smoothstep(0.0, 1.0, d) * scan;
    float4 c = Add(body, holo * gk, gk);

    // the emitter and its fan of light
    float2 e = float2(0, h + 8.0);
    float2 v = p - e;
    float up = -v.y;
    float spread = abs(v.x) / max(up, 0.01);
    float cone = step(0.0, up) * (1.0 - smoothstep(1.0, 1.15, spread)) * smoothstep(-2.0, 3.0, d) * (1.0 - smoothstep(9.5, 10.0, up));
    float rays = Band(frac(atan2(v.x, up) * 9.0 + 0.5) - 0.5, 0.1) * (0.55 + 0.45 * H11(floor(atan2(v.x, up) * 9.0) + floor(t * 9.0)));
    float beam = cone * (0.12 + rays * 0.5) * flick;
    float speck = step(0.985, H21(floor(p * 1.3) + floor(t * 12.0))) * cone;
    float emit = Fill(length(v) - 1.0) + Band(length(v * float2(1.0, 2.2)) - 2.6, 0.2) * 0.8;
    c = Add(c, holo * (beam + speck * 1.2) + float3(0.9, 0.97, 1.0) * emit, saturate(beam + speck + emit) * 0.8);
    return lerp(icon, c, Room(p));
}
