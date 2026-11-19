// F03 Harry Potter, the Patronus. Three silver-blue threads of light wind round the icon in a helix, crossing
// in front of it and going behind; they are thicker and brighter where they are near. Bright pulses run up
// each thread with a white star at their head, and tiny sparks peel off and drift up, twinkling.

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float3 silver = float3(0.72, 0.88, 1.0);
    float4 back = float4(0, 0, 0, 0);
    float4 front = float4(0, 0, 0, 0);
    float R = 33.5;
    float w = 0.085;

    [unroll] for (int k = 0; k < 3; k++)
    {
        float ph = k * TAU / 3.0 + t * 1.1;
        float ang = w * p.y + ph;
        float x = R * cos(ang);
        float z = sin(ang);
        float slope = -R * w * sin(ang);
        float dist = (p.x - x) / sqrt(1.0 + slope * slope);
        float near = 0.5 + 0.5 * z;
        float width = 0.22 + 0.42 * near;
        // pulses rising along the thread
        float run = frac(-p.y * 0.022 + t * 0.55 + k * 0.33);
        float pulse = pow(run, 5.0);
        float strand = Band(dist, width) * (0.3 + 0.7 * near) * (0.35 + 0.9 * pulse);
        float sheen = Band(dist + width * 0.35, width * 0.3) * near * 0.6;
        // the head of each pulse burns white
        float head = smoothstep(0.93, 0.985, run) * (1.0 - smoothstep(0.985, 1.0, run));
        strand += Band(dist, width * 1.6) * head * near * 1.5;
        float3 rgb = silver * strand + float3(1, 1, 1) * sheen;
        float4 s = float4(rgb, saturate(strand + sheen));
        if (z > 0.0) front = Over(s, front);
        else back = Over(s * 0.8, back);
    }

    // sparks peeling off and drifting up
    float sp = 0;
    [unroll] for (int i = 0; i < 10; i++)
    {
        float fi = i;
        float life = frac(t * 0.35 + H11(fi * 2.1));
        float born = floor(t * 0.35 + H11(fi * 2.1));
        float y0 = (H11(fi + born * 7.0) - 0.5) * TOTAL * 0.8;
        float k = floor(H11(fi * 9.0 + born) * 3.0);
        float ang = w * y0 + k * TAU / 3.0 + (t - life / 0.35) * 1.1;
        float2 c = float2(R * cos(ang) + sin(life * 7.0 + fi) * 2.0, y0 - life * 14.0);
        float tw = 0.5 + 0.5 * sin(t * 13.0 + fi * 5.0);
        sp += Star(p - c, 1.3 * (1.0 - life) + 0.3) * (1.0 - life) * (0.5 + 0.5 * tw);
    }

    // the icon takes a cold rim from the light
    float d = Dist(uv);
    float rim = Band(d + 0.5, 0.5) * icon.a * 0.3;
    float4 c = Over(icon, back);
    c = Add(c, silver * rim, 0);
    c = Over(front, c);
    c = Add(c, float3(0.9, 0.97, 1.0) * sp, sp * 0.9);
    return lerp(icon, c, Room(p));
}
