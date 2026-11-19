// F04 Frozen, ice crystals. Ice ferns grow out from the icon's edge: each a faceted spine (bright face and
// shaded face meeting in a ridge, so it reads as a 3D crystal) with side branches at 60 degrees, tapering to
// sharp tips. The icon's own edge frosts over in a crisp cell pattern. Tips glint like stars. After a while
// it all melts back and grows again.

#define CYCLE 8.0

float SegDist(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = saturate(dot(pa, ba) / dot(ba, ba));
    return length(pa - ba * h);
}

// the fern in its own frame: x across, y out from the edge
float2 Fern(float2 q, float len, float id)
{
    // returns x: coverage, y: facet light
    float w = 0.55 * saturate(1.0 - q.y / max(len, 0.01));
    float spine = Fill(abs(q.x) - w) * step(0.0, q.y) * step(q.y, len);
    float face = step(0.0, q.x);
    float cover = spine;
    float light = spine * lerp(0.55, 1.0, face);
    [unroll] for (int j = 0; j < 3; j++)
    {
        float by = len * (0.25 + 0.22 * j);
        float bl = len * (0.42 - 0.1 * j) * (0.7 + 0.5 * H11(id * 3.0 + j));
        float side = q.x < 0.0 ? -1.0 : 1.0;
        float2 dir = float2(0.866 * side, 0.5);
        float2 a0 = float2(0, by);
        float2 b0 = a0 + dir * bl;
        float sd = SegDist(q, a0, b0);
        float h = saturate(dot(q - a0, dir) / bl);
        float bw = 0.35 * (1.0 - h) + 0.05;
        float br = Fill(sd - bw) * step(by * 0.0, len);
        float facet = step(0.0, (q - a0).x * dir.y - (q - a0).y * dir.x) ;
        cover = max(cover, br);
        light = max(light, br * lerp(0.5, 0.95, facet));
    }
    return float2(cover, light);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float ct = frac(t / CYCLE) * CYCLE;
    float k = floor(t / CYCLE);
    float grow = smoothstep(0.0, 2.6, ct);
    float melt = smoothstep(6.4, 7.8, ct);
    float d = Dist(uv);
    float2 g = Grad(uv);
    float a = atan2(p.y, p.x);

    // ferns in cells round the edge, each pointing straight out
    float N = 26.0;
    float cellF = (a + PI) * N / TAU;
    float id = floor(cellF);
    float rnd = H11(id * 1.7 + k * 13.0);
    float off = (frac(cellF) - 0.5) * TAU * (HALF + max(d, 0.0) * 0.6) / N;
    float len = (3.5 + 6.5 * rnd) * grow * (1.0 - melt);
    float2 q = float2(off + (rnd - 0.5) * 1.5, d - 0.2);
    float2 fern = Fern(q, len, id) * step(0.25, rnd);
    float3 ice = lerp(float3(0.42, 0.66, 0.92), float3(0.9, 0.97, 1.0), fern.y);
    float tip = Star(q - float2(0, len), 1.4 + rnd) * step(0.25, rnd) * (0.5 + 0.5 * sin(t * 4.0 + id * 2.3)) * grow * (1.0 - melt);

    // frost on the icon's edge: hard little cells of white
    float2 fc = floor(p * 0.9);
    float2 fr = frac(p * 0.9) - 0.5;
    float frostCell = step(0.45, H21(fc + k)) * step(max(abs(fr.x), abs(fr.y)), 0.38);
    float frost = frostCell * smoothstep(-4.5, -3.0, d) * Fill(d) * grow * (1.0 - melt) * icon.a;
    // and a thin icy rim line
    float rim = Band(d + 0.3, 0.3) * grow * (1.0 - melt);

    float4 c = icon;
    c = Add(c, float3(0.85, 0.95, 1.0) * frost * 0.55, frost * 0.4);
    c = Add(c, float3(0.85, 0.95, 1.0) * rim * 0.9, rim * 0.6);
    float fa = fern.x * smoothstep(-0.2, 0.4, d) * (1.0 - melt * 0.5);
    c = Over(Lit(ice, fa * 0.92), c);
    c = Add(c, float3(1, 1, 1) * tip, tip);
    // fine ice dust in the air
    float2 dc = floor(p / 2.2);
    float dust = Fill(length(p - (dc + H22(dc + k)) * 2.2) - 0.22) * step(0.9, H21(dc + 4.0)) * (0.5 + 0.5 * sin(t * 3.0 + H21(dc) * 30.0)) * grow * smoothstep(1.0, 4.0, d);
    c = Add(c, float3(0.9, 0.97, 1.0) * dust, dust * 0.8);
    return lerp(icon, c, Room(p));
}
