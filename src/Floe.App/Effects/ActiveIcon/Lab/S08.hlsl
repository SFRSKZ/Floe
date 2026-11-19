// S08 The Matrix, code rain in depth. Three layers of falling glyph columns: the far ones small and dim behind
// the icon, the near one large, bright and passing in front of it (thinly, so the icon still reads). Each column
// has a white leading glyph, a green trail that fades, and glyphs that keep changing. The icon catches the
// green on its edge.

float Glyph(float2 f, float2 cell, float seed, float2 grid)
{
    // a 3 x 5 pixel glyph with a structure: a stem or bar most glyphs have, plus random bits
    float2 g = floor(f * grid);
    float2 fp = frac(f * grid);
    float r = H21(g + cell * 7.13 + seed * 13.7);
    float stem = step(0.5, H21(cell + seed)) * step(abs(g.x - floor(grid.x * 0.5)), 0.1);
    float bar = step(0.6, H21(cell * 1.3 + seed)) * step(abs(g.y - floor(H21(cell + 2.0 + seed) * 5.0)), 0.1);
    float on = max(step(0.62, r), stem * step(0.3, r));
    float pix = step(0.16, fp.x) * step(fp.x, 0.84) * step(0.14, fp.y) * step(fp.y, 0.86);
    float inside = step(0.1, f.x) * step(f.x, 0.9) * step(0.06, f.y) * step(f.y, 0.94);
    return on * pix * inside;
}

float2 Layer(float2 p, float cw, float2 grid, float speed, float seed, float t)
{
    float ch = cw * 1.45;
    float2 q = p + float2(seed * 3.7, 0);
    float col = floor(q.x / cw);
    float r = H11(col * 1.91 + seed);
    float rowF = q.y / ch;
    float row = floor(rowF);
    float period = 22.0 + 14.0 * r;
    float head = frac(t * speed / (ch * period) + r) * period - 0.5 * TOTAL / ch - 4.0;
    float behind = head - row;
    float len = 6.0 + 8.0 * H11(col + seed * 3.0);
    float trail = step(0.0, behind) * step(behind, len) * pow(saturate(1.0 - behind / len), 1.4);
    float isHead = step(0.0, behind) * step(behind, 1.0);
    float swap = floor(t * (3.0 + 6.0 * H21(float2(col, row))) + H21(float2(row, col)) * 10.0);
    float2 f = float2(frac(q.x / cw), frac(rowF));
    float g = Glyph(f, float2(col, row), swap + seed, grid) * step(0.5, r);
    return float2(g * trail, g * isHead);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float3 green = float3(0.22, 1.0, 0.42);
    float3 white = float3(0.85, 1.0, 0.9);
    float2 far = Layer(p, 2.4, float2(2.0, 3.0), 9.0, 1.0, t);
    float2 mid = Layer(p, 3.4, float2(3.0, 4.0), 13.0, 5.0, t);
    float2 near = Layer(p, 4.6, float2(3.0, 5.0), 19.0, 9.0, t);

    float farA = far.x * 0.35;
    float midA = mid.x * 0.6;
    float4 back = Lit(green * 0.7, farA);
    back = Over(Lit(lerp(green, white, mid.y), midA), back);
    float4 c = Over(icon, back);
    // the icon's rim takes the green
    float rim = Band(d + 0.6, 0.6) * icon.a * 0.35;
    c = Add(c, green * rim, 0);
    // the near layer crosses over the icon, thin there
    float nearA = near.x * lerp(0.95, 0.38, icon.a);
    c = Over(Lit(lerp(green * 1.1, white, near.y), nearA), c);
    return lerp(icon, c, Room(p));
}
