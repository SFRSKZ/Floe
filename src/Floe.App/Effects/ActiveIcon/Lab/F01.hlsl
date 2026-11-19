// F01 The Lord of the Rings, the One Ring, with the real ring's shape (measured from ring_lotr_12mm6.stl:
// inner radius 6.3 mm, outer 8.29, band 4.64 mm wide, both faces domed: an elliptical section, 2.0 x 4.64 mm).
// Ray-marched per pixel, so as it tumbles slowly round the icon its inner face shows too; the near half crosses
// over the icon, the far half goes behind. Polished gold reflecting a warm room, the inscription cut into the
// outside and the inside; every few seconds the ring heats and the letters burn, written along the band.

// the measured ring scaled to the icon: outer radius 32.5 DIP
#define K (32.5 / 8.29)
#define RC (7.295 * K)
#define RA (0.995 * K)
#define RB (2.321 * K)
#define HEAT_PERIOD 8.0

float Script(float2 q, float id, float W)
{
    // a flowing letter drawn in DIP (q from the cell centre, W the cell width, about 5 tall): a baseline swash
    // joining it to the next letter, a bowl or loop, a slanted stem, sometimes a dot above; thin crisp strokes
    float r1 = H11(id * 3.1), r2 = H11(id * 5.7), r3 = H11(id * 9.3);
    float sw = 0.42;
    float swash = Band(q.y - 1.5 - 0.45 * sin(q.x / W * 6.2832 + r1 * 6.0), sw);
    float2 lc = q - float2((r2 - 0.5) * 0.3 * W, 0.2);
    float loop = Band(length(lc * float2(1.0, lerp(0.8, 1.25, r3))) - 1.25, sw) * step(0.3, r1);
    float stem = Band(q.x - (r3 - 0.5) * 0.4 * W + q.y * 0.35, sw) * step(-2.3, q.y) * step(q.y, 1.5) * step(0.5, r2);
    float hook = Band(length(q - float2((r1 - 0.5) * 0.5 * W, -1.3)) - 0.8, sw) * step(q.y, -1.3) * step(0.6, r3) * (1.0 - step(0.5, r2));
    float dot1 = Fill(length(q - float2((r1 - 0.5) * 0.4 * W, -2.3)) - 0.45) * step(0.55, r3) * step(0.5, r2);
    return saturate(swash + loop + stem + hook + dot1);
}

float3 Room3(float3 n)
{
    // reflect the view in the normal and look up a warm room: bright sky, a dark horizon line, a darker floor
    float3 r = reflect(float3(0, 0, -1), n);
    float up = -r.y;
    float sky = smoothstep(-0.18, 0.02, up);
    float3 env = lerp(float3(0.26, 0.14, 0.04), float3(1.0, 0.86, 0.55), sky);
    env += float3(1.0, 0.95, 0.85) * smoothstep(0.55, 0.7, up) * 0.55;
    env *= 1.0 - 0.6 * Band(up + 0.04, 0.035);
    // a window off to the side for a second, sharp reflection
    env += float3(1.0, 0.97, 0.9) * Fill(abs(r.x - 0.55) - 0.12) * Fill(abs(up - 0.25) - 0.2) * 0.8;
    return float3(1.0, 0.80, 0.38) * env;
}

// distance to the ring in its own frame (l.xy in its plane, l.z along its axis)
float RingSdf(float3 l)
{
    float2 q = float2(length(l.xy) - RC, l.z);
    float e = length(q / float2(RA, RB));
    return (e - 1.0) * RA;
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float incl = 1.05 + 0.25 * sin(t * 0.29);
    float node = 0.35 * sin(t * 0.21) + 0.15;
    float3 u, v;
    Basis(incl, node, u, v);
    float3 w = cross(u, v);

    // march straight into the screen from in front of everything
    float z0 = 42.0;
    float tt = 0.0;
    float best = 1e3;
    float bestT = 0.0;
    float hit = 0.0;
    [loop] for (int i = 0; i < 44; i++)
    {
        float3 P = float3(p, z0 - tt);
        float3 l = float3(dot(P, u), dot(P, v), dot(P, w));
        float dd = RingSdf(l);
        if (dd < best) { best = dd; bestT = tt; }
        if (dd < 0.02) { hit = 1.0; break; }
        tt += max(dd * 0.85, 0.08);
        if (tt > 2.0 * z0) break;
    }
    float cover = max(hit, 1.0 - smoothstep(0.0, 1.5 * AA, best));

    float3 P = float3(p, z0 - bestT);
    float3 l = float3(dot(P, u), dot(P, v), dot(P, w));
    float rr = length(l.xy);
    float2 radial = l.xy / max(rr, 1e-3);
    float2 q = float2(rr - RC, l.z);
    float2 g = q / (float2(RA, RB) * float2(RA, RB));
    float3 nl = normalize(float3(radial * g.x, g.y));
    float3 n = normalize(nl.x * u + nl.y * v + nl.z * w);
    float3 rgb = Room3(n);
    rgb += pow(saturate(dot(n, normalize(float3(-0.35, -0.7, 0.6)))), 90.0) * 1.5;
    // grazing faces darken a little: the edge stays sharp against the icon
    rgb *= 0.55 + 0.45 * smoothstep(0.0, 0.35, abs(n.z));

    // the inscription, round the band on the outside and the inside; the ring turns slowly about its axis
    float ht = frac(t / HEAT_PERIOD) * HEAT_PERIOD;
    float heat = smoothstep(0.0, 1.2, ht) * (1.0 - smoothstep(3.2, 4.6, ht));
    float th = atan2(l.y, l.x) + t * 0.25;
    float N = 34.0;
    float along = th * N / TAU;
    // the letter cell in DIP: arc length on the face the ray hit, and the height across the band
    float W = TAU * rr / N;
    float2 lq = float2((frac(along) - 0.5) * W, l.z);
    float band = step(abs(l.z), 3.2);
    float letters = Script(lq, floor(along) + step(q.x, 0.0) * 50.0, W) * band;
    float wake = smoothstep(0.0, 0.08, heat * 1.15 - frac((th + PI) / TAU));
    // cut into the gold when cool, burning when hot
    rgb *= 1.0 - letters * 0.3 * (1.0 - heat);
    float3 fire = lerp(float3(1.0, 0.28, 0.04), float3(1.0, 0.85, 0.45), letters);
    rgb = lerp(rgb, rgb * 0.8, heat) + fire * letters * heat * wake * 1.9;
    float4 ring = float4(rgb * cover, cover);

    // embers off the band while it is hot
    float ember = 0;
    [unroll] for (int k = 0; k < 6; k++)
    {
        float life = frac(t * 0.5 + H11(k * 1.3));
        float ea = H11(k * 4.7 + floor(t * 0.5 + H11(k * 1.3))) * TAU;
        float3 e = OnOrbit(u, v, RC + RA, ea);
        float2 ep = e.xy + float2(sin(life * 6.0 + k) * 1.2, -life * 11.0);
        ember += Fill(length(p - ep) - 0.4 * (1.0 - life)) * (1.0 - life) * heat * step(0.0, e.z);
    }

    float front = step(0.0, P.z);
    float4 c = Over(icon, ring * (1.0 - front));
    c = Over(ring * front, c);
    c = Add(c, float3(1.0, 0.55, 0.15) * ember * 1.4, ember);
    return lerp(icon, c, Room(p));
}
