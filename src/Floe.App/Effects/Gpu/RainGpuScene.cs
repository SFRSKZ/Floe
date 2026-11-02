using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Data rain: cyan points fall on long hard streaks and stay where they land, so a shape builds up out of them as a
/// cloud of single points. The feedback only remembers the settled points; the streaks are worked out per column and the
/// mask per pixel, so nothing blurs. Plain, the shape is the ridge of the spectrum, thick on the crest and thinning under
/// it. With the mask the rain gives way to a smoke of fine points folding out of the spectrum, and the Samurai mask
/// burns on the left of it, gathering on the bass, its flames licking with the music and dripping points of itself.
/// </summary>
public sealed class RainGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 56;
    private static readonly Color Ink = Color.FromRgb(0xEA, 0xFC, 0xFF);
    private static readonly Color Cyan = Color.FromRgb(0x3F, 0xD0, 0xD8);
    private static readonly Color Deep = Color.FromRgb(0x1C, 0x6A, 0x72);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x1F, 0x4C);

    private readonly bool _mask;
    private float _rain, _hop = 1, _form, _seed;

    public RainGpuScene(bool mask) : base(ConstantFloats, Shared + SamuraiMask.Hlsl() + MaskPs + FeedbackPs, Shared + SamuraiMask.Hlsl() + MaskPs + ShowPs) => _mask = mask;

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _rain += dt * (.6f + (1.4f * audio.Level) + (1.5f * Beat.Swell));
        if (Beat.Hit) _hop = 0;
        _hop = Math.Min(1, _hop + (dt * 2.6f));
        float wanted = Math.Max(Math.Max(audio.Bass, Beat.Pulse), audio.Level);
        _form += (wanted - _form) * (1 - MathF.Exp(-dt * (wanted > _form ? 14 : 2.5f)));
        _seed = (_seed + 1) % 977;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _rain;
        d[5] = frame.Audio.Level;
        d[6] = Beat.Pulse;
        d[7] = 4 * _hop * (1 - _hop);
        d[8] = _seed;
        d[9] = frame.Dt * 60;
        d[10] = .3f + (.7f * Math.Clamp(_form, 0, 1));
        d[11] = _mask ? 1 : 0;
        PutBands(16, frame);
        var lead = Lead(Cyan);
        Put(40, Theme is null ? Ink : Mix(lead, Colors.White, .75f));
        Put(44, lead);
        Put(48, Theme is null ? Deep : Mix(Colors.Black, lead, .45f));
        Put(52, Second(Red));
    }

    private const string Shared = """
        cbuffer Rain : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Fall;
            float Level;
            float Pulse;
            float Hop;
            float Seed;
            float Step;
            float Form;
            float Figure;
            float4 Spare;
            float4 Bands[6];
            float4 InkColor;
            float4 CyanColor;
            float4 DeepColor;
            float4 RedColor;
        };

        """ + ShaderHelpers + """

        int Grain() { return max(1, (int)floor(Dip)); }

        // the ridge of the spectrum as it is now
        float Skyline(float x)
        {
            float mid = abs((x / Size.x) - .5) * 2;
            return Size.y * (1 - (.08 + (.74 * saturate(Band(mid * .92) * 1.3))));
        }

        """;

    private const string MaskPs = """

        float MaskCenter() { return Size.x * .1; }

        // what the mask has at this pixel: 0 nothing, 1 red, 2 chrome; the upper rows, its flames, sway
        int Mask(float2 pixel)
        {
            float cell = Size.y * .96 / 120;
            float2 p = float2(pixel.x - MaskCenter() + (55 * cell), pixel.y - (Size.y * .02)) / cell;
            float up = saturate(1 - (p.y / 46));
            p.x += sin((p.y * .32) - (Fall * 7)) * up * (1 + (3 * Pulse));
            int2 at = (int2)floor(p);
            if (at.x < 0 || at.y < 0 || at.x >= 110 || at.y >= 120) return 0;
            return (int)((MaskBits[(at.y * 7) + (at.x / 16)] >> ((at.x % 16) * 2)) & 3);
        }

        """;

    private const string FeedbackPs = """

        float Stream(float2 q)
        {
            return Noise((q * 1.7) + float2(Fall * .19, 7)) + (.5 * Noise((q * 4.1) - float2(5, Fall * .27)));
        }

        // the mirrored smoke: the curl has no sinks, so the mass folds into fibres
        float Smoke(float2 at)
        {
            float2 uv = at / Size;
            float aspect = Size.x / Size.y;
            float mid = abs(uv.x - .5) * 2;
            float2 q = float2(mid * aspect * .5, uv.y);
            float e = .02;
            float2 flow = float2(Stream(q + float2(0, e)) - Stream(q - float2(0, e)),
                                 Stream(q - float2(e, 0)) - Stream(q + float2(e, 0))) / (2 * e);
            float2 away = (flow * (.008 + (.014 * Form))) + float2(0, -.0022);
            away.x *= sign(uv.x - .5);
            float2 back = at - (away * Size.y * Step) - MilkFlow(at);
            float last = Last.SampleLevel(Linear, back / Size, 0).r * pow(.955, Step);

            float energy = saturate(Band(mid * .92) * 1.3);
            float ridge = 1 - (.12 + (.72 * energy));
            float width = .03 + (.07 * energy);
            float grain = Noise(float2(q.x * 26, (q.y * 9) - (Fall * .6)));
            float fibre = .5 + (.5 * sin((q.x * 70) + (grain * 9)));
            float body = exp(-pow((uv.y - ridge) / width, 2)) * energy * (.3 + (fibre * grain * 1.6));
            float clear = smoothstep(.45, .8, abs(at.x - MaskCenter()) / Size.y);
            return min(1.5, last + (body * clear * (.2 + (.35 * Level)) * Step));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int size = Grain();
            float2 id = floor(position.xy / size);
            float2 pixel = (id + .5) * size;
            int2 at = (int2)pixel;
            float landing = Hash(id + float2(Seed, Seed * .37));

            if (Figure > .5)
            {
                // the mask sheds points that run down the strip
                float above = Last.Load(int3(at - int2(0, size), 0)).g * pow(.9, Step);
                float inside = Mask(pixel) == 1 ? 1 : 0;
                float red = max(above, inside * step(landing, (.0012 + (.03 * Pulse)) * Step));
                return float4(Smoke(position.xy), red, 0, 1);
            }

            // points gather on the crest and thin out under it, and the ones that lost it go out fast
            float depth = (pixel.y - Skyline(pixel.x)) / Size.y;
            float shape = depth > 0 ? exp(-depth * 9) : 0;
            float settled = Last.Load(int3(at - MilkStep(pixel, size, Hash(id.yx + (Seed * .71))), 0)).r * pow(shape > .01 ? .978 : .8, Step);
            if (shape > .01 && landing < (.0016 + (.01 * Level)) * shape * Step) settled = 1;
            return float4(settled, 0, 0, 1);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 pixel = position.xy;
            int size = Grain();
            float column = floor(pixel.x / (6 * size));
            float3 color = 0;
            float alpha = 0;

            if (Figure > .5)
            {
                // the smoke as points that gather on its folds and thin out inside it
                int2 fine = (int2)pixel / size;
                int2 at = fine * size;
                int reach = 3 * size;
                float v = saturate(Last.Load(int3(at, 0)).r * 1.5);
                float fold = abs(Last.Load(int3(at + int2(reach, 0), 0)).r - Last.Load(int3(at - int2(reach, 0), 0)).r)
                    + abs(Last.Load(int3(at + int2(0, reach), 0)).r - Last.Load(int3(at - int2(0, reach), 0)).r);
                float show = step(Hash((float2)fine), min(.42, (v * .1) + (fold * .6)));
                color = v > .9 ? InkColor.rgb : CyanColor.rgb;
                alpha = show * (.3 + (.5 * v)) * smoothstep(0, .35, pixel.y / Size.y);

                // a drop that left the mask
                if (Last.Load(int3((fine * size) + (size / 2), 0)).g > .12)
                {
                    color = RedColor.rgb;
                    alpha = 1;
                }

                // the mask gathers on the bass and scatters as it fades, and a beat slips its rows sideways
                float row = floor(pixel.y / (6 * size));
                float2 place = pixel + float2((Hash(float2(row, Seed)) - .5) * Pulse * 5 * Dip, 0);
                float2 grain = floor(place / size);
                int kind = Mask((grain + .5) * size);
                if (kind > 0 && Hash(grain) < .72 + (.28 * Form))
                {
                    float spark = Hash(grain + 5);
                    color = kind == 2 ? (spark < .5 ? InkColor.rgb : CyanColor.rgb) : spark < .02 * Pulse ? InkColor.rgb : RedColor.rgb;
                    alpha = 1;
                }

                return float4(color * alpha, alpha) * Fade * smoothstep(0, .05, pixel.y / Size.y);
            }

            // the settled points; a beat throws each column of them up by its own amount
            float2 thrown = pixel + float2(0, Hop * Hash(float2(column, 7)) * Size.y * .15);
            float2 id = floor(thrown / size);
            float2 at = (id + .5) * size;
            if (at.y < Size.y)
            {
                float v = Last.Load(int3((int2)at, 0)).r;
                if (v > .06)
                {
                    color = v > .7 ? InkColor.rgb : v > .35 ? CyanColor.rgb : DeepColor.rgb;
                    alpha = 1;
                }
            }

            // the streaks: one thin line a column, with its head on the end
            float line1 = Size.y * 1.3;
            float speed = .5 + Hash(float2(column, 1));
            float head = (frac((Fall * speed) + Hash(float2(column, 3))) * line1) - (Size.y * .1);
            float length1 = Size.y * (.2 + (.3 * Hash(float2(column, 4))));
            float away = head - pixel.y;
            bool lit = Hash(float2(column, 2)) < .25 + (.65 * Level);
            bool open = pixel.y < Skyline(pixel.x);
            if (lit && open && fmod(pixel.x, 6 * size) < 1)
            {
                if (away > 0 && away < length1)
                {
                    color = DeepColor.rgb;
                    alpha = max(alpha, step(Hash(float2(column, floor(pixel.y / 3))), 1 - (away / length1)));
                }
            }

            if (lit && open && abs(fmod(pixel.x, 6 * size)) < size && away <= 0 && away > -size * 2)
            {
                color = InkColor.rgb;
                alpha = 1;
            }

            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .3, pixel.y / Size.y);
            return float4(color * alpha, alpha) * Fade * top;
        }
        """;
}
