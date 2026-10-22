using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// Tileable white noise for the material's grain. The reference hashes the pixel
/// position; a small texture is the same information for a ps_2_0 shader.
/// </summary>
internal static class ChromaticNoise
{
    internal const int Size = 64;

    private static readonly Lazy<BitmapSource> Cached = new(Create);

    internal static BitmapSource Texture => Cached.Value;

    private static BitmapSource Create()
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                unchecked
                {
                    var h = (x * 374761393) + (y * 668265263) + 1013904223;
                    h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                    h ^= (int)((uint)h >> 16);
                    var value = (byte)((uint)h >> 24);
                    var offset = ((y * Size) + x) * 4;
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                    pixels[offset + 3] = 255;
                }
            }
        }

        var bitmap = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, pixels, Size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
