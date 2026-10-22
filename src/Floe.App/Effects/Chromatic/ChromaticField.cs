// Ported from the ReactDrops chromatic button's chromatic-field.ts (reference only).
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// Seamless equirectangular light field for the chromatic material, generated once
/// in code: soft blobs ranked onto the reference's brightness distribution, then
/// softened because the projection squeezes most of the field into the rim.
/// The material uses a single fixed tint, so only brightness is kept.
/// </summary>
internal static class ChromaticField
{
    internal const int Size = 128;

    // Brightness quantiles (share of texels, value): mostly dark with a long tail
    // of bright highlights, which become the material's glints.
    private static readonly double[][] Quantiles =
    [
        [0, 0.024], [0.05, 0.078], [0.25, 0.157], [0.5, 0.243],
        [0.75, 0.325], [0.9, 0.4], [0.97, 0.459], [0.995, 0.506], [1, 0.533],
    ];

    private static readonly Lazy<BitmapSource> Cached = new(Create);

    internal static BitmapSource Texture => Cached.Value;

    private static BitmapSource Create()
    {
        var values = Fractal(basePeriod: 4, octaves: 4, seed: 11);
        var levels = MatchQuantiles(values);
        levels = Blur(Blur(levels));

        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < levels.Length; i++)
        {
            var value = (byte)Math.Round(Math.Clamp(levels[i], 0, 1) * 255);
            pixels[i * 4] = value;
            pixels[i * 4 + 1] = value;
            pixels[i * 4 + 2] = value;
            pixels[i * 4 + 3] = 255;
        }

        var bitmap = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, pixels, Size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Fractal value noise that repeats horizontally, as the field is a 360 degree band.</summary>
    private static double[] Fractal(int basePeriod, int octaves, int seed)
    {
        var values = new double[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                double sum = 0, amplitude = 1, norm = 0;
                for (var octave = 0; octave < octaves; octave++)
                {
                    var period = basePeriod << octave;
                    // latitude uses half as many cells: the field is twice as wide as tall
                    sum += amplitude * PeriodicNoise((double)x / Size * period, (double)y / Size * (period / 2.0),
                        period, seed + (octave * 97));
                    norm += amplitude;
                    amplitude *= 0.5;
                }
                values[(y * Size) + x] = sum / norm;
            }
        }
        return values;
    }

    private static double PeriodicNoise(double x, double y, int period, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var sx = fx * fx * fx * ((fx * ((fx * 6) - 15)) + 10);
        var sy = fy * fy * fy * ((fy * ((fy * 6) - 15)) + 10);
        var a = Hash(Wrap(x0, period), y0, seed);
        var b = Hash(Wrap(x0 + 1, period), y0, seed);
        var c = Hash(Wrap(x0, period), y0 + 1, seed);
        var d = Hash(Wrap(x0 + 1, period), y0 + 1, seed);
        return a + ((b - a) * sx) + ((c - a) * sy) + ((a - b - c + d) * sx * sy);
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (x * 374761393) + (y * 668265263) + (seed * int.MaxValue);
            h = (h ^ (int)((uint)h >> 13)) * 1274126177;
            h ^= (int)((uint)h >> 16);
            return (uint)h / 4294967295.0;
        }
    }

    private static int Wrap(int value, int period) => ((value % period) + period) % period;

    /// <summary>Maps values by rank onto the reference's piecewise-linear quantile table.</summary>
    private static double[] MatchQuantiles(double[] values)
    {
        var order = Enumerable.Range(0, values.Length).OrderBy(index => values[index]).ToArray();
        var mapped = new double[values.Length];
        for (var rank = 0; rank < order.Length; rank++)
        {
            var share = (double)rank / (order.Length - 1);
            var k = 1;
            while (k < Quantiles.Length - 1 && Quantiles[k][0] < share)
            {
                k++;
            }
            var (s0, v0) = (Quantiles[k - 1][0], Quantiles[k - 1][1]);
            var (s1, v1) = (Quantiles[k][0], Quantiles[k][1]);
            mapped[order[rank]] = v0 + ((share - s0) / Math.Max(1e-6, s1 - s0) * (v1 - v0));
        }
        return mapped;
    }

    /// <summary>Separable 1-2-1 blur: the projection magnifies single texels near the rim.</summary>
    private static double[] Blur(double[] values)
    {
        var horizontal = new double[values.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                horizontal[(y * Size) + x] =
                    (values[(y * Size) + Wrap(x - 1, Size)] + (2 * values[(y * Size) + x]) +
                     values[(y * Size) + Wrap(x + 1, Size)]) * 0.25;
            }
        }

        var result = new double[values.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                result[(y * Size) + x] =
                    (horizontal[(Math.Max(0, y - 1) * Size) + x] + (2 * horizontal[(y * Size) + x]) +
                     horizontal[(Math.Min(Size - 1, y + 1) * Size) + x]) * 0.25;
            }
        }
        return result;
    }
}
