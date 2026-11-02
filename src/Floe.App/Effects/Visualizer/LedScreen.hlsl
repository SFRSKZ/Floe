// A colour LED screen: the picture is cut into Pitch-pixel cells, each cell takes the average of five
// samples of what lies under it, and each cell is drawn as a red, green and blue bar with dark gaps.
sampler2D Input : register(s0);
float2 Size : register(c0);  // Element size in device pixels, so the grid can be placed in pixels.
float Pitch : register(c1);  // Cell pitch in device pixels.

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 size = max(Size, float2(1.0, 1.0));
    float pitch = max(Pitch, 2.0);
    float2 pixel = uv * size;
    float2 local = frac(pixel / pitch);
    // The cell's light: its centre plus four points a quarter cell out, so a cell shows the average of
    // what it covers instead of whatever pixel happens to sit under its middle.
    float2 centre = (pixel - local * pitch + 0.5 * pitch) / size;
    float2 offset = pitch * 0.25 / size;
    float4 cell = (tex2D(Input, centre)
        + tex2D(Input, centre + float2(offset.x, 0.0))
        + tex2D(Input, centre - float2(offset.x, 0.0))
        + tex2D(Input, centre + float2(0.0, offset.y))
        + tex2D(Input, centre - float2(0.0, offset.y))) * 0.2;
    float alpha = saturate(cell.a);
    float3 lit = saturate(cell.rgb / max(cell.a, 0.001) * 1.6);

    // Three capsules per cell, each lit by its own channel. The gap between them stays dark, but a
    // little light spills into it, as it does between the emitters of a real panel.
    float qy = max(abs(local.y - 0.5) - 0.2825, 0.0);
    float3 q = abs(local.x - float3(0.19, 0.5, 0.81));
    float3 bars = sqrt(q * q + qy * qy) - 0.1175;  // cell units; rounded ends, not rectangles
    float3 cover = saturate(0.5 - bars * pitch);
    float3 spill = saturate(1.0 - max(bars, 0.0) * pitch * 0.55);
    float3 colour = min(lit * (cover + spill * 0.14), 1.0);
    // Premultiplied: the cell keeps the alpha of what lies under it, so the screen can sit on anything.
    return float4(colour * alpha, alpha);
}
