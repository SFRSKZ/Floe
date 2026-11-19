using System.Text;

namespace Floe.App.Effects.Gpu.Cp77;

/// <summary>
/// Asymmetric attack/release audio spectrum and band-level smoother with peak-hold tracking so CP77 visualizers
/// react smoothly to music dynamics without 60 Hz frame jitter.
/// </summary>
public sealed class Cp77AudioSmoother
{
    public float[] Bands { get; } = new float[24];
    public float[] Peaks { get; } = new float[24];
    public float Level { get; private set; }
    public float Bass { get; private set; }
    public float Mid { get; private set; }
    public float Treble { get; private set; }

    public void Update(GpuFrame frame, float attack = 14f, float release = 3.8f, float peakDecay = 0.55f)
    {
        float dt = Math.Clamp(frame.Dt, 0f, 0.1f);
        var audio = frame.Audio;

        float atk = 1f - MathF.Exp(-dt * attack);
        float rel = 1f - MathF.Exp(-dt * release);
        var raw = audio.Bands;
        int len = raw.Length;
        for (int i = 0; i < 24; i++)
        {
            float target = len > 0 ? Math.Clamp(raw[i * len / 24], 0f, 1f) : 0f;
            float k = target > Bands[i] ? atk : rel;
            Bands[i] += (target - Bands[i]) * k;
            Peaks[i] = Math.Clamp(Math.Max(Bands[i], Peaks[i] - (peakDecay * dt)), 0f, 1f);
        }

        float sAtk = 1f - MathF.Exp(-dt * 10f);
        float sRel = 1f - MathF.Exp(-dt * 3.2f);
        Level += (Math.Clamp(audio.Level, 0f, 1f) - Level) * (audio.Level > Level ? sAtk : sRel);
        Bass += (Math.Clamp(audio.Bass, 0f, 1f) - Bass) * (audio.Bass > Bass ? sAtk : sRel);
        Mid += (Math.Clamp(audio.Mid, 0f, 1f) - Mid) * (audio.Mid > Mid ? sAtk : sRel);
        Treble += (Math.Clamp(audio.Treble, 0f, 1f) - Treble) * (audio.Treble > Treble ? sAtk : sRel);
    }

    public void WriteBands(float[] data, int offset)
    {
        for (int i = 0; i < 24; i++) data[offset + i] = Bands[i];
    }

    public void WritePeaks(float[] data, int offset)
    {
        for (int i = 0; i < 24; i++) data[offset + i] = Peaks[i];
    }
}

/// <summary>
/// Shared HLSL library providing pixel-exact anti-aliased vector primitives (without derivative branch hazards)
/// and a crisp 5x7 bitpacked CP77 HUD font rendered in physical screen coordinates.
/// </summary>
internal static class Cp77ShaderLib
{
    public const string SdfPrimitives = """
        float SdfBox(float2 p, float2 b, float r)
        {
            float2 d = (abs(p) - b) + r;
            return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
        }

        float SdfSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
            return length((a + (ab * t)) - p);
        }
        """;

    private static readonly string[] FontRows =
    {
        "..... ..... ..... ..... ..... ..... .....", // 0: ' '
        ".###. #...# #...# ##### #...# #...# #...#", // 1: A
        "####. #...# #...# ####. #...# #...# ####.", // 2: B
        ".###. #...# #.... #.... #.... #...# .###.", // 3: C
        "####. #...# #...# #...# #...# #...# ####.", // 4: D
        "##### #.... #.... ####. #.... #.... #####", // 5: E
        "##### #.... #.... ####. #.... #.... #....", // 6: F
        ".###. #...# #.... #.### #...# #...# .####", // 7: G
        "#...# #...# #...# ##### #...# #...# #...#", // 8: H
        ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.", // 9: I
        "..### ...#. ...#. ...#. ...#. #..#. .##..", // 10: J
        "#...# #..#. #.#.. ##... #.#.. #..#. #...#", // 11: K
        "#.... #.... #.... #.... #.... #.... #####", // 12: L
        "#...# ##.## #.#.# #.#.# #...# #...# #...#", // 13: M
        "#...# ##..# #.#.# #..## #...# #...# #...#", // 14: N
        ".###. #...# #...# #...# #...# #...# .###.", // 15: O
        "####. #...# #...# ####. #.... #.... #....", // 16: P
        ".###. #...# #...# #...# #.#.# #..#. .##.#", // 17: Q
        "####. #...# #...# ####. #.#.. #..#. #...#", // 18: R
        ".#### #.... #.... .###. ....# ....# ####.", // 19: S
        "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..", // 20: T
        "#...# #...# #...# #...# #...# #...# .###.", // 21: U
        "#...# #...# #...# #...# #...# .#.#. ..#..", // 22: V
        "#...# #...# #...# #.#.# #.#.# #.#.# .#.#.", // 23: W
        "#...# #...# .#.#. ..#.. .#.#. #...# #...#", // 24: X
        "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..", // 25: Y
        "##### ....# ...#. ..#.. .#... #.... #####", // 26: Z
        ".###. #...# #..## #.#.# ##..# #...# .###.", // 27: 0
        "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.", // 28: 1
        ".###. #...# ....# ...#. ..#.. .#... #####", // 29: 2
        "##### ...#. ..#.. ...#. ....# #...# .###.", // 30: 3
        "...#. ..##. .#.#. #..#. ##### ...#. ...#.", // 31: 4
        "##### #.... ####. ....# ....# #...# .###.", // 32: 5
        "..##. .#... #.... ####. #...# #...# .###.", // 33: 6
        "##### ....# ...#. ..#.. .#... .#... .#...", // 34: 7
        ".###. #...# #...# .###. #...# #...# .###.", // 35: 8
        ".###. #...# #...# .#### ....# ...#. .##..", // 36: 9
        "..... ..... ..... ##### ..... ..... .....", // 37: -
        "....# ...#. ...#. ..#.. .#... .#... #....", // 38: /
        "..... ..... ..... ..... ..... .##.. .##..", // 39: .
        "..... .##.. .##.. ..... .##.. .##.. .....", // 40: :
        ".###. .#... .#... .#... .#... .#... .###.", // 41: [
        ".###. ...#. ...#. ...#. ...#. ...#. .###.", // 42: ]
    };

    public const string CoreHelpers = """
        float3 AcesToneClean(float3 x)
        {
            return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
        }

        float WingInfo(float u, out float wingU, out float wingEdge, out float side)
        {
            if (CrownMode > 0.5)
            {
                side = 1.0;
                wingU = u;
                wingEdge = smoothstep(0.0, 0.012, u) * smoothstep(1.0, 0.988, u);
                return 1.0;
            }
            float leftW = max(DockLeft, 0.001);
            float rightW = max(1.0 - DockRight, 0.001);
            if (u < DockLeft)
            {
                side = -1.0;
                wingU = saturate((DockLeft - u) / leftW);
                wingEdge = smoothstep(0.0, 0.018, wingU) * smoothstep(1.0, 0.96, wingU);
                return 1.0;
            }
            if (u > DockRight)
            {
                side = 1.0;
                wingU = saturate((u - DockRight) / rightW);
                wingEdge = smoothstep(0.0, 0.018, wingU) * smoothstep(1.0, 0.96, wingU);
                return 1.0;
            }
            side = 0.0;
            wingU = 0.0;
            wingEdge = 0.0;
            return 0.0;
        }

        void WingBoundsPx(float u, out float xMinPx, out float xMaxPx)
        {
            if (CrownMode > 0.5)
            {
                xMinPx = 0.0;
                xMaxPx = Size.x;
                return;
            }
            if (u < DockLeft)
            {
                xMinPx = 0.0;
                xMaxPx = DockLeft * Size.x;
                return;
            }
            xMinPx = DockRight * Size.x;
            xMaxPx = Size.x;
        }

        float HudFs(float dipScale)
        {
            return max(1.0, floor(dipScale + 0.5));
        }

        float StrokePx(float distPx, float widthPx)
        {
            float halfW = widthPx * 0.5;
            return 1.0 - smoothstep(max(0.0, halfW - 0.55), halfW + 0.55, abs(distPx));
        }

        float FillPx(float sdfPx)
        {
            return 1.0 - smoothstep(-0.6, 0.6, sdfPx);
        }

        float LineSegPx(float2 p, float2 a, float2 b, float widthPx)
        {
            return StrokePx(SdfSegment(p, a, b), widthPx);
        }

        float ChamferBoxSdf(float2 p, float2 b, float c)
        {
            float2 q = abs(p) - b;
            float bevel = (q.x + q.y + c) * 0.70710678;
            float box = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            return max(box, bevel);
        }

        float RectFramePx(float2 p, float2 mn, float2 mx, float thickPx)
        {
            float2 c = (mn + mx) * 0.5;
            float2 h = max((mx - mn) * 0.5, 1.0);
            float d = SdfBox(p - c, h, 0.0);
            return StrokePx(d, thickPx);
        }

        float CornerBracketsPx(float2 p, float2 mn, float2 mx, float armPx, float thickPx)
        {
            float2 c = (mn + mx) * 0.5;
            float2 h = max((mx - mn) * 0.5, 1.0);
            float d = SdfBox(p - c, h, 0.0);
            float2 a = abs(p - c);
            float inCorner = step(h.x - armPx, a.x) * step(h.y - armPx, a.y);
            return StrokePx(d, thickPx) * inCorner;
        }

        void TickRulerPx(float xPx, float stepPx, float majorEvery, out float minorTick, out float majorTick)
        {
            float idx = floor(xPx / stepPx + 0.5);
            float dist = abs(xPx - idx * stepPx);
            float modIdx = idx - majorEvery * floor(idx / majorEvery + 1e-4);
            float isMajor = step(abs(modIdx), 0.25);
            float linePx = StrokePx(dist, 1.0);
            minorTick = linePx * (1.0 - isMajor);
            majorTick = linePx * isMajor;
        }
        """;

    public static readonly string FontHlsl = BuildFontHlsl();

    private static string BuildFontHlsl()
    {
        var sb = new StringBuilder();
        sb.Append("static const uint2 Cp77Glyphs[").Append(FontRows.Length).Append("] =\n{\n");
        foreach (var row in FontRows)
        {
            ulong bits = 0;
            int at = 0;
            foreach (var ch in row.Replace(" ", string.Empty))
            {
                if (ch == '#') bits |= 1UL << at;
                at++;
            }
            sb.Append("    uint2(").Append((uint)bits).Append("u, ").Append((uint)(bits >> 32)).Append("u),\n");
        }
        sb.Append("};\n\n");
        sb.Append("""
            float Cp77GlyphCell(int id, int gx, int gy)
            {
                if (id <= 0 || gx < 0 || gx > 4 || gy < 0 || gy > 6) return 0.0;
                int bit = (gy * 5) + gx;
                uint2 glyph = Cp77Glyphs[id];
                uint v = (bit < 32 ? (glyph.x >> bit) : (glyph.y >> (bit - 32))) & 1u;
                return (float)v;
            }

            float Cp77Hex2(float2 sp, float2 at, int val, float fs)
            {
                float s = max(1.0, floor(fs + 0.5));
                float2 q = floor(sp - floor(at)) / s;
                if (q.x < 0.0 || q.y < 0.0 || q.x >= 11.0 || q.y >= 7.0) return 0.0;
                int x = (int)q.x;
                if (x == 5) return 0.0;
                int nib = x < 5 ? ((val >> 4) & 15) : (val & 15);
                int gx = x < 5 ? x : (x - 6);
                int id = nib < 10 ? (27 + nib) : (1 + (nib - 10));
                return Cp77GlyphCell(id, gx, (int)q.y);
            }

            """);
        return sb.ToString();
    }

    public static string LabelFn(string fnName, string text)
    {
        var sb = new StringBuilder();
        int cells = Math.Max(1, (text.Length * 6) - 1);
        sb.Append("float ").Append(fnName).Append("(float2 sp, float2 at, float fs)\n{\n");
        sb.Append("    float s = max(1.0, floor(fs + 0.5));\n");
        sb.Append("    float2 q = floor(sp - floor(at)) / s;\n");
        sb.Append("    if (q.x < 0.0 || q.y < 0.0 || q.x >= ").Append(cells).Append(".0 || q.y >= 7.0) return 0.0;\n");
        sb.Append("    int ci = (int)(q.x / 6.0);\n");
        sb.Append("    int gx = (int)q.x - (ci * 6);\n");
        sb.Append("    if (gx > 4) return 0.0;\n");
        sb.Append("    int gy = (int)q.y;\n");
        sb.Append("    int gid = 0;\n");
        for (int i = 0; i < text.Length; i++)
        {
            int idx = GlyphIndex(text[i]);
            if (idx != 0)
            {
                sb.Append("    if (ci == ").Append(i).Append(") gid = ").Append(idx).Append(";\n");
            }
        }
        sb.Append("    return Cp77GlyphCell(gid, gx, gy);\n}\n\n");
        return sb.ToString();
    }

    private static int GlyphIndex(char c) => c switch
    {
        ' ' => 0,
        >= 'A' and <= 'Z' => 1 + (c - 'A'),
        >= 'a' and <= 'z' => 1 + (c - 'a'),
        >= '0' and <= '9' => 27 + (c - '0'),
        '-' => 37,
        '/' => 38,
        '.' => 39,
        ':' => 40,
        '[' => 41,
        ']' => 42,
        _ => 0
    };
}
