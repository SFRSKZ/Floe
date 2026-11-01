using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Services;

/// <summary>The Cyberpunk icon set: each app's logo in a framed data card, drawn in its own grey, or in the ink on a surface the grey fades into.</summary>
public static class CardIcons
{
    private static readonly IconCatalog Set = new("CardIcons");
    private static readonly Color Grey = Color.FromRgb(0xAB, 0xB2, 0xC0);
    private static readonly Dictionary<(ImageSource, Color, Color), ImageSource> Toned = new();
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> Forged = new();

    // the set's cards are 192 px with the logo in this square
    private const int CardPixels = 192;
    private const int LogoPixels = 106;
    private const double LogoAt = 43;
    private const double Shadow = 0.4; // the darkest tone a forged logo keeps, as a share of the grey

    /// <summary>The whole set, in catalog order.</summary>
    public static IReadOnlyList<(string Name, string File)> All => Set.All;

    /// <summary>The app's card, or nothing when the set has none.</summary>
    public static ImageSource? Image(string name, string? process = null) => Set.Match(name, process) is { } file ? Set.Load(file) : null;

    /// <summary>One card of the set by its file name.</summary>
    public static ImageSource Load(string file) => Set.Load(file);

    /// <summary>A card for an app the set doesn't know: its own icon, greyed the way the set's were, in the set's frame.</summary>
    public static ImageSource Forge(ImageSource icon) => Forged.GetValue(icon, ForgeCard);

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

    private static ImageSource ForgeCard(ImageSource icon)
    {
        var scene = new DrawingVisual();
        using (var dc = scene.RenderOpen())
        {
            dc.DrawImage(Set.Load("card-frame"), new Rect(0, 0, CardPixels, CardPixels));
            dc.DrawImage(Greyed(icon), new Rect(LogoAt, LogoAt, LogoPixels, LogoPixels));
        }

        var card = new RenderTargetBitmap(CardPixels, CardPixels, 96, 96, PixelFormats.Pbgra32);
        card.Render(scene);
        card.Freeze();
        return card;
    }

    private static BitmapSource Greyed(ImageSource icon)
    {
        var scene = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(scene, BitmapScalingMode.HighQuality);
        using (var dc = scene.RenderOpen()) dc.DrawImage(icon, new Rect(0, 0, LogoPixels, LogoPixels));
        var sized = new RenderTargetBitmap(LogoPixels, LogoPixels, 96, 96, PixelFormats.Pbgra32);
        sized.Render(scene);

        var straight = new FormatConvertedBitmap(sized, PixelFormats.Bgra32, null, 0);
        const int stride = LogoPixels * 4;
        var pixels = new byte[stride * LogoPixels];
        straight.CopyPixels(pixels, stride, 0);

        // tones run from the icon's own darkest to its brightest, so a flat logo doesn't end up one dull grey
        double low = 255, high = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 20) continue;
            var value = Value(pixels, i);
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        low = Math.Min(low, high * Shadow); // an icon's main colour must not sink to the bottom of its range
        for (var i = 0; i < pixels.Length; i += 4)
        {
            double tone = Shadow + ((1 - Shadow) * Math.Clamp((Value(pixels, i) - low) / Math.Max(1, high - low), 0, 1));
            pixels[i] = (byte)(Grey.B * tone);
            pixels[i + 1] = (byte)(Grey.G * tone);
            pixels[i + 2] = (byte)(Grey.R * tone);
        }

        var greyed = BitmapSource.Create(LogoPixels, LogoPixels, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        greyed.Freeze();
        return greyed;
    }

    private static double Value(byte[] pixels, int i)
    {
        double blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
        return (.6 * ((.299 * red) + (.587 * green) + (.114 * blue))) + (.4 * Math.Max(red, Math.Max(green, blue)));
    }

    private static byte Mix(byte from, byte to, double amount) => (byte)(from + ((to - from) * amount));
}
