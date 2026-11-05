using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// A rainy Night City boulevard seen from above, held open: the strip is the roadway, nine lanes wide, and the
/// spectrum is the traffic on it. Each lane runs its own stream of long-exposure light trails — the trail's length
/// and brightness are that lane's band, its thickness is the low end, one kick sends a hot white car through the
/// next lane in turn, the snare drags the trails towards brake-light red, the hats throw sparks off the wet. Light is
/// authored in linear HDR and resolved through an ACES curve and an sRGB encode, then graded, vignetted and grained;
/// every edge is anti-aliased against the pixel footprint, and each trail pulls a soft vertical reflection into the
/// asphalt beneath it.
/// <para>
/// The four traps it does not fall into: not a still illustration (the trails stream, and their speed, length and
/// thickness are the music — the clock is advanced by the level alone, so a silent frame is exactly still); not
/// blind to the dock gap (the zebra crossings and the light pools sit at x ~.10 and ~.91, the trails run clean
/// through the middle); not unmeasured (level = flow, bass = trail thickness, band = that lane's trail, kick = one
/// car, snare = brake red, hat = sparks); not decorated (asphalt, markings, trails, kerbs).
/// </para>
/// <para>
/// Port note: the shader's y runs down from the top row, so <c>main</c> converts twice — <c>g.y = 1 - uv.y</c> for the
/// design space and <c>px.y = Size.y - position.y</c> for the grain, the zebra bars and the 1 px edge maths. If the
/// shipping target hands over a y-up position, delete both flips; nothing else assumes a direction. The chromatic
/// aberration lives in the show pass, which resamples the field, so the field stays a texture in the port.
/// </para>
/// </summary>
[SceneDoc("Traffic: nine lanes of long-exposure light on a wet boulevard, HDR and ACES graded")]
public sealed class Traffic : FeedbackGpuScene
{
    private const int ConstantFloats = 68; // a multiple of 4: the constant buffer is 16-byte aligned

    private float _clock;    // the exposure: advanced by the level alone, so silence freezes it
    private float _flow;     // how much light there is, eased
    private float _thick;    // the low end, eased: how thick the trails are
    private float _lastKick;
    private int _pass;       // which lane the last kick sent a car through

    public Traffic() : base(ConstantFloats, Shared + FieldPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        var audio = frame.Audio;
        _flow += (Math.Clamp(audio.Level, 0, 1) - _flow) * (1 - MathF.Exp(-frame.Dt * 8f));
        _thick += (Math.Clamp(audio.Bass, 0, 1) - _thick) * (1 - MathF.Exp(-frame.Dt * 9f));
        if (_flow < 0.001f) _flow = 0;
        if (_thick < 0.001f) _thick = 0;

        // the exposure only runs while the music does, straight off the level: no sound, no movement at all
        _clock += frame.Dt * 3.4f * Math.Clamp(audio.Level, 0, 1);

        // a kick is one car: the next lane in turn gets the hot trail
        if (audio.Kick > 0.55f && _lastKick <= 0.55f) _pass = (_pass + 1) % 5;
        _lastKick = audio.Kick;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = frame.Audio.Level;
        d[6] = frame.Audio.Bass;
        d[7] = frame.Audio.Treble;
        d[8] = frame.Audio.Kick;
        d[9] = frame.Audio.Snare;
        d[10] = frame.Audio.Hat;
        d[11] = frame.Audio.Mid;
        PutBands(12, frame);                                  // 24 bands: 12..35
        Put(36, Lead(Color.FromRgb(0xFF, 0xD4, 0x00)));       // sodium yellow: the warm kerb
        Put(40, Color.FromRgb(0x00, 0xE5, 0xFF));             // cyan: the cold kerb, cool glints
        Put(44, Color.FromRgb(0xFF, 0x2E, 0x88));             // magenta: the far kerb, brake glow
        Put(48, Color.FromRgb(0xFF, 0x2E, 0x40));             // brake lights
        Put(52, Color.FromRgb(0x10, 0x14, 0x1E));             // the sidewalk outside the kerbs
        Put(56, Color.FromRgb(0x12, 0x08, 0x1E));             // the night beyond the road
        Put(60, Color.FromRgb(0xFF, 0x8A, 0x36));             // the sodium wash off the kerb
        d[64] = _clock;
        d[65] = _pass;
        d[66] = _thick;
        d[67] = _flow;
    }

    private const string Shared = """
        cbuffer Road : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Treble;
            float Kick;
            float Snare;
            float Spark;
            float Mid;
            float4 Bands[6];
            float4 Core;
            float4 Cyan;
            float4 Magic;
            float4 Alert;
            float4 Body;
            float4 Night;
            float4 Warm;
            float Clock;
            float PassLane;
            float Thick;
            float Flow;
        };

        """ + ShaderHelpers;

    private const string FieldPs = """

        // =============================================================================================
        // The road from above, in linear light. Nine lanes, one spectrum band each, a stream of long-exposure
        // trails per lane, and a wet asphalt that mirrors every one of them with a soft vertical smear. All the
        // edges are resolved against the pixel footprint and the whole thing is rolled off through ACES.
        // =============================================================================================
        static const float LANES = 5.0;
        static const float KERB = 0.055;  // the near kerb, in strip heights above the bottom edge
        static const float FAR = 0.945;   // the far kerb

        float Slab(float x, float a, float b)
        {
            return step(a, x) * step(x, b);
        }

        // an anti-aliased band: soft over one pixel
        float Band(float x, float a, float b, float w)
        {
            return smoothstep(a - w, a + w, x) * smoothstep(b + w, b - w, x);
        }

        float3 Aces(float3 x)
        {
            x = max(x, 0);
            return saturate((x * ((2.51 * x) + 0.03)) / ((x * ((2.43 * x) + 0.59)) + 0.14));
        }

        float3 Film(float3 light, float2 g)
        {
            float3 c = pow(Aces(light), 1.0 / 2.2);
            float l = dot(c, float3(0.2126, 0.7152, 0.0722));
            c = lerp(c * float3(0.76, 0.98, 1.16), c * float3(1.10, 1.01, 0.90), smoothstep(0.10, 0.60, l));
            float2 d = (g - 0.5) * float2(0.85, 2.0);
            c *= 1.0 - (0.36 * dot(d, d));
            float grain = Hash((g * Size) + (Clock * 61.3)) - 0.5;
            c += grain * 0.026 * (1.0 - (0.5 * l));
            return saturate(c);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;                        // y runs down from the top row
            float2 g = float2(uv.x, 1.0 - uv.y);                   // the design space: y up from the bottom
            float2 px = float2(position.x, Size.y - position.y);   // pixels, y up
            float fx = 1.0 / Size.x;
            float fy = 1.0 / Size.y;
            float aa = 1.1 * fy;

            // the three banks keep to their own colour family: yellow west, magenta middle, cyan east
            float zone = floor(g.x * 3.0);
            float3 zoneCol = lerp(lerp(Core.rgb, Magic.rgb, step(0.5, zone)), Cyan.rgb, step(1.5, zone));

            float roadH = FAR - KERB;
            float laneH = roadH / LANES;
            float laneF = saturate((g.y - KERB) / laneH);
            float lane = min(floor(laneF), LANES - 1.0);
            float edge = frac(laneF);
            float onRoad = Band(g.y, KERB, FAR, aa);
            float lband = Band(lane / (LANES - 1.0));
            float laneTone = 0.80 + (0.45 * Hash(float2(lane, 1.7)));

            // --- the asphalt in linear light: grain, standing water, and a soft sheen that catches the neon
            float grain = Hash(float2(floor(px.x / 2.0), floor(px.y / 2.0)));
            float puddle = smoothstep(0.52, 0.92, Noise(float2(g.x * 7.0, g.y * 15.0)));
            float3 road = float3(0.0020, 0.0026, 0.0042) * (0.50 + (0.90 * grain));
            road *= 0.78 + (0.42 * laneTone); road += float3(0.0060, 0.0070, 0.0110) * puddle * (0.30 + (0.70 * Flow));
            road += zoneCol * 0.008 * puddle * (0.4 + (0.6 * Flow));

            // --- the lane markings: dashed, static, dim, so the road is still a road when the music stops
            float dash = smoothstep(0.42, 0.48, frac(px.x / 52.0));
            float mark = Band(edge * laneH, 0.0, 1.3 * fy, aa) * dash;
            road += lerp(float3(0.20, 0.22, 0.27), zoneCol * 0.30, 0.55) * mark;

            // --- street light pools along the kerbs: soft, warm, and only where the eye expects them
            float pool = 0.0;
            for (int i = 0; i < 7; i++)
            {
                float fi = (float)i;
                float lx = ((fi + 0.5) / 7.0) + (0.012 * sin((fi * 3.1) + 0.7));
                float2 dp = float2((g.x - lx) * 3.1, (g.y - 0.52) * 3.4);
                pool += exp(-dot(dp, dp) * 26.0);
            }

            // --- one stream per lane; the clock is what moves it, and only the music moves the clock
            float seed = Hash(float2(lane, 3.7));
            float dir = 1.0 - (2.0 * step(0.5, frac(lane * 0.5)));
            float period = 150.0 + (110.0 * Hash(float2(lane, 9.1)));
            float speedL = 0.55 + (0.75 * Hash(float2(lane, 5.9)));
            float phase = frac((px.x / period) + (dir * Clock * speedL) + seed);
            float len = 0.12 + (0.38 * lband) + (0.16 * Kick);
            float on1 = smoothstep(0.0, 0.035, len - phase);
            float phase2 = frac((px.x / (period * 1.63)) - (dir * Clock * speedL * 1.21) + (seed * 0.51) + 0.31);
            float on2 = smoothstep(0.0, 0.035, (len * (0.30 + (0.45 * lband))) - phase2);
            float on = max(on1, on2);
            float head = 1.0 - (0.55 * saturate(phase / max(len, 0.0001)));   // the exposure head
            float tail = smoothstep(0.62, 0.95, phase / max(len, 0.0001));     // the stop lamp behind it

            // --- a kick is one car through the next lane in turn: thicker, longer and white hot
            float passCar = step(abs(lane - PassLane), 0.5) * Kick;
            float thick = (2.2 + (3.4 * Thick) + (2.2 * lband) + (2.4 * Kick) + (5.0 * passCar)) * fy / laneH;
            float stripe = Band(edge, 0.5 - (thick * 0.5), 0.5 + (thick * 0.5), 1.2 * fy / laneH);
            float3 lamp = lerp(lerp(float3(1.0, 0.94, 0.82), zoneCol, 0.16), Alert.rgb, saturate(tail * 0.9));
            float bright = (0.14 + (0.42 * lband) + (0.30 * Kick) + (0.80 * passCar)) * head * (0.30 + (0.70 * Flow));

            road += lamp * bright * 2.6 * laneTone * on * stripe;
            // the halo a lamp leaves in the air above its own trail: energy, not a blur
            float halo = 1.0 - saturate(abs(edge - 0.5) / (thick * 0.5 + (7.0 * fy / laneH)));
            road += lamp * bright * 0.14 * on * halo * halo;

            // --- the wet mirror under each trail: a hard-edged smear pulled towards the viewer
            float smear = Band(edge, 0.5 - (thick * 0.5) - (4.5 * fy / laneH), 0.5 - (thick * 0.5), 1.6 * fy / laneH);
            road += lamp * bright * smear * (0.40 * (0.30 + (0.70 * puddle)));

            // --- the zebra crossings, on the two ends where the dock never covers them
            float zebra = saturate(Band(g.x, 0.078, 0.132, 0.004) + Band(g.x, 0.878, 0.932, 0.004));
            float bars = smoothstep(0.38, 0.46, frac(px.x / 13.0)) * smoothstep(0.92, 0.84, frac(px.x / 13.0));
            road += float3(0.20, 0.21, 0.24) * zebra * bars * onRoad * (0.40 + (0.30 * Flow));

            // --- rain: a still speckle on the asphalt, and sparks where the hats hit it
            float2 sgrid = floor(px / 2.0);
            float wet = step(0.997, Hash(sgrid + 7.0)) * 0.12;
            float spark = smoothstep(0.986, 1.0, Hash(sgrid + 41.0)) * Spark;
            road += float3(0.16, 0.19, 0.24) * wet;
            road += lerp(float3(1, 1, 1), Cyan.rgb, 0.5) * spark * 1.1;

            // --- depth: the far end of the boulevard sinks into the city's violet air
            float fog = smoothstep(0.52, 1.02, g.y);
            float3 fogCol = lerp(float3(0.030, 0.016, 0.048), Magic.rgb * 0.10, 0.35) * (0.6 + (0.7 * Flow));
            road = lerp(road, fogCol, fog * 0.42);

            // --- the light pools sit on top of the asphalt, warm sodium, with a cool rim
            road += Warm.rgb * pool * (0.026 + (0.022 * Flow)) * (0.55 + (0.45 * (1.0 - g.y)));
            road += Cyan.rgb * 0.03 * pool * (1.0 - pool);

            // --- the kerbs: a dark sidewalk, a soft neon edge, and the night beyond
            float nearKerb = smoothstep(KERB + aa, KERB - aa, g.y);
            float farKerb = smoothstep(FAR - aa, FAR + aa, g.y);
            float sidewalk = saturate(nearKerb + farKerb);
            float nearLine = 1.0 - saturate(abs(g.y - KERB) / (1.4 * fy));
            float farLine = 1.0 - saturate(abs(g.y - FAR) / (1.4 * fy));
            float3 kerbCol = zoneCol * (1.1 + (0.5 * Flow)) * (1.0 + (0.30 * Kick));

            float3 light = lerp(Body.rgb * 0.45, Night.rgb * 0.8, saturate(g.y * 1.2));
            light = lerp(light, road, onRoad);
            light += kerbCol * (nearLine * nearLine + farLine * farLine) * 0.9;
            light += Warm.rgb * 0.05 * sidewalk;

            // the outermost rows sink into the wallpaper instead of ending in a cut line
            float3 col = Film(light, g);
            float a = (0.22 + (0.78 * saturate(g.y / 0.035))) * (0.22 + (0.78 * saturate((1.0 - g.y) / 0.035))) * Fade;
            return float4(col * a, a);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // the picture is the field; here it only gets its lens: chromatic aberration towards the edges
            float2 uv = position.xy / Size;
            float2 bend = (uv - 0.5) * float2(0.0009, 0.0032);
            float r = Last.SampleLevel(Linear, uv + bend, 0).r;
            float4 m = Last.SampleLevel(Linear, uv, 0);
            float b = Last.SampleLevel(Linear, uv - bend, 0).b;
            return float4(r, m.g, b, m.a);
        }
        """;
}
