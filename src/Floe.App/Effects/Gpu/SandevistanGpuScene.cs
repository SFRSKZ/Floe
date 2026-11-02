using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The Sandevistan is time, so this scene is a clock that the music can grab. Thin lines of light run from the dock to
/// the screen edge, one head to a row, each writing the song's band for that row into the feedback, and what they leave
/// behind ages through the implant's colours. A beat grabs the clock: the heads nearly stop, the trails stop ageing and
/// hard copies of them hang in the air ahead of the light. Then the clock lets go, the heads catch up at speed and the
/// old light burns off. R is the light left on each pixel, and its age is its colour.
/// </summary>
public sealed class SandevistanGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private const float Held = .3f;
    private const float Catching = .28f;
    private const float Rearm = .85f;

    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Cold = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Paper = Color.FromRgb(0xEC, 0xEC, 0xE6);
    private static readonly Color Green = Color.FromRgb(0x2B, 0xFF, 0x88);
    private static readonly Color Magenta = Color.FromRgb(0xFF, 0x2B, 0xD6);

    private float _flow, _before, _hold, _catch, _rearm, _burn = 1;

    public SandevistanGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        float level = frame.Audio.Level;

        _rearm = Math.Max(0, _rearm - dt);
        if (Beat.Hit && _rearm <= 0)
        {
            _hold = Held;
            _rearm = Rearm;
        }

        float speed = .5f + (1.1f * level);
        _burn = 1.1f;
        if (_hold > 0)
        {
            _hold -= dt;
            speed = .03f;
            _burn = .03f;
            if (_hold <= 0) _catch = Catching;
        }
        else if (_catch > 0)
        {
            _catch -= dt;
            speed *= 3.4f;
            _burn = 3.6f;
        }

        // no music, no clock: the heads stop where they are and the strip fades out over them
        speed *= Math.Min(1, level * 25);
        _before = _flow;
        _flow += dt * speed;
        if (_flow > 4096)
        {
            _flow -= 4096;
            _before -= 4096;
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _flow;
        d[5] = _before;
        d[6] = MathF.Exp(-frame.Dt * _burn);
        d[7] = Math.Clamp(_hold / Held, 0, 1);
        d[8] = frame.Audio.Level;
        d[9] = 0;
        d[10] = 0;
        d[11] = 0;
        d[12] = 0;
        d[13] = 0;
        d[14] = 0;
        d[15] = 0;
        PutBands(16, frame);
        Put(40, Lead(Yellow));
        Put(44, Second(Red));
        Put(48, Cold);
        Put(52, Paper);
        Put(56, Green);
        Put(60, Magenta);
    }

    private const string Shared = """
        cbuffer Sandevistan : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Flow;
            float Before;
            float Keep;
            float Hold;
            float Level;
            float Spare0;
            float Spare1;
            float Spare2;
            float Spare3;
            float Spare4;
            float Spare5;
            float Spare6;
            float4 Bands[6];
            float4 LeadColor;
            float4 SecondColor;
            float4 ColdColor;
            float4 PaperColor;
            float4 GreenColor;
            float4 MagentaColor;
        };

        """ + ShaderHelpers + """

        int RowHeight() { return max(3, (int)((2 * Dip) + .5)); }
        float WingWidth() { return Size.x * .5; }

        // x in the wing, counted from the screen edge in towards the dock
        float WingX(float2 p) { return p.x < Size.x * .5 ? p.x : Size.x - p.x; }

        """;

    private const string FeedbackPs = """

        float HeadAt(float clock, float offset, float rate)
        {
            return WingWidth() * (1 - frac(offset + (clock * rate)));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float x = WingX(p);
            if (x > WingWidth()) return 0;

            int row = (int)p.y / RowHeight();
            float light = Last.Load(int3((int2)p, 0)).r * Keep;

            float offset = Hash(float2(row, 3));
            float rate = .55 + (.9 * Hash(float2(row, 7)));
            float now = HeadAt(Flow, offset, rate);
            float then = HeadAt(Before, offset, rate);

            // the head moves many pixels a frame and must leave no holes; it wraps back to the dock when then < now
            float low = now, high = then < now ? WingWidth() : then;
            if (x >= low && x <= high)
            {
                float lap = floor(offset + (Flow * rate));
                float energy = Band(saturate((float)row * RowHeight() / Size.y));
                if (energy > .16 + (.42 * Hash(float2(row, lap + 11)))) light = 1;
            }

            return float4(light, 0, 0, 1);
        }
        """;

    private const string ShowPs = """

        float3 AgeColour(float light)
        {
            if (light > .9) return PaperColor.rgb;
            if (light > .72) return ColdColor.rgb;
            if (light > .52) return GreenColor.rgb;
            if (light > .34) return LeadColor.rgb;
            if (light > .2) return MagentaColor.rgb;
            return SecondColor.rgb;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float x = WingX(p);
            if (x > WingWidth()) return 0;

            int rowHeight = RowHeight();
            int ypx = (int)p.y;
            if (ypx % rowHeight == rowHeight - 1) return 0;

            float light = Last.Load(int3((int2)p, 0)).r;
            float3 color = 0;
            float alpha = 0;
            if (light > .09)
            {
                color = AgeColour(light);
                alpha = light > .2 ? 1 : .6;
            }

            // while the clock is held, hard copies of the bright light hang ahead of it, one colour each
            if (Hold > 0 && alpha < .5)
            {
                float toward = p.x < Size.x * .5 ? 1 : -1;
                float gap = 11 * Dip;
                for (int k = 1; k <= 4; k++)
                {
                    int2 source = int2((int)(p.x + (toward * gap * k)), ypx);
                    if (Last.Load(int3(source, 0)).r > .72)
                    {
                        color = k == 1 ? ColdColor.rgb : (k == 2 ? GreenColor.rgb : (k == 3 ? LeadColor.rgb : MagentaColor.rgb));
                        alpha = 1 - (k * .16);
                        break;
                    }
                }
            }

            return float4(color, 1) * alpha * Fade;
        }
        """;
}
