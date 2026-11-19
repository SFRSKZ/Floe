// S01 Blade Runner, rain on a neon sign. A two-colour tube hugs the icon with one stretch that keeps failing,
// slanted rain crosses the light and catches its colour, beads sit on the icon and bend it like lenses,
// and the icon shows again, rippling, in the wet floor under it.

float3 TubeColor(float y)
{
    return lerp(float3(1.0, 0.16, 0.52), float3(0.12, 0.86, 1.0), smoothstep(-5.0, 5.0, y));
}

float RainLayer(float2 p, float spacing, float speed, float len, float w, float seed, float t)
{
    float2 q = float2(p.x + p.y * 0.24, p.y);
    float col = floor(q.x / spacing);
    float r = H11(col * 7.31 + seed);
    float r2 = H11(col * 3.17 + seed * 1.7);
    float x = (frac(q.x / spacing) - 0.5) * spacing - (r2 - 0.5) * spacing * 0.6;
    float period = 30.0 + r * 26.0;
    float s = frac((t * speed * (0.8 + 0.45 * r) + r * 97.0 - q.y) / period) * period;
    float body = step(s, len) * (1.0 - s / len);
    return Band(x, w) * body * step(0.3, r2);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float h = HALF;
    float d = Dist(uv);
    float a = atan2(p.y, p.x);

    // the tube: a failing stretch on the upper right stutters in its fault windows
    float fault = step(0.62, frac(t * 0.21));
    float stutter = step(0.45, H11(floor(t * 17.0)));
    float dead = smoothstep(0.42, 0.36, abs(Ang(a + 0.9))) * fault * (1.0 - stutter);
    float hum = 0.93 + 0.07 * H11(floor(t * 30.0));
    float power = (1.0 - dead * 0.92) * hum;
    float3 tint = TubeColor(p.y);
    float tb = d - 3.0;
    float tube = Band(tb, 0.6);
    float hot = Band(tb, 0.22);
    float halo = Glow(tb, 1.1) * 0.4 + Glow(tb, 2.6) * 0.12;
    float outside = smoothstep(0.3, 1.4, d);
    float3 neonRgb = (tint * (tube * 1.25 + halo) + hot * 0.85) * power * outside;
    float neonA = saturate(tube * 0.95 + halo) * power * outside;

    // the wet floor: the icon and the tube again, mirrored about the icon's base line, rippling
    float4 back = float4(0, 0, 0, 0);
    float below = p.y - h;
    if (below > 0.0)
    {
        float2 m = float2(p.x + sin(p.y * 2.3 - t * 5.0) * 0.45 + sin(p.y * 5.1 + t * 3.0) * 0.2, h - below);
        float2 muv = ToUv(m);
        float4 mirror = ICON(muv);
        float fade = pow(saturate(1.0 - below / 12.0), 1.5);
        float dm = Dist(muv);
        float mt = Band(dm - 3.0, 0.7) * power;
        // the floor shines in streaks, not as a mirror
        float streak = 0.55 + 0.45 * step(0.5, frac(m.y * 0.9 + H11(floor(p.x * 0.7)) * 3.0));
        back = float4(mirror.rgb * 0.5, mirror.a * 0.5) * fade * streak;
        back = Add(back, TubeColor(-m.y) * mt * fade * 0.6, mt * fade * 0.45);
    }

    // beads on the icon: lenses that flip what is under them, a dark rim, a sky highlight and the tube's colour
    float4 body = icon;
    float2 cell = floor(p / 6.5);
    float2 rnd = H22(cell + 11.0);
    float slide = step(0.82, H21(cell + 5.0)) * frac(t * 0.12 + rnd.x) * 6.5;
    float2 c = (cell + 0.25 + rnd * 0.5) * 6.5 + float2(0, slide);
    float rad = 0.9 + 0.9 * H21(cell + 2.0);
    float2 l = (p - c) / rad;
    float ll = length(l);
    float onIcon = ICON(ToUv(c)).a;
    if (ll < 1.0 && rnd.y > 0.35 && onIcon > 0.6)
    {
        float4 bent = ICON(ToUv(c - l * rad * 0.85));
        float rim = smoothstep(0.7, 1.0, ll);
        float3 rgb = lerp(bent.rgb * 1.08, bent.rgb * 0.45, rim * 0.8);
        float spec = Fill(length(l - float2(-0.38, -0.42)) - 0.22);
        float neonSpot = Fill(length(l - float2(0.32, 0.45)) - 0.16);
        rgb += spec * 0.85 + neonSpot * TubeColor(p.y) * 0.6;
        float k = 1.0 - smoothstep(0.92, 1.0, ll);
        body = lerp(body, float4(rgb, max(bent.a, body.a)), k);
    }

    // rain: a far, thin, dim layer and a near one; lit by the tube when it falls close to it
    float near = RainLayer(p, 2.1, 62.0, 9.0, 0.26, 1.0, t);
    float far = RainLayer(p + 1.3, 1.5, 44.0, 5.5, 0.14, 9.0, t) * 0.6;
    float lit = exp(-max(d - 3.0, 0.0) / 3.5) * power;
    float3 rainRgb = lerp(float3(0.62, 0.7, 0.82) * 0.8, TubeColor(p.y) * 1.3 + 0.3, lit);
    float rain = (near + far) * (0.55 + 0.45 * lit) * lerp(1.0, 0.5, icon.a);

    float4 c0 = Over(body, back);
    c0 = Add(c0, neonRgb, neonA);
    c0 = Add(c0, rainRgb * rain, rain * 0.7);
    float room = Room(p);
    return lerp(icon, c0, room);
}
