using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The horizon splits: one line on each side of the dock that the music tears into two shores, widest at the dock and
/// closed at the ends of the strip. The bands shape the tear along its length, a kick throws a spray of short streaks
/// out along the upper shore, and a snare lights a few striped shafts that stand on it, with their reflection under
/// the lower one. Inside the tear the strip goes dark. In silence the shores close back into one line and stay.
/// Every mark is a hard edged shape with a dark rim; nothing is kept between frames.
/// </summary>
public sealed class RiftGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 52;
    private static readonly Color Core = Color.FromRgb(0xFF, 0x5A, 0x9A);
    private static readonly Color Body = Color.FromRgb(0xD8, 0x12, 0x5C);
    private static readonly Color Deep = Color.FromRgb(0x6A, 0x06, 0x2A);

    private readonly Random _random = new(23);
    private float _open, _spray, _shaft, _time;
    private int _seed = 1;

    public RiftGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        // the tear jumps open on the bass and closes slower, so it breathes with the song
        _open = Math.Max(_open * MathF.Exp(-dt * 3), Math.Max(audio.Bass * .8f, Beat.Pulse));
        _spray = Math.Max(_spray * MathF.Exp(-dt * 4), Beat.Hit ? 1 : 0);
        // a snare lights the shafts; in a song without one every other kick does
        if (Beat.Snare || (Beat.Hit && _random.Next(2) == 0))
        {
            _seed = (_seed % 997) + 1 + _random.Next(30);
            _shaft = 1;
        }

        _shaft *= MathF.Exp(-dt * 2.5f);
        _time += dt;
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = Math.Clamp(frame.DockLeft, .05f, .5f);
        d[5] = Math.Clamp(frame.DockRight, .5f, .95f);
        d[6] = audio.Level;
        d[7] = _open;
        d[8] = _spray;
        d[9] = _shaft;
        d[10] = _seed;
        d[11] = _time;
        PutBands(16, frame);
        var body = Lead(Body);
        Put(40, Theme is null ? Core : Mix(body, Colors.White, .45f));
        Put(44, body);
        Put(48, Theme is null ? Deep : Mix(Colors.Black, body, .45f));
    }

    private const string Shared = """
        cbuffer Rift : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float DockLeft;
            float DockRight;
            float Level;
            float Open;
            float Spray;
            float Shaft;
            float Seed;
            float Time;
            float4 Spare;
            float4 Bands[6];
            float4 CoreColor;
            float4 BodyColor;
            float4 DeepColor;
        };

        """ + ShaderHelpers;

    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            return 0;
        }
        """;

    private const string ShowPs = """

        // 0 at the dock's edge, 1 at the end of the strip; -1 under the dock
        float Side(float x)
        {
            float u = x / Size.x;
            if (u <= DockLeft) return (DockLeft - u) / DockLeft;
            if (u >= DockRight) return (u - DockRight) / (1 - DockRight);
            return -1;
        }

        // How far each shore stands from the middle, in pixels: the bands along the tear, the bass for how wide.
        float Gap(float x)
        {
            float side = saturate(Side(x));
            float near = (1 - side) * (1 - side);
            float energy = saturate(Band(side * .95) * 1.2);
            float ripple = .75 + (.25 * sin((side * 31) + (Time * 2)));
            return (.6 + (30 * Open * near * (.35 + (.65 * energy)) * ripple)) * Dip;
        }

        // distance from a pixel to the curve y = base + sign * Gap(x), slope corrected
        float Shore(float2 p, float base, float reach)
        {
            float y = base + (reach * Gap(p.x));
            float slope = reach * (Gap(p.x + 1) - Gap(p.x - 1)) * .5;
            return abs(p.y - y) / sqrt(1 + (slope * slope));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float side = Side(p.x);
            if (side < 0) return 0;

            float base = Size.y * .6;
            float gap = Gap(p.x);
            float upper = base - gap;
            float lower = base + (gap * .7);
            float core = .8 * Dip;
            float rim = core + max(1, Dip);
            float up = Shore(p, base, -1);
            float down = Shore(p, base, .7);

            float3 color = 0;
            float alpha = 0;

            // inside the tear the strip goes dark, so the split reads over any wallpaper
            if (p.y > upper && p.y < lower) alpha = .55 * saturate((gap / Dip) - 1);

            float energy = saturate(Band(side * .95) * 1.2);
            float close = 1 - side;

            // the dust: speckle clouds that hug both shores, thick close the dock and with the bands, stepped in time
            float speck = max(1, round(Dip));
            float2 cellAt = floor(p / speck);
            float overUpper = upper - p.y;
            float underLower = p.y - lower;
            float cloud = overUpper > 0 ? overUpper : (underLower > 0 ? underLower * 1.6 : 1e4);
            float depth = (4 + (14 * energy * close) + (6 * Spray * close)) * Dip;
            if (cloud < depth)
            {
                float chance = (.08 + (.5 * energy * energy) + (.25 * Spray)) * close * (1 - (cloud / depth));
                float flick = Hash(cellAt + float2(floor(Time * 12), Seed * .1));
                if (flick < chance)
                {
                    color = flick < chance * .25 ? CoreColor.rgb : (overUpper > 0 ? BodyColor.rgb : DeepColor.rgb);
                    alpha = 1;
                }
            }

            // the smears: long thin streaks on the rows next to the shores, running out from the dock, faster on a kick
            float rowStep = max(2, round(1.5 * Dip));
            float rowUp = floor(overUpper / rowStep);
            float rowDown = floor(underLower / rowStep);
            bool onRow = fmod(overUpper > 0 ? overUpper : underLower, rowStep) < max(1, Dip * .7);
            float rowIndex = overUpper > 0 ? rowUp : -1 - rowDown;
            if (onRow && ((overUpper > 0 && rowUp < 6) || (underLower > 0 && rowDown < 4)))
            {
                float speed = 60 + (220 * Level) + (260 * Spray);
                float along = ((side * Size.x * .5) / Dip) - (Time * speed * (.7 + (.6 * Hash(float2(rowIndex, 4)))));
                float span = 40 + (80 * Hash(float2(rowIndex, 6)));
                float dash = floor(along / span);
                float part = frac(along / span);
                float keep = Hash(float2(dash, rowIndex + (Seed * .01)));
                float dense = (.15 + (.6 * energy) + (.4 * Spray)) * close * (1 - (abs(rowIndex) / 7));
                float size = .25 + (.6 * Hash(float2(dash, rowIndex + 9)));
                if (keep < dense && part < size)
                {
                    // a smear is brightest at its head, the end that runs outward, in hard steps
                    float head = floor((1 - (part / size)) * 3) / 3;
                    float3 hot = lerp(BodyColor.rgb, CoreColor.rgb, head);
                    color = underLower > 0 ? lerp(DeepColor.rgb, BodyColor.rgb, head) : hot;
                    alpha = 1;
                }
            }

            // the shafts a snare lights: striped, pixel sorted columns on the upper shore, their broken reflection under
            float slot = floor(p.x / (60 * Dip));
            float pick = Hash(float2(slot, Seed));
            float centre = (slot + .2 + (.6 * Hash(float2(slot, Seed + 1)))) * 60 * Dip;
            float wide = (2 + (5 * Hash(float2(slot, Seed + 2)))) * Dip;
            float tall = (.5 + (.5 * Hash(float2(slot, Seed + 3)))) * upper * saturate(Shaft * 1.4);
            bool shaftHere = pick > .45 && abs(p.x - centre) < wide && side < .75;
            float strand = floor((p.x - centre) / max(1, round(.75 * Dip)));
            bool stripe = Hash(float2(strand, slot + Seed)) > .3;
            if (shaftHere && stripe && overUpper > 0 && overUpper < tall)
            {
                float block = floor(overUpper / ((2 + (4 * Hash(float2(strand, 7)))) * Dip));
                float b = Hash(float2(strand, block + Seed));
                if (b > .15)
                {
                    float fall = 1 - (overUpper / tall);
                    float steps = floor(((b > .7 ? 1 : .7) * (.4 + (.6 * fall))) * 4) / 4;
                    color = lerp(DeepColor.rgb, b > .9 ? CoreColor.rgb : BodyColor.rgb, steps);
                    alpha = 1;
                }
            }

            if (shaftHere && stripe && underLower > 0 && underLower < tall * .45 && fmod(floor(underLower / (2 * Dip)), 2) < 1)
            {
                color = DeepColor.rgb;
                alpha = 1;
            }

            // the two shores, the upper one hot near the dock on a kick
            if (min(up, down) <= rim && alpha < 1)
            {
                color = 0;
                alpha = max(alpha, .7);
            }

            if (up <= core)
            {
                color = lerp(BodyColor.rgb, CoreColor.rgb, saturate((Spray * (1 - side) * 1.5) + (.2 * Level)));
                alpha = 1;
            }
            else if (down <= core)
            {
                color = BodyColor.rgb;
                alpha = 1;
            }

            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .2, p.y / Size.y);
            return float4(color * min(alpha, 1), alpha) * Fade * top;
        }
        """;
}
