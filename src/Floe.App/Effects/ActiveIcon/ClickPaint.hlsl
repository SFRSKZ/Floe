// Van Gogh's brush on a click: the icon stays where it is, but its surface turns into small, curling, thick
// strokes of its own paint, lit like impasto, and then dries back into the picture.
// Shares ClickGlass's first registers, so ClickGlassEffect drives it.
sampler2D Input : register(s0);
sampler2D Field : register(s1);
float Progress : register(c0);
float Tone : register(c1);
float2 Box : register(c2);

float Hash(float2 i) { return frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453); }
float Noise(float2 x);
// the slow, curling field the strokes follow
float Field2(float2 q, float t) { return Noise(q * 2.6 + 7.0 + t * 0.4) + Noise(q * 5.2 + 1.0) * 0.45; }
float Noise(float2 x)
{
    float2 i = floor(x);
    float2 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), f.x), f.y);
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float t = saturate(Progress);
    float total = max(Box.x, 1.0);
    float unit = max(total - 24.0, 1.0);
    float2 q = (uv - 0.5) * total / unit;
    // in, hold, out; zero at both ends, so the overlay starts and ends clear
    float paint = smoothstep(0.0, 0.4, t) * (1.0 - smoothstep(0.66, 1.0, t));

    // The strokes follow the level lines of a slow, curling field: side by side they bend round together and
    // close into eddies at its peaks and dips, the way the sky turns in The Starry Night. The field drifts a
    // little while the paint is wet.
    float field = Field2(q, t);
    float bands = field * 44.0;
    float band = floor(bands / 3.14159);
    // each stroke breaks off and starts again somewhere else along its line
    float dab = smoothstep(0.3, 0.6, Noise(q * 13.0 + band * 3.7));
    float wave = sin(bands);
    // bristles score fine lines along each stroke
    float h = abs(wave) * dab * (0.85 + 0.15 * sin(bands * 4.0));

    // impasto: the strokes are ridges of paint, lit from the top left; the slope comes from the field's own
    // gradient, so it is smooth (screen derivatives would come out in 2x2 blocks)
    float e = 0.02;
    float2 gradient = float2(Field2(q + float2(e, 0), t) - field, Field2(q + float2(0, e), t) - field) / e;
    float2 slope = gradient * (44.0 * cos(bands) * sign(wave)) * dab;
    float light = clamp(dot(slope, float2(-0.6, -0.8)) * 0.012, -1.0, 1.0);

    // The paint is the icon's own, dragged a few pixels along the stroke: flat areas stay flat in colour, but
    // every edge in the picture is pulled out into brush marks.
    float2 along = float2(-gradient.y, gradient.x) / max(length(gradient), 0.001);
    float2 reach = along * (0.7 * paint / total);
    float4 c = tex2D(Input, uv) * 0.3
             + (tex2D(Input, uv + reach) + tex2D(Input, uv - reach)) * 0.22
             + (tex2D(Input, uv + reach * 2.0) + tex2D(Input, uv - reach * 2.0)) * 0.13;

    float3 color = c.rgb / max(c.a, 0.001);
    color = saturate(lerp(dot(color, float3(0.3, 0.59, 0.11)), color, 1.2));
    // thick paint catches the light on one flank and falls into shadow on the other; the grooves between
    // strokes are a darker shade of the same paint, never another colour
    color *= lerp(0.7, 1.0, saturate(h * 1.6)) * (0.92 + light * 0.4);
    color += pow(saturate(light), 2.5) * 0.45 * float3(1.0, 0.97, 0.9);

    // the brush lands in scattered curls first, then covers it all, then lifts away the same way
    float blot = Noise(q * 7.0 + 19.0);
    float alpha = c.a * saturate((paint * 1.3 - blot + 0.05) / 0.12) * step(0.001, paint);
    return float4(saturate(color) * alpha, alpha);
}
