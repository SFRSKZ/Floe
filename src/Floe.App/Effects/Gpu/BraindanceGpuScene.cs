using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A Braindance tape that is really being recorded. The head crosses each wing and writes the song onto the tape column
/// by column, as a spectrogram in the editor's three layers: thermal for the low end, visual for the middle, audio for
/// the top. Nothing on the tape moves until the head comes round again. A snare makes the head stop listening and copy
/// the last beat of the tape over itself instead, so the song's own past repeats on the strip, losing a little more
/// with every generation, and the timecode jumps back with it.
/// <para>
/// The feedback field is the tape, cell by cell: R the level written, G how many copies old it is, B a beat mark,
/// A the row's slow average.
/// </para>
/// </summary>
public sealed class BraindanceGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 64;
    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);
    private static readonly Color Cold = Color.FromRgb(0x02, 0xD7, 0xF2);
    private static readonly Color Paper = Color.FromRgb(0xEC, 0xEC, 0xE6);
    private static readonly Color PanelInk = Color.FromRgb(0x0A, 0x0A, 0x10);

    private const float ColumnsPerSecond = 30;

    private float _snare, _owed, _copying, _clock;
    private bool _step, _mark;
    private int _head, _lastBeat, _beatSpan = 24, _copyFrom;

    public BraindanceGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        // the analyser's snare is a one-frame flag; the design wants a short envelope to read as a rewind
        _snare = Beat.Snare ? 1 : _snare * MathF.Exp(-frame.Dt * 12f);

        int columns = Columns(frame);
        if (Beat.Hit)
        {
            _mark = true;
            int span = (_head - _lastBeat + columns) % columns;
            if (span >= 4) _beatSpan = span;
            _lastBeat = _head;
        }

        // a snare starts one copy of the last beat, long enough to lay it down at least once
        if (Beat.Snare && _copying <= 0)
        {
            _copyFrom = Math.Min(_beatSpan, columns / 3);
            _copying = Math.Max(_copyFrom / ColumnsPerSecond, .35f);
        }

        _copying = Math.Max(0, _copying - frame.Dt);

        // the tape only runs while there is something to record
        bool running = frame.Audio.Level > .015f;
        if (running) _owed = Math.Min(_owed + (frame.Dt * ColumnsPerSecond), 2);
        _step = _owed >= 1;
        if (_step)
        {
            _owed -= 1;
            _head = (_head + 1) % columns;
        }

        if (running) _clock = _copying > 0 ? Math.Max(0, _clock - (frame.Dt * .5f)) : _clock + frame.Dt;
    }

    private static int Columns(GpuFrame frame)
    {
        int cell = Math.Max(3, (int)((3 * frame.PixelsPerDip) + .5f));
        return Math.Max(8, (int)((frame.Width * .5f) - (28 * frame.PixelsPerDip)) / cell);
    }

    protected override void Fill(GpuFrame frame)
    {
        var d = Data;
        var audio = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = frame.PixelsPerDip;
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = frame.Time;
        d[5] = Math.Clamp(frame.Dt, .001f, .1f);
        d[6] = Beat.Pulse;
        d[7] = Beat.Swell;
        d[8] = _snare;
        d[9] = audio.Level;
        d[10] = audio.Bass;
        d[11] = audio.Mid;
        d[12] = audio.Treble;
        d[13] = frame.Balance;
        d[14] = _step && _mark ? 1 : 0;
        d[15] = _step ? 1 : 0;
        if (_step) _mark = false;
        PutBands(16, frame);
        Put(40, Lead(Yellow));
        Put(44, Second(Red));
        Put(48, Cold);
        Put(52, Paper);
        Put(56, PanelInk);
        d[60] = _head;
        d[61] = _copying > 0 ? _copyFrom : 0;
        d[62] = _clock;
        d[63] = Columns(frame);
    }

    private const string Shared = """
        cbuffer Braindance : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Dt;
            float Pulse;
            float Swell;
            float Snare;
            float Level;
            float Bass;
            float Mid;
            float Treble;
            float Balance;
            float Mark;
            float Stepping;
            float4 Bands[6];
            float4 LeadColor;
            float4 SecondColor;
            float4 ColdColor;
            float4 PaperColor;
            float4 InkColor;
            float4 Tape;
        };

        """ + ShaderHelpers + """

        // the strip is mirrored around the dock: 0 sits at the outer screen edge, the dock boundary is WingWidth()
        float BdWingWidth()
        {
            return Size.x * .5 / Dip;
        }

        float BdLocalX(float2 p)
        {
            float halfWidth = Size.x / Dip;
            float sx = p.x / Dip;
            return min(sx, halfWidth - sx);
        }

        static const int TapeRows = 16;

        float MarginX() { return floor((14 * Dip) + .5); }
        float MarginY() { return floor(2 * Dip); }
        int TapeCellW() { return max(3, (int)((3 * Dip) + .5)); }
        int TapeCellH() { return max(2, (int)((Size.y - MarginY() - (15 * Dip)) / TapeRows)); }
        int WingSide(float2 p) { return p.x < Size.x * .5 ? 0 : 1; }
        float2 WingPixel(float2 p) { return float2(p.x < Size.x * .5 ? p.x : Size.x - p.x, p.y); }

        bool TapeCellOf(float2 d, out int2 c)
        {
            c = (int2)floor((d - float2(MarginX(), MarginY())) / float2(TapeCellW(), TapeCellH()));
            return c.x >= 0 && c.x < (int)Tape.w && c.y >= 0 && c.y < TapeRows;
        }

        float4 TapeAt(int2 c, int side)
        {
            float2 m = float2(MarginX(), MarginY()) + ((float2(c) + .5) * float2(TapeCellW(), TapeCellH()));
            return Last.Load(int3((int)(side == 0 ? m.x : Size.x - m.x), (int)m.y, 0));
        }

        float BdLine(float v, float at, float halfw)
        {
            return step(abs(v - at), halfw);
        }

        // the analyser is mirrored too: the low end sits by the dock, the top end at the outer edge
        float BdSpec(float x)
        {
            float u = saturate((x - 16) / max(BdWingWidth() - 32, 1));
            return Band(1 - u);
        }

        float4 BdPut(float4 acc, float3 col, float a)
        {
            a = saturate(a);
            return float4((col * a) + (acc.rgb * (1 - a)), a + (acc.a * (1 - a)));
        }

        float BdPlate(float2 q, float x0, float y0, float x1, float y1, float c)
        {
            float dx = min(q.x - x0, x1 - q.x);
            float dy = min(q.y - y0, y1 - q.y);
            return step(0, dx) * step(0, dy) * step(c, dx + dy);
        }

        // 5x7 bitmap glyphs: 0..9 and 10 = B, 11 = D, packed as four rows in .x and three in .y
        int2 BdBits(float d)
        {
            if (d < .5) return int2(575022, 14897);
            if (d < 1.5) return int2(135556, 14468);
            if (d < 2.5) return int2(67118, 32004);
            if (d < 3.5) return int2(69727, 14881);
            if (d < 4.5) return int2(600258, 2143);
            if (d < 5.5) return int2(64031, 14881);
            if (d < 6.5) return int2(999686, 14897);
            if (d < 7.5) return int2(133183, 8456);
            if (d < 8.5) return int2(476718, 14897);
            if (d < 9.5) return int2(509486, 12353);
            if (d < 10.5) return int2(1001022, 31281);
            return int2(575038, 31281);
        }

        float BdGlyph(float2 q, float d)
        {
            if (q.x < 0 || q.x > 5 || q.y < 0 || q.y > 7) return 0;
            int c = (int)floor(q.x);
            int r = (int)floor(q.y);
            int2 bits = BdBits(d);
            int column = 4 - c;
            int v = r < 4 ? (bits.x >> ((r * 5) + column)) : (bits.y >> (((r - 4) * 5) + column));
            return (float)(v & 1);
        }

        float BdColon(float2 q)
        {
            float cx = step(0, q.x) * step(q.x, 1);
            float dots = step(abs(q.y - 2.5), .5) + step(abs(q.y - 4.5), .5);
            return cx * step(.5, dots);
        }

        float BdDigitOf(int i, float mm, float ss, float ff)
        {
            if (i == 0) return floor(mm / 10);
            if (i == 1) return fmod(mm, 10);
            if (i == 2) return floor(ss / 10);
            if (i == 3) return fmod(ss, 10);
            if (i == 4) return floor(ff / 10);
            return fmod(ff, 10);
        }

        """;

    private const string FeedbackPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            if (Stepping < .5) return Last.Load(int3((int2)p, 0));

            int side = WingSide(p);
            int2 c;
            if (!TapeCellOf(WingPixel(p), c)) return 0;

            // each row keeps its own slow average, so what lands on the tape is the song's movement, not its loudness
            float4 here = TapeAt(c, side);
            float live = Band((float)(TapeRows - 1 - c.y) / (TapeRows - 1));
            float usual = lerp(here.a, live, .04);

            int columns = (int)Tape.w;
            int head = (int)Tape.x;
            int ahead = (c.x - head + columns) % columns;

            // the head wipes a short stretch in front of itself, so the place it is writing can be seen
            if (ahead > 0 && ahead <= 6) return float4(0, 0, 0, usual);
            if (ahead != 0) return float4(here.rgb, usual);

            int copyFrom = (int)Tape.y;
            if (copyFrom > 0)
            {
                float4 source = TapeAt(int2((head - copyFrom + columns) % columns, c.y), side);
                float lost = Hash(float2(c) + float2(Time * 37, source.g * 3.1));
                float kept = lost < .04 + (.07 * source.g) ? 0 : source.r * .94;
                return float4(kept, source.g + 1, source.b, usual);
            }

            float written = saturate((live - (usual * .8)) * 3.6);
            return float4(written, 0, Mark, usual);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float4 acc = float4(0, 0, 0, 0);
            float2 pb = float2(p.x, Size.y - p.y);
            float2 d = WingPixel(p);
            int side = WingSide(p);
            float x = BdLocalX(p);
            float yd = pb.y / Dip;
            float wingEnd = MarginX() + (Tape.w * TapeCellW());
            int head = (int)Tape.x;
            int copyFrom = (int)Tape.y;
            float headX = MarginX() + ((head + .5) * TapeCellW());
            float sourceX = MarginX() + ((((head - copyFrom + (int)Tape.w) % (int)Tape.w) + .5) * TapeCellW());
            float tapeBottom = MarginY() + (TapeRows * TapeCellH());

            int2 c;
            if (TapeCellOf(d, c))
            {
                float4 cell = TapeAt(c, side);
                float2 at = d - float2(MarginX(), MarginY()) - (float2(c) * float2(TapeCellW(), TapeCellH()));
                float layer = (float)c.y / TapeRows;
                float3 ink = layer < .34 ? ColdColor.rgb : (layer < .68 ? LeadColor.rgb : SecondColor.rgb);
                float level = ceil(cell.r * 4) / 4;

                // a copied stretch reads as a copy: paler, and with its rows starting to fall out
                float pale = saturate(cell.g * .22);
                ink = lerp(ink, PaperColor.rgb, pale);
                bool gap = at.x >= TapeCellW() - 1 || at.y >= TapeCellH() - 1;
                bool dropped = cell.g > .5 && ((c.y + (int)cell.g) & 3) == 0;
                if (!gap && !dropped && cell.r > .08) acc = BdPut(acc, ink, .2 + (.8 * level));
                else if (!gap && c.x % 4 == 0 && c.y % 4 == 0) acc = BdPut(acc, ColdColor.rgb, .1);
            }

            // the rail under the tape, lit behind the head, with the beats it has marked
            float railY = tapeBottom + (6 * Dip);
            bool onRail = d.x >= MarginX() && d.x < wingEnd;
            if (onRail && abs(p.y - railY) <= .5 * Dip)
            {
                acc = BdPut(acc, d.x < headX ? LeadColor.rgb : PaperColor.rgb, d.x < headX ? .9 : .3);
            }

            int2 railCell = int2((int)((d.x - MarginX()) / TapeCellW()), 0);
            if (onRail && p.y >= railY && p.y < railY + (5 * Dip) && TapeAt(railCell, side).b > .5)
            {
                acc = BdPut(acc, PaperColor.rgb, .95);
            }

            // while it copies, the stretch being copied is framed on the tape and joined on the rail
            if (copyFrom > 0)
            {
                bool span = head >= copyFrom ? d.x >= sourceX && d.x <= headX : d.x >= sourceX || d.x <= headX;
                if (onRail && span && p.y <= railY && p.y > railY - (3 * Dip)) acc = BdPut(acc, ColdColor.rgb, 1);
                if (abs(d.x - sourceX) <= Dip && p.y < railY) acc = BdPut(acc, ColdColor.rgb, 1);
            }

            // the head, in the sharpest paper white on the strip
            if (abs(d.x - headX) <= Dip && p.y < railY + (5 * Dip))
            {
                acc = BdPut(acc, copyFrom > 0 ? SecondColor.rgb : PaperColor.rgb, 1);
            }

            if (abs(d.x - headX) <= railY + (6 * Dip) - p.y && p.y >= railY && p.y <= railY + (6 * Dip))
            {
                acc = BdPut(acc, LeadColor.rgb, 1);
            }

            // the timecode plate, on top of everything
            float plateX0 = 12;
            float plateX1 = 176;
            float plateY0 = 46;
            float plateY1 = 75;
            float2 q = float2(x, yd);
            float plate = BdPlate(q, plateX0, plateY0, plateX1, plateY1, 8);
            float plateIn = BdPlate(q, plateX0 + 1.4, plateY0 + 1.4, plateX1 - 1.4, plateY1 - 1.4, 10.8);
            float3 borderColor = lerp(PaperColor.rgb, SecondColor.rgb, step(.30, Snare));
            acc = BdPut(acc, lerp(InkColor.rgb, float3(0, 0, 0), .30), plate * .85);
            acc = BdPut(acc, borderColor, plate * (1 - plateIn) * .45);

            float sideRight = step(.5, p.x / Size.x);
            float textX = sideRight > .5 ? (plateX1 - x) : (x - plateX0);
            float cellSize = 4 / Dip;
            float qy = (70.2 - yd) / cellSize;
            float textOn = step(0, qy) * step(qy, 7) * plate;

            // the record lamp
            float blink = .40 + (.60 * step(.40, frac((Time * 1.5) + .13)));
            float recordLamp = step(6, textX) * step(textX, 11.4) * step(58.2, yd) * step(yd, 63.6) * plate;
            acc = BdPut(acc, SecondColor.rgb, recordLamp * blink);

            // the BD label
            float qbd = (textX - 16) / cellSize;
            float label = max(BdGlyph(float2(qbd, qy), 10), BdGlyph(float2(qbd - 6, qy), 11));
            acc = BdPut(acc, PaperColor.rgb, label * textOn * .85);

            // the timecode follows the tape, so it jumps back while a stretch is being copied
            float qtx = (textX - 54) / cellSize;
            float frames = floor(Tape.z * 30);
            frames = max(frames, 0);
            float ff = fmod(frames, 30);
            float secs = floor(frames / 30);
            float ss = fmod(secs, 60);
            float mm = fmod(floor(secs / 60), 100);
            float glyph = 0;
            for (int i = 0; i < 6; i++)
            {
                float slot = (i < 2) ? ((float)i * 6) : ((i < 4) ? (13 + ((float)(i - 2) * 6)) : (26 + ((float)(i - 4) * 6)));
                glyph = max(glyph, BdGlyph(float2(qtx - slot, qy), BdDigitOf(i, mm, ss, ff)));
            }

            glyph = max(glyph, BdColon(float2(qtx - 12.5, qy)));
            glyph = max(glyph, BdColon(float2(qtx - 25.5, qy)));
            float3 textColor = lerp(PaperColor.rgb, LeadColor.rgb, step(.28, Snare));
            acc = BdPut(acc, textColor, glyph * textOn * .95);

            float alpha = saturate(acc.a) * Fade;
            return float4(acc.rgb / max(acc.a, 1e-4), 1) * alpha;
        }
        """;
}
