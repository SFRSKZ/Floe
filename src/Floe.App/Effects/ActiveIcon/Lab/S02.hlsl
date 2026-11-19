// S02 Tron, light cycles. Two cycles chase round the icon in opposite directions on a lane 4.5 DIP out.
// Each leaves a light wall that stands up off the floor (seen a little from above, so it rises on screen),
// with bright top and base edges, a translucent scanned body, and a tail that crumbles into pixels.
// The floor grid only shows where the walls light it. When the two heads pass, sparks.

#define LANE 4.5
#define WALL 3.2

float4 Cycle(float2 p, float d0, float d1, float a, float head, float dir, float3 col, float t)
{
    // how far behind its head this pixel's angle is, along the way the cycle came
    float behind = Ang((head - a) * dir);
    behind = behind < -0.05 ? behind + TAU : behind;
    float trail = 3.0;
    float on = step(-0.05, behind) * step(behind, trail);
    float age = saturate(behind / trail);

    // the wall: between the lane line at the floor and the same line lifted by WALL
    float base = Band(d0 - LANE, 0.32);
    float top = Band(d1 - LANE, 0.42);
    float inside = step((d0 - LANE) * (d1 - LANE), 0.0);
    float scan = 0.55 + 0.45 * step(0.5, frac(p.y * 1.25 + t * 3.0));
    // the tail breaks into square pixels that wink out
    float2 cell = floor(p / 0.9);
    float crumble = step(H21(cell + floor(head * 3.0)), 1.0 - smoothstep(0.65, 1.0, age) * 1.15);
    float fade = (1.0 - age * 0.55) * crumble * on;
    float rise = saturate((d0 - LANE) / (d0 - d1 + 1e-3));
    float bodyK = inside * (0.3 + 0.6 * rise) * scan;
    float3 rgb = col * (bodyK + base * 0.9) + (col * 0.6 + 0.5) * top;
    float alpha = saturate(bodyK * 0.9 + base * 0.85 + top);

    // the cycle itself: a white-hot capsule at the head with a coloured shell
    float near = abs(Ang(a - head));
    float bike = smoothstep(0.16, 0.1, near) * (Band(d0 - LANE, 0.9) + Band(d1 - LANE, 0.9) * 0.8 + inside);
    float bikeCore = smoothstep(0.1, 0.05, near) * Band(d0 - LANE - 0.0, 0.45);
    rgb = rgb * fade + col * bike * 1.3 + bikeCore * 1.2;
    alpha = saturate(alpha * fade + bike + bikeCore);
    return float4(rgb, alpha);
}

float4 Shade(float2 uv, float2 p, float4 icon)
{
    float t = Time;
    float d0 = Dist(uv);
    float d1 = DistP(p + float2(0, WALL));
    float a = atan2(p.y, p.x);

    float h1 = t * 1.25;
    float h2 = -t * 1.05 + 2.0;
    float3 cyan = float3(0.25, 0.9, 1.0);
    float3 amber = float3(1.0, 0.5, 0.08);
    float4 w1 = Cycle(p, d0, d1, a, h1, 1.0, cyan, t);
    float4 w2 = Cycle(p, d0, d1, a, h2, -1.0, amber, t);

    // the floor grid around the icon, lit only near the light
    float2 g = abs(frac(p / 3.0) - 0.5) * 3.0;
    float grid = max(Band(g.x, 0.12), Band(g.y, 0.12));
    float lane = exp(-abs(d0 - LANE) / 2.5);
    float3 floorRgb = (cyan * w1.a + amber * w2.a + (cyan + amber) * 0.12 * lane) * grid * 0.45;
    float floorA = grid * saturate(0.06 * lane + (w1.a + w2.a) * 0.35) * smoothstep(0.5, 2.0, d0);

    // a hairline of the lane itself so the loop reads even where no wall stands
    float laneLine = Band(d0 - LANE, 0.12) * 0.22;

    // sparks where the heads pass each other
    float meet = Ang(h1 - h2);
    float burst = saturate(1.0 - abs(meet) / 0.35);
    float ma = h1 - meet * 0.5;
    float2 mp = float2(cos(ma), sin(ma));
    float spark = 0;
    [unroll] for (int i = 0; i < 6; i++)
    {
        float sa = ma + (H11(i + floor(t)) - 0.5) * 2.4;
        float2 dirv = float2(cos(sa), sin(sa));
        float2 q = p - mp * (HALF + LANE) - dirv * burst * 0.5 * (4.0 + 6.0 * H11(i * 3.0));
        spark += Star(q, 1.6) * burst;
    }

    float4 c = icon;
    c = Add(c, floorRgb, floorA);
    c = Add(c, float3(0.6, 0.8, 0.9) * laneLine, laneLine);
    c = Over(w2, c);
    c = Over(w1, c);
    c = Add(c, float3(1.0, 0.95, 0.85) * spark, spark);
    return lerp(icon, c, Room(p));
}
