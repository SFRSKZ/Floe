using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Services;

/// <summary>The Cyberpunk icon set: each app's logo in a framed data card, drawn in its own grey, or in the ink on a surface the grey fades into.</summary>
public static class CardIcons
{
    private static readonly IconCatalog Set = new("CardIcons");
    private static readonly Color Grey = Color.FromRgb(0xAB, 0xB2, 0xC0);
    private static readonly Dictionary<(ImageSource, Color, Color), ImageSource> Toned = new();

    /// <summary>The whole set, in catalog order.</summary>
    public static IReadOnlyList<(string Name, string File)> All => Set.All;

    /// <summary>The app's card, or nothing when the set has none.</summary>
    public static ImageSource? Image(string name, string? process = null) => Set.Match(name, process) is { } file ? Set.Load(file) : null;

    /// <summary>One card of the set by its file name.</summary>
    public static ImageSource Load(string file) => Set.Load(file);

    /// <summary>The card as it reads on a surface: the grey where it shows, else redrawn from the ink to the surface.</summary>
    public static ImageSource OnBody(ImageSource card, Color body, Color ink)
    {
        if (CyberLook.Contrast(Grey, body) >= 3 || card is not BitmapSource bitmap) return card;
        if (Toned.TryGetValue((card, body, ink), out var known)) return known;

        var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = straight.PixelWidth * 4;
        var pixels = new byte[stride * straight.PixelHeight];
        straight.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // the brightest grey is the ink; the shading inside a logo falls toward the surface and stays visible
            double tone = Math.Min(1, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) / (double)Grey.B);
            pixels[i] = Mix(body.B, ink.B, tone);
            pixels[i + 1] = Mix(body.G, ink.G, tone);
            pixels[i + 2] = Mix(body.R, ink.R, tone);
        }

        var redrawn = BitmapSource.Create(straight.PixelWidth, straight.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        redrawn.Freeze();
        return Toned[(card, body, ink)] = redrawn;
    }

    private static byte Mix(byte from, byte to, double amount) => (byte)(from + ((to - from) * amount));
}
