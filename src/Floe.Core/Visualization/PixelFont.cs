using System.Globalization;
using System.Text;

namespace Floe.Core.Visualization;

/// <summary>
/// A compact 5x7 bitmap font for the monochrome OLED cell grid. Each glyph row is a byte with bit 4 as
/// the leftmost column, so the literals below read like the pixels they draw. The table is built once;
/// Draw only looks glyphs up, so steady text does not allocate.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7, Advance = 6;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Visible to tests through reflection: keeping the public surface to the two drawing calls.
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['A'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
        ['B'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110 },
        ['C'] = new byte[] { 0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110 },
        ['D'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110 },
        ['E'] = new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111 },
        ['F'] = new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000 },
        ['G'] = new byte[] { 0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111 },
        ['H'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
        ['I'] = new byte[] { 0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
        ['J'] = new byte[] { 0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100 },
        ['K'] = new byte[] { 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001 },
        ['L'] = new byte[] { 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111 },
        ['M'] = new byte[] { 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001 },
        ['N'] = new byte[] { 0b10001, 0b11001, 0b10101, 0b10101, 0b10011, 0b10001, 0b10001 },
        ['O'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
        ['P'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000 },
        ['Q'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101 },
        ['R'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001 },
        ['S'] = new byte[] { 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110 },
        ['T'] = new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 },
        ['U'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
        ['V'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 },
        ['W'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b11011, 0b10001 },
        ['X'] = new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001 },
        ['Y'] = new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100 },
        ['Z'] = new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111 },

        ['0'] = new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 },
        ['1'] = new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
        ['2'] = new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
        ['3'] = new byte[] { 0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110 },
        ['4'] = new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
        ['5'] = new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 },
        ['6'] = new byte[] { 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
        ['7'] = new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 },
        ['8'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
        ['9'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100 },

        // Turkish letters: marks above sit on row 0 with a blank row under them, the body shrunk to the five
        // rows that end on the common baseline; cedillas hang in row 6 under a six-row body.
        ['Ç'] = new byte[] { 0b01110, 0b10001, 0b10000, 0b10000, 0b10001, 0b01110, 0b00100 },
        ['Ğ'] = new byte[] { 0b01110, 0b00000, 0b01111, 0b10000, 0b10011, 0b10001, 0b01111 },
        ['İ'] = new byte[] { 0b00100, 0b00000, 0b01110, 0b00100, 0b00100, 0b00100, 0b01110 },
        ['Ö'] = new byte[] { 0b01010, 0b00000, 0b01110, 0b10001, 0b10001, 0b10001, 0b01110 },
        ['Ş'] = new byte[] { 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b11110, 0b00100 },
        ['Ü'] = new byte[] { 0b01010, 0b00000, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },

        [' '] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000 },
        ['.'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100 },
        [','] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100, 0b01000 },
        [':'] = new byte[] { 0b00000, 0b00000, 0b00100, 0b00000, 0b00000, 0b00100, 0b00000 },
        [';'] = new byte[] { 0b00000, 0b00000, 0b00100, 0b00000, 0b00000, 0b00100, 0b01000 },
        ['!'] = new byte[] { 0b00000, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100 },
        ['?'] = new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100 },
        ['\''] = new byte[] { 0b00100, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000 },
        ['"'] = new byte[] { 0b01010, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000 },
        ['-'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b01110, 0b00000, 0b00000, 0b00000 },
        ['_'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b11111 },
        ['/'] = new byte[] { 0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000 },
        ['\\'] = new byte[] { 0b10000, 0b01000, 0b01000, 0b00100, 0b00010, 0b00010, 0b00001 },
        ['('] = new byte[] { 0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010 },
        [')'] = new byte[] { 0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000 },
        ['['] = new byte[] { 0b01110, 0b01000, 0b01000, 0b01000, 0b01000, 0b01000, 0b01110 },
        [']'] = new byte[] { 0b01110, 0b00010, 0b00010, 0b00010, 0b00010, 0b00010, 0b01110 },
        ['&'] = new byte[] { 0b01100, 0b10010, 0b10100, 0b01000, 0b10101, 0b10010, 0b01101 },
        ['+'] = new byte[] { 0b00000, 0b00000, 0b00100, 0b11111, 0b00100, 0b00000, 0b00000 },
        ['='] = new byte[] { 0b00000, 0b00000, 0b11111, 0b00000, 0b11111, 0b00000, 0b00000 },
        ['#'] = new byte[] { 0b01010, 0b01010, 0b11111, 0b01010, 0b11111, 0b01010, 0b01010 },
        ['%'] = new byte[] { 0b11001, 0b11010, 0b00010, 0b00100, 0b01000, 0b01011, 0b10011 },
        ['*'] = new byte[] { 0b00000, 0b10101, 0b01110, 0b11111, 0b01110, 0b10101, 0b00000 },
        ['<'] = new byte[] { 0b00010, 0b00100, 0b01000, 0b10000, 0b01000, 0b00100, 0b00010 },
        ['>'] = new byte[] { 0b01000, 0b00100, 0b00010, 0b00001, 0b00010, 0b00100, 0b01000 },
        ['@'] = new byte[] { 0b01110, 0b10001, 0b10111, 0b10101, 0b10111, 0b10000, 0b01110 },
        ['♪'] = new byte[] { 0b00110, 0b00101, 0b00100, 0b00100, 0b00100, 0b01110, 0b01110 },
    };

    /// <summary>Width in cells of <paramref name="s"/> as Draw would draw it, without the trailing gap.</summary>
    public static int Measure(string s) => string.IsNullOrEmpty(s) ? 0 : (s.Length * Advance) - 1;

    /// <summary>
    /// Draws <paramref name="s"/> with its top-left at (x, y). Lit pixels raise the grid to at least
    /// <paramref name="value"/>; anything outside the grid or the clip columns is skipped. Returns Measure(s).
    /// </summary>
    public static int Draw(float[] grid, int cols, int rows, string s, int x, int y, float value, int clipLeft = int.MinValue, int clipRight = int.MaxValue)
    {
        int width = Measure(s);
        if (grid is null || string.IsNullOrEmpty(s) || cols <= 0 || rows <= 0) return width;
        // a lowercase i becomes a dotted İ only in text that is plainly Turkish, not in "Minor Empire"
        bool turkish = s.AsSpan().IndexOfAny("şğıŞĞİ") >= 0;
        for (int i = 0; i < s.Length; i++)
        {
            byte[] glyph = Resolve(s[i], turkish);
            int left = x + (i * Advance);
            for (int gy = 0; gy < GlyphHeight; gy++)
            {
                byte bits = glyph[gy];
                if (bits == 0) continue;
                int row = y + gy;
                if ((uint)row >= (uint)rows) continue;
                int rowStart = row * cols;
                for (int gx = 0; gx < GlyphWidth; gx++)
                {
                    if ((bits & (1 << (GlyphWidth - 1 - gx))) == 0) continue;
                    int col = left + gx;
                    if (col < clipLeft || col >= clipRight || (uint)col >= (uint)cols) continue;
                    int index = rowStart + col;
                    if ((uint)index >= (uint)grid.Length) continue;
                    grid[index] = MathF.Max(grid[index], value);
                }
            }
        }

        return width;
    }

    private static byte[] Resolve(char c, bool turkish)
    {
        if (Glyphs.TryGetValue(c, out byte[]? glyph)) return glyph;
        char upper = Uppercase(c, turkish);
        if (upper != c && Glyphs.TryGetValue(upper, out glyph)) return glyph;
        // Accents (é, á, ñ) draw as their base letter; only this uncommon path may allocate.
        if (TryBaseLetter(c, out char baseLetter) && Glyphs.TryGetValue(Uppercase(baseLetter, turkish), out glyph)) return glyph;
        return Glyphs['?'];
    }

    private static char Uppercase(char c, bool turkish) => c switch
    {
        >= 'a' and <= 'z' => c == 'i' && turkish ? 'İ' : (char)(c - 'a' + 'A'),
        'ı' => 'I',
        _ => char.ToUpper(c, Turkish),
    };

    private static bool TryBaseLetter(char c, out char baseLetter)
    {
        baseLetter = c;
        // ASCII and lone surrogates never decompose, so keep the hot path string-free.
        if (c < 0x80 || char.IsSurrogate(c)) return false;
        string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (char part in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(part) == UnicodeCategory.NonSpacingMark) continue;
            baseLetter = part;
            return true;
        }

        return false;
    }
}
