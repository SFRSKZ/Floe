// S04 Dune, Holtzman shield. A shell 8.5 DIP out follows the icon's outline; it is nearly clear facing us and
// thickens toward its rim (Fresnel), with thin-film colour bands sliding over it and a fine moire weave.
// The icon behind shimmers through it. Every few seconds a strike lands: rings run over the shell from the
// hit and a flash marks the spot; now and then it is the slow blade, red, and the shell gives way there.

#define SHELL 8.5

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d = Dist(uv);
    float inShell = Fill(d - SHELL);
    float x = saturate((d + 4.0) / (SHELL + 4.0));
    float fres = pow(x, 3.5);
    float rim = Band(d - SHELL, 0.32);

    // strikes: one per period, at a random point of the shell
    float period = 2.7;
    float k = floor(t / period);
    float age = frac(t / period) * period;
    float ha = H11(k * 1.7) * TAU;
    float2 hit = float2(cos(ha), sin(ha)) * (HALF + SHELL - 1.0);
    float slow = step(0.68, H11(k * 5.3 + 2.0));
    float dist = length(p - hit);
    float speed = lerp(26.0, 9.0, slow);
    float ripple = 0;
    [unroll] for (int i = 0; i < 3; i++)
    {
        float rr = age * speed - i * 2.2;
        ripple += Band(dist - rr, 0.35) * step(0.0, rr) * exp(-age * lerp(1.6, 0.9, slow)) * (1.0 - i * 0.28);
    }
    float flash = exp(-age * 7.0) * Fill(dist - 2.6 - age * 6.0);
    float breach = slow * Fill(dist - 3.5) * smoothstep(0.2, 0.6, age) * (1.0 - smoothstep(1.4, 2.2, age));

    // the shimmer: the icon seen through the shell, nudged by a slow noise field
    float2 n = float2(Fbm(p * 0.22 + t * 0.35), Fbm(p * 0.22 - t * 0.31 + 5.0)) - 0.5;
    float4 seen = ICON(uv + n * (0.6 + ripple * 1.2) / TOTAL);

    // thin film colour, moving bands, and the weave
    float film = d * 0.21 + Fbm(p * 0.12 + t * 0.12) * 1.4 - t * 0.12;
    float3 thin = 0.55 + 0.45 * cos(TAU * (film + float3(0.0, 0.33, 0.67)));
    float3 blue = float3(0.36, 0.62, 1.0);
    float3 shellRgb = lerp(blue, thin, 0.45);
    float2 w = Rot(p, 0.785) * 1.9;
    float weave = Band(sin(w.x + n.x * 4.0) * sin(w.y + n.y * 4.0), 0.06);
    float3 hitRgb = lerp(float3(0.75, 0.9, 1.0), float3(1.0, 0.25, 0.12), slow);

    float cover = inShell * (0.07 + fres * 0.85 + weave * 0.12 * (0.3 + fres)) * (1.0 - breach);
    float3 rgb = shellRgb * cover + shellRgb * rim * 0.9 * (1.0 - breach) + float3(1, 1, 1) * rim * 0.25;
    rgb += hitRgb * (ripple * inShell * 0.9 + flash * 0.9);
    float alpha = saturate(max(rgb.r, max(rgb.g, rgb.b)) * 0.85);

    float4 c = lerp(icon, seen, inShell * (1.0 - breach));
    c = Add(c, rgb, alpha);
    // the breach glows at its torn edge
    float tear = slow * Band(dist - 3.5, 0.3) * smoothstep(0.2, 0.6, age) * (1.0 - smoothstep(1.4, 2.2, age)) * inShell;
    c = Add(c, float3(1.0, 0.35, 0.15) * tear, tear);
    return lerp(icon, c, Room(p));
}
