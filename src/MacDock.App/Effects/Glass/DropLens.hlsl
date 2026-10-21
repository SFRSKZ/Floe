sampler2D Input : register(s0);
sampler2D Field : register(s1);
float2 Box : register(c0);
float2 Center : register(c1);
float Radius : register(c2);

// Two eight-tap rings retain the neck and tail while smoothing the mask into a lens.
float Height(float2 uv)
{
    float h = tex2D(Input, uv).a * 0.12;
    [unroll] for (int ring = 0; ring < 2; ring++)
    {
        float2 step = (3.0 + ring * 4.0) / Box;
        float2 diagonal = step * 0.707107;
        h += (tex2D(Input, uv + float2(step.x, 0)).a
            + tex2D(Input, uv - float2(step.x, 0)).a
            + tex2D(Input, uv + float2(0, step.y)).a
            + tex2D(Input, uv - float2(0, step.y)).a
            + tex2D(Input, uv + diagonal).a
            + tex2D(Input, uv - diagonal).a
            + tex2D(Input, uv + float2(diagonal.x, -diagonal.y)).a
            + tex2D(Input, uv + float2(-diagonal.x, diagonal.y)).a) * 0.055;
    }
    return h;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float alpha = tex2D(Input, uv).a;
    float2 px = 1.0 / Box;
    float h = Height(uv);
    float2 grad = float2(Height(uv + float2(2.0 * px.x, 0)) - Height(uv - float2(2.0 * px.x, 0)),
                        Height(uv + float2(0, 2.0 * px.y)) - Height(uv - float2(0, 2.0 * px.y))) * 0.25;
    float2 normal = -grad / max(length(grad), 0.0001);
    float2 p = uv * Box - Center;
    // 1.2x central magnification; the steep shoulder folds the image inward at the rim.
    float body = 1.0 - smoothstep(max(Radius, 1.0), max(Radius, 1.0) * 2.2, length(p));
    float2 offset = (-p / 6.0 * body * h + grad * 105.0) * px;
    float nearEdge = saturate((1.0 - h) * 2.0);
    float2 split = normal * px * 0.12 * nearEdge * nearEdge;
    float3 glass = float3(tex2D(Field, saturate(uv + offset + split)).r,
                          tex2D(Field, saturate(uv + offset)).g,
                          tex2D(Field, saturate(uv + offset - split)).b);
    float inner = (tex2D(Input, uv + float2(px.x, 0)).a + tex2D(Input, uv - float2(px.x, 0)).a
                 + tex2D(Input, uv + float2(0, px.y)).a + tex2D(Input, uv - float2(0, px.y)).a) * 0.25;
    float rim = saturate((1.0 - inner) * 2.5);
    float arc = pow(saturate(dot(normal, normalize(float2(-0.7, -0.7)))), 8.0);
    float caustic = pow(saturate(normal.y), 6.0) * exp(-pow(h - 0.72, 2.0) / 0.025);
    glass *= 1.0 - nearEdge * 0.22;
    glass += rim * 0.55 + arc * nearEdge * 0.65 + caustic * 0.24;
    return float4(saturate(glass) * alpha, alpha);
}
