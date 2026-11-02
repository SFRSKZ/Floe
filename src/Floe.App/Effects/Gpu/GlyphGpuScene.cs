using System.Text;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A mirrored mass of fibres grows out of the spectrum and rolls down the strip. Above a wavering line near the bottom it
/// is fine stippled points, and where it crosses that line it turns into a matrix of letters, denser glyphs where it
/// burns hotter, with a few stray ones around it. Both are drawn pixel by pixel from the field, so nothing blurs: the font
/// is a 5x7 bitmap inside the shader. In negative the cells are filled and the letters are cut out of them.
/// </summary>
public sealed class GlyphGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 48;
    private static readonly Color Ink = Color.FromRgb(0xF3, 0xEF, 0xDC);
    private static readonly Color Hot = Color.FromRgb(0xFC, 0xEE, 0x0A);

    // light to heavy, as the field gets denser
    private static readonly string[] Font =
    {
        "..... ..... ..... ..... ..... ..#.. ..#..", // .
        "..... ..... ..#.. ..... ..... ..#.. .....", // :
        "..#.. ..... .##.. ..#.. ..#.. ..#.. .###.", // i
        "..#.. ..#.. .###. ..#.. ..#.. ..#.. ...##", // t
        "..... ..... ##### ...#. ..#.. .#... #####", // z
        "..... ..... #...# .#.#. ..#.. .#.#. #...#", // x
        "..... ..... .###. #...# #...# #...# .###.", // o
        "..... ..... .###. ....# .#### #...# .####", // a
        "..... ..... ##.#. #.#.# #.#.# #.#.# #.#.#", // m
        "#...# #...# #...# #.#.# #.#.# ##.## #...#", // W
        "..#.. .#### #.#.. .###. ..#.# ####. ..#..", // $
        "##..# ##.#. ..#.. ..#.. .#... #.#.# #..##", // %
        ".#.#. .#.#. ##### .#.#. ##### .#.#. .#.#.", // #
        ".###. #...# #...# .###. #...# #...# .###.", // 8
        "####. #...# #...# ####. #...# #...# ####.", // B
        ".###. #...# #.### #.#.# #.### #.... .####", // @
    };

    private static readonly string Shared = """
        cbuffer Letters : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Bass;
            float Pulse;
            float Level;
            float Line;
            float Tick;
            float Step;
            float Flash;
            float Hold;
            float BurstAt;
            float BurstPower;
            float Negative;
            float4 Bands[6];
            float4 InkColor;
            float4 HotColor;
        };

        """ + ShaderHelpers;

    private readonly Random _random = new(23);
    private float _time, _tick;
    private float _flash;
    private float _burstAt, _burstPower;
    private float _hold;

    public GlyphGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + GlyphTable() + ShowPs)
    {
    }

    // the font as two uints a glyph: 35 bits, row by row from the top
    private static string GlyphTable()
    {
        var table = new StringBuilder($"static const uint2 Glyphs[{Font.Length}] = {{");
        foreach (var glyph in Font)
        {
            ulong bits = 0;
            int at = 0;
            foreach (var c in glyph.Replace(" ", string.Empty))
            {
                if (c == '#') bits |= 1UL << at;
                at++;
            }

            table.Append($"uint2({(uint)bits}u,{(uint)(bits >> 32)}u),");
        }

        return table.Append("};\n").ToString();
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;
        var audio = frame.Audio;

        // a quiet bar must not empty the strip: this falls over seconds, so the mass stays until the music really stops
        float loud = Math.Max(audio.Level, audio.Bass);
        _hold += (loud - _hold) * (1 - MathF.Exp(-dt * (loud > _hold ? 8 : .3f)));
        _time += dt * (.5f + audio.Level + (2 * Beat.Swell));
        _tick += dt * (10 + (30 * audio.Level));

        if (Beat.Hit)
        {
            _flash = 1;
            _burstAt = (float)_random.NextDouble();
            _burstPower = 1;
        }

        _flash *= MathF.Exp(-dt * 8);
        _burstPower *= MathF.Exp(-dt * 6);
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _time;
        d[5] = Math.Max(audio.Bass, Beat.Pulse);
        d[6] = Beat.Pulse;
        d[7] = audio.Level;
        d[8] = .5f + (.12f * audio.Level);
        d[9] = MathF.Floor(_tick);
        d[10] = frame.Dt * 60;
        d[11] = _flash;
        d[12] = _hold;
        d[13] = _burstAt;
        d[14] = _burstPower;
        d[15] = Services.GlassSettings.GlyphNegative ? 1 : 0;
        PutBands(16, frame);
        Put(40, Lead(Ink));
        Put(44, Second(Hot));
    }

    private const string FeedbackPs = """

        float Stream(float2 q)
        {
            return Noise((q * 1.7) + float2(Time * .19, 7)) + (.5 * Noise((q * 4.1) - float2(5, Time * .27)));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / Size;
            float aspect = Size.x / Size.y;
            float mid = abs(uv.x - .5) * 2;
            float2 q = float2(mid * aspect * .5, uv.y);

            // mirrored like a scan of something alive; the curl has no sinks, so the mass folds into fibres
            float e = .02;
            float2 flow = float2(Stream(q + float2(0, e)) - Stream(q - float2(0, e)),
                                 Stream(q - float2(e, 0)) - Stream(q + float2(e, 0))) / (2 * e);
            float2 away = (flow * (.008 + (.014 * Bass))) + float2(0, .004 + (.006 * Bass));
            away.x *= sign(uv.x - .5);
            float2 back = position.xy - (away * Size.y * Step) - MilkFlow(position.xy);
            float last = Last.SampleLevel(Linear, back / Size, 0).r * pow(.97, Step);

            float energy = max(saturate(Band(mid * .92) * 1.3), Hold * (.35 + (.25 * sin((q.x * 5) + Time))));
            float ridge = .55 - (.45 * energy);
            float width = .03 + (.07 * energy);
            float grain = Noise(float2(q.x * 26, (q.y * 9) - (Time * .6)));
            float fibre = .5 + (.5 * sin((q.x * 70) + (grain * 9)));
            float body = exp(-pow((uv.y - ridge) / width, 2)) * energy * (.3 + (fibre * grain * 1.6));

            float burst = exp(-pow((mid - BurstAt) / .05, 2)) * BurstPower * smoothstep(.8, 0, uv.y);
            float gain = (.2 + (.35 * max(Level, Hold * .7))) * Step;
            return float4(min(1.5, last + ((body + (.5 * burst * fibre)) * gain)), 0, 0, 1);
        }
        """;

    private const string ShowPs = """

        float Letter(int index, int2 at)
        {
            if (at.x < 0 || at.x > 4 || at.y < 0 || at.y > 6) return 0;
            int bit = (at.y * 5) + at.x;
            uint2 glyph = Glyphs[index];
            return (bit < 32 ? (glyph.x >> bit) : (glyph.y >> (bit - 32))) & 1;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 pixel = position.xy;
            int size = max(1, (int)floor(Dip));
            int2 box = int2(6, 9) * size;
            float mid = abs((pixel.x / Size.x) - .5) * 2 * Size.x / Size.y;

            // the line where waves become letters wavers, and a beat tears it
            int2 cell = (int2)pixel / box;
            float2 id = (float2)cell;
            float tear = (Hash(float2(id.x, floor(Tick / 2))) - .5) * Flash * .2;
            float turnLine = Size.y * (Line + (.05 * sin((mid * 2.5) + Time)) + tear);

            float3 color = InkColor.rgb;
            float alpha;
            if ((cell.y + .5) * box.y < turnLine)
            {
                // the fine side: points gather on the folds of the mass and thin out inside it, so it reads as fibres
                int2 grain = (int2)pixel / size;
                int2 at = grain * size;
                int reach = 3 * size;
                float v = saturate(Last.Load(int3(at, 0)).r * 1.5);
                float fold = abs(Last.Load(int3(at + int2(reach, 0), 0)).r - Last.Load(int3(at - int2(reach, 0), 0)).r)
                    + abs(Last.Load(int3(at + int2(0, reach), 0)).r - Last.Load(int3(at - int2(0, reach), 0)).r);
                alpha = step(Hash((float2)grain), (v * .14) + (fold * 1.1)) * (.35 + (.65 * v));
                if (Negative > .5) color = HotColor.rgb;
            }
            else
            {
                // the letters: every cell reads the field once, and picks its glyph anew now and then
                float v = saturate(Last.Load(int3((cell * box) + (box / 2), 0)).r * 2.2);
                float turn = floor((Tick + (Hash(id) * 37)) / 6);
                float wobble = (Hash(id + turn) - .5) * 3;
                int index = clamp((int)((v * 15) + wobble), 0, 15);

                // a few strays around the mass
                float stray = step(.992, Hash(id + float2(turn * .37, 9)));
                float inked = step(.05, v);
                if (inked < .5) index = (int)(Hash(id + turn + 3) * 11);

                int2 inside = ((int2)pixel - (cell * box)) / size;
                float ink = Letter(index, inside);
                float strength = max(inked, stray * saturate(.4 + Level));
                alpha = (Negative > .5 ? 1 - ink : ink) * strength;
                alpha *= inked > .5 ? .75 + (.25 * v) : .55;
                if (v > .72) color = HotColor.rgb;
            }

            // the top melts away, so the strip has no cut line over an empty desktop
            float top = smoothstep(0, .25, pixel.y / Size.y);
            return float4(color * alpha, alpha) * Fade * top;
        }
        """;
}
