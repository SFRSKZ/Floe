using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The Blackwall: something alive behind a lattice. Two fluids, red and a dark blood red, drift through a curl flow in the
/// feedback, fed by the bands along two wandering lines, by a kick's ring and by a front that runs out from the
/// middle. The picture stays sharp because the fluid is never shown as it is: it only sets how wide each bar of a
/// hard vertical lattice stands, so a faint level is a hairline, a stronger one a spindle, and the loudest bars
/// merge into a solid mass cut by the fine rings of a moiré.
/// </summary>
public sealed class BlackwallGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 48;
    // its own red, whatever the theme: an accent that leans to pink turned the wall pink
    private static readonly Color Red = Color.FromRgb(0xF0, 0x0C, 0x10);
    private static readonly Color Blood = Color.FromRgb(0x6A, 0x02, 0x08);

    private readonly Random _random = new(11);
    private float _time;
    private float _front = 2, _frontPower;
    private float _ringX, _ringY, _ringRadius, _ringPower;

    public BlackwallGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _time += dt * (.5f + audio.Level + (2 * Beat.Swell));

        if (Beat.Hit)
        {
            _front = 0;
            _frontPower = 1;
            _ringX = (float)_random.NextDouble();
            _ringY = .25f + ((float)_random.NextDouble() * .5f);
            _ringRadius = 0;
            _ringPower = 1;
        }

        _front += dt * 1.6f;
        _frontPower *= MathF.Exp(-dt * 2.2f);
        _ringRadius += dt * 2.4f;
        _ringPower *= MathF.Exp(-dt * 5);
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        float step = frame.Dt * 60;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _time;
        d[5] = Math.Max(audio.Bass, Beat.Pulse);
        d[6] = audio.Mid;
        d[7] = Beat.Pulse;
        d[8] = audio.Level;
        d[9] = _front;
        d[10] = _frontPower;
        d[11] = step;
        d[12] = _ringX * frame.Width / frame.Height;
        d[13] = _ringY;
        d[14] = _ringRadius;
        d[15] = _ringPower;
        PutBands(16, frame);
        Put(40, Red);
        Put(44, Blood);
    }

    private const string Shared = """
        cbuffer Wall : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Bass;
            float Mid;
            float Treble;
            float Level;
            float Front;
            float FrontPower;
            float Step;
            float2 RingAt;
            float RingRadius;
            float RingPower;
            float4 Bands[6];
            float4 Hot;
            float4 Cold;
        };

        """ + ShaderHelpers;

    private const string FeedbackPs = """

        float Stream(float2 q)
        {
            return Noise((q * 1.5) + float2(Time * .17, 3)) + (.5 * Noise((q * 3.6) - float2(3, Time * .23)));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float2 q = float2(uv.x * Size.x / Size.y, uv.y);
            float mid = abs(uv.x - .5) * 2;

            // a curl flow: no sinks, so the fluid folds into filaments instead of piling up
            float e = .02;
            float2 flow = float2(Stream(q + float2(0, e)) - Stream(q - float2(0, e)),
                                 Stream(q - float2(e, 0)) - Stream(q + float2(e, 0))) / (2 * e);
            float2 away = (flow * (.009 + (.016 * Bass))) + float2(sign(uv.x - .5) * .0016, 0);
            float2 back = position.xy - (away * Size.y * Step);
            float2 last = Last.SampleLevel(Linear, back / Size, 0).rg * pow(.92, Step);

            float clump = Noise((q * 4) + float2(Time * .4, -Time * .3));
            float energy = saturate(Band(mid * .9) * 1.25);
            float front = exp(-pow((mid - Front) / .14, 2)) * FrontPower;

            float line1 = .5 + (.24 * sin((uv.x * 9) + (Time * .7))) + (.12 * sin((uv.x * 23) - (Time * 1.1)));
            float width = .05 + (.15 * energy);
            float red = exp(-pow((uv.y - line1) / width, 2)) * ((energy * energy) + (1.4 * front)) * (.35 + clump);

            float line2 = .5 - (.3 * sin((uv.x * 6) - (Time * .5)));
            float high = saturate(Band(.45 + (.55 * mid)) * 1.3) + (.3 * Treble);
            float blue = exp(-pow((uv.y - line2) / .09, 2)) * high * high * (.2 + clump);

            float ring = exp(-pow((length(q - RingAt) - RingRadius) / .05, 2)) * RingPower;

            float gain = (.15 + (.3 * Treble)) * Step;
            return float4(min(1.5, last.r + ((red + (.7 * ring)) * gain)), min(1.5, last.g + (blue * gain)), 0, 1);
        }
        """;

    private const string ShowPs = """

        float3 Ramp(float v)
        {
            float3 deep = Hot.rgb * .32;
            float3 ember = float3(1, .42, .22);
            if (v < .4) return lerp(deep, Hot.rgb, v / .4);
            if (v < .8) return lerp(Hot.rgb, ember, (v - .4) / .4);
            return lerp(ember, float3(1, .93, .86), saturate((v - .8) / .35));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 pixel = position.xy;
            float2 field = Last.Load(int3((int2)pixel, 0)).rg;
            float red = field.r;
            float blue = field.g;
            float total = max(red, blue);

            // the lattice leans a little where the fluid pushes it, which gives the bars their S
            float spacing = max(3, round(5 * Dip));
            float x = pixel.x + (sin((pixel.y / (9 * Dip)) + (red * 5)) * Dip * 1.8 * saturate(total * 2));
            float k = abs(frac(x / spacing) - .5) * 2;
            // the fluid is the memory, the bars are the moment: each column stands as tall as its band does now
            float mid = abs((pixel.x / Size.x) - .5) * 2;
            float live = .3 + (1.3 * saturate(Band(mid * .9) * 1.25)) + (.7 * Treble);
            float reach = saturate((total - .03) * 1.7 * live);
            float bar = step(k, reach);
            float hair = step(k, 1.5 / spacing);

            // the moiré of two drifting sources, only where the wall burns hottest
            float2 a = float2(Size.x * (.5 + (.3 * sin(Time * .31))), Size.y * .5);
            float2 b = float2(Size.x * (.5 + (.3 * sin((Time * .23) + 2))), Size.y * (.5 + (.4 * sin(Time * .4))));
            float wave = sin(length(pixel - a) / (.8 * Dip)) + sin(length(pixel - b) / (.9 * Dip));
            float hot = smoothstep(.62, .8, red);
            float rings = step(.55, wave) * hot;

            float v = red * (1 - (.55 * hot)) + (rings * .9);
            float3 color = Ramp(v);
            float cold = saturate(blue * 1.5);
            color = lerp(color, Cold.rgb * (.5 + (.7 * cold)), saturate(blue / max(red + blue, 1e-3)));
            color += float3(.45, .04, 0) * saturate(min(red, blue) * 2);

            float alpha = bar * saturate(total * 6);
            float baseline = hair * .2 * saturate(.35 + Level);
            if (alpha < baseline)
            {
                color = Hot.rgb * .6;
                alpha = baseline;
            }

            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .35, pixel.y / Size.y);
            return float4(color * alpha, alpha) * Fade * top;
        }
        """;
}
