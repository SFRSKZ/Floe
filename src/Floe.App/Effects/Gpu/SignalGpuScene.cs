using Floe.App.Services;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A lost video signal: jagged white ribbons that hold still, jump on the beat and crumble where they stood.
/// <para>
/// MilkDrop's trick is the feedback: every frame is the last one warped and eaten a little, with the new shapes
/// on top. Here the warp is whole pixels (Load, never a filter), so nothing blurs: the "burn" channel loses a
/// noisy amount per frame and is shown through a hard threshold, which breaks a left-behind ribbon into crisp
/// crumbs, and the "ghost" channel keeps a dim copy that the rows drag sideways.
/// </para>
/// <para>
/// The field is drawn in stepped time: one seed holds until a kick, a snare or a short timeout changes it, which
/// is what makes it read as a signal rather than as an animation.
/// </para>
/// </summary>
public sealed class SignalGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 60;
    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Cold = Color.FromRgb(0x02, 0xD7, 0xF2);

    private int _frame;

    // the stepped clock
    private int _seed = 1, _fast;
    private float _hold, _fastHold, _sinceJump;
    private float _tear, _roll;
    private readonly Random _random = new(7);

    public SignalGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    // The field keeps one seed until something knocks it over; a quiet song holds longer than a loud one.
    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;
        _frame = (_frame + 1) % 8192;
        _sinceJump += dt;
        _hold -= dt;
        if (Beat.Hit || _hold <= 0)
        {
            _seed = (_seed % 997) + 1 + _random.Next(40);
            _sinceJump = 0;
            _hold = 1.4f;
        }

        _fastHold -= dt;
        if (_fastHold <= 0)
        {
            _fast = (_fast + 1) % 4096;
            _fastHold = .07f;
        }

        _tear = Math.Max(_tear * MathF.Exp(-dt * 8), Beat.Hit ? 1 : 0);
        if (Beat.Snare) _roll = ((float)_random.NextDouble() - .5f) * .5f;
        _roll *= MathF.Exp(-dt * 14);
    }

    protected override void Fill(GpuFrame frame)
    {
        var audio = frame.Audio;
        var d = Data;
        float step = frame.Dt * 60;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _seed;
        d[5] = _fast;
        d[6] = _frame;
        d[7] = .06f * step;
        d[8] = Math.Max(audio.Bass, Beat.Pulse);
        d[9] = audio.Mid;
        d[10] = Beat.Sizzle;
        d[11] = audio.Level;
        d[12] = _tear;
        d[13] = _roll;
        d[14] = MathF.Pow(.9f, step);
        d[15] = frame.Time;
        PutBands(16, frame);
        var body = Lead(Yellow);
        Put(40, Mix(Colors.White, body, .18f));
        Put(44, body);
        Put(48, Second(Red));
        Put(52, Theme is null ? Cold : body);
        d[56] = GlassSettings.SignalEqualizer ? 1 : 0;
    }

    private const string Shared = """
        cbuffer Signal : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Seed;
            float Fast;
            float Tick;
            float Erode;
            float Bass;
            float Mid;
            float Treble;
            float Level;
            float Tear;
            float Roll;
            float Decay;
            float Time;
            float4 Bands[6];
            float4 Core;
            float4 Body;
            float4 Hot;
            float4 Cold;
            float4 Form;
        };

        """ + ShaderHelpers;

    private const string FeedbackPs = """

        // Positive inside the signal. The zigzag is the video's sawtooth laid on its side: one displacement per
        // column, so the ribbons run along the strip and the loudest bands throw them furthest.
        float Field(int2 pixel)
        {
            float col = floor(pixel.x / (1.5 * Dip));
            float mid = abs((pixel.x / Size.x) - .5) * 2;
            float energy = saturate(Band(mid * .9) * 1.2);

            float period = 11 + (Hash(float2(Seed, 1)) * 15);
            float zig = (abs(frac(col / period) - .5) * 4) - 1;
            float section = floor(col / 64);
            float amp = (.05 + (.34 * energy)) * lerp(.35, 1, Hash(float2(section, Seed)));
            float wander = sin((col * .027) + (Seed * 1.7)) * (.1 + (.22 * Bass));
            float thrown = Hash(float2(floor(col / 22), Seed + 3)) > .78 ? (Hash(float2(floor(col / 22), Seed + 5)) - .5) * (.2 + (.6 * Tear)) : 0;
            float y = (pixel.y / Size.y) + Roll + (zig * amp) + wander + thrown;

            // some steps drop the picture altogether and leave only the sparks and the static, as a bad signal does
            float present = Hash(float2(Seed, 9)) < .2 ? 0 : 1;
            float field = -1;
            for (int k = 0; k < 3; k++)
            {
                float centre = .24 + (k * .26) + (.1 * sin((col * .013) + (Seed * (k + 1.3))));
                float gate = Noise(float2((col * .016) + (Seed * 2.3), k * 5.7));
                float thick = (.018 + (.13 * energy * energy) + (.025 * Level)) * smoothstep(.5, .62, gate + (.16 * energy)) * present;
                field = max(field, thick - abs(y - centre));
            }

            float blob = Noise(float2((col * .014) + (Seed * 3.1), y * 3.2)) - .9 + (.22 * energy * energy);
            return max(field, blob * .5 * present);
        }

        // The other shape: thin rows reaching out from the middle, each row as long as its own band.
        float Rows(int2 p)
        {
            float row = floor(p.y / (5 * Dip));
            float mid = abs((p.x / Size.x) - .5) * 2;
            float reach = saturate(Band(Hash(float2(row, 3))) * 1.3) * 1.1;
            return step(fmod(p.y, 5 * Dip), 3.2 * Dip) * step(mid, reach) - .5;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 p = (int2)position.xy;
            float fresh = (Form.x > .5 ? Rows(p) : Field(p)) > 0 ? 1 : 0;

            // the burn stays where it was, shivers by the row, and is eaten in small cells
            float row = floor(p.y / (2 * Dip));
            int shiver = (int)round((Hash(float2(row, Tick)) - .5) * Dip * (2 + (10 * Tear)));
            float cell = Hash(floor(p / (2 * Dip)) + float2(Seed * 3, 0));
            int2 moved = p - MilkStep(position.xy, 1, Hash(floor(p / (2 * Dip)) + float2(Tick * .37, 1)));
            float burn = Last.Load(int3(moved + int2(shiver, 0), 0)).r;
            burn -= Erode * (.3 + (2.2 * cell));

            // the ghost slides along the rows, each band of them its own way
            float band = floor(p.y / (5 * Dip));
            int slide = (int)round(sin((band * 1.3) + (Time * 3)) * Dip * 1.5);
            float ghost = Last.Load(int3(moved + int2(slide, (int)Dip), 0)).g * Decay;

            return float4(max(burn, fresh), max(ghost, fresh * .55), 0, 1);
        }
        """;

    private const string ShowPs = """

        float Burn(int2 p)
        {
            return step(.5, Last.Load(int3(p, 0)).r);
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 p = (int2)position.xy;
            float2 pixel = position.xy;

            // chroma: the picture's channels miss each other by a little, more on a kick
            int split = (int)max(1, round(Dip * (1.2 + (3 * Tear))));
            float core = Burn(p);
            float left = Burn(p + int2(split, 0));
            float right = Burn(p - int2(split, 0));

            // a hard black shadow keeps the white readable over a bright wallpaper
            int reach = (int)round(3 * Dip);
            float shade = max(max(Burn(p + int2(reach, 0)), Burn(p - int2(reach, 0))), max(Burn(p + int2(0, reach)), Burn(p - int2(0, reach))));

            float ghost = Last.Load(int3(p, 0)).g;

            // sparks: short dashes on their own rows, a red and a blue end
            float h = max(2, round(1.5 * Dip));
            float rowIndex = floor(pixel.y / h);
            float width = 58 * Dip;
            float offset = Hash(float2(rowIndex, 9)) * width;
            float column = floor((pixel.x + offset) / width);
            float along = frac((pixel.x + offset) / width);
            float pick = Hash(float2(column, rowIndex) + (Fast * .37));
            float pair = Hash(float2(column + 3, floor(Fast * .5)));
            float on = step(1 - (.004 + (.05 * Treble) + (.03 * Tear)), pick);
            float start = Hash(float2(column, rowIndex + 7)) * .7;
            float size = .05 + (.16 * Hash(float2(rowIndex, column + 11)));
            float dash = on * step(start, along) * step(along, start + size);
            float t = (along - start) / size;
            float3 sparkColor = t < .22 ? Hot.rgb : (t > .78 ? Cold.rgb : float3(1, 1, 1));

            // static: fine indigo grain, heavier at the bottom of the strip, in slow patches
            float2 grain = floor(pixel / max(1, round(.75 * Dip)));
            float dots = step(.8, Hash(grain + float2(Tick, 0)));
            float patch = Noise(float2((pixel.x / Size.x * 9) + (Seed * .7), (pixel.y / Size.y * 2.5) + 4));
            float weight = smoothstep(.3, 1, pixel.y / Size.y) * saturate((patch * 1.7) - .35) * (.18 + (.55 * Mid));
            float3 staticColor = lerp(float3(.22, .14, .95), Cold.rgb, .22);

            // fine vertical scan lines through everything
            float lines = fmod(floor(pixel.x / max(1, round(.75 * Dip))), 2) < 1 ? 1 : .84;

            float3 color = 0;
            float alpha = 0;

            float s = dots * weight;
            color = staticColor * s * lines;
            alpha = s * .9;

            float g = ghost * (1 - core) * .7;
            color = max(color, Body.rgb * g * lines);
            alpha = max(alpha, g);

            if (shade > 0 && core < .5)
            {
                alpha = max(alpha, .62);
            }

            if (left > 0 && core < .5) { color = Hot.rgb; alpha = 1; }
            if (right > 0 && core < .5) { color = Cold.rgb; alpha = 1; }
            if (core > .5) { color = Core.rgb * lines; alpha = 1; }
            if (dash > 0) { color = sparkColor; alpha = 1; }

            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .35, pixel.y / Size.y);
            return float4(color, alpha) * Fade * top;
        }
        """;
}
