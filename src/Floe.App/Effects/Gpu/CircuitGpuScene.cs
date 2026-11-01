using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A live circuit, not a picture of one. Current is an excitable medium: a pulse crawls along the traces, forks where they
/// fork and cannot run back the way it came. Every pulse leaves heat behind, heat soaks into the board, and a trace that
/// overloads burns out, throws sparks across the gap and stays charred for a while, so the song changes where current can
/// go. The hottest tiles rewire themselves, and the relic mark is laid into the left wing as traces that never burn out. The
/// state is kept per cell in the feedback texture: heat, charge, burn, wiring.
/// </summary>
public sealed class CircuitGpuScene : FeedbackGpuScene
{
    private const int ConstantFloats = 48;
    private static readonly Color Ember = Color.FromRgb(0xFF, 0x12, 0x30);
    private static readonly Color Steel = Color.FromRgb(0x8E, 0x94, 0xA0);

    private float _owed;
    private bool _step, _surge;
    private int _tick;

    public CircuitGpuScene() : base(ConstantFloats, Shared + FeedbackPs, Shared + ShowPs)
    {
    }

    protected override void Advance(GpuFrame frame)
    {
        // the medium moves one cell a step, so its speed must not follow the refresh rate
        _owed = Math.Min(_owed + (frame.Dt * 60), 2);
        _step = _owed >= 1;
        if (_step) _owed -= 1;
        _surge |= Beat.Hit;
        if (_step) _tick = (_tick + 1) % 8192;
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
        d[7] = frame.Audio.Level;
        if (_step) _surge = false;
        PutBands(16, frame);
        Put(40, Lead(Ember));
        Put(44, Steel);
    }

    private const string Shared = """
        cbuffer Circuit : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Tick;
            float Stepping;
            float Surge;
            float Level;
            float4 Spare;
            float4 Spare2;
            float4 Bands[6];
            float4 Hot;
            float4 Cold;
        };

        """ + ShaderHelpers + """

        static const int Tile = 12;
        static const int Lane = 3;

        int CellSize() { return max(2, (int)round(1.5 * Dip)); }

        int2 CellCentre(int2 cell)
        {
            int size = CellSize();
            return (cell * size) + int2(size / 2, size / 2);
        }

        // the relic mark's one line, in the logo's own pixels: on the board it is a ribbon of traces like any other
        static const float2 RelicLine[15] =
        {
            float2(39, 345), float2(39, 541), float2(268, 541), float2(186, 425), float2(281, 328), float2(281, 223),
            float2(39, 223), float2(39, 49), float2(491, 49), float2(491, 541), float2(420, 541), float2(305, 403),
            float2(405, 290), float2(405, 134), float2(110, 134),
        };

        static const float RelicHigh = 590;
        static const float RelicWide = 535;

        float RelicScale() { return Size.y * .6 / RelicHigh; }

        // how far a cell is from the mark's line, or below zero when it is outside the mark
        float RelicDistance(int2 cell)
        {
            float2 p = float2(CellCentre(cell));
            float2 local = (p - float2(Size.x * .05, Size.y * .2)) / RelicScale();
            if (local.x < -70 || local.x > RelicWide + 70 || local.y < -70 || local.y > RelicHigh + 70) return -1;
            float best = 1e6;
            for (int i = 0; i < 14; i++)
            {
                float2 a = RelicLine[i], b = RelicLine[i + 1] - a;
                float2 d = local - a - (b * saturate(dot(local - a, b) / dot(b, b)));
                best = min(best, dot(d, d));
            }

            return sqrt(best);
        }

        // each tile carries a ribbon of three traces; because their ends meet on the tile edges, the ribbons join up
        // into one winding board, and changing a tile's era reroutes everything that passes through it
        float Wire(int2 cell, float era)
        {
            float relic = RelicDistance(cell);
            float pitch = CellSize() / RelicScale();
            if (relic >= 0 && relic < pitch * 3.75) return abs(relic - (pitch * 1.25)) < pitch * .55 ? 2 : 0;

            int2 tile = cell / Tile;
            int2 at = cell - (tile * Tile);
            int u = at.x, v = at.y, c = Tile / 2;
            float pick = Hash(float2(tile) + float2(era * 7.3, era * 3.1));
            float shape = frac(pick * 11.7);
            if (shape < .46 && u == v) return 1;
            if (shape >= .46 && shape < .92 && u + v == Tile) return 1;
            for (int k = -Lane; k <= Lane; k += Lane)
            {
                if (shape < .46 && (u - v == c + k || v - u == c + k)) return 1;
                if (shape >= .46 && shape < .92 && (u + v == c + k || u + v == Tile + c + k)) return 1;
                if (shape >= .92 && shape < .96 && v == c + k) return 1;
                if (shape >= .96 && u == c + k) return 1;
            }

            return 0;
        }

        float Era(int2 cell)
        {
            int2 tile = cell / Tile;
            return Last.Load(int3(CellCentre((tile * Tile) + int2(Tile / 2, Tile / 2)), 0)).a;
        }

        float Energy(float x)
        {
            return saturate(Band(abs((x / Size.x) - .5) * 1.8) * 1.3);
        }

        """;

    private const string FeedbackPs = """

        static const int2 Around[16] =
        {
            int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1), int2(-1, -1), int2(1, -1), int2(-1, 1), int2(1, 1),
            int2(-2, 0), int2(2, 0), int2(0, -2), int2(0, 2), int2(-2, -2), int2(2, -2), int2(-2, 2), int2(2, 2),
        };

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 pixel = (int2)position.xy;
            if (Stepping < .5) return Last.Load(int3(pixel, 0));

            int2 cell = pixel / CellSize();
            int2 at = CellCentre(cell);
            float4 s = Last.Load(int3(at, 0));
            float heat = s.r, charge = s.g, burn = s.b, era = s.a;
            float wiring = Era(cell);
            float wire = Wire(cell, wiring) * step(burn, .3);

            int heads = 0;
            float warm = 0, hottest = 0;
            for (int i = 0; i < 16; i++)
            {
                float4 n = Last.Load(int3(CellCentre(cell + Around[i]), 0));
                if (n.g > .9) heads++;
                if (i < 4) warm += n.r;
                hottest = max(hottest, n.r);
            }

            float dice = Hash(float2(cell) + float2(Tick * .71, Tick * .37));
            float x = at.x;
            float e = Energy(x);
            bool head = false;

            if (wire > .5)
            {
                if (charge > 0) charge = max(0, charge - .1);
                else if (heads > 0) head = true;
                else if (dice < (.00014 + (.0036 * e * e * e)) * (.45 + Level)) head = true;

                // a beat blows a few spots of the board at once, loudest where the music is, and runs out from each
                float2 spot = floor(position.xy / (54 * Dip));
                bool seed = length(position.xy - ((spot + .5) * 54 * Dip)) < 5 * Dip;
                if (Surge > .5 && seed && Hash(spot + float2(Tick * .13, 5)) < .03 + (.07 * e)) head = true;
            }
            if (wire > 1.5 && charge <= 0 && dice < (.002 + (.03 * Surge)) * (.5 + Level)) head = true;

            if (wire < .5)
            {
                charge = max(0, charge - .3);

                // a trace at its limit arcs over to whatever is near
                if (hottest > .92 && dice < .03) head = true;
            }

            if (head)
            {
                charge = 1;
                heat += .25 * saturate(wire);
            }

            heat = lerp(heat, warm * .25, .08) * (.989 + (.004 * Level));

            if (wire > .5 && wire < 1.5 && heat > 1)
            {
                burn = 1;
                heat = 1.2;
                charge = 1;
            }

            burn *= .9975;

            // a tile that keeps running hot gets its wiring torn up: only its centre cell holds the era
            int2 middle = (cell / Tile) * Tile + int2(Tile / 2, Tile / 2);
            if (all(cell == middle) && heat > .55 && dice < .035) era += 1;

            return float4(min(heat, 1.3), charge, burn, era);
        }
        """;

    private const string ShowPs = """

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 pixel = (int2)position.xy;
            int2 cell = pixel / CellSize();
            float4 s = Last.Load(int3(CellCentre(cell), 0));
            float heat = s.r, charge = s.g, burn = s.b;
            float wire = Wire(cell, Era(cell));
            float3 color = 0;
            float alpha = 0;

            // the bare board is a hard checker that never goes black, and brightens where the heat has soaked in
            if (wire < .5 && ((cell.x + cell.y) & 1) == 0)
            {
                color = Hot.rgb * (heat > .3 ? .6 : (heat > .16 ? .32 : (heat > .07 ? .2 : .11)));
                alpha = 1;
            }

            if (wire > .5)
            {
                float t = ceil(saturate(heat) * 7) / 7;
                color = t < .2 ? (wire > 1.5 ? Hot.rgb * .62 : (Hot.rgb * .3) + (Cold.rgb * .1))
                    : t < .45 ? Hot.rgb * .5
                    : t < .75 ? Hot.rgb
                    : t < .95 ? float3(1, .52, .16)
                    : float3(1, .94, .78);
                alpha = 1;
            }

            if (wire > .5 && burn > .3)
            {
                float ember = Hash(float2(cell) + floor(Tick / 5));
                color = ember < burn * .4 ? Hot.rgb * .8 : float3(.03, .025, .03);
            }
            else if (charge > .9)
            {
                color = 1;
                alpha = 1;
            }
            else if (charge > .1)
            {
                color = lerp(color, wire > .5 ? float3(.8, .84, .9) : Hot.rgb, wire > .5 ? charge : 1);
                alpha = 1;
            }

            return float4(color, 1) * alpha * Fade;
        }
        """;
}
