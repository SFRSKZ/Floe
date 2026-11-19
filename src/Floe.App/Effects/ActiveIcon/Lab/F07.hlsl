// F07 Thor, the Bifrost. Under the icon a seal is burned into the floor, seen in perspective: concentric
// rings, a band of angular runes, an interlace, all etched in white-gold light that chases round. Out of it a
// prismatic column rises round the icon, thin spectral streaks shooting upward, and every few seconds the
// bridge fires: the streaks race and spectral shards fly up.

#define SQUASH 0.3

float Rune(float2 f, float id)
{
    float2 q = f - 0.5;
    float r1 = H11(id * 4.1), r2 = H11(id * 7.3), r3 = H11(id * 1.9);
    float stem = Band(q.x - (r1 - 0.5) * 0.3, 0.07) * step(abs(q.y), 0.42);
    float diag = Band(q.y + q.x * (r2 > 0.5 ? 1.2 : -1.2) - (r3 - 0.5) * 0.4, 0.07) * step(abs(q.x), 0.3) * step(abs(q.y), 0.42);
    float twig = Band(q.y - q.x * 1.2 + 0.1, 0.07) * step(0.0, q.x) * step(q.x, 0.3) * step(0.55, r3);
    return saturate(stem + diag + twig);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float h = HALF;
    float cyc = frac(t / 5.0) * 5.0;
    float fire = exp(-cyc * 1.6) * step(0.0, cyc);

    // the seal in its plane under the icon
    float2 sc = float2(p.x, (p.y - (h + 2.5)) / SQUASH);
    float r = length(sc);
    float a = atan2(sc.y, sc.x);
    float lw = 0.2 / lerp(1.0, SQUASH, abs(sc.y) / max(r, 1e-3));
    lw = 0.22;
    float rot = t * 0.3;
    float rings = Band(r - 36.0, 0.35) + Band(r - 34.5, 0.2) + Band(r - 28.0, 0.25) + Band(r - 21.0, 0.2);
    float N = 30.0;
    float ra = (a - rot) * N / TAU;
    float2 f = float2(frac(ra), (r - 29.0) / 5.0);
    float runes = Rune(f, floor(ra)) * step(0.0, f.y) * step(f.y, 1.0);
    float inter = Band(r - 24.5 - 2.2 * sin((a + rot * 1.5) * 16.0), 0.25) + Band(r - 24.5 + 2.2 * sin((a + rot * 1.5) * 16.0), 0.25);
    float chase = 0.45 + 0.55 * pow(saturate(0.5 + 0.5 * cos(a * 2.0 - t * 2.4)), 6.0) + fire;
    float seal = saturate(rings + runes * 0.9 + inter * 0.6) * chase * step(r, 37.0);
    // the seal is in front of nothing: hidden behind the icon where they overlap, apart from its near rim
    float4 sealL = Lit(float3(1.0, 0.86, 0.55), seal * 0.95);
    sealL.rgb += float3(1.0, 1.0, 0.9) * Band(r - 36.0, 0.12) * chase;

    // the prismatic column round the icon
    float colX = abs(p.x) - h;
    float inCol = smoothstep(9.0, 2.0, colX) * step(p.y, h + 2.5) * smoothstep(-0.5, 1.5, d);
    float id = floor(p.x / 1.15);
    float rid = H11(id * 1.37);
    float sp = lerp(0.6, 2.2, fire) + rid * 0.6;
    float run = frac(p.y * 0.035 + t * sp + rid * 5.0);
    float streak = Band(frac(p.x / 1.15) - 0.5, 0.1) * pow(run, 4.0) * step(0.3, rid);
    float3 spec = Hue(p.x / 46.0 + t * 0.08 + rid * 0.1) * 0.9 + 0.15;
    float column = streak * inCol * (0.5 + 0.8 * fire);

    // shards: little spectral diamonds flying up when it fires
    float sh = 0;
    float3 shc = 0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float fi = i;
        float2 c = float2((H11(fi * 3.1 + floor(t / 5.0)) - 0.5) * (2.0 * h + 14.0), h - cyc * (16.0 + 10.0 * H11(fi)));
        float2 q = Rot(p - c, t * 3.0 + fi);
        float k = Fill(abs(q.x) * 1.8 + abs(q.y) - 1.0) * fire * 1.5;
        sh += k;
        shc += Hue(fi / 8.0) * k;
    }

    float4 c = Over(icon, sealL);
    c = Add(c, spec * column * 1.3, column);
    c = Add(c, shc + sh * 0.3, sh);
    float rim = Band(d - 0.3, 0.25) * (0.35 + fire);
    c = Add(c, Hue(p.y / 50.0 + t * 0.1) * rim, rim * 0.6);
    return lerp(icon, c, Room(p));
}
