// Ported from the ReactDrops chromatic button's chromatic-field.ts (reference only).
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// Seamless light field for the chromatic material, generated once in code:
/// broad, soft blobs normalised to a gentle spread, so the face stays bright
/// and the reflections stay within a few percent of the fixed tint.
/// </summary>
internal static class ChromaticField
{
    internal const int Size = 128;

    private static readonly Lazy<BitmapSource> Cached = new(Create);

    internal static BitmapSource Texture => Cached.Value;

    private static BitmapSource Create()
    {
        var values = Fractal(basePeriod: 2, octaves: 3, seed: 11);
        var levels = Normalise(values);
        levels = Blur(Blur(Blur(levels)));

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
                    // latitude uses half as many cells, so the tiled field is
                    // twice as wide as it is tall, like the reference band.
                    sum += amplitude * PeriodicNoise((double)x / Size * period, (double)y / Size * (period / 2.0),
                        period, period / 2, seed + (octave * 97));
                    norm += amplitude;
                    amplitude *= 0.5;
                }
                values[(y * Size) + x] = sum / norm;
            }
        }
        return values;
    }

    private static double PeriodicNoise(double x, double y, int periodX, int periodY, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var sx = fx * fx * fx * ((fx * ((fx * 6) - 15)) + 10);
        var sy = fy * fy * fy * ((fy * ((fy * 6) - 15)) + 10);
        var xa = Wrap(x0, periodX);
        var xb = Wrap(x0 + 1, periodX);
        var ya = Wrap(y0, periodY);
        var yb = Wrap(y0 + 1, periodY);
        var a = Hash(xa, ya, seed);
        var b = Hash(xb, ya, seed);
        var c = Hash(xa, yb, seed);
        var d = Hash(xb, yb, seed);
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

    /// <summary>
    /// Centres the noise on 0.5 and stretches it to roughly 0..1: the face keeps
    /// a bright body, and the bands only vary by a few percent either way.
    /// </summary>
    private static double[] Normalise(double[] values)
    {
        var mean = values.Average();
        var deviation = Math.Sqrt(values.Select(value => (value - mean) * (value - mean)).Average());
        var mapped = new double[values.Length];
        var scale = 1.0 / Math.Max(1e-6, deviation * 3.4);
        for (var i = 0; i < values.Length; i++)
        {
            mapped[i] = Math.Clamp(0.5 + ((values[i] - mean) * scale), 0, 1);
        }
        return mapped;
    }

    /// <summary>Separable 1-2-1 blur; both axes wrap so the tiled field has no seams.</summary>
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
                    (horizontal[(Wrap(y - 1, Size) * Size) + x] + (2 * horizontal[(y * Size) + x]) +
                     horizontal[(Wrap(y + 1, Size) * Size) + x]) * 0.25;
            }
        }
        return result;
    }
}
