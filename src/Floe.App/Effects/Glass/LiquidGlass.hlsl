sampler2D Input : register(s0);
float2 Size : register(c0);
// capsule at rest: left, top, width, height (window DIPs)
float4 Capsule : register(c1);
float Radius : register(c2);
float Hover : register(c3);
float Bulge : register(c4);
float Plateau : register(c5);
float Sigma : register(c6);
// how far the rim reaches out for the scene behind it, and how much red and blue part there
float Refraction : register(c7);
float Dispersion : register(c8);
float Bevel : register(c9);
// the oil-film rainbow along the rim, and the room's window mirrored in it: 0 leaves the plain refracting glass
float Iridescence : register(c10);
float Specular : register(c11);

// the top edge rises under the cursor the same way the rim warp lifts it
float Top(float x)
{
    float d = max(0, abs(x - Hover) - Plateau);
    return Capsule.y - Bulge * exp(-d * d / (2 * Sigma * Sigma));
}

// signed distance to the rounded capsule, negative inside
float Distance(float2 p)
{
    float top = Top(p.x);
    float bottom = Capsule.y + Capsule.w;
    float2 centre = float2(Capsule.x + Capsule.z * 0.5, (top + bottom) * 0.5);
    float2 extent = float2(Capsule.z * 0.5, (bottom - top) * 0.5);
    float2 q = abs(p - centre) - extent + Radius;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - Radius;
}

float3 Vibrant(float3 color)
{
    float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    // lift deep wallpaper tones, compress highlights, keep the hue
    return saturate(0.06 + luminance * 0.88 + (color - luminance) * 1.8);
}

// a hard-edged band, antialiased over one screen pixel of its own argument
float Band(float value, float from, float to)
{
    float w = max(fwidth(value), 0.0005);
    return saturate((value - from) / w + 0.5) * saturate((to - value) / w + 0.5);
}

// The room the glass mirrors: a wide window above, its two frame bars off to the sides so a level rim shows
// unbroken light and only a bulge's slopes swing the bars into view; a dim strip light below, dark elsewhere.
// r is a reflected direction, x right, y down (screen), z towards the viewer.
float Room(float3 r)
{
    float up = -r.y;
    float pane = Band(up, 0.42, 1.01) * Band(r.x, -0.75, 0.80);
    float bars = Band(r.x, -0.42, -0.38) + Band(r.x, 0.44, 0.48);
    float sky = lerp(1.0, 0.72, saturate((up - 0.3) * 1.8));
    float floorLight = Band(up, -0.74, -0.52) * 0.30;
    return pane * saturate(1 - bars) * sky + floorLight + 0.025;
}

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 p = uv * Size;
    float d = Distance(p);
    float2 normal = float2(Distance(p + float2(1, 0)) - Distance(p - float2(1, 0)),
                           Distance(p + float2(0, 1)) - Distance(p - float2(0, 1)));
    normal /= max(length(normal), 0.0001);

    // a convex bevel: flat in the middle, steepening towards the rim, where it pulls in the scene from just outside;
    // the last couple of pixels stay unbent, so the metal outline lies on a clean edge, not a smeared copy of it
    float rim = 1 - saturate(-d / Bevel);
    float bend = rim * rim * Refraction * saturate(-d / 2.5);
    float2 shift = normal * bend / Size;
    float2 spread = shift * Dispersion;

    // five wavelengths, each bent a little more than the last, so the bent rim splits into a spectrum
    float3 color = tex2D(Input, saturate(uv + shift + spread)).rgb * float3(1, 0, 0)
                 + tex2D(Input, saturate(uv + shift + spread * 0.5)).rgb * float3(0.5, 0.5, 0)
                 + tex2D(Input, saturate(uv + shift)).rgb * float3(0, 1, 0)
                 + tex2D(Input, saturate(uv + shift - spread * 0.5)).rgb * float3(0, 0.5, 0.5)
                 + tex2D(Input, saturate(uv + shift - spread)).rgb * float3(0, 0, 1);
    color /= float3(1.5, 2, 1.5);
    float alpha = tex2D(Input, uv).a;
    color /= max(alpha, 0.0001);
    color = Vibrant(color);

    // the surface as a drop: level in the middle, curling down steeply at the rim
    float slope = rim * rim * 3.2 + rim * 0.2;
    float3 n = normalize(float3(normal * slope, 1));
    float3 r = float3(2 * n.z * n.xy, 2 * n.z * n.z - 1);
    float facing = 1 - n.z;
    // outside the capsule nothing is added: the clip may lag the shape by a frame
    float inside = saturate(0.5 - d);
    float fresnel = (0.05 + 0.95 * facing * facing * facing) * inside;

    // the rim is thicker glass: it takes a little of the light through it
    color *= 1 - 0.12 * smoothstep(0.5, 0.95, rim) * saturate(Specular);

    // the window mirrored, each colour off a slightly different slope: its edges fray into rainbows
    float2 fray = normal * (0.04 + 0.22 * Iridescence) * rim;
    float3 room = float3(Room(r + float3(fray, 0)), Room(r), Room(r - float3(fray, 0)));
    color += room * fresnel * Specular * 1.8;

    // a thin oil film on the rim: its colour turns with the slope and drifts along the dock
    float film = rim * rim * fresnel;
    float3 hue = 0.5 + 0.5 * cos(6.2832 * (rim * 1.4 + facing * 1.1 + p.x / Size.x * 0.8 + float3(0, 0.33, 0.67)));
    color += hue * film * Iridescence * 0.85;

    // the bevel catches a little more light than the flat middle
    color = saturate(color + rim * rim * 0.04);
    return float4(color * alpha, alpha);
}
