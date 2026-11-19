// S03 Interstellar, Gargantua. A thin accretion disk seen almost edge-on crosses in front of the icon's lower
// half; its far half goes behind the icon, and the far side bent by the hole shows as a bright arc hugging the
// icon's outline, strongest over the top. A hairline photon ring sits right on the edge. Inner gas turns faster,
// the side coming toward us is brighter (Doppler), the disk is cut by thin dark gaps.

#define RIN 29.5
#define ROUT 38.5
#define TILT 0.17

float3 Heat(float x)
{
    float3 c = lerp(float3(0.55, 0.12, 0.02), float3(1.0, 0.55, 0.16), saturate(x * 1.6));
    c = lerp(c, float3(1.0, 0.86, 0.6), saturate(x * 1.6 - 0.6));
    return lerp(c, float3(1.0, 0.98, 0.94), saturate(x * 2.0 - 1.4));
}

// the gas pattern at radius fraction rr and angle phi (seam behind the icon)
float Gas(float phi, float rr, float t)
{
    float spin = t * (0.55 / (0.35 + rr));
    float n = Fbm(float2((phi + spin) * 2.6, rr * 5.0));
    float fil = VNoise(float2((phi + spin) * 11.0, rr * 23.0));
    float gaps = 0.72 + 0.28 * step(0.18, abs(frac(rr * 7.0 + n * 0.6) - 0.5) * 2.0);
    return saturate((n * 1.5 - 0.15) * (0.75 + 0.5 * fil)) * gaps;
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);

    // the disk in its own plane
    float2 q = float2(p.x, (p.y - 1.5) / TILT);
    float r = length(q);
    float rr = (r - RIN) / (ROUT - RIN);
    float phi = atan2(-q.x, q.y);
    float inDisk = smoothstep(-0.02, 0.03, rr) * (1.0 - smoothstep(0.93, 1.0, rr));
    float gas = Gas(phi, rr, t);
    float dop = 1.0 + 0.8 * sin(phi);
    float heat = gas * dop * (1.25 - rr * 0.7);
    float3 diskRgb = Heat(heat) * (0.35 + heat * 1.3);
    float diskA = saturate(0.5 + heat * 1.6) * inDisk;
    // the near rim edge, a crisp bright lip
    float lip = (1.0 - smoothstep(0.0, 0.06, abs(rr))) * 0.8;
    diskRgb += Heat(1.0) * lip;
    float4 disk = float4(diskRgb * inDisk, saturate(diskA + lip * 0.5));
    float front = step(1.5, p.y);

    // the bent far side: an arc on the icon's outline, thick and bright over the top, thin underneath
    float a = atan2(p.y, p.x);
    float topness = saturate(-p.y / HALF);
    float width = lerp(1.2, 4.2, topness);
    float arcBand = smoothstep(0.6, 1.1, d) * (1.0 - smoothstep(width, width + 0.8, d));
    float arcGas = Gas(atan2(-p.x, -p.y), saturate((d - 0.6) / 4.5), t * 1.2);
    float arcHeat = arcGas * (0.55 + 0.6 * topness) * (1.0 + 0.6 * sin(-a));
    float3 arcRgb = Heat(arcHeat) * (0.3 + arcHeat) * arcBand;
    float arcA = saturate(0.2 + arcHeat * 1.3) * arcBand;

    // the photon ring right on the edge
    float ring = Band(d - 0.55, 0.16);

    // a few lensed stars, crisp, smeared a little round the hole
    float2 sc = floor(p / 5.0);
    float2 sr = H22(sc + 3.0);
    float star = Star(p - (sc + sr) * 5.0, 0.9) * step(0.82, H21(sc)) * smoothstep(6.0, 9.0, d) * (0.5 + 0.5 * sin(t * 2.0 + sr.x * 9.0));

    float4 c = float4(float3(0.9, 0.9, 1.0) * star * 0.7, star * 0.7);
    c = Over(disk * (1.0 - front), c);
    c = Over(float4(arcRgb, arcA), c);
    c = Over(icon, c);
    c = Add(c, float3(1.0, 0.92, 0.78) * ring * 1.1, ring * smoothstep(-0.2, 0.4, d));
    c = Over(disk * front, c);
    return lerp(icon, c, Room(p));
}
