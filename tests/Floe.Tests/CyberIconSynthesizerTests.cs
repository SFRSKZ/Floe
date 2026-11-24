using Floe.Core.Services;

namespace Floe.Tests;

public sealed class CyberIconSynthesizerTests
{
    private const int Size = CyberIconSynthesizer.LogoPixels;

    [Fact]
    public void MonochromeBlackGlyph_BecomesBrightCoolGrey()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 20; y < 108; y++)
        {
            for (int x = 20; x < 108; x++)
            {
                bool onPlus = Math.Abs(x - 64) <= 12 || Math.Abs(y - 64) <= 12;
                if (!onPlus) continue;
                SetPixel(src, w, x, y, r: 0, g: 0, b: 0, a: 255);
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.MonochromeGlyph, archetype);
        Assert.Equal(Size * Size * 4, output.Length);

        GetPixel(output, Size, Size / 2, Size / 2, out byte r, out byte g, out byte b, out byte a);
        Assert.InRange(a, 250, 255);
        Assert.InRange(r, 0xA8, 0xAE);
        Assert.InRange(g, 0xAF, 0xB5);
        Assert.InRange(b, 0xBD, 0xC3);
    }

    [Fact]
    public void SolidSquareTileWithInnerGlyph_StripsOuterBoxAndLiftsEmblem()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 4; y < 124; y++)
        {
            for (int x = 4; x < 124; x++)
            {
                // Rounded-square purple tile with a crisp centered white diamond glyph inside.
                if (!InsideRoundedRect(x, y, 4, 4, 123, 123, 14)) continue;
                int dx = Math.Abs(x - 64), dy = Math.Abs(y - 64);
                bool glyph = (dx + dy) <= 28;
                if (glyph)
                {
                    SetPixel(src, w, x, y, r: 245, g: 245, b: 250, a: 255);
                }
                else
                {
                    SetPixel(src, w, x, y, r: 95, g: 55, b: 190, a: 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.TileEmblemLift, archetype);

        // Tile corners must be stripped transparent; lifted inner emblem scales up to fill the center brightly.
        GetPixel(output, Size, 4, 4, out _, out _, out _, out byte cornerAlpha);
        Assert.True(cornerAlpha < 10, $"Expected stripped transparent corner, got alpha={cornerAlpha}");

        GetPixel(output, Size, Size / 2, Size / 2, out _, out _, out byte centerB, out byte centerAlpha);
        Assert.InRange(centerAlpha, 245, 255);
        Assert.True(centerB >= 175, $"Expected bright lifted emblem center, got B={centerB}");
    }

    [Fact]
    public void CircularBadgeWithCutoutGlyph_ClassifiesAsBadgeAndPreservesContrast()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dist = MathF.Sqrt(((x - 64f) * (x - 64f)) + ((y - 64f) * (y - 64f)));
                if (dist > 58f) continue;

                bool stripe1 = y is >= 40 and <= 48 && Math.Abs(x - 64) <= 28;
                bool stripe2 = y is >= 58 and <= 66 && Math.Abs(x - 64) <= 24;
                bool stripe3 = y is >= 76 and <= 84 && Math.Abs(x - 64) <= 20;
                if (stripe1 || stripe2 || stripe3)
                {
                    SetPixel(src, w, x, y, r: 20, g: 24, b: 20, a: 255);
                }
                else
                {
                    SetPixel(src, w, x, y, r: 30, g: 215, b: 96, a: 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.IconicBadgeCutout, archetype);

        // Outer green circle rim becomes opaque #ABB2C0 plate.
        GetPixel(output, Size, Size / 2, 14, out _, out _, out byte rimB, out byte rimAlpha);
        Assert.InRange(rimAlpha, 245, 255);
        Assert.True(rimB >= 175, $"Expected bright circular rim, got B={rimB}");

        // Inner single-tone bars punch out cleanly to transparent cutouts.
        GetPixel(output, Size, Size / 2, (int)MathF.Round(62f * Size / 128f), out _, out _, out _, out byte cutoutAlpha);
        Assert.True(cutoutAlpha < 40, $"Expected transparent cutout inside inner glyph bar, got alpha={cutoutAlpha}");
    }

    [Fact]
    public void MultiColorEqualLuminanceLogo_SeparatesTonesAndCarvesSeam()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x - 64f, dy = y - 64f;
                float dist = MathF.Sqrt((dx * dx) + (dy * dy));
                if (dist > 56f) continue;

                double angle = Math.Atan2(dy, dx);
                if (dist < 18f)
                {
                    // Central cyan core
                    SetPixel(src, w, x, y, r: 40, g: 165, b: 245, a: 255);
                }
                else if (angle < -Math.PI / 3.0)
                {
                    SetPixel(src, w, x, y, r: 235, g: 70, b: 60, a: 255);
                }
                else if (angle < Math.PI / 3.0)
                {
                    SetPixel(src, w, x, y, r: 245, g: 205, b: 40, a: 255);
                }
                else
                {
                    SetPixel(src, w, x, y, r: 45, g: 175, b: 85, a: 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.MultiColorVector, archetype);

        var tones = new HashSet<int>();
        foreach (var (nx, ny) in new[] { (53, 53), (78, 53), (36, 30), (36, 75) })
        {
            GetPixel(output, Size, nx, ny, out _, out _, out byte b, out byte a);
            Assert.True(a >= 220, $"Sector interior ({nx},{ny}) should remain opaque, got alpha={a}");
            tones.Add(b / 24);
        }

        Assert.True(tones.Count >= 2, $"Expected >= 2 distinct brightness levels across chromatic sectors, found {tones.Count}");

        // Right along the horizontal center seam (angle == PI/3 or x=64,y=64 boundary) check minimum alpha across boundary scan.
        byte minSeamAlpha = 255;
        for (int scanY = 20; scanY <= 40; scanY++)
        {
            GetPixel(output, Size, 53, scanY, out _, out _, out _, out byte a);
            if (a < minSeamAlpha) minSeamAlpha = a;
        }
        Assert.True(minSeamAlpha < 180, $"Expected carved seam drop near chromatic boundary, min alpha={minSeamAlpha}");
    }

    [Fact]
    public void DetailedGameCoverArt_PosterizesAndChamfersCorners()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // High-frequency textured artwork filling the entire frame with varied perimeter & interior edges.
                float wave1 = MathF.Sin((x * 0.45f) + (y * 0.25f));
                float wave2 = MathF.Cos((x * 0.20f) - (y * 0.55f));
                float radial = MathF.Sqrt(((x - 52f) * (x - 52f)) + ((y - 60f) * (y - 60f)));
                byte r = (byte)Math.Clamp((int)(128 + (95 * wave1) + (40 * MathF.Sin(radial * 0.4f))), 0, 255);
                byte g = (byte)Math.Clamp((int)(110 + (85 * wave2) + (35 * MathF.Cos(x * 0.35f))), 0, 255);
                byte b = (byte)Math.Clamp((int)(135 + (90 * wave1 * wave2)), 0, 255);
                SetPixel(src, w, x, y, r, g, b, 255);
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.DetailedArtwork, archetype);

        // Top-right and bottom-left corners are chamfered cleanly transparent.
        GetPixel(output, Size, Size - 3, 2, out _, out _, out _, out byte trAlpha);
        GetPixel(output, Size, 2, Size - 3, out _, out _, out _, out byte blAlpha);
        Assert.Equal(0, trAlpha);
        Assert.Equal(0, blAlpha);

        // Un-chamfered corners stay opaque.
        GetPixel(output, Size, 4, 4, out _, out _, out _, out byte tlAlpha);
        GetPixel(output, Size, Size - 5, Size - 5, out _, out _, out _, out byte brAlpha);
        Assert.True(tlAlpha >= 200, $"Expected opaque top-left corner, got alpha={tlAlpha}");
        Assert.True(brAlpha >= 200, $"Expected opaque bottom-right corner, got alpha={brAlpha}");

        // Interior colors stay bounded within the cool-grey palette [0..0xC0].
        for (int i = 0; i < output.Length; i += 4)
        {
            if (output[i + 3] == 0) continue;
            Assert.True(output[i] <= 0xC2);
            Assert.True(output[i + 1] <= output[i] + 2);
            Assert.True(output[i + 2] <= output[i + 1] + 2);
        }
    }

    [Fact]
    public void TransparentInput_DoesNotThrow_AndReturnsEmpty106x106()
    {
        var transparent = new byte[64 * 64 * 4];
        var out1 = CyberIconSynthesizer.Synthesize(transparent, 64, 64, out _);
        Assert.Equal(Size * Size * 4, out1.Length);
        Assert.All(out1, b => Assert.Equal(0, b));

        var outInvalid = CyberIconSynthesizer.Synthesize(Array.Empty<byte>(), 0, 0, out _);
        Assert.Equal(Size * Size * 4, outInvalid.Length);
        Assert.All(outInvalid, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ComputeCacheKey_IsDeterministicAndSensitiveToChanges()
    {
        var bufA = new byte[32 * 32 * 4];
        var bufB = new byte[32 * 32 * 4];
        for (int i = 0; i < bufA.Length; i++) bufA[i] = bufB[i] = (byte)(i * 17);

        string key1 = CyberIconSynthesizer.ComputeCacheKey(bufA, 32, 32);
        string key2 = CyberIconSynthesizer.ComputeCacheKey(bufB, 32, 32);

        Assert.Equal(19, key1.Length);
        Assert.StartsWith("v3_", key1, StringComparison.Ordinal);
        Assert.Equal(key1, key2);
        Assert.Equal(key1, key1.ToLowerInvariant());

        bufB[100] ^= 0x01;
        string keyChanged = CyberIconSynthesizer.ComputeCacheKey(bufB, 32, 32);
        Assert.NotEqual(key1, keyChanged);

        string keyDimsChanged = CyberIconSynthesizer.ComputeCacheKey(bufA, 16, 64);
        Assert.NotEqual(key1, keyDimsChanged);
    }

    [Fact]
    public void LowRes32x32Glyph_SharpensCleanlyWithoutBlurryFringe()
    {
        const int w = 32, h = 32;
        var src = new byte[w * h * 4];
        for (int y = 4; y < 28; y++)
        {
            for (int x = 4; x < 28; x++)
            {
                if (Math.Abs(x - 16) <= 4 || Math.Abs(y - 16) <= 4)
                {
                    SetPixel(src, w, x, y, r: 40, g: 42, b: 48, a: 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.MonochromeGlyph, archetype);
        int softFringeCount = 0;
        int solidCount = 0;
        for (int i = 3; i < output.Length; i += 4)
        {
            byte a = output[i];
            if (a >= 240) solidCount++;
            else if (a is >= 30 and <= 200) softFringeCount++;
        }

        Assert.True(solidCount > 1000, $"Expected strong solid upscaled glyph pixels, got {solidCount}");
        Assert.True(softFringeCount < solidCount / 3, $"Expected sharp edges with low blurry halo ratio, soft={softFringeCount}, solid={solidCount}");
    }

    [Fact]
    public void GradientRoundedTile_StripsSmoothGradientAndLiftsEmblem()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 4; y < 124; y++)
        {
            for (int x = 4; x < 124; x++)
            {
                if (!InsideRoundedRect(x, y, 4, 4, 123, 123, 20)) continue;
                int dx = Math.Abs(x - 64), dy = Math.Abs(y - 64);
                if (dx <= 16 && dy <= 24 && (dx <= 7 || dy <= 7))
                {
                    SetPixel(src, w, x, y, r: 250, g: 250, b: 252, a: 255);
                }
                else
                {
                    // Smooth sky-to-royal blue iOS-style container gradient (ΔE ~ 22 across tile).
                    float t = (y - 4f) / 120f;
                    byte r = (byte)(45 + (15 * (1 - t)));
                    byte g = (byte)(155 - (50 * t));
                    byte b = (byte)(245 - (25 * t));
                    SetPixel(src, w, x, y, r, g, b, 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.TileEmblemLift, archetype);
        GetPixel(output, Size, 8, 8, out _, out _, out _, out byte cornerAlpha);
        Assert.True(cornerAlpha < 15, $"Expected stripped smooth gradient corner, got alpha={cornerAlpha}");
        GetPixel(output, Size, Size / 2, Size / 2, out _, out _, out byte centerB, out byte centerAlpha);
        Assert.InRange(centerAlpha, 240, 255);
        Assert.True(centerB >= 175);
    }

    [Fact]
    public void DarkMoodySquareCoverArt_ClassifiesAsDetailedArtworkAndChamfers()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Moody low-key game key art with varied rim tones & moderate-to-rich interior contrast (edge > 12).
                float rimFade = x / 127f;
                float detail = MathF.Sin(x * 0.38f) * MathF.Cos(y * 0.34f);
                float spot = MathF.Exp(-(((x - 64f) * (x - 64f)) + ((y - 54f) * (y - 54f))) / 900f);
                byte r = (byte)Math.Clamp((int)(35 + (60 * rimFade) + (48 * detail) + (75 * spot)), 0, 255);
                byte g = (byte)Math.Clamp((int)(25 + (40 * (1f - rimFade)) + (42 * detail) + (55 * spot)), 0, 255);
                byte b = (byte)Math.Clamp((int)(48 + (70 * (1f - rimFade)) - (35 * detail) + (40 * spot)), 0, 255);
                SetPixel(src, w, x, y, r, g, b, 255);
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.DetailedArtwork, archetype);
        GetPixel(output, Size, Size - 3, 2, out _, out _, out _, out byte trAlpha);
        GetPixel(output, Size, 2, Size - 3, out _, out _, out _, out byte blAlpha);
        Assert.Equal(0, trAlpha);
        Assert.Equal(0, blAlpha);
    }

    [Fact]
    public void FullBleedMultiColorSquare_AppliesCyberChamfer()
    {
        const int w = 128, h = 128;
        var src = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Crisp full-bleed flat 2-band square (low interior edge density -> MultiColorVector + chamfer).
                if (x + y < 128)
                {
                    SetPixel(src, w, x, y, r: 235, g: 65, b: 120, a: 255);
                }
                else
                {
                    SetPixel(src, w, x, y, r: 45, g: 135, b: 245, a: 255);
                }
            }
        }

        var output = CyberIconSynthesizer.Synthesize(src, w, h, out var archetype);

        Assert.Equal(IconArchetype.MultiColorVector, archetype);
        GetPixel(output, Size, Size - 3, 2, out _, out _, out _, out byte trAlpha);
        GetPixel(output, Size, 2, Size - 3, out _, out _, out _, out byte blAlpha);
        Assert.Equal(0, trAlpha);
        Assert.Equal(0, blAlpha);
    }

    private static bool InsideRoundedRect(int x, int y, int left, int top, int right, int bottom, int radius)
    {
        int cx = Math.Clamp(x, left + radius, right - radius);
        int cy = Math.Clamp(y, top + radius, bottom - radius);
        int dx = x - cx, dy = y - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static void SetPixel(byte[] buf, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = ((y * width) + x) * 4;
        buf[i] = b;
        buf[i + 1] = g;
        buf[i + 2] = r;
        buf[i + 3] = a;
    }

    private static void GetPixel(byte[] buf, int width, int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        int i = ((y * width) + x) * 4;
        b = buf[i];
        g = buf[i + 1];
        r = buf[i + 2];
        a = buf[i + 3];
    }
}
