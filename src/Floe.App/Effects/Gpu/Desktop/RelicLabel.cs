using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Floe.App.Effects.Gpu.Desktop;

/// <summary>
/// The label on the Relic chip, as the concept art prints it between the cell bands: Arasaka's emblem and wordmark,
/// taken from the logo's own outline (Assets/Relic/arasaka.svg) and filled with a fine grid of dots, and under it
/// the chip's status line in a small monospaced dot face. Laid out once as points in chip space: x, y, weight,
/// dot size.
/// </summary>
internal static partial class RelicLabel
{
    // where the logo sits on the chip and how tall it is
    private const float Left = .2f, Base = .07f, LogoHeight = .17f;
    private const float Grid = .0042f;          // world units between the dots that fill the logo
    private const float MarkSize = 6.5f, MarkWeight = .55f;

    // the status line: dot pitch, dot size and weight
    private const float Pitch = .0068f, TextSize = 5.5f, TextWeight = .9f;

    public static float[] Build()
    {
        var points = new List<float>();
        float bottom = Base;
        try
        {
            bottom = Logo(points);
        }
        catch (Exception)
        {
            // without the logo the chip still carries its status line
        }

        Text(points, "USER ID 90887 LENS DRVR 3.0.03", Left + .02f, bottom - .03f);
        Text(points, "STATUS: ONLINE  SERIAL>34 73 32", Left + .02f, bottom - .03f - (Pitch * 9f));
        return points.ToArray();
    }

    // fills the logo's outline with dots; returns the y of its foot
    private static float Logo(List<float> points)
    {
        using var stream = typeof(RelicLabel).Assembly.GetManifestResourceStream("Floe.Relic.arasaka.svg");
        if (stream is null) return Base;
        string svg = new StreamReader(stream).ReadToEnd();

        // potrace's outlines: absolute paths in a y-up frame, each a closed ring of lines and cubics
        var rings = new List<List<(float X, float Y)>>();
        foreach (Match m in PathData().Matches(svg)) Parse(m.Groups[1].Value, rings);
        if (rings.Count == 0) return Base;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var ring in rings)
        {
            foreach (var (x, y) in ring)
            {
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        }

        float k = LogoHeight / (maxY - minY);
        var crossings = new List<float>();
        for (float wy = 0; wy <= LogoHeight; wy += Grid)
        {
            // each row of the grid: where it crosses the outline; even-odd fills between the crossings
            float y = minY + (wy / k);
            crossings.Clear();
            foreach (var ring in rings)
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Count];
                    if ((a.Y <= y) == (b.Y <= y)) continue;
                    crossings.Add(a.X + ((y - a.Y) / (b.Y - a.Y) * (b.X - a.X)));
                }
            }

            crossings.Sort();
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                float from = (crossings[i] - minX) * k, to = (crossings[i + 1] - minX) * k;
                for (float wx = MathF.Ceiling(from / Grid) * Grid; wx <= to; wx += Grid)
                {
                    Add(points, Left + wx, Base + wy, MarkWeight, MarkSize);
                }
            }
        }

        return Base;
    }

    // reads one path's d attribute (M, L, C, Z in either case) into closed rings, curves flattened
    private static void Parse(string d, List<List<(float X, float Y)>> rings)
    {
        var tokens = Token().Matches(d);
        List<(float X, float Y)>? ring = null;
        float cx = 0, cy = 0;
        char cmd = 'M';
        int t = 0;
        float Next() => float.Parse(tokens[t++].Value, CultureInfo.InvariantCulture);
        while (t < tokens.Count)
        {
            string tok = tokens[t].Value;
            if (char.IsLetter(tok[0]))
            {
                cmd = tok[0];
                t++;
                if (cmd is 'z' or 'Z')
                {
                    if (ring is { Count: > 2 }) rings.Add(ring);
                    ring = null;
                }

                continue;
            }

            bool rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    float x = Next(), y = Next();
                    if (rel) { x += cx; y += cy; }
                    if (ring is { Count: > 2 }) rings.Add(ring);
                    ring = new List<(float, float)> { (x, y) };
                    cx = x; cy = y;
                    // further pairs after a move are lines
                    cmd = rel ? 'l' : 'L';
                    break;
                }

                case 'L':
                {
                    float x = Next(), y = Next();
                    if (rel) { x += cx; y += cy; }
                    ring?.Add((x, y));
                    cx = x; cy = y;
                    break;
                }

                case 'C':
                {
                    float x1 = Next(), y1 = Next(), x2 = Next(), y2 = Next(), x = Next(), y = Next();
                    if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                    for (int s = 1; s <= 8; s++)
                    {
                        float u = s / 8f, v = 1 - u;
                        ring?.Add(((v * v * v * cx) + (3 * v * v * u * x1) + (3 * v * u * u * x2) + (u * u * u * x),
                                   (v * v * v * cy) + (3 * v * v * u * y1) + (3 * v * u * u * y2) + (u * u * u * y)));
                    }

                    cx = x; cy = y;
                    break;
                }

                default:
                    t++;
                    break;
            }
        }

        if (ring is { Count: > 2 }) rings.Add(ring);
    }

    [GeneratedRegex("<path[^>]*\\sd=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex PathData();

    [GeneratedRegex("[A-Za-z]|-?\\d*\\.?\\d+(?:[eE][-+]?\\d+)?")]
    private static partial Regex Token();

    private static void Text(List<float> pts, string text, float x, float top)
    {
        foreach (char c in text)
        {
            if (Font.TryGetValue(c, out var rows))
            {
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (rows[row][col] == '#') Add(pts, x + (col * Pitch), top - (row * Pitch), TextWeight, TextSize);
                    }
                }
            }

            x += Pitch * 6;
        }
    }

    private static void Add(List<float> pts, float x, float y, float weight, float size)
    {
        pts.Add(x);
        pts.Add(y);
        pts.Add(weight);
        pts.Add(size);
    }

    // a small 5x7 monospaced face for the status line; anything missing prints as a space
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
        ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
        ['I'] = new[] { " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
        ['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
        ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
        ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
        ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { "#### ", "    #", "    #", " ### ", "    #", "    #", "#### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  " },
        [':'] = new[] { "     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     " },
        ['>'] = new[] { "#    ", " #   ", "  #  ", "   # ", "  #  ", " #   ", "#    " },
    };
}
