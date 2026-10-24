namespace Floe.Core.Visualization;

/// <summary>
/// Turns an album cover into a tiny monochrome OLED picture: centre crop, area-averaged
/// downscale, perceptual luminance, gentle auto-contrast and unsharp mask, then a four-level
/// ordered dither. Alpha is ignored because the OLED only has one channel to spend.
/// </summary>
public static class CoverDither
{
    private const int MaxSize = 64;
    private static readonly float[] Linear = BuildLinearTable();
    private static readonly int[] Bayer =
    {
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    };

    /// <summary>
    /// Reads 32bpp BGRA pixels, crops to the largest centred square, and returns
    /// <paramref name="size"/> squared brightness values in the set 0, 1/3, 2/3, 1.
    /// </summary>
    public static float[] Convert(ReadOnlySpan<byte> bgra, int width, int height, int stride, int size)
    {
        if (size <= 0) return Array.Empty<float>();
        size = Math.Min(size, MaxSize);
        var result = new float[size * size];
        if (width <= 0 || height <= 0) return result;
        long rowBytes = (long)width * 4;
        // A short buffer cannot describe the requested image; leave the OLED dark instead of reading garbage.
        if (stride < rowBytes || bgra.Length < ((long)(height - 1) * stride) + rowBytes) return result;

        int side = Math.Min(width, height);
        int left = (width - side) / 2;
        int top = (height - side) / 2;
        var cells = new float[size * size];
        for (int cy = 0; cy < size; cy++)
        {
            int y0 = top + (int)((long)cy * side / size);
            int y1 = top + (int)((long)(cy + 1) * side / size);
            // Sources smaller than the grid fall back to the nearest source pixel.
            if (y1 <= y0) y1 = y0 + 1;
            for (int cx = 0; cx < size; cx++)
            {
                int x0 = left + (int)((long)cx * side / size);
                int x1 = left + (int)((long)(cx + 1) * side / size);
                if (x1 <= x0) x1 = x0 + 1;
                float sum = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * stride;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = row + (x * 4);
                        // Averaging happens in linear light so a thin bright detail keeps its weight.
                        sum += (Linear[bgra[i + 2]] * .2126f) + (Linear[bgra[i + 1]] * .7152f) + (Linear[bgra[i]] * .0722f);
                    }
                }

                cells[(cy * size) + cx] = MathF.Pow(sum / ((y1 - y0) * (x1 - x0)), 1 / 2.2f);
            }
        }

        Contrast(cells);
        Sharpen(cells, size);
        for (int i = 0; i < cells.Length; i++)
        {
            float at = Math.Clamp(cells[i], 0, 1) * 3;
            float threshold = (Bayer[(((i / size) & 3) * 4) + ((i % size) & 3)] + .5f) / 16f;
            int level = (int)(at + threshold);
            result[i] = Math.Min(level, 3) / 3f;
        }

        return result;
    }

    private static void Contrast(float[] cells)
    {
        var sorted = (float[])cells.Clone();
        Array.Sort(sorted);
        int last = sorted.Length - 1;
        float low = sorted[(int)(last * .02f)];
        float high = sorted[(int)MathF.Ceiling(last * .98f)];
        if (high - low < .08f)
        {
            // No real range to stretch: leave flat covers exactly as flat as they arrived.
            return;
        }

        float scale = 1 / (high - low);
        for (int i = 0; i < cells.Length; i++) cells[i] = Math.Clamp((cells[i] - low) * scale, 0, 1);
    }

    private static void Sharpen(float[] cells, int size)
    {
        var blurred = (float[])cells.Clone();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int sy = Math.Clamp(y + dy, 0, size - 1);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = Math.Clamp(x + dx, 0, size - 1);
                        sum += cells[(sy * size) + sx];
                    }
                }

                blurred[(y * size) + x] = sum / 9;
            }
        }

        // A mild unsharp mask keeps the cover's edges legible after such a small reduction.
        for (int i = 0; i < cells.Length; i++) cells[i] = Math.Clamp(cells[i] + ((cells[i] - blurred[i]) * .5f), 0, 1);
    }

    private static float[] BuildLinearTable()
    {
        var table = new float[256];
        for (int i = 0; i < table.Length; i++)
        {
            float c = i / 255f;
            table[i] = c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        }

        return table;
    }
}
