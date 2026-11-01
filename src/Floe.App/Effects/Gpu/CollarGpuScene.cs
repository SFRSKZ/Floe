namespace Floe.App.Effects.Gpu;

/// <summary>
/// The neon collar of the Samurai jacket, seen from inside the screen. Milky COB tubes run from the two ends of the
/// strip towards a point behind the dock, in true perspective: x gives the scale, the scale gives the depth, and the
/// leather grain is measured in depth. The light climbs: the song enters the lowest tube at each end, walks up it
/// towards the neck, and each tube above takes what the one below carried a moment ago, so the glow rises across the
/// collar as well as along it. One row of the feedback texture's corner to a tube and side: R light, G the snare's red.
/// </summary>
public sealed class CollarGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 36;
    private const float ChipsPerSecond = 32;
    private const float Chain = .4f;

    private float _owed, _count, _surge, _tag, _dockLeft, _dockRight;
    private int _shift;

    public CollarGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        _owed += frame.Dt * ChipsPerSecond * (.7f + (.6f * frame.Audio.Level));
        _shift = (int)_owed;
        _owed -= _shift;
        _count = (_count + _shift) % 1048576;

        // the strip only hears of the dock on its poll, so the shadow glides to a new size
        float glide = _dockRight <= 0 ? 1 : 1 - MathF.Exp(-frame.Dt * 10);
        _dockLeft += (frame.DockLeft - _dockLeft) * glide;
        _dockRight += (frame.DockRight - _dockRight) * glide;

        if (Beat.Hit) _surge = 1;
        else _surge = Math.Max(0, _surge - (frame.Dt * 8));
        if (Beat.Snare) _tag = 1;
        else if (_surge <= 0) _tag = 0;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _shift;
        d[5] = _count;
        d[6] = _surge;
        d[7] = _tag;
        d[8] = MathF.Pow(Chain, _shift / 200f);
        d[9] = frame.Audio.Level;
        d[10] = _dockLeft;
        d[11] = _dockRight;
        PutBands(12, frame);
    }

    private const string Shared = """
        cbuffer Collar : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Shift;
            float Count;
            float Surge;
            float Tag;
            float Keep;
            float Level;
            float DockLeft;
            float DockRight;
            float4 Bands[6];
        };

        """ + ShaderHelpers + """

        static const int Tubes = 7;
        static const int Chips = 200;
        static const int Climb = 12;

        """;

    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 q = (int2)position.xy;
            if (q.y >= Tubes * 2 || q.x >= Chips) return 0;

            int from = q.x - (int)Shift;
            if (from >= 0)
            {
                if (Shift < 1) return Last.Load(int3(q, 0));

                // the phosphor of a COB strip: light leaks into its neighbours as it walks
                float4 chip = (Last.Load(int3(max(from - 1, 0), q.y, 0)) + (6 * Last.Load(int3(from, q.y, 0))) + Last.Load(int3(min(from + 1, Chips - 1), q.y, 0))) * .125;
                chip.r *= Keep;
                return chip;
            }

            // every tube but the lowest is fed by the one under it, a little late
            int tube = q.y % Tubes;
            if (tube < Tubes - 1)
            {
                float4 under = Last.Load(int3(Climb, q.y + 1, 0));
                under.r *= .93;
                return under;
            }

            float lane = Hash(float2(q.y, 3));
            float energy = saturate((Band(lane * .3) - .05) * 3);
            float wave = .5 + (.5 * cos(((Count - q.x) / (26 + (18 * lane))) * 6.2832));
            float light = max(energy * wave * wave, Surge * .8);
            return float4(light, Tag, 0, 1);
        }
        """;

    private const string ShowPs = """

        static const float Far = .34;
        static const float Density = 100;
        static const float3 Ice = float3(.34, .74, 1);
        static const float3 Deep = float3(.03, .2, .8);
        static const float3 Alarm = float3(1, .16, .24);
        static const float3 Hide = float3(.05, .056, .05);

        float4 Chip(int tube, bool right, float depth)
        {
            int row = right ? tube + Tubes : tube;
            float at = saturate(depth * Density / (Chips - 2)) * (Chips - 2);
            int cell = (int)at;
            return lerp(Last.Load(int3(cell, row, 0)), Last.Load(int3(cell + 1, row, 0)), at - cell);
        }

        float Grain(float2 q)
        {
            return Noise(q * float2(14, 2.3)) + (.5 * Noise(q * float2(38, 6)));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float H = Size.y, u = p.x / Size.x;
            float across = abs((u - .5) * 2);
            bool right = u > .5;

            // straight rails to one point: the scale is linear in x, and depth is its inverse
            float scale = lerp(Far, 1, across);
            float depth = (1 / scale) - 1;
            float fog = pow(saturate(scale * 1.3), .7);

            // the collar rises as it wraps round behind the neck
            float inward = 1 - across;
            float horizon = (H * .42) - (H * .22 * inward * inward) + (H * .14 * across * across * across);
            float spacing = H / 6;
            float lane = ((p.y - horizon) / (spacing * scale)) + ((Tubes - 1) * .5);
            int tube = clamp((int)round(lane), 0, Tubes - 1);
            float radius = spacing * scale * .42;
            float centre = horizon + ((tube - ((Tubes - 1) * .5)) * spacing * scale);
            float t = (p.y - centre) / radius;

            // leather, its creases in depth, lit by the tube beside it
            float2 hide = float2(depth, lane);
            float crease = saturate(.5 + ((Grain(hide + float2(.012, 0)) - Grain(hide)) * 5));
            float seam = abs(frac(lane + .5) - .5) * 2;
            float3 color = Hide * (.45 + crease) * fog * lerp(.55, 1, seam);

            float4 chip = Chip(tube, right, depth);
            float spill = (.04 + (sqrt(chip.r) * .7)) * exp(-max(abs(t) - 1, 0) * .7);
            color += Deep * spill * (.3 + (1.2 * crease)) * fog;
            color += Ice * spill * spill * .25 * crease;

            // the tube's contact shadow, on its lower side only
            if (t > 1) color *= 1 - (.72 * exp(-(t - 1) * 2.4));

            if (abs(t) < 1)
            {
                float round1 = sqrt(1 - (t * t));
                float3 hue = lerp(Ice, Alarm, chip.g * .6);

                // dark glass round a milky strip: no dots, and a soft core instead of a white one
                float glow = .07 + (sqrt(chip.r) * .72);
                float3 inside = Deep * lerp(.5, .1, round1) * fog;
                inside += lerp(Deep, hue, saturate(glow * 1.6)) * glow * (.4 + (.75 * round1 * round1));
                inside += float3(.75, .92, 1) * exp(-t * t * 7) * glow * glow * .45;
                inside += float3(.82, .92, 1) * exp(-pow((t + .58) * 7, 2)) * .16 * fog;
                inside *= lerp(.6, 1, fog);

                // a dark line on the edge keeps it sharp against the leather
                float edge = saturate((1 - abs(t)) * radius);
                color = lerp(color * .4, inside, edge);
            }

            // the dock stands in front: it hides the middle and throws its shadow out along the collar
            float clearance = max(DockLeft - u, u - DockRight) * Size.x;
            color *= clearance < 0 ? .2 : 1 - (.8 * exp(-clearance / (26 * Dip)));
            return float4(color, 1) * Fade;
        }
        """;
}
