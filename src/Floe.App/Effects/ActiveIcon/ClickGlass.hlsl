// Adapted from ReactDrops Liquid Glass Logo, copyright (c) 2026 ReactDrops.
// See ClickGlass.LICENSE.txt for the retained ReactDrops Component License 1.0.
sampler2D Input : register(s0);
sampler2D Field : register(s1);
float Progress : register(c0);
float Tone : register(c1);
float2 Box : register(c2);
float Palette : register(c3);
float4 ColorA : register(c4);
float4 ColorB : register(c5);
// The active icon's loop: 0 plays the click as usual; above 0 the finished glass hollows into a ring
// that runs from the edge in to the middle (1), where the next drop opens.
float Inward : register(c6);
// 1 keeps the icon unbent under the glass (the active icon's loop): only the glass's light moves
float Sharp : register(c7);
// 1 shows the icon through the glass as an LED screen: rows of light dots that curve with the lens
float Screen : register(c8);

float Dist(float2 uv) { return (tex2D(Field, uv).r - 0.5) * 32.0; }
float Bell(float x, float width) { x /= width; return exp(-x * x); }
// one lit dot of the screen grid, slightly taller than wide like a row of LEDs
float Dot(float2 q, float pitch) { float2 c = frac(q / pitch) - 0.5; float soft = 0.5 / pitch; return 1.0 - smoothstep(0.30 - soft, 0.30 + soft, length(c * float2(1.0, 0.85))); }

// Three wavelength bands replace the reference's 32-band integration on ps_3_0.
float2 Bend(float3 normal, float wavelength, float spread, float depth)
{
    float ior = 1.38 + (0.04 / (wavelength * wavelength) - 0.12) * spread;
    float3 ray = refract(float3(0, 0, -1), normal, 1.0 / ior);
    return ray.xy / max(-ray.z, 0.25) * depth;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float t = saturate(Progress) * 1.8;
    float total = max(Box.x, 1.0);
    float unit = max(total - 24.0, 1.0);
    float enter = smoothstep(0.025, 0.16, t);
    float fade = enter * (1.0 - smoothstep(1.05, 1.8, t));
    float glass = 1.0 - smoothstep(1.05, 1.65, t);
    float morph = smoothstep(0.29, 0.48, t);
    float scale = max(0.02, enter * (0.64 + 0.36 * (1.0 - exp(-max(t - 0.16, 0.0) * 5.8))));
    float inward = saturate(Inward);
    scale = max(0.02, scale * lerp(1.0, 0.04, inward));
    float2 p = (uv - 0.5) * total;
    float2 radial = p / max(length(p), 0.001);
    float2 fieldUv = 0.5 + p / (total * scale);
    float d = Dist(fieldUv) * scale;
    float sd = lerp(length(p) - unit * scale * 0.5, d, morph);
    float px = 1.0 / total;
    float2 gradient = float2(Dist(fieldUv + float2(px, 0)) - Dist(fieldUv - float2(px, 0)),
                             Dist(fieldUv + float2(0, px)) - Dist(fieldUv - float2(0, px)));
    float2 outward = lerp(radial, gradient / max(length(gradient), 0.001), morph);
    outward /= max(length(outward), 0.001);
    float mask = 1.0 - smoothstep(-0.7, 0.7, sd);
    // a wave rather than a drop: only a band of glass along the shape's edge
    float band = max(unit * 0.11, 1.6);
    mask = lerp(mask, Bell(sd + band * 0.5, band * 0.55), smoothstep(0.0, 0.15, Inward));
    float radius = saturate(1.0 + sd / max(unit * scale * 0.5, 0.01));
    float shoulder = smoothstep(0.60, 1.0, radius);
    float youth = 1.0 - smoothstep(0.245, 0.42, t);
    float slope = (0.04 * radius + 1.05 * shoulder * shoulder + youth * radius * 0.32) * glass;
    float2 optical = lerp(outward, radial, 0.65);
    float3 normal = normalize(float3(optical * slope * (0.8 + 0.4 * radial.x * radial.y), 1));
    float mono = Palette > 0.5 && Palette < 1.5 ? 1.0 : 0.0;
    float sharp = saturate(Sharp);
    float spread = lerp(0.85, 2.2, morph) * (1.0 - mono) * (1.0 - sharp);
    float depth = unit * 1.2 * 0.80 * scale;
    float focus = 1.0 - smoothstep(0.225, 0.335, t);
    float opening = 1.0 - smoothstep(0.19, 0.265, t);
    float profile = lerp(0.7 + 0.6 * radius * radius, 1.0 - 0.9 * radius * radius, opening);
    float2 samplePoint = p * lerp(1.0, profile, focus) + float2(0.025, 0.10) * unit * opening;
    float bendScale = lerp(1.0, lerp(0.35, 0.12, opening), focus) * (1.0 - sharp);
    float screen = saturate(Screen);
    // as the lens is born it drags what it shows sideways, like a picture pulled across a curved screen
    samplePoint.x *= lerp(1.0, 0.55, screen * youth * smoothstep(0.35, 1.0, radius));
    samplePoint = lerp(samplePoint, p, sharp);
    float4 red = tex2D(Input, 0.5 + (samplePoint + Bend(normal, 0.65, spread, depth) * bendScale) / total);
    float4 green = tex2D(Input, 0.5 + (samplePoint + Bend(normal, 0.55, spread, depth) * bendScale) / total);
    float4 blue = tex2D(Input, 0.5 + (samplePoint + Bend(normal, 0.45, spread, depth) * bendScale) / total);
    float3 color = float3(red.r, green.g, blue.b) * 0.94;
    float alpha = max(red.a, max(green.a, blue.a));

    // The screen: the grid lives in the bent space, so its rows curve with the glass, and each colour's grid is
    // a hair larger than the last, so where the lens squeezes them they beat into rainbow moire.
    float2 bent = lerp(p, samplePoint + Bend(normal, 0.55, spread, depth) * bendScale, 0.6);
    float pitch = max(unit / 15.0, 3.0);
    float3 dots = float3(Dot(bent, pitch), Dot(bent * 1.008, pitch), Dot(bent * 1.016, pitch));
    float lit = screen * glass;
    color = lerp(color, color * (0.22 + dots * 1.6), lit);
    // the empty glass between the icon's shapes glows as a dim, unlit screen
    float idle = Dot(bent, pitch) * 0.09 * lit * (1.0 - alpha);
    color += idle * float3(0.75, 0.85, 1.0);
    alpha = max(alpha, idle);

    // Fixed studio lights and a separate inner wall give the shoulder its thickness.
    float key = pow(saturate(0.5 + 0.5 * dot(outward, float2(-0.486, -0.874))), 5.0);
    float fill = pow(saturate(0.5 + 0.5 * dot(outward, float2(0.8, 0.6))), 5.0);
    float wave = sin(p.x * 0.35 + p.y * 0.22) * sin(t * 6.3 + 1.0) * 0.15 * morph;
    float rim = Bell(sd + wave + 0.12, 0.40);
    float wallGap = unit * scale * (0.055 + 0.025 * morph);
    float centerFade = 1.0 - smoothstep(0.62, 1.22, t);
    float wall = Bell(sd + wallGap, 0.50) * centerFade;
    float caustic = (Bell(sd + unit * 0.027, 0.65) + Bell(sd + unit * 0.045, 0.45) * 0.6);
    caustic *= (0.15 + fill * 0.85) * centerFade * (0.75 + 0.25 * sin(p.x * 0.55 + p.y * 0.3));
    float3 cool = float3(0.22, 0.62, 1.0);
    float3 warm = float3(1.0, 0.45, 0.18);
    if (Palette > 1.5) { cool = ColorA.rgb; warm = ColorB.rgb; }
    cool = lerp(cool, float3(0.9, 0.94, 1), mono);
    warm = lerp(warm, float3(0.9, 0.94, 1), mono);
    float3 spectral = cool * Bell(sd + 0.85, 0.45) + warm * Bell(sd - 0.25, 0.45);
    float light = rim * (0.10 + key * 0.80 + fill * 0.3) + wall * (key * 0.5 + fill * 0.35 + 0.02);
    float3 reflection = float3(0.65, 0.80, 0.94) * light + spectral * (0.06 + 0.25 * alpha);
    reflection += lerp(cool, float3(0.8, 0.97, 1), 0.6) * caustic * 0.7;
    float2 sphere = p / max(unit * scale * 0.5, 0.01);
    float3 sphereNormal = normalize(float3(sphere, sqrt(max(0.001, 1.0 - dot(sphere, sphere)))));
    float3 ray = reflect(float3(0, 0, -1), sphereNormal);
    float softbox = Bell(ray.x + 0.12, 1.05) * Bell(ray.y + 0.56, 0.52) * youth * 0.7;
    reflection += float3(0.50, 0.61, 0.72) * softbox;
    // Local shoulder glow approximates the reference's bloom pass (default 0.55).
    reflection += cool * Bell(sd + 1.2, 2.0) * (key + fill) * 0.055;
    reflection *= glass;
    color *= 1.0 - shoulder * lerp(0.10, 0.22, Tone);
    color += reflection * lerp(1.0, 0.8, Tone);
    alpha = max(alpha, max(reflection.r, max(reflection.g, reflection.b)));
    color *= mask;
    alpha *= mask;

    float age = smoothstep(0.36, 1.4, t);
    float rings = (Bell(sd - (1.0 + 1.8 * age), 0.45) * 0.08
                 + Bell(sd - (2.0 + 1.5 * age), 0.5) * 0.04);
    rings *= smoothstep(0.34, 0.48, t) * exp(-1.4 * max(t - 0.36, 0.0)) * (1.0 - age);
    float echo = Bell(sd - 0.8, 0.28) * (0.025 + key * 0.12);
    float3 ghost = lerp(cool, float3(0.7, 0.82, 0.92), 0.65) * (echo + rings * (0.2 + 0.8 * abs(outward.y))) * glass;
    color += ghost;
    alpha = saturate(alpha + max(ghost.r, max(ghost.g, ghost.b)));
    // This is an overlay: the unchanged icon already lives below it.
    return float4(min(color, alpha), alpha) * fade;
}
