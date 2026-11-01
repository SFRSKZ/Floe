using System.Text;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Breach Protocol played by the music. Each wing is a live matrix of hex codes and every cell has its own state in the
/// feedback texture. On a beat the cursor moves the way the game moves it, along a row and then along a column, and the
/// cell it picks is corrupted. Corruption runs through its neighbours as a wave, faster where the music is loud, burns
/// the codes out behind it and they grow back as new ones. Some cells lock yellow as the wave dies on them and stand in
/// the way of the next wave, so the board keeps changing where it can burn. Beats fill the buffer, and a full buffer
/// sweeps out from the dock and wipes the wing clean. The state is infection, recovery, lock and generation.
/// </summary>
public sealed class BreachGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 72;

    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Cyan = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Paper = Color.FromRgb(0xEC, 0xEC, 0xE6);

    // Seven rows of five cells per glyph: space, A-Z, 0-9, '-'. '# ' is ink.
    private static readonly string[] Font =
    {
        "..... ..... ..... ..... ..... ..... .....", // space
        ".###. #...# #...# ##### #...# #...# #...#", // A
        "####. #...# #...# ####. #...# #...# ####.", // B
        ".###. #...# #.... #.... #.... #...# .###.", // C
        "####. #...# #...# #...# #...# #...# ####.", // D
        "##### #.... #.... ####. #.... #.... #####", // E
        "##### #.... #.... ####. #.... #.... #....", // F
        ".###. #...# #.... #.### #...# #...# .####", // G
        "#...# #...# #...# ##### #...# #...# #...#", // H
        ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.", // I
        "..### ...#. ...#. ...#. ...#. #..#. .##..", // J
        "#...# #..#. #.#.. ##... #.#.. #..#. #...#", // K
        "#.... #.... #.... #.... #.... #.... #####", // L
        "#...# ##.## #.#.# #.#.# #...# #...# #...#", // M
        "#...# ##..# #.#.# #..## #...# #...# #...#", // N
        ".###. #...# #...# #...# #...# #...# .###.", // O
        "####. #...# #...# ####. #.... #.... #....", // P
        ".###. #...# #...# #...# #.#.# #..#. .##.#", // Q
        "####. #...# #...# ####. #.#.. #..#. #...#", // R
        ".#### #.... #.... .###. ....# ....# ####.", // S
        "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..", // T
        "#...# #...# #...# #...# #...# #...# .###.", // U
        "#...# #...# #...# #...# #...# .#.#. ..#..", // V
        "#...# #...# #...# #.#.# #.#.# #.#.# .#.#.", // W
        "#...# #...# .#.#. ..#.. .#.#. #...# #...#", // X
        "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..", // Y
        "##### ....# ...#. ..#.. .#... #.... #####", // Z
        ".###. #...# #..## #.#.# ##..# #...# .###.", // 0
        "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.", // 1
        ".###. #...# ....# ...#. ..#.. .#... #####", // 2
        "##### ...#. ..#.. ...#. ....# #...# .###.", // 3
        "...#. ..##. .#.#. #..#. ##### ...#. ...#.", // 4
        "##### #.... ####. ....# ....# #...# .###.", // 5
        "..##. .#... #.... ####. #...# #...# .###.", // 6
        "##### ....# ...#. ..#.. .#... .#... .#...", // 7
        ".###. #...# #...# .###. #...# #...# .###.", // 8
        ".###. #...# #...# .#### ....# ...#. .##..", // 9
        "..... ..... ..... ##### ..... ..... .....", // -
    };

    private static readonly string Labels = LabelTable();

    private const int Rows = 4;
    private const int PathLength = 4;

    private readonly Random _dice = new(2077);
    private readonly int[,,] _path = new int[2, PathLength, 2];
    private readonly bool[] _alongRow = { true, false };
    private float _owed, _fill, _sweep = -1;
    private bool _step, _surge, _snare;
    private int _tick;

    public BreachGpuScene() : base(ConstantFloats, Source(FeedbackPs), Source(ShowPs))
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        float dt = frame.Dt;

        // the board runs on its own clock, and the music is what speeds it up
        _owed = Math.Min(_owed + (dt * (2 + (26 * frame.Audio.Level))), 2);
        _step = _owed >= 1;
        if (_step)
        {
            _owed -= 1;
            _tick = (_tick + 1) % 8192;
        }

        if (Beat.Hit)
        {
            _surge = true;
            _fill = Math.Min(_fill + .045f, 1);
            int columns = Math.Max(2, (int)((frame.Width * .5f) - (2 * Margin(frame))) / CellWide(frame));
            Move(0, columns);
            Move(1, columns);
        }

        if (Beat.Snare)
        {
            _snare = true;
            _fill = Math.Max(0, _fill - .015f);
        }

        if (_fill >= 1 && _sweep < 0) _sweep = 1;
        if (_sweep >= 0)
        {
            _sweep -= dt / 1.3f;
            if (_sweep < 0)
            {
                _sweep = -1;
                _fill = 0;
            }
        }
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = _tick;
        d[5] = _step ? 1 : 0;
        d[6] = _step && _surge ? 1 : 0;
        d[7] = _step && _snare ? 1 : 0;
        d[8] = frame.Audio.Level;
        d[9] = frame.Audio.Bass;
        d[10] = _fill;
        d[11] = _sweep;
        d[12] = Beat.Pulse;
        if (_step) _surge = _snare = false;
        PutBands(16, frame);
        Put(40, Lead(Yellow));
        Put(44, Cyan);
        Put(48, Second(Red));
        Put(52, Paper);
        for (int i = 0; i < PathLength; i++)
        {
            d[56 + (i * 4)] = _path[0, i, 0];
            d[57 + (i * 4)] = _path[0, i, 1];
            d[58 + (i * 4)] = _path[1, i, 0];
            d[59 + (i * 4)] = _path[1, i, 1];
        }
    }

    private static float Margin(GpuFrame frame) => 16 * frame.Height / 126f;

    private static int CellWide(GpuFrame frame) => 14 * Math.Max(1, (int)((1.34f * frame.PixelsPerDip) + .5f));

    // one move of the game's cursor: the wing shifts along its row, the next one along its column
    private void Move(int wing, int columns)
    {
        for (int i = PathLength - 1; i > 0; i--)
        {
            _path[wing, i, 0] = _path[wing, i - 1, 0];
            _path[wing, i, 1] = _path[wing, i - 1, 1];
        }

        int axis = _alongRow[wing] ? 0 : 1;
        int span = _alongRow[wing] ? columns : Rows;
        int next = _dice.Next(span - 1);
        _path[wing, 0, axis] = next >= _path[wing, 0, axis] ? next + 1 : next;
        _alongRow[wing] = !_alongRow[wing];
    }

    // ---------------------------------------------------------------- shader --
    private static string Source(string pixelShader) =>
        Prefix + FontTable() + GlyphHelper + Labels + Helpers + pixelShader;

    private const string Prefix = """
        cbuffer Breach : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Tick;
            float Stepping;
            float Surge;
            float Snare;
            float Level;
            float Bass;
            float BufferFill;
            float Sweep;
            float Pulse;
            float Spare0;
            float Spare1;
            float Spare2;
            float4 Bands[6];
            float4 Hot;
            float4 Cold;
            float4 Warn;
            float4 Paper;
            float4 Path[4];
        };

        """ + ShaderHelpers;

    private const string GlyphHelper = """
        float GlyphCell(int id, int gx, int gy)
        {
            if (gx < 0 || gx > 4 || gy < 0 || gy > 6) return 0;
            int bit = (gy * 5) + gx;
            uint2 glyph = Glyphs[id];
            uint v = (bit < 32 ? (glyph.x >> bit) : (glyph.y >> (bit - 32))) & 1u;
            return (float)v;
        }

        """;

    private const string Helpers = """
        static const int RowCount = 4;

        float S() { return Size.y / 126.0; }
        float WingW() { return Size.x * .5; }
        float HeaderEnd() { return Size.x * .27; }
        int FontPx() { return max(1, (int)((1.34 * Dip) + .5)); }
        float MX0() { return 16 * S(); }
        float MY0() { return floor(20 * S()); }
        int CellW() { return 14 * FontPx(); }
        int CellH() { return (int)((Size.y - MY0() - (8 * S())) / RowCount); }
        int Columns() { return max(2, (int)(WingW() - (2 * MX0())) / CellW()); }
        int WingOf(float2 p) { return p.x < Size.x * .5 ? 0 : 1; }

        // the wing's own coordinates: x from the outer edge inwards, y from the top down
        float2 WingPos(float2 p) { return float2(p.x < Size.x * .5 ? p.x : Size.x - p.x, p.y); }

        bool CellOf(float2 d, out int2 c)
        {
            c = (int2)floor((d - float2(MX0(), MY0())) / float2(CellW(), CellH()));
            return c.x >= 0 && c.x < Columns() && c.y >= 0 && c.y < RowCount;
        }

        float2 CellStart(int2 c) { return float2(MX0(), MY0()) + (float2(c) * float2(CellW(), CellH())); }
        float2 CellMiddle(int2 c) { return CellStart(c) + (float2(CellW(), CellH()) * .5); }

        float4 CellState(int2 c, int wing)
        {
            if (c.x < 0 || c.x >= Columns() || c.y < 0 || c.y >= RowCount) return 0;
            float2 m = CellMiddle(c);
            return Last.Load(int3((int)(wing == 0 ? m.x : Size.x - m.x), (int)m.y, 0));
        }

        float Energy(int column)
        {
            return saturate(Band(1 - ((float)column / (Columns() - 1))) * 1.3);
        }

        int2 Picked(int i, int wing) { return (int2)(wing == 0 ? Path[i].xy : Path[i].zw); }

        int ReqCode(int i)
        {
            return (int)(Hash(float2((i * 1.31) + 7, 21)) * 256);
        }

        float HexInk(float2 q, int code, float fs)
        {
            float2 f = q / fs;
            if (f.x < 0 || f.y < 0 || f.x >= 11 || f.y >= 7) return 0;
            int ci = f.x >= 6 ? 1 : 0;
            int gx = (int)f.x - (ci * 6);
            int gy = (int)f.y;
            int nib = ci == 0 ? (code / 16) : (code & 15);
            int id = nib < 10 ? (27 + nib) : (1 + (nib - 10));
            return GlyphCell(id, gx, gy);
        }

        """;

    private static string LabelTable() =>
        Label("TitleInk", "BREACH PROTOCOL") + Label("GrantedInk", "ACCESS GRANTED") + Label("ReqInk", "REQ");

    private static string Label(string name, string text)
    {
        var sb = new StringBuilder();
        int cells = (text.Length * 6) - 1;
        sb.Append("float ").Append(name).Append("(float2 sp, float2 at, float fs, float mirror)\n{\n");
        sb.Append("    float wid = ").Append(cells).Append(" * fs;\n");
        sb.Append("    float2 a2 = float2(mirror > .5 ? (Size.x - at.x - wid) : at.x, at.y);\n");
        sb.Append("    float2 q = (sp - a2) / fs;\n");
        sb.Append("    if (q.x < 0 || q.y < 0 || q.x >= ").Append(cells).Append(" || q.y >= 7) return 0;\n");
        sb.Append("    int ci = (int)(q.x / 6);\n");
        sb.Append("    int gx = (int)q.x - (ci * 6);\n");
        sb.Append("    int gy = (int)q.y;\n");
        sb.Append("    if (gx > 4) return 0;\n");
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append("    if (ci == ").Append(i).Append(") return GlyphCell(").Append(GlyphIndex(text[i])).Append(", gx, gy);\n");
        }

        return sb.Append("    return 0;\n}\n\n").ToString();
    }

    private static string FontTable()
    {
        var table = new StringBuilder();
        table.Append("static const uint2 Glyphs[").Append(Font.Length).Append("] =\n{\n");
        foreach (var glyph in Font)
        {
            ulong bits = 0;
            int at = 0;
            foreach (var cell in glyph.Replace(" ", string.Empty))
            {
                if (cell == '#') bits |= 1UL << at;
                at++;
            }

            table.Append("    uint2(").Append((uint)bits).Append("u, ").Append((uint)(bits >> 32)).Append("u),\n");
        }

        return table.Append("};\n\n").ToString();
    }

    private static int GlyphIndex(char c) => c switch
    {
        ' ' => 0,
        >= 'A' and <= 'Z' => 1 + (c - 'A'),
        >= '0' and <= '9' => 27 + (c - '0'),
        '-' => 37,
        _ => 0
    };

    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            if (Stepping < .5) return Last.Load(int3((int2)p, 0));

            int wing = WingOf(p);
            int2 c;
            if (!CellOf(WingPos(p), c)) return 0;

            float4 s = CellState(c, wing);
            float infected = s.r, recovering = s.g, locked = s.b, generation = s.a;

            // the sweep leaves a clean board behind it
            if (Sweep >= 0 && abs(c.x - (Sweep * Columns())) < 1.5) return float4(0, 0, 0, generation + 1);

            int burning = 0;
            if (CellState(c + int2(-1, 0), wing).r > .01) burning++;
            if (CellState(c + int2(1, 0), wing).r > .01) burning++;
            if (CellState(c + int2(0, -1), wing).r > .01) burning++;
            if (CellState(c + int2(0, 1), wing).r > .01) burning++;

            float dice = Hash(float2(c) + float2((Tick * .37) + (wing * 9.1), Tick * .71));
            float e = Energy(c.x);

            if (locked > .5)
            {
                // a snare breaks a few locks, and they go up like any other cell
                if (Snare > .5 && dice < .14)
                {
                    locked = 0;
                    infected = 1;
                }
            }
            else if (infected > 0)
            {
                infected -= .22;
                if (infected <= 0)
                {
                    infected = 0;
                    if (dice < .1) locked = 1;
                    else recovering = 1;
                }
            }
            else if (recovering > 0)
            {
                recovering -= .05 + (.06 * Level);
                if (recovering <= 0)
                {
                    recovering = 0;
                    generation += 1;
                }
            }
            else
            {
                if (burning > 0 && dice < .2 + (.6 * e)) infected = 1;
                else if (Surge > .5 && dice < .006 + (.03 * e)) infected = 1;
            }

            if (Surge > .5 && all(c == Picked(0, wing)))
            {
                locked = 0;
                recovering = 0;
                infected = 1;
            }

            return float4(infected, recovering, locked, generation);
        }

        """;

    private const string ShowPs = """

        // how close a point is to the box around a stretch of the cursor's path
        float SegmentInk(float2 d, float2 a, float2 b, float width)
        {
            float2 low = min(a, b) - width, high = max(a, b) + width;
            return d.x >= low.x && d.x <= high.x && d.y >= low.y && d.y <= high.y ? 1 : 0;
        }

        float FrameInk(float2 d, float2 low, float2 high, float width)
        {
            if (d.x < low.x || d.y < low.y || d.x >= high.x || d.y >= high.y) return 0;
            return d.x < low.x + width || d.y < low.y + width || d.x >= high.x - width || d.y >= high.y - width ? 1 : 0;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float W = Size.x, H = Size.y, s = S(), ww = WingW();
            int fsi = FontPx();
            float fs = fsi;
            float2 d = WingPos(p);
            int wing = WingOf(p);
            bool inWing = d.x <= ww;
            float3 color = Cold.rgb;
            float alpha = 0;

            int2 c;
            if (inWing && CellOf(d, c))
            {
                float4 cell = CellState(c, wing);
                float2 start = CellStart(c);
                float2 q = float2(d.x - start.x, d.y - start.y);

                // text reads left to right on both wings, so a mirrored cell starts from its far side
                float2 box = float2(CellW(), CellH());
                float2 textAt = (box - float2(11 * fs, 7 * fs)) * .5;
                textAt = float2(floor(textAt.x), floor(textAt.y));
                float2 tq = float2((wing == 0 ? q.x : box.x - q.x) - textAt.x, q.y - textAt.y);
                bool edge = q.x < 1 || q.y < 1 || q.x >= box.x - 1 || q.y >= box.y - 1;

                float e = Energy(c.x);
                float seed = Hash(float2((c.x * 1.7) + (c.y * 3.9) + (wing * 5.3), (cell.a * 1.3) + .5));
                int code = (int)(seed * 256);
                bool sweeping = Sweep >= 0 && abs(c.x - (Sweep * Columns())) < 1.5;

                if (sweeping)
                {
                    color = edge ? float3(0, 0, 0) : (HexInk(tq, code, fs) > .5 ? float3(0, 0, 0) : Cold.rgb);
                    alpha = edge ? 0 : 1;
                }
                else if (cell.b > .5)
                {
                    code = ReqCode((int)(seed * 3));
                    bool ink = HexInk(tq, code, fs) > .5;
                    color = ink || q.y >= box.y - (2 * s) ? Hot.rgb : Hot.rgb * .3;
                    alpha = edge ? 0 : (ink ? 1 : .55);
                }
                else if (cell.r > 0)
                {
                    code = (int)(Hash(float2(seed * 91, Tick)) * 256);
                    bool ink = HexInk(tq, code, fs) > .5;
                    color = cell.r > .7 ? Paper.rgb : Warn.rgb;
                    if (ink) color = float3(.02, .01, .02);
                    alpha = edge ? 0 : 1;
                }
                else if (cell.g > 0)
                {
                    // a burnt cell is an empty socket, and its new code comes up bright
                    if (cell.g < .12)
                    {
                        color = Paper.rgb;
                        alpha = HexInk(tq, code, fs) * .95;
                    }
                    else
                    {
                        color = Warn.rgb * (.14 + (.4 * cell.g));
                        alpha = edge ? 0 : .8;
                        if (HexInk(tq, 0x00, fs) > .5) alpha = 0;
                    }
                }
                else
                {
                    color = Paper.rgb;
                    alpha = HexInk(tq, code, fs) * (.3 + (.68 * e));
                    if (alpha < .05)
                    {
                        color = Cold.rgb;
                        alpha = edge ? .05 : .09;
                    }
                }

                // the game's highlight: the row or column the cursor must pick from next
                int2 now = Picked(0, wing), before = Picked(1, wing);
                bool nextColumn = now.y == before.y;
                if ((nextColumn ? c.x == now.x : c.y == now.y) && cell.r <= 0 && alpha < .5 && !edge)
                {
                    color = Cold.rgb;
                    alpha = .2 + (.14 * Pulse);
                }
            }

            // the cursor's path through the matrix, oldest first so the newest lies on top
            if (inWing)
            {
                float width = max(1, round(s));
                for (int i = 2; i >= 0; i--)
                {
                    float2 a = CellMiddle(Picked(i, wing)), b = CellMiddle(Picked(i + 1, wing));
                    if (SegmentInk(d, a, b, width) > .5)
                    {
                        color = Cold.rgb;
                        alpha = 1 - (i * .28);
                    }
                }

                float2 head = CellStart(Picked(0, wing));
                float2 size = float2(CellW(), CellH());
                float thick = max(2, round(2 * s));
                if (FrameInk(d, head - 1, head + size + 1, thick) > .5)
                {
                    color = Paper.rgb;
                    alpha = 1;
                }

                float reach = 3 + (5 * Pulse);
                if (FrameInk(d, head - reach, head + size + reach, 1) > .5)
                {
                    color = Cold.rgb;
                    alpha = .8;
                }
            }

            // the header shows what the run is after, and says so when the buffer breaks through
            if (inWing && d.y < MY0() - 1)
            {
                float2 at = float2(MX0(), 3 * s);
                float mirror = wing;
                if (Sweep >= 0)
                {
                    if (GrantedInk(p, at, fs, mirror) > .5)
                    {
                        color = Cold.rgb;
                        alpha = 1;
                    }
                }
                else if (TitleInk(p, at, fs, mirror) > .5)
                {
                    color = Paper.rgb;
                    alpha = .8;
                }

                float2 reqAt = float2(HeaderEnd() - MX0() - (24 * fs) - (3 * CellW()), 3 * s);
                if (ReqInk(p, reqAt, fs, mirror) > .5)
                {
                    color = Cold.rgb;
                    alpha = .8;
                }

                for (int i = 0; i < 3; i++)
                {
                    float x = HeaderEnd() - MX0() - ((3 - i) * CellW());
                    float left = wing == 0 ? x : W - x - (11 * fs);
                    if (HexInk(p - float2(left, 3 * s), ReqCode(i), fs) > .5)
                    {
                        color = Hot.rgb;
                        alpha = .9;
                    }
                }
            }

            // the buffer runs under the dock as well, one bar over the whole strip
            float bx = p.x - MX0();
            float bw = W - (2 * MX0());
            float by = H - (4 * s);
            if (bx >= 0 && bx <= bw && abs(p.y - by) <= 2.5 * s)
            {
                float fillTo = BufferFill * bw;
                float segment = fmod(bx, 24 * s);
                if (segment > 7 * s)
                {
                    color = bx < fillTo ? Hot.rgb : Cold.rgb;
                    alpha = bx < fillTo ? .95 : .16;
                }

                if (abs(bx - fillTo) < 1.5 * s)
                {
                    color = Paper.rgb;
                    alpha = 1;
                }
            }

            return float4(color, 1) * alpha * Fade;
        }

        """;
}
