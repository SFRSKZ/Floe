using System.Reflection;
using Floe.Core.Visualization;

namespace Floe.Tests;

public sealed class PixelFontTests
{
    [Fact]
    public void MeasureCountsCellsWithoutTheTrailingGap()
    {
        Assert.Equal(0, PixelFont.Measure(""));
        Assert.Equal(5, PixelFont.Measure("A"));
        Assert.Equal(11, PixelFont.Measure("AB"));
    }

    [Fact]
    public void CapitalIDrawsExactlyItsSerifs()
    {
        var grid = new float[PixelFont.GlyphWidth * PixelFont.GlyphHeight];
        Assert.Equal(5, PixelFont.Draw(grid, PixelFont.GlyphWidth, PixelFont.GlyphHeight, "I", 0, 0, 1));
        int[] rows = { 0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 };
        for (int y = 0; y < PixelFont.GlyphHeight; y++)
        {
            for (int x = 0; x < PixelFont.GlyphWidth; x++)
            {
                bool expected = (rows[y] & (1 << (PixelFont.GlyphWidth - 1 - x))) != 0;
                Assert.Equal(expected, grid[(y * PixelFont.GlyphWidth) + x] > 0);
            }
        }
    }

    [Fact]
    public void ClippingAndOutOfGridWritesAreSkipped()
    {
        // Fully outside positions must not wrap into neighbouring rows or throw.
        var outside = new float[5 * 7];
        PixelFont.Draw(outside, 5, 7, "A", -5, 0, 1);
        PixelFont.Draw(outside, 5, 7, "A", 5, 0, 1);
        PixelFont.Draw(outside, 5, 7, "A", 0, 7, 1);
        Assert.All(outside, pixel => Assert.Equal(0, pixel));

        // A partially visible glyph is clipped, not rejected.
        var partial = new float[5 * 7];
        PixelFont.Draw(partial, 5, 7, "A", -2, -3, 1);
        Assert.True(partial[0] > 0);

        // Clip columns keep the neighbouring cells untouched.
        var grid = new float[5 * 7];
        PixelFont.Draw(grid, 5, 7, "A", 0, 0, 1, 2, 4);
        for (int y = 0; y < 7; y++)
        {
            Assert.Equal(0, grid[(y * 5) + 0]);
            Assert.Equal(0, grid[(y * 5) + 1]);
            Assert.Equal(0, grid[(y * 5) + 4]);
        }

        Assert.True(grid[(0 * 5) + 2] > 0);
        Assert.True(grid[(0 * 5) + 3] > 0);

        // A buffer smaller than cols * rows must be treated as an edge, not an exception.
        var small = new float[6];
        PixelFont.Draw(small, 3, 2, "AB", 0, 0, 1);
    }

    [Fact]
    public void LowercaseIDrawsAsDottedCapitalAndDotlessIAsCapitalI()
    {
        AssertSameGlyphs("şi", "Şİ");
        AssertSameGlyphs("i", "I");
        AssertSameGlyphs("ı", "I");
    }

    [Fact]
    public void AccentedLettersFallBackToTheirBase()
    {
        AssertSameGlyphs("é", "E");
        AssertSameGlyphs("á", "A");
        AssertSameGlyphs("ñ", "N");
    }

    [Fact]
    public void UnknownCharacterDrawsAsQuestionMark()
    {
        AssertSameGlyphs("☃", "?");
    }

    [Fact]
    public void EveryGlyphFitsTheCellAndTheRequiredSetExists()
    {
        var field = typeof(PixelFont).GetField("Glyphs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var glyphs = Assert.IsType<Dictionary<char, byte[]>>(field!.GetValue(null));
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, glyph =>
        {
            Assert.Equal(PixelFont.GlyphHeight, glyph.Value.Length);
            Assert.All(glyph.Value, row => Assert.True(row < 32, $"Glyph '{glyph.Key}' has a row wider than five columns"));
        });

        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ÇĞİÖŞÜ .,:;!?'\"-_/\\()[]&+=#%*<>@♪")
        {
            Assert.True(glyphs.ContainsKey(c), $"Missing glyph '{c}'");
        }
    }

    private static void AssertSameGlyphs(string a, string b)
    {
        int cols = Math.Max(PixelFont.Measure(a), PixelFont.Measure(b));
        var first = new float[cols * PixelFont.GlyphHeight];
        var second = new float[cols * PixelFont.GlyphHeight];
        PixelFont.Draw(first, cols, PixelFont.GlyphHeight, a, 0, 0, 1);
        PixelFont.Draw(second, cols, PixelFont.GlyphHeight, b, 0, 0, 1);
        Assert.Equal(second, first);
    }
}
