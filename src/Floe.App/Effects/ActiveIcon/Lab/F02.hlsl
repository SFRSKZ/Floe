// F02 Doctor Strange, the spell mandala. A mandala in a plane that tilts and sways round the icon; every line of
// it is made of streaming sparks (dashes running along it), in burning orange with white-hot cores: a double
// outer circle, a zigzag star band, a rune band, an inner circle. Sparks fly off the outer circle along its
// tangent and die. Near half over the icon, far half behind.

float Sigil(float2 f, float id)
{
    float2 q = f - 0.5;
    float r1 = H11(id * 2.3), r2 = H11(id * 6.1);
    float circ = Band(length(q) - 0.3, 0.05) * step(0.5, r1);
    float tri = Band(max(abs(q.x) * 0.87 + q.y * 0.5, -q.y) - 0.18, 0.05) * step(r1, 0.5);
    float bar = Band(q.x, 0.05) * step(abs(q.y), 0.4) * step(0.3, r2);
    float cross1 = Band(q.y + 0.1, 0.05) * step(abs(q.x), 0.3) * step(r2, 0.6);
    return saturate(circ + tri + bar + cross1);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float incl = 0.75 + 0.15 * sin(t * 0.45);
    float node = 0.25 * sin(t * 0.3) - 0.1;
    float2 q = Rot(p, -node);
    q.y /= cos(incl);
    float r = length(q);
    float a = atan2(q.y, q.x);
    float sq = lerp(1.0, 1.0 / cos(incl), abs(q.y) / max(r, 1e-3));
    float lw = 0.24 * sq;
    float front = step(0.0, q.y);

    // lines made of running sparks
    float a1 = a + t * 0.6;
    float dashes1 = 0.35 + 0.65 * step(0.4, H11(floor(a1 * 37.0 * 6.0 / TAU - t * 30.0)));
    float outer = (Band(r - 37.0, lw) + Band(r - 35.6, lw * 0.7)) * dashes1;
    float a2 = a - t * 0.4;
    float zig = 26.0 + 5.0 * abs(frac(a2 * 12.0 / TAU) - 0.5) * 2.0;
    float star = Band((r - zig) * 0.75, lw) * (0.4 + 0.6 * step(0.35, H11(floor(a2 * 160.0 / TAU + t * 18.0))));
    float inner = Band(r - 31.5, lw * 0.8) * (0.5 + 0.5 * step(0.3, H11(floor(a * 120.0 / TAU - t * 25.0))));
    // the rune band between 32.2 and 35, read along the circle
    float N = 60.0;
    float ra = (a - t * 0.15) * N / TAU;
    float2 f = float2(frac(ra), (r - 32.2) / 2.8);
    float rune = Sigil(f, floor(ra)) * step(0.0, f.y) * step(f.y, 1.0) * (0.55 + 0.45 * dashes1);
    float lines = saturate(outer + star + inner + rune);

    // sparks off the rim: each cell of angle throws one along the tangent
    float spark = 0;
    float M = 40.0;
    float sa = a + t * 0.6;
    float cell = floor(sa * M / TAU);
    float life = frac(t * 1.6 + H11(cell * 3.3));
    float emitA = (cell + 0.5) * TAU / M;
    float along = Ang(sa - emitA) * r;
    float travel = life * 9.0;
    float radial = r - 37.0 - life * life * 4.0;
    spark = Band(radial, lw * 1.3) * step(along, travel) * step(travel - 2.5, along) * (1.0 - life) * step(0.35, H11(cell + floor(t * 1.6 + H11(cell * 3.3))));

    float3 orange = float3(1.0, 0.52, 0.1);
    float3 rgb = orange * (lines + spark) * 1.3 + float3(1.0, 0.9, 0.6) * Band(r - 37.0, lw * 0.35) * dashes1;
    float alpha = saturate((lines + spark) * 0.95);
    float4 m = float4(rgb, alpha);
    float4 c = Over(icon, m * (1.0 - front));
    c = Over(m * front * lerp(1.0, 0.8, icon.a), c);
    return lerp(icon, c, Room(p));
}
