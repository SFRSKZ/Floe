using Floe.App.Effects.Gpu;
using Color = System.Windows.Media.Color;

namespace SceneLab.Scenes;

/// <summary>
/// Sinematik referans sahne - "altin standart" (characters kept ASCII so the file survives any tool chain).
/// <para>
/// KONSEPT (tek cumle): "Sular altinda kalmis bir Night City sokagi: neon tabelalarin ve sokak lambalarinin
/// islak asfalta dusen yansimasi, camda bokeh'e donusen isiklar ve spektrumdan yukselen sehir silueti."
/// </para>
/// <para>
/// Neden 8-bit degil: dogrusal HDR sahne + ACES filmic tonlama + gercek bloom (feedback gecisi parlak
/// pikselleri yayar) + lift/gamma/gain derecelendirmesi + kromatik aberasyon + film grain + vignette.
/// Buyuk formlar, az sayida guclu oge, yumusak gecisler: 126 px yukseklikte detay degil siluet ve isik okunur.
/// </para>
/// <para>
/// Ses: sehir = spektrum (24 bant, ortadan disa aynali bina yukseklikleri), bas = yansima dalgasi ve bokeh
/// buyumesi, kick = camda isik surgu + neon flas, snare = tabela parlamasi, hat = su ustunde ince kirisma.
/// Sessizlik: tum animasyon sesle ilerleyen saatten gecer, ses sifirken durur; sonmus isik iskeleti kalir.
/// </para>
/// <para>
/// Port notu: HLSL'de y asagi; shader p.y'yi cevirip yukari-pozitif cercevede calisiyor (sky +, street -).
/// Portta streetY (-0.34), skyline esigi ve Band() eslemesi (|x|/7.6) birlikte otelenmeli.
/// </para>
/// </summary>
[SceneDoc("cinematic: flooded Night City street - spectral skyline, HDR neon, real bloom, ACES grade")]
public sealed class NeonRain : FeedbackGpuScene
{
    // Size(0,1) Dip(2) Fade(3) Time(4) Level(5) Bass(6) Treble(7) Mid(8) Kick(9) Snare(10) Hat(11)
    // Bands 12..35, Core 36, Hot 40, Cold 44, Pad0 48 (ripple, drift), Pad1 52 (clock), pad to 64
    private const int ConstantFloats = 64;

    private float _kickEnv, _snareEnv, _hatEnv;
    private float _ripple;
    private float _drift;
    private float _clock;

    public NeonRain() : base(ConstantFloats, Shared + BloomPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;

        // light surges and settles over 6-10 frames: a real sign, not a toggle
        _kickEnv = Math.Max(audio.Kick, _kickEnv * MathF.Exp(-dt * 4.5f));
        _snareEnv = Math.Max(audio.Snare, _snareEnv * MathF.Exp(-dt * 6.5f));
        _hatEnv = Math.Max(audio.Hat, _hatEnv * MathF.Exp(-dt * 9f));

        // every clock in this scene runs on the music, so silence freezes the whole picture
        float motion = Math.Clamp(audio.Level * 2.2f, 0f, 2.2f);
        _clock += dt * motion;
        _ripple += dt * motion * (1.5f + (2.4f * audio.Bass) + (2.0f * _kickEnv));
        _drift += dt * motion * 0.30f;
    }

    protected override void Fill(GpuFrame frame)
    {
        var audio = frame.Audio;
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = audio.Level;
        d[6] = audio.Bass;
        d[7] = audio.Treble;
        d[8] = audio.Mid;
        d[9] = Math.Clamp(_kickEnv, 0, 1);
        d[10] = Math.Clamp(_snareEnv, 0, 1);
        d[11] = Math.Clamp(_hatEnv, 0, 1);
        PutBands(12, frame);

        // the grade's anchors: warm sodium key, magenta signage, cold cyan rim
        Put(36, Mix(Color.FromRgb(0xFF, 0xC2, 0x4A), Color.FromRgb(0xFF, 0x8A, 0x3D), 0.35f));
        Put(40, Color.FromRgb(0xFF, 0x2E, 0x88));
        Put(44, Color.FromRgb(0x2A, 0xD8, 0xFF));

        d[48] = _ripple;
        d[49] = _drift;
        d[52] = _clock;
    }

    private const string Shared = """
        cbuffer Neon : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Treble;
            float Mid;
            float Kick;
            float Snare;
            float Hat;
            float4 Bands[6];
            float4 Core;
            float4 Hot;
            float4 Cold;
            float4 Pad0;
            float4 Pad1;
            float4 Pad2;
            float4 Pad3;
        };

        """ + ShaderHelpers + """

        // the music-driven clock; set once per pixel before anything reads it
        static float AnimT = 0.0;

        // ACES filmic curve (Narkowicz fit): highlights roll off instead of clipping.
        float3 Tonemap(float3 x)
        {
            x = max(x, 0);
            return saturate((x * ((2.51 * x) + .03)) / ((x * ((2.43 * x) + .59)) + .14));
        }

        float3 LinearToSrgb(float3 c)
        {
            return pow(saturate(c), 1.0 / 2.2);
        }

        // The city is the spectrum: 24 mirrored bands set the block heights, and each block keeps its own
        // character from a hash so the skyline reads as a city, not as a bar chart.
        float CityHeight(float x)
        {
            float s = x * .34;
            float i = floor(s);
            float h1 = Hash(float2(i, 1.7));
            float h2 = Hash(float2(i, 9.3));
            float h3 = Hash(float2(i, 4.1));

            float band = Band(saturate(abs(x) / 7.6));
            float height = .10 + (.34 * band * band) + (.12 * band) + (.10 * h1 * h1) + (.04 * h3);
            height += step(.58, h2) * (.08 + (.10 * h1)) * step(abs(frac(s) - .5), .17);
            height += step(.88, h3) * .025;
            return height;
        }

        // Distant windows: fine, sparse, warm-to-cold, brighter where their own band is loud.
        float3 Windows(float2 p, float cityTop, float streetY)
        {
            if (p.y > cityTop - .015 || p.y < streetY + .015) return 0;
            float2 cell = p * float2(17.0, 27.0);
            float2 id = floor(cell);
            float2 f = frac(cell);

            float lit = smoothstep(.80, .82, Hash(id + 5.3));
            float shape = smoothstep(.42, .16, abs(f.x - .5)) * smoothstep(.40, .14, abs(f.y - .5));
            float3 tint = Hash(id + 2.2) < .66 ? float3(1.0, .74, .42) : float3(.55, .80, 1.0);
            float band = Band(saturate(abs(p.x) / 7.6));
            float glow = .18 + (1.0 * band) + (.40 * Mid);
            float variation = .35 + (.85 * Hash(id + 13.7));
            return tint * shape * lit * glow * variation;
        }

        // A few big neon signs: the focal points the eye actually reads at 126 px.
        float3 Signs(float2 p, float streetY)
        {
            float3 sum = 0;
            for (int i = 0; i < 3; i++)
            {
                float fi = (float)i;
                float h1 = Hash(float2(fi, 61));
                float h2 = Hash(float2(fi, 83));
                float h3 = Hash(float2(fi, 97));

                float x = ((fi - 1.0) * 4.6) + (((h1 * 2.0) - 1.0) * 1.6);
                float base = streetY + .06 + (.10 * h2);
                float height = .16 + (.22 * h3);
                float width = .012 + (.010 * h3);
                float band = Band(saturate(abs(x) / 7.6));

                float dx = abs(p.x - x);
                float core = smoothstep(width, width * .30, dx);
                float within = smoothstep(base + height + .005, base + height - .03, p.y) * smoothstep(base - .012, base + .010, p.y);

                // signage is a frame, not a solid bar: a bright border and a dimmer middle
                float frame = smoothstep(width * .55, width * .95, dx);
                float3 tint = fi < .5 ? Hot.rgb : (fi < 1.5 ? Cold.rgb : Core.rgb);
                float energy = (2.2 + (9.0 * band * band) + (3.0 * Snare)) * (1.0 + (.55 * Kick));
                sum += tint * core * within * energy * (.55 + (.45 * frame));
            }

            return sum;
        }

        // Out-of-focus street lights behind the glass: large, soft, depth-scaled.
        float3 Lights(float2 p, float2 pR, float2 pB)
        {
            float3 sum = 0;
            for (int i = 0; i < 8; i++)
            {
                float fi = (float)i;
                float h1 = Hash(float2(fi, 3));
                float h2 = Hash(float2(fi, 11));
                float h3 = Hash(float2(fi, 29));

                float depth = pow(h1, .75);
                float2 pos = float2(((h2 * 2.0) - 1.0) * 7.0, (-.30 + (.30 * h3) + (.008 * sin((AnimT * .25) + (fi * 1.7)))));
                pos.y += .02 * Level * sin((AnimT * .7) + (fi * .9));

                float band = Band(saturate((fi + .5) / 8.0));
                float radius = (.035 + (.110 * depth)) * (1.0 + (.30 * band) + (.20 * Kick));
                float dist = length(p - pos);
                float energy = (.55 + (.75 * depth)) * (.35 + (1.5 * band) + (1.2 * Kick));
                float3 tint = fi < 2.4 ? Hot.rgb : (fi < 5.6 ? Core.rgb : Cold.rgb);

                float body = smoothstep(radius, radius * .45, dist);
                float rim = exp(-pow((dist - (radius * .90)) / max(radius * .10, 1e-5), 2.0));
                float shape = (body * .45) + (rim * .30);
                sum += tint * energy * shape;

                // a slight per-channel offset gives the lens its fringing
                float2 dr = pR - pos;
                float2 db = pB - pos;
                float fringe = (exp(-pow((length(dr) - radius) / max(radius * .10, 1e-5), 2.0))
                              - exp(-pow((length(db) - radius) / max(radius * .10, 1e-5), 2.0))) * .5;
                sum += tint * energy * fringe * .25;
            }

            return sum;
        }

        // The flooded street below the kerb: a vertically smeared, rippled mirror of the light above it.
        float3 Street(float2 p, float streetY, float ripple)
        {
            if (p.y > streetY) return 0;
            float depth = saturate((streetY - p.y) / (streetY + .5));
            float2 q = float2(p.x + (.055 * sin((p.y * 32) + ripple)), (2.0 * streetY) - p.y);
            float3 mirror = Lights(q, q, q);
            float streak = .58 + (.42 * Noise(float2(q.x * 4.0, q.y * 1.6 + (AnimT * .45))));
            float wet = (1.0 - depth) * (1.0 - depth);

            float3 asphalt = float3(.0038, .0052, .0092) * (.30 + (.70 * depth));
            return asphalt + (mirror * streak * wet * 1.75);
        }
        """;

    // The show pass: the whole cinematic composite.
    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float2 p = (uv - .5) * float2(Size.x / Size.y, 1.0);

            AnimT = Pad1.x;
            float ripple = Pad0.x;
            p.x += Pad0.y * .05;
            p.y = -p.y;                       // y-up frame: sky at +, street at -

            const float streetY = -.34;
            float cityTop = streetY + CityHeight(p.x);

            // sky: near-black blue, a tight band of light pollution above the skyline
            float3 col = lerp(float3(.0085, .0145, .0300), float3(.0008, .0014, .0034), saturate((p.y + .34) / .84));
            col += float3(.20, .060, .16) * exp(-pow((p.y - cityTop - .04) / .065, 2.0)) * (.20 + (.9 * Level));

            // the city mass, with a facade that brightens toward the street
            float inCity = step(streetY, p.y);
            float belowTop = smoothstep(cityTop + .005, cityTop - .018, p.y);
            float facade = saturate((p.y - streetY) / max(cityTop - streetY, .001));
            float3 cityCol = lerp(float3(.0018, .0022, .0044), float3(.0110, .0090, .0180), facade * facade) * (.70 + (.55 * Mid));
            col = lerp(col, cityCol, inCity * belowTop);

            // a hairline of light on the skyline so the silhouette separates from the sky
            col += (Hot.rgb * .13 + Cold.rgb * .10) * exp(-pow((p.y - cityTop) / .0035, 2.0)) * inCity * (.20 + (.65 * Mid));

            col += Windows(p, cityTop, streetY);
            col += Signs(p, streetY);
            col += Lights(p, p + float2(.004, 0), p - float2(.004, 0));
            col += Street(p, streetY, ripple);

            // the kerb: one crisp cold line where the water starts
            col += Cold.rgb * .35 * exp(-pow((p.y - streetY) / .0028, 2.0)) * (.25 + (.5 * Treble));

            // light bleed from the previous frame: real bloom, fed through the same tonemap so it rolls off
            float3 bleed = Last.Load(int3((int2)position.xy, 0)).rgb;
            col += bleed * 1.10;

            col *= .85 + (.45 * Level);

            float3 graded = LinearToSrgb(Tonemap(col));

            // lift / gamma / gain: teal shadows, warm highlights - the grade that holds the strip together
            graded = lerp(graded, graded * float3(.80, .94, 1.16), .34 * (1.0 - graded));
            graded *= float3(1.04, 1.00, .95);
            graded = pow(saturate(graded), 1.08);

            // vignette left and right: the strip is wide, the eye should stay toward the middle
            float vx = abs((uv.x * 2.0) - 1.0);
            graded *= 1.0 - (.30 * pow(vx, 2.4));

            // film grain, frozen with the clock so silence stays perfectly still
            graded += (Hash(position.xy + frac(AnimT) * 91.0) - .5) * .018;

            return float4(graded * Fade, Fade);
        }
        """;

    // The feedback pass, used as a cheap HDR bloom: spread the previous frame's bright pixels by a few whole
    // pixels, keep only what is bright enough to glow, and store it in the float target the show pass samples.
    // It is not a blur of the picture - it is the light bleeding, which is why it reads as film.
    private const string BloomPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 p = (int2)position.xy;
            int r = (int)max(2, round(3.0 * Dip));

            float3 c = Last.Load(int3(p, 0)).rgb * .30;
            c += Last.Load(int3(p + int2(r, 0), 0)).rgb * .175;
            c += Last.Load(int3(p - int2(r, 0), 0)).rgb * .175;
            c += Last.Load(int3(p + int2(0, r), 0)).rgb * .175;
            c += Last.Load(int3(p - int2(0, r), 0)).rgb * .175;

            float lum = dot(c, float3(.2126, .7152, .0722));
            c *= smoothstep(.30, 1.0, lum);
            return float4(c * .85, 1);
        }
        """;
}
