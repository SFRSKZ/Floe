using Floe.Core.Visualization;

namespace Floe.Tests;

public sealed class CoverDitherTests
{
    private static readonly float[] Levels = { 0f, 1f / 3f, 2f / 3f, 1f };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(64)]
    public void OutputHasOneValuePerCellAndOnlyFourLevels(int size)
    {
        var source = Gradient(37, 23);
        var output = CoverDither.Convert(source, 37, 23, 37 * 4, size);
        Assert.Equal(size * size, output.Length);
        AssertLevels(output);
    }

    [Fact]
    public void AllWhiteStaysFullyLit()
    {
        var output = CoverDither.Convert(Solid(8, 8, 255), 8, 8, 32, 8);
        Assert.All(output, value => Assert.Equal(1f, value));
    }

    [Fact]
    public void AllBlackStaysDark()
    {
        var output = CoverDither.Convert(Solid(8, 8, 0), 8, 8, 32, 8);
        Assert.All(output, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void LeftBlackRightWhiteKeepsSidesSeparated()
    {
        const int side = 32;
        const int size = 16;
        var source = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                byte value = (byte)(x < side / 2 ? 0 : 255);
                int i = ((y * side) + x) * 4;
                source[i] = source[i + 1] = source[i + 2] = value;
                source[i + 3] = 255;
            }
        }

        var output = CoverDither.Convert(source, side, side, side * 4, size);
        AssertLevels(output);
        int half = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float value = output[(y * size) + x];
                if (x < half) Assert.Equal(0f, value);
                else Assert.Equal(1f, value);
            }
        }
    }

    [Fact]
    public void NonSquareCoverUsesOnlyItsCentreSquare()
    {
        const int width = 30;
        const int height = 10;
        const int size = 8;
        var source = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // The white square sits exactly where the centre crop lands; the black side bars must not survive.
                byte value = (byte)(x is >= 10 and < 20 ? 255 : 0);
                int i = ((y * width) + x) * 4;
                source[i] = source[i + 1] = source[i + 2] = value;
                source[i + 3] = 255;
            }
        }

        var output = CoverDither.Convert(source, width, height, width * 4, size);
        AssertLevels(output);
        Assert.All(output, value => Assert.Equal(1f, value));
    }

    [Fact]
    public void PaddingBeyondEachRowIsIgnored()
    {
        const int width = 8;
        const int height = 8;
        const int stride = 64;
        var source = new byte[stride * height];
        Array.Fill(source, (byte)255); // White padding would light every cell if the stride were ignored.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * stride) + (x * 4);
                source[i] = source[i + 1] = source[i + 2] = 0;
            }
        }

        var output = CoverDither.Convert(source, width, height, stride, width);
        AssertLevels(output);
        Assert.All(output, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void SourceSmallerThanGridRepeatsNearestPixels()
    {
        var source = Solid(2, 2, 255);
        source[0] = source[1] = source[2] = 0; // Top-left pixel only.
        var output = CoverDither.Convert(source, 2, 2, 8, 4);
        Assert.Equal(12, output.Count(value => value == 1f));
        Assert.Equal(0f, output[0]);
        Assert.Equal(0f, output[1]);
        Assert.Equal(0f, output[4]);
        Assert.Equal(0f, output[5]);
    }

    [Fact]
    public void ZeroSizeReturnsEmptyOutput()
    {
        var source = Solid(4, 4, 128);
        Assert.Empty(CoverDither.Convert(source, 4, 4, 16, 0));
        Assert.Empty(CoverDither.Convert(source, 4, 4, 16, -3));
    }

    [Fact]
    public void EmptySourceReturnsAZeroGrid()
    {
        var output = CoverDither.Convert(ReadOnlySpan<byte>.Empty, 0, 12, 0, 8);
        Assert.Equal(64, output.Length);
        Assert.All(output, value => Assert.Equal(0f, value));
        output = CoverDither.Convert(ReadOnlySpan<byte>.Empty, 12, 0, 48, 8);
        Assert.Equal(64, output.Length);
        Assert.All(output, value => Assert.Equal(0f, value));
    }

    private static void AssertLevels(float[] output) =>
        Assert.All(output, value => Assert.Contains(Levels, level => level == value));

    private static byte[] Solid(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                pixels[i] = (byte)((x * 255) / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)(((x + y) * 255) / Math.Max(1, width + height - 2));
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }
}
