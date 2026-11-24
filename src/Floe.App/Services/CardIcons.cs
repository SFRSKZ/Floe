using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>The Cyberpunk icon set: each app's logo in a framed data card, drawn in its own grey, or in the ink on a surface the grey fades into.</summary>
public static class CardIcons
{
    private static readonly IconCatalog Set = new("CardIcons");
    private static readonly Color Grey = Color.FromRgb(0xAB, 0xB2, 0xC0);
    private static readonly Dictionary<(ImageSource, Color, Color, bool), ImageSource> Toned = new();
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> Forged = new();
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, ImageSource>> ForgedColor = new();
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> ColorToMonoCard = new();
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> ColorToMonoLogo = new();
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> ExtractedLogos = new();
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, (ImageSource Inner, ImageSource Outer)>> RadialSplits = new();
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, (ImageSource Top, ImageSource Bottom)>> HorizontalSplits = new();
    private static readonly Dictionary<string, ImageSource?> CustomLoaded = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string ForgedCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Product.Folder,
        "forged-cards",
        "v3");

    private static readonly string CustomCardsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Product.Folder,
        "custom-cards");

    // the set's cards are 192 px with the logo in this square
    private const int CardPixels = 192;
    private const int LogoPixels = CyberIconSynthesizer.LogoPixels;
    private const double LogoAt = 43;

    /// <summary>Raised after the forged/custom card cache is cleared so visible docks can re-apply icons.</summary>
    public static event Action? CacheCleared;

    /// <summary>The whole set, in catalog order.</summary>
    public static IReadOnlyList<(string Name, string File)> All => Set.All;

    /// <summary>The app's card (custom override first, then bundled catalog), or nothing when none matches.</summary>
    public static ImageSource? Image(string name, string? process = null) =>
        LoadCustomCard(name, process) ?? (Set.Match(name, process) is { } file ? Set.Load(file) : null);

    /// <summary>One card of the set by its file name.</summary>
    public static ImageSource Load(string file) => Set.Load(file);

    /// <summary>The empty Cyberpunk data-card border used while live original-logo animations render inside the aperture.</summary>
    public static ImageSource FrameOnly => Set.Load("card-frame");

    /// <summary>Extracts and caches the exact <c>106×106</c> logo bitmap inside a <c>192×192</c> Cyberpunk card.</summary>
    public static ImageSource? ExtractLogo(ImageSource card)
    {
        if (card is not BitmapSource bmp || bmp.PixelWidth < 32 || bmp.PixelHeight < 32) return null;
        if (ExtractedLogos.TryGetValue(card, out var cached)) return cached;

        int x = Math.Clamp((int)Math.Round(bmp.PixelWidth * (LogoAt / CardPixels)), 0, bmp.PixelWidth - 2);
        int y = Math.Clamp((int)Math.Round(bmp.PixelHeight * (LogoAt / CardPixels)), 0, bmp.PixelHeight - 2);
        int w = Math.Clamp((int)Math.Round(bmp.PixelWidth * ((double)LogoPixels / CardPixels)), 1, bmp.PixelWidth - x);
        int h = Math.Clamp((int)Math.Round(bmp.PixelHeight * ((double)LogoPixels / CardPixels)), 1, bmp.PixelHeight - y);

        var cropped = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
        var straight = cropped.Format == PixelFormats.Bgra32 ? (BitmapSource)cropped : new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
        int stride = w * 4;
        var bytes = new byte[stride * h];
        straight.CopyPixels(bytes, stride, 0);

        var logo = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        logo.Freeze();
        if (ColorToMonoCard.TryGetValue(card, out var monoCard)
            && !ReferenceEquals(monoCard, card)
            && ExtractLogo(monoCard) is { } monoLogo)
        {
            ColorToMonoLogo.AddOrUpdate(logo, monoLogo);
        }

        ExtractedLogos.AddOrUpdate(card, logo);
        return logo;
    }

    /// <summary>
    /// If <paramref name="logo"/> was extracted from a semantic colour card, returns the underlying monochrome CP77 card logo
    /// whose alpha holes, island seams, and 3-tone shading define the exact anatomical masks.
    /// </summary>
    public static bool TryGetMonoLogo(ImageSource logo, out ImageSource monoLogo)
    {
        if (ColorToMonoLogo.TryGetValue(logo, out var found) && !ReferenceEquals(found, logo))
        {
            monoLogo = found;
            return true;
        }

        monoLogo = logo;
        return false;
    }

    /// <summary>Splits an extracted or original icon into lossless inner and outer concentric rings at <paramref name="radiusFraction"/>.</summary>
    public static (ImageSource Inner, ImageSource Outer)? SplitRadial(ImageSource logo, double radiusFraction)
    {
        if (CopyStraightBgra32(logo, out int w, out int h, out var pixels) is false) return null;

        int key = (int)Math.Round(Math.Clamp(radiusFraction, 0.05, 0.95) * 1000.0);
        var cache = RadialSplits.GetOrCreateValue(logo);
        lock (cache)
        {
            if (cache.TryGetValue(key, out var cached)) return cached;

            int stride = w * 4;
            var innerBytes = new byte[pixels.Length];
            var outerBytes = new byte[pixels.Length];
            Buffer.BlockCopy(pixels, 0, innerBytes, 0, pixels.Length);
            Buffer.BlockCopy(pixels, 0, outerBytes, 0, pixels.Length);

            double cx = (w - 1) * 0.5;
            double cy = (h - 1) * 0.5;
            double splitR = Math.Min(w, h) * 0.5 * radiusFraction;
            const double feather = 1.5;
            double innerEdge = splitR - (feather * 0.5);

            for (int y = 0; y < h; y++)
            {
                double dy = y - cy;
                int rowOffset = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowOffset + (x * 4);
                    byte origA = pixels[idx + 3];
                    if (origA == 0) continue;

                    double dx = x - cx;
                    double d = Math.Sqrt((dx * dx) + (dy * dy));
                    double outerT = Math.Clamp((d - innerEdge) / feather, 0.0, 1.0);
                    byte outerA = (byte)Math.Round(origA * outerT);
                    byte innerA = (byte)(origA - outerA);
                    outerBytes[idx + 3] = outerA;
                    innerBytes[idx + 3] = innerA;
                }
            }

            var innerBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, innerBytes, stride);
            innerBmp.Freeze();
            var outerBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, outerBytes, stride);
            outerBmp.Freeze();
            return cache[key] = (innerBmp, outerBmp);
        }
    }

    /// <summary>Splits an extracted or original icon horizontally at <paramref name="splitYFraction"/> into lossless top and bottom layers.</summary>
    public static (ImageSource Top, ImageSource Bottom)? SplitHorizontal(ImageSource logo, double splitYFraction)
    {
        if (CopyStraightBgra32(logo, out int w, out int h, out var pixels) is false) return null;

        int key = (int)Math.Round(Math.Clamp(splitYFraction, 0.05, 0.95) * 1000.0);
        var cache = HorizontalSplits.GetOrCreateValue(logo);
        lock (cache)
        {
            if (cache.TryGetValue(key, out var cached)) return cached;

            int stride = w * 4;
            int splitRow = Math.Clamp((int)Math.Round(h * splitYFraction), 1, h - 1);
            int topByteCount = splitRow * stride;

            var topBytes = new byte[pixels.Length];
            var bottomBytes = new byte[pixels.Length];
            Buffer.BlockCopy(pixels, 0, topBytes, 0, topByteCount);
            Buffer.BlockCopy(pixels, topByteCount, bottomBytes, topByteCount, pixels.Length - topByteCount);

            var topBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, topBytes, stride);
            topBmp.Freeze();
            var bottomBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bottomBytes, stride);
            bottomBmp.Freeze();
            return cache[key] = (topBmp, bottomBmp);
        }
    }

    private static bool CopyStraightBgra32(ImageSource logo, out int width, out int height, out byte[] pixels)
    {
        if (logo is not BitmapSource bmp || bmp.PixelWidth < 16 || bmp.PixelHeight < 16)
        {
            width = height = 0;
            pixels = Array.Empty<byte>();
            return false;
        }

        var straight = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        width = straight.PixelWidth;
        height = straight.PixelHeight;
        int stride = width * 4;
        pixels = new byte[stride * height];
        straight.CopyPixels(pixels, stride, 0);
        return true;
    }

    /// <summary>A card for an app the set doesn't know: synthesized cleanly into a 3-tone CP77 emblem in the set's frame.</summary>
    public static ImageSource Forge(ImageSource icon) => Forged.GetValue(icon, ForgeCard);

    /// <summary>
    /// Colorizes a CP77 data card (<paramref name="baseCard"/>) by understanding each icon's semantic parts
    /// (islands, cutout symbols, 3-tone depth layers, and brand colours from <paramref name="realIcon"/>)
    /// while linking the resulting card to <paramref name="baseCard"/>'s topological masks for per-part animation.
    /// </summary>
    public static ImageSource ForgeColor(ImageSource baseCard, ImageSource? realIcon, string? appName = null, string? processName = null)
    {
        var kind = AnimatedIconCatalog.Match(appName, processName);
        var dict = ForgedColor.GetOrCreateValue(baseCard);
        int key = (int)kind;
        lock (dict)
        {
            if (dict.TryGetValue(key, out var cached)) return cached;
            var created = ForgeColorCard(baseCard, realIcon, kind);
            ColorToMonoCard.AddOrUpdate(created, baseCard);
            return dict[key] = created;
        }
    }

    /// <summary>Clears in-memory and on-disk forged card caches as well as custom overrides, then notifies open docks.</summary>
    public static void ClearForgedCache()
    {
        Forged.Clear();
        ForgedColor.Clear();
        ColorToMonoCard.Clear();
        ColorToMonoLogo.Clear();
        ExtractedLogos.Clear();
        RadialSplits.Clear();
        HorizontalSplits.Clear();
        CyberIconAnatomy.ClearCache();
        Toned.Clear();
        CustomLoaded.Clear();

        try
        {
            if (Directory.Exists(ForgedCacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(ForgedCacheDir, "*.png"))
                {
                    try { File.Delete(file); } catch { /* Best effort cleanup. */ }
                }
            }
        }
        catch
        {
            // Ignore IO errors during cache cleanup.
        }

        CacheCleared?.Invoke();
    }

    /// <summary>The card as it reads on a surface: the grey where it shows, else redrawn from the ink to the surface.</summary>
    public static ImageSource OnBody(ImageSource card, Color body, Color ink, bool preserveLogoColors = false)
    {
        if (CyberLook.Contrast(Grey, body) >= 3 || card is not BitmapSource bitmap) return card;
        if (Toned.TryGetValue((card, body, ink, preserveLogoColors), out var known)) return known;

        var straight = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int w = straight.PixelWidth;
        int h = straight.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        straight.CopyPixels(pixels, stride, 0);

        int logoMinX = (int)Math.Round(w * (LogoAt / CardPixels));
        int logoMaxX = logoMinX + (int)Math.Round(w * ((double)LogoPixels / CardPixels));
        int logoMinY = (int)Math.Round(h * (LogoAt / CardPixels));
        int logoMaxY = logoMinY + (int)Math.Round(h * ((double)LogoPixels / CardPixels));

        for (int y = 0; y < h; y++)
        {
            bool inLogoY = preserveLogoColors && y >= logoMinY && y < logoMaxY;
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (inLogoY && x >= logoMinX && x < logoMaxX) continue;
                int i = row + (x * 4);
                // the brightest grey is the ink; the shading inside a logo falls toward the surface and stays visible
                double tone = Math.Min(1, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) / (double)Grey.B);
                pixels[i] = Mix(body.B, ink.B, tone);
                pixels[i + 1] = Mix(body.G, ink.G, tone);
                pixels[i + 2] = Mix(body.R, ink.R, tone);
            }
        }

        var redrawn = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        redrawn.Freeze();
        if (ColorToMonoCard.TryGetValue(card, out var monoCard))
        {
            ColorToMonoCard.AddOrUpdate(redrawn, monoCard);
        }

        return Toned[(card, body, ink, preserveLogoColors)] = redrawn;
    }

    private static ImageSource? LoadCustomCard(string name, string? process)
    {
        var cacheKey = $"{name}|{process}";
        if (CustomLoaded.TryGetValue(cacheKey, out var cached)) return cached;

        if (!Directory.Exists(CustomCardsDir))
        {
            return CustomLoaded[cacheKey] = null;
        }

        foreach (var candidate in CustomCardCandidates(name, process))
        {
            var path = Path.Combine(CustomCardsDir, $"{candidate}.png");
            if (!File.Exists(path)) continue;

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.DecodePixelWidth = CardPixels;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.EndInit();
                image.Freeze();
                return CustomLoaded[cacheKey] = image;
            }
            catch
            {
                // Skip unreadable custom card files and try remaining candidates.
            }
        }

        return CustomLoaded[cacheKey] = null;
    }

    private static IEnumerable<string> CustomCardCandidates(string name, string? process)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { name, process })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var hyphen = Slug(raw, '-');
            if (hyphen.Length > 0 && seen.Add(hyphen)) yield return hyphen;

            var compact = new string(raw.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (compact.Length > 0 && seen.Add(compact)) yield return compact;
        }
    }

    private static string Slug(string value, char separator)
    {
        var chars = new List<char>(value.Length);
        bool pendingSep = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSep && chars.Count > 0) chars.Add(separator);
                chars.Add(ch);
                pendingSep = false;
            }
            else
            {
                pendingSep = true;
            }
        }
        return new string(chars.ToArray());
    }

    private static ImageSource ForgeCard(ImageSource icon)
    {
        var pixels = ExtractBgra32(icon, out int width, out int height);
        var cacheKey = CyberIconSynthesizer.ComputeCacheKey(pixels, width, height);
        var cachePath = Path.Combine(ForgedCacheDir, $"{cacheKey}.png");

        if (TryLoadCachedCard(cachePath) is { } cached)
        {
            return cached;
        }

        var logoPixels = CyberIconSynthesizer.Synthesize(pixels, width, height, out _);
        var logo = BitmapSource.Create(LogoPixels, LogoPixels, 96, 96, PixelFormats.Bgra32, null, logoPixels, LogoPixels * 4);
        logo.Freeze();

        var scene = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(scene, BitmapScalingMode.HighQuality);
        using (var dc = scene.RenderOpen())
        {
            dc.DrawImage(Set.Load("card-frame"), new Rect(0, 0, CardPixels, CardPixels));
            dc.DrawImage(logo, new Rect(LogoAt, LogoAt, LogoPixels, LogoPixels));
        }

        var rendered = new RenderTargetBitmap(CardPixels, CardPixels, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(scene);

        // Un-premultiply back to straight Bgra32 so OnBody reads exact brightness ratios across any dock color.
        var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
        straight.Freeze();

        TrySaveCachedCard(cachePath, straight);
        return straight;
    }

    private static ImageSource ForgeColorCard(ImageSource baseCard, ImageSource? realIcon, AnimatedIconKind kind)
    {
        if (baseCard is not BitmapSource cardBmp || ExtractLogo(baseCard) is not BitmapSource monoLogo)
        {
            return baseCard;
        }

        var straightCard = cardBmp.Format == PixelFormats.Bgra32
            ? cardBmp
            : new FormatConvertedBitmap(cardBmp, PixelFormats.Bgra32, null, 0);
        int cw = straightCard.PixelWidth;
        int ch = straightCard.PixelHeight;
        int cStride = cw * 4;
        var cardPixels = new byte[cStride * ch];
        straightCard.CopyPixels(cardPixels, cStride, 0);

        int lw = monoLogo.PixelWidth;
        int lh = monoLogo.PixelHeight;
        int lStride = lw * 4;
        var monoPx = new byte[lStride * lh];
        monoLogo.CopyPixels(monoPx, lStride, 0);
        var colorPx = new byte[lStride * lh];
        Buffer.BlockCopy(monoPx, 0, colorPx, 0, monoPx.Length);

        bool painted = kind switch
        {
            AnimatedIconKind.Chrome => PaintChromeColor(lw, lh, monoPx, colorPx, monoLogo),
            AnimatedIconKind.AppleMusic => PaintCutoutPlateColor(
                lw, lh, colorPx, monoLogo, isCircular: false,
                Color.FromRgb(0xFF, 0x4E, 0x6A), Color.FromRgb(0xD9, 0x14, 0x32),
                Color.FromRgb(0xFF, 0xFF, 0xFF)),
            AnimatedIconKind.Spotify => PaintCutoutPlateColor(
                lw, lh, colorPx, monoLogo, isCircular: true,
                Color.FromRgb(0x25, 0xE8, 0x70), Color.FromRgb(0x14, 0xA8, 0x48),
                Color.FromRgb(0x10, 0x14, 0x14)),
            AnimatedIconKind.Steam => PaintSteamColor(lw, lh, colorPx, monoLogo),
            AnimatedIconKind.Antigravity => PaintAntigravityColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.OpenCode => PaintOpenCodeColor(lw, lh, monoPx, colorPx, monoLogo),
            AnimatedIconKind.Claude => PaintClaudeColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.OrcaSlicer => PaintOrcaSlicerColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.FileExplorer => PaintFileExplorerColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.Terminal => PaintTerminalColor(lw, lh, colorPx, monoLogo),
            AnimatedIconKind.TaskManager => PaintTaskManagerColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.WindowsSettings => PaintSettingsGearColor(lw, lh, monoPx, colorPx),
            AnimatedIconKind.Notepad => PaintNotepadColor(lw, lh, monoPx, colorPx, monoLogo),
            AnimatedIconKind.Blender => PaintBlenderColor(lw, lh, monoPx, colorPx, monoLogo),
            AnimatedIconKind.VSCode => PaintGradientToneColor(lw, lh, monoPx, colorPx, Color.FromRgb(0x00, 0x66, 0xB8), Color.FromRgb(0x29, 0xAD, 0xFF)),
            AnimatedIconKind.GitHub => PaintCutoutPlateColor(
                lw, lh, colorPx, monoLogo, isCircular: true,
                Color.FromRgb(0x2D, 0x33, 0x3B), Color.FromRgb(0x1C, 0x21, 0x28),
                Color.FromRgb(0xF0, 0xF6, 0xFC)),
            AnimatedIconKind.SevenZip => PaintSevenZipColor(lw, lh, colorPx, monoLogo),
            AnimatedIconKind.Fusion => PaintGradientToneColor(lw, lh, monoPx, colorPx, Color.FromRgb(0xE8, 0x5D, 0x04), Color.FromRgb(0xFF, 0xB7, 0x03)),
            AnimatedIconKind.RivaTuner => PaintRivatunerColor(lw, lh, colorPx, monoLogo),
            AnimatedIconKind.MSIAfterburner => PaintGradientToneColor(lw, lh, monoPx, colorPx, Color.FromRgb(0xB8, 0x0F, 0x22), Color.FromRgb(0xFF, 0x47, 0x57)),
            _ => false,
        };

        if (!painted)
        {
            PaintGeneralRealIconColor(lw, lh, monoPx, colorPx, monoLogo, realIcon);
        }

        int logoX0 = Math.Clamp((int)Math.Round(cw * (LogoAt / CardPixels)), 0, cw - lw);
        int logoY0 = Math.Clamp((int)Math.Round(ch * (LogoAt / CardPixels)), 0, ch - lh);
        for (int y = 0; y < lh; y++)
        {
            Buffer.BlockCopy(colorPx, y * lStride, cardPixels, ((logoY0 + y) * cStride) + (logoX0 * 4), lStride);
        }

        var result = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, cardPixels, cStride);
        result.Freeze();
        return result;
    }

    private static bool PaintChromeColor(int w, int h, byte[] monoPx, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetConnectedIslands(monoLogo) is not { Count: >= 4 } islands) return false;
        Array.Clear(outPx, 0, outPx.Length);

        var sorted = islands
            .OrderBy(i => Math.Sqrt(Math.Pow(i.NormalizedCenter.X - 0.5, 2) + Math.Pow(i.NormalizedCenter.Y - 0.5, 2)))
            .ToList();
        var core = sorted[0];
        var petals = sorted.Skip(1).ToList();

        // Paint center core sphere in Google Chrome Blue (#1A73E8 -> #4285F4).
        if (CopyStraightBgra32(core.Bitmap, out _, out _, out var coreBytes))
        {
            for (int y = 0; y < h; y++)
            {
                float ny = y / (float)Math.Max(1, h - 1);
                for (int x = 0; x < w; x++)
                {
                    int bi = ((y * w) + x) * 4;
                    byte a = coreBytes[bi + 3];
                    if (a == 0) continue;
                    float nx = x / (float)Math.Max(1, w - 1);
                    float grad = Math.Clamp(1f - (((nx - 0.38f) + (ny - 0.38f)) * 1.4f), 0f, 1f);
                    outPx[bi] = Mix(0xD8, 0xF8, grad);
                    outPx[bi + 1] = Mix(0x68, 0x90, grad);
                    outPx[bi + 2] = Mix(0x18, 0x45, grad);
                    outPx[bi + 3] = a;
                }
            }
        }

        // Paint each outer shutter blade by its angular position around (0.5, 0.5).
        foreach (var petal in petals)
        {
            if (!CopyStraightBgra32(petal.Bitmap, out _, out _, out var pBytes)) continue;
            double angle = Math.Atan2(petal.NormalizedCenter.Y - 0.5, petal.NormalizedCenter.X - 0.5) * 180.0 / Math.PI;
            Color cDark, cBright;
            if (angle >= -155.0 && angle < -20.0)
            {
                // Top blade: Chrome Red (#EA4335)
                cDark = Color.FromRgb(0xC5, 0x22, 0x1F);
                cBright = Color.FromRgb(0xF2, 0x55, 0x46);
            }
            else if (angle >= -20.0 && angle < 95.0)
            {
                // Right blade: Chrome Yellow (#FBBC04)
                cDark = Color.FromRgb(0xF2, 0x99, 0x00);
                cBright = Color.FromRgb(0xFD, 0xD6, 0x48);
            }
            else
            {
                // Left blade: Chrome Green (#34A853)
                cDark = Color.FromRgb(0x1E, 0x8E, 0x3E);
                cBright = Color.FromRgb(0x46, 0xC4, 0x6A);
            }

            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte a = pBytes[bi + 3];
                if (a == 0) continue;
                float tone = Math.Clamp(Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B, 0.25f, 1f);
                float t = (tone - 0.25f) / 0.75f;
                outPx[bi] = Mix(cDark.B, cBright.B, t);
                outPx[bi + 1] = Mix(cDark.G, cBright.G, t);
                outPx[bi + 2] = Mix(cDark.R, cBright.R, t);
                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintCutoutPlateColor(
        int w, int h, byte[] outPx, ImageSource monoLogo, bool isCircular,
        Color plateTopLeft, Color plateBottomRight, Color symbolColor)
    {
        if (CyberIconAnatomy.GetCutoutStencil(monoLogo, isCircular) is not { Parts.Count: >= 1 } stencil) return false;
        if (!CopyStraightBgra32(stencil.FilledBase, out _, out _, out var baseBytes)) return false;

        Array.Clear(outPx, 0, outPx.Length);
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte ba = baseBytes[bi + 3];
                if (ba == 0) continue;
                float nx = x / (float)Math.Max(1, w - 1);
                float grad = Math.Clamp((nx * 0.35f) + (ny * 0.65f), 0f, 1f);
                outPx[bi] = Mix(plateTopLeft.B, plateBottomRight.B, grad);
                outPx[bi + 1] = Mix(plateTopLeft.G, plateBottomRight.G, grad);
                outPx[bi + 2] = Mix(plateTopLeft.R, plateBottomRight.R, grad);
                outPx[bi + 3] = ba;
            }
        }

        foreach (var part in stencil.Parts)
        {
            if (!CopyStraightBgra32(part.Bitmap, out _, out _, out var pBytes)) continue;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte pa = pBytes[bi + 3];
                if (pa == 0) continue;
                double t = pa / 255.0;
                outPx[bi] = Mix(outPx[bi], symbolColor.B, t);
                outPx[bi + 1] = Mix(outPx[bi + 1], symbolColor.G, t);
                outPx[bi + 2] = Mix(outPx[bi + 2], symbolColor.R, t);
                if (pa > outPx[bi + 3]) outPx[bi + 3] = pa;
            }
        }

        return true;
    }

    private static bool PaintSteamColor(int w, int h, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetSteamAnatomy(monoLogo) is not { } steam) return false;
        if (!CopyStraightBgra32(steam.FilledDisc, out _, out _, out var discBytes)) return false;

        Array.Clear(outPx, 0, outPx.Length);
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte da = discBytes[bi + 3];
                if (da == 0) continue;
                float nx = x / (float)Math.Max(1, w - 1);
                float g = Math.Clamp((nx * 0.35f) + (ny * 0.65f), 0f, 1f);
                outPx[bi] = Mix(0x52, 0xEC, g);
                outPx[bi + 1] = Mix(0x24, 0x92, g);
                outPx[bi + 2] = Mix(0x10, 0x1A, g);
                outPx[bi + 3] = da;
            }
        }

        void OverlayWhitePart(ImageSource? partImg)
        {
            if (partImg is null || !CopyStraightBgra32(partImg, out _, out _, out var pBytes)) return;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte pa = pBytes[bi + 3];
                if (pa == 0) continue;
                double t = pa / 255.0;
                outPx[bi] = Mix(outPx[bi], 0xFF, t);
                outPx[bi + 1] = Mix(outPx[bi + 1], 0xF8, t);
                outPx[bi + 2] = Mix(outPx[bi + 2], 0xF2, t);
                if (pa > outPx[bi + 3]) outPx[bi + 3] = pa;
            }
        }

        OverlayWhitePart(steam.CrankCutout);

        // Keep the two internal wheel axle hubs inside the piston ends in deep Steam Navy-Blue
        // so the piston rings stay hollow and the rotating wheel hubs contrast against the white crank.
        void OverlayAxleHub(ImageSource? hubImg)
        {
            if (hubImg is null || !CopyStraightBgra32(hubImg, out _, out _, out var hBytes)) return;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte ha = hBytes[bi + 3];
                if (ha == 0) continue;
                double t = ha / 255.0;
                outPx[bi] = Mix(outPx[bi], 0x82, t);
                outPx[bi + 1] = Mix(outPx[bi + 1], 0x42, t);
                outPx[bi + 2] = Mix(outPx[bi + 2], 0x14, t);
                if (ha > outPx[bi + 3]) outPx[bi + 3] = ha;
            }
        }

        OverlayAxleHub(steam.UpperWheel?.Bitmap);
        OverlayAxleHub(steam.LowerWheel?.Bitmap);
        return true;
    }

    private static bool PaintOpenCodeColor(int w, int h, byte[] monoPx, byte[] outPx, ImageSource monoLogo)
    {
        // OpenCode is a hollow rectangular monolith portal ([ ]): keep the interior chamber hollow so
        // when SplitSeam slides the left and right jaws apart, the interior telemetry stream is visible.
        byte[]? holeBytes = null;
        if (CyberIconAnatomy.GetCutoutStencil(monoLogo) is { Parts.Count: >= 1 } stencil)
        {
            CopyStraightBgra32(stencil.Parts[0].Bitmap, out _, out _, out holeBytes);
        }

        Array.Clear(outPx, 0, outPx.Length);
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float nx = x / (float)Math.Max(1, w - 1);

                // Distance to the inner portal cutout edge gives a glowing Ice-Cyan inner rim (#5CEEFF)
                // while the outer monolith body is sleek metallic cyber-slate (#2B364A -> #1C2331).
                float innerRim = 0f;
                if (holeBytes is not null)
                {
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        int cy = y + dy;
                        if ((uint)cy >= (uint)h) continue;
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int cx = x + dx;
                            if ((uint)cx >= (uint)w) continue;
                            byte ha = holeBytes[(((cy * w) + cx) * 4) + 3];
                            if (ha > 80)
                            {
                                float d = MathF.Sqrt((dx * dx) + (dy * dy));
                                float r = Math.Clamp(1f - ((d - 0.8f) / 2.6f), 0f, 1f);
                                if (r > innerRim) innerRim = r;
                            }
                        }
                    }
                }

                byte bodyB = Mix(0x52, 0x36, (nx * 0.3f) + (ny * 0.7f));
                byte bodyG = Mix(0x3E, 0x28, (nx * 0.3f) + (ny * 0.7f));
                byte bodyR = Mix(0x30, 0x1E, (nx * 0.3f) + (ny * 0.7f));

                outPx[bi] = Mix(bodyB, 0xFF, innerRim);
                outPx[bi + 1] = Mix(bodyG, 0xF2, innerRim);
                outPx[bi + 2] = Mix(bodyR, 0x58, innerRim);
                outPx[bi + 3] = a;
            }
        }

        // Fill the inner chamber at rest with a dark obsidian screen + subtle cyan code lines,
        // while keeping alpha <= 210 so the chamber reads as a recessed dark screen on any dock colour.
        if (holeBytes is not null)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int bi = ((y * w) + x) * 4;
                    byte ha = holeBytes[bi + 3];
                    if (ha < 128 || outPx[bi + 3] > 180) continue;
                    outPx[bi] = 0x1C;
                    outPx[bi + 1] = 0x14;
                    outPx[bi + 2] = 0x0E;
                    outPx[bi + 3] = ha;
                }
            }
        }

        return true;
    }

    private static bool PaintAntigravityColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float nx = x / (float)Math.Max(1, w - 1);

                // Grounded dark space-navy rounded square tile (#161924 -> #222738 top bevel).
                byte tileB = Mix(0x36, 0x22, ny);
                byte tileG = Mix(0x26, 0x18, ny);
                byte tileR = Mix(0x20, 0x14, ny);

                float cardTone = Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B;
                float shipWeight = Math.Clamp((0.92f - cardTone) / 0.34f, 0f, 1f);
                if (shipWeight > 0.02f && nx > 0.10f && nx < 0.90f && ny > 0.10f && ny < 0.88f)
                {
                    // Sharp 4-zone Antigravity / Google delta-wing palette so Red, Blue, Yellow, Green stay vibrant.
                    float dRed = ((nx - 0.28f) * (nx - 0.28f)) + ((ny - 0.48f) * (ny - 0.48f));
                    float dBlue = ((nx - 0.52f) * (nx - 0.52f)) + ((ny - 0.22f) * (ny - 0.22f));
                    float dYellow = ((nx - 0.74f) * (nx - 0.74f)) + ((ny - 0.54f) * (ny - 0.54f));
                    float dGreen = ((nx - 0.48f) * (nx - 0.48f)) + ((ny - 0.78f) * (ny - 0.78f));

                    float wRed = 1f / (0.0025f + (dRed * dRed * 18f));
                    float wBlue = 1f / (0.0025f + (dBlue * dBlue * 18f));
                    float wYellow = 1f / (0.0025f + (dYellow * dYellow * 18f));
                    float wGreen = 1f / (0.0025f + (dGreen * dGreen * 18f));
                    float wSum = wRed + wBlue + wYellow + wGreen;

                    byte sR = (byte)Math.Clamp(((0xFF * wRed) + (0x38 * wBlue) + (0xFF * wYellow) + (0x28 * wGreen)) / wSum, 0, 255);
                    byte sG = (byte)Math.Clamp(((0x3B * wRed) + (0x84 * wBlue) + (0xCC * wYellow) + (0xD0 * wGreen)) / wSum, 0, 255);
                    byte sB = (byte)Math.Clamp(((0x30 * wRed) + (0xFF * wBlue) + (0x00 * wYellow) + (0x48 * wGreen)) / wSum, 0, 255);

                    outPx[bi] = Mix(tileB, sB, shipWeight);
                    outPx[bi + 1] = Mix(tileG, sG, shipWeight);
                    outPx[bi + 2] = Mix(tileR, sR, shipWeight);
                }
                else
                {
                    outPx[bi] = tileB;
                    outPx[bi + 1] = tileG;
                    outPx[bi + 2] = tileR;
                }

                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintClaudeColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int y = 0; y < h; y++)
        {
            float dy = (y / (float)Math.Max(1, h - 1)) - 0.485f;
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float dx = (x / (float)Math.Max(1, w - 1)) - 0.542f;
                float r = MathF.Sqrt((dx * dx) + (dy * dy));
                float coreGlow = Math.Clamp(1f - (r * 1.85f), 0f, 1f);
                outPx[bi] = Mix(0x52, 0x82, coreGlow);
                outPx[bi + 1] = Mix(0x74, 0xA2, coreGlow);
                outPx[bi + 2] = Mix(0xD9, 0xFA, coreGlow);
                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintOrcaSlicerColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float cardTone = Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B;
                float orcaT = Math.Clamp((cardTone - 0.36f) / 0.54f, 0f, 1f);

                byte plateB = 0x2C, plateG = 0x22, plateR = 0x13;
                byte orcaB = Mix(0xC8, 0xF8, 1f - ny);
                byte orcaG = Mix(0xD8, 0xFF, 1f - ny);
                byte orcaR = Mix(0x00, 0x90, 1f - ny);

                outPx[bi] = Mix(plateB, orcaB, orcaT);
                outPx[bi + 1] = Mix(plateG, orcaG, orcaT);
                outPx[bi + 2] = Mix(plateR, orcaR, orcaT);
                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintFileExplorerColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float nx = x / (float)Math.Max(1, w - 1);
                float cardTone = Math.Clamp(Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B, 0.3f, 1f);

                if (ny >= 0.185f && ny < 0.245f && nx >= 0.22f && nx <= 0.82f)
                {
                    // Windows 11 Explorer Azure Blue pocket clip strip (#0078D4 -> #2998F2).
                    outPx[bi] = 0xF0;
                    outPx[bi + 1] = 0x8A;
                    outPx[bi + 2] = 0x10;
                }
                else if (ny < 0.23f)
                {
                    // Back folder tab: Deeper Amber-Gold (#E59A00).
                    outPx[bi] = (byte)Math.Round(0x08 * cardTone);
                    outPx[bi + 1] = (byte)Math.Round(0x9E * cardTone);
                    outPx[bi + 2] = (byte)Math.Round(0xEB * cardTone);
                }
                else
                {
                    // Front folder flap: Rich Golden-Yellow gradient (#FFE26A -> #F5B700).
                    float flapT = Math.Clamp((ny - 0.23f) / 0.75f, 0f, 1f);
                    outPx[bi] = (byte)Math.Round(Mix(0x68, 0x0A, flapT) * cardTone);
                    outPx[bi + 1] = (byte)Math.Round(Mix(0xE2, 0xBA, flapT) * cardTone);
                    outPx[bi + 2] = (byte)Math.Round(Mix(0xFF, 0xF7, flapT) * cardTone);
                }

                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintTerminalColor(int w, int h, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetTerminalAnatomy(monoLogo) is not { } term) return false;
        if (!CopyStraightBgra32(term.FilledBody, out _, out _, out var bodyBytes)) return false;

        Array.Clear(outPx, 0, outPx.Length);
        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte ba = bodyBytes[bi + 3];
            if (ba == 0) continue;
            outPx[bi] = 0x26;
            outPx[bi + 1] = 0x1E;
            outPx[bi + 2] = 0x19;
            outPx[bi + 3] = ba;
        }

        ReadOnlySpan<Color> tabColors =
        [
            Color.FromRgb(0x4C, 0xC2, 0xFF),
            Color.FromRgb(0x88, 0x96, 0xAB),
            Color.FromRgb(0x58, 0x64, 0x78),
        ];

        for (int t = 0; t < term.TopTabs.Count; t++)
        {
            var tc = tabColors[Math.Min(t, tabColors.Length - 1)];
            if (!CopyStraightBgra32(term.TopTabs[t].Bitmap, out _, out _, out var tBytes)) continue;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte ta = tBytes[bi + 3];
                if (ta == 0) continue;
                outPx[bi] = tc.B;
                outPx[bi + 1] = tc.G;
                outPx[bi + 2] = tc.R;
                outPx[bi + 3] = ta;
            }
        }

        void OverlayPart(CyberIconAnatomy.IconPart? part, Color c)
        {
            if (part is not { } validPart || !CopyStraightBgra32(validPart.Bitmap, out _, out _, out var pBytes)) return;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte pa = pBytes[bi + 3];
                if (pa == 0) continue;
                double k = pa / 255.0;
                outPx[bi] = Mix(outPx[bi], c.B, k);
                outPx[bi + 1] = Mix(outPx[bi + 1], c.G, k);
                outPx[bi + 2] = Mix(outPx[bi + 2], c.R, k);
                if (pa > outPx[bi + 3]) outPx[bi + 3] = pa;
            }
        }

        OverlayPart(term.PromptHole, Color.FromRgb(0xF6, 0xFA, 0xFF));
        OverlayPart(term.CursorHole, Color.FromRgb(0x00, 0xF0, 0xFF));
        return true;
    }

    private static bool PaintTaskManagerColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte a = monoPx[bi + 3];
            if (a == 0) continue;
            float cardTone = Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B;
            if (cardTone < 0.45f)
            {
                // Monitor sky: Deep Navy (#121D28)
                outPx[bi] = 0x28;
                outPx[bi + 1] = 0x1D;
                outPx[bi + 2] = 0x12;
            }
            else if (cardTone < 0.76f)
            {
                // Secondary CPU wave: Teal-Blue (#008B9E)
                outPx[bi] = 0x9E;
                outPx[bi + 1] = 0x8B;
                outPx[bi + 2] = 0x00;
            }
            else
            {
                // Primary CPU wave: Electric Cyan-Mint (#00F5D4)
                outPx[bi] = 0xD4;
                outPx[bi + 1] = 0xF5;
                outPx[bi + 2] = 0x00;
            }

            outPx[bi + 3] = a;
        }

        return true;
    }

    private static bool PaintSettingsGearColor(int w, int h, byte[] monoPx, byte[] outPx)
    {
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float cardTone = Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B;
                float hubT = Math.Clamp((cardTone - 0.42f) / 0.48f, 0f, 1f);

                byte gearB = Mix(0xF8, 0xC8, ny);
                byte gearG = Mix(0x90, 0x62, ny);
                byte gearR = Mix(0x30, 0x16, ny);

                outPx[bi] = Mix(gearB, 0xFF, hubT);
                outPx[bi + 1] = Mix(gearG, 0xF8, hubT);
                outPx[bi + 2] = Mix(gearR, 0xF0, hubT);
                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintNotepadColor(int w, int h, byte[] monoPx, byte[] outPx, ImageSource monoLogo)
    {
        byte[]? penBytes = null;
        if (CyberIconAnatomy.SplitSeam(monoLogo, seamKind: 1) is { } seam)
        {
            CopyStraightBgra32(seam.Second, out _, out _, out penBytes);
        }

        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float cardTone = Math.Clamp(Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B, 0.3f, 1f);
                byte penA = penBytes is not null ? penBytes[bi + 3] : (byte)0;

                if (penA > 128)
                {
                    // Diagonal stylus pen: Crisp Ice-White / Cyan (#B8F4FF)
                    outPx[bi] = 0xFF;
                    outPx[bi + 1] = 0xF4;
                    outPx[bi + 2] = 0xB8;
                }
                else if (ny < 0.21f)
                {
                    // Top binding bar: Deep Teal-Navy (#005A87)
                    outPx[bi] = 0x98;
                    outPx[bi + 1] = 0x62;
                    outPx[bi + 2] = 0x05;
                }
                else
                {
                    // Notepad page: Vibrant Aqua-Teal (#1DA8E8)
                    outPx[bi] = (byte)Math.Round(0xF2 * cardTone);
                    outPx[bi + 1] = (byte)Math.Round(0xB4 * cardTone);
                    outPx[bi + 2] = (byte)Math.Round(0x1E * cardTone);
                }

                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static bool PaintBlenderColor(int w, int h, byte[] monoPx, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetConnectedIslands(monoLogo) is not { Count: >= 2 } islands) return false;
        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        CopyStraightBgra32(sorted[1].Bitmap, out _, out _, out var pupilBytes);

        for (int i = 0; i < w * h; i++)
        {
            int bi = i * 4;
            byte a = monoPx[bi + 3];
            if (a == 0) continue;
            float cardTone = Math.Clamp(Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B, 0.35f, 1f);
            if (pupilBytes[bi + 3] > 60)
            {
                // Eye pupil: Blender Cobalt Blue (#2563EB)
                outPx[bi] = (byte)Math.Round(0xF5 * cardTone);
                outPx[bi + 1] = (byte)Math.Round(0x68 * cardTone);
                outPx[bi + 2] = (byte)Math.Round(0x22 * cardTone);
            }
            else
            {
                // Finned body: Blender Orange (#F5792A)
                outPx[bi] = (byte)Math.Round(0x2A * cardTone);
                outPx[bi + 1] = (byte)Math.Round(0x7C * cardTone);
                outPx[bi + 2] = (byte)Math.Round(0xFA * cardTone);
            }

            outPx[bi + 3] = a;
        }

        return true;
    }

    private static bool PaintSevenZipColor(int w, int h, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetConnectedIslands(monoLogo) is not { Count: >= 3 } islands) return false;
        Array.Clear(outPx, 0, outPx.Length);
        var sorted = islands.OrderByDescending(i => i.PixelCount).ToList();
        var frame = sorted[0];
        var inner = sorted.Skip(1).OrderBy(i => i.NormalizedCenter.X).ToList();

        void PaintPart(ImageSource bmp, Color c)
        {
            if (!CopyStraightBgra32(bmp, out _, out _, out var bytes)) return;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte a = bytes[bi + 3];
                if (a == 0) continue;
                outPx[bi] = c.B;
                outPx[bi + 1] = c.G;
                outPx[bi + 2] = c.R;
                outPx[bi + 3] = a;
            }
        }

        PaintPart(frame.Bitmap, Color.FromRgb(0x32, 0x3C, 0x4E));
        for (int i = 0; i < inner.Count; i++)
        {
            PaintPart(inner[i].Bitmap, i == 0 ? Color.FromRgb(0x00, 0xE5, 0xFF) : Color.FromRgb(0xFF, 0xB7, 0x03));
        }

        return true;
    }

    private static bool PaintRivatunerColor(int w, int h, byte[] outPx, ImageSource monoLogo)
    {
        if (CyberIconAnatomy.GetRivatunerAnatomy(monoLogo) is not { } riva) return false;
        Array.Clear(outPx, 0, outPx.Length);

        void PaintLayer(ImageSource? bmp, Color c)
        {
            if (bmp is null || !CopyStraightBgra32(bmp, out _, out _, out var bytes)) return;
            for (int i = 0; i < w * h; i++)
            {
                int bi = i * 4;
                byte a = bytes[bi + 3];
                if (a == 0) continue;
                outPx[bi] = c.B;
                outPx[bi + 1] = c.G;
                outPx[bi + 2] = c.R;
                outPx[bi + 3] = a;
            }
        }

        PaintLayer(riva.ScreenBody, Color.FromRgb(0x25, 0x63, 0xEB));
        PaintLayer(riva.StandBase, Color.FromRgb(0x64, 0x74, 0x8B));
        PaintLayer(riva.FpsBadge?.Bitmap, Color.FromRgb(0xF7, 0x25, 0x85));
        PaintLayer(riva.BezelSlider?.Bitmap, Color.FromRgb(0x00, 0xF0, 0xFF));
        return true;
    }

    private static bool PaintGradientToneColor(int w, int h, byte[] monoPx, byte[] outPx, Color darkColor, Color brightColor)
    {
        for (int y = 0; y < h; y++)
        {
            float ny = y / (float)Math.Max(1, h - 1);
            for (int x = 0; x < w; x++)
            {
                int bi = ((y * w) + x) * 4;
                byte a = monoPx[bi + 3];
                if (a == 0) continue;
                float cardTone = Math.Clamp(Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2])) / (float)Grey.B, 0.25f, 1f);
                float t = Math.Clamp(((cardTone - 0.25f) / 0.75f * 0.7f) + ((1f - ny) * 0.3f), 0f, 1f);
                outPx[bi] = Mix(darkColor.B, brightColor.B, t);
                outPx[bi + 1] = Mix(darkColor.G, brightColor.G, t);
                outPx[bi + 2] = Mix(darkColor.R, brightColor.R, t);
                outPx[bi + 3] = a;
            }
        }

        return true;
    }

    private static void PaintGeneralRealIconColor(
        int lw, int lh, byte[] monoPx, byte[] outPx, ImageSource monoLogo, ImageSource? realIcon)
    {
        float domR = 0.38f, domG = 0.86f, domB = 1.0f;
        float centerR = 1.0f, centerG = 0.55f, centerB = 0.25f;
        var colorGrid = new (float R, float G, float B, float Weight)[lw * lh];

        if (realIcon is not null)
        {
            var realBytes = ExtractBgra32(realIcon, out int rw, out int rh);
            int minX = rw, minY = rh, maxX = -1, maxY = -1;
            for (int y = 0; y < rh; y++)
            {
                int row = y * rw * 4;
                for (int x = 0; x < rw; x++)
                {
                    if (realBytes[row + (x * 4) + 3] >= 24)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX >= minX && maxY >= minY)
            {
                int cropW = maxX - minX + 1;
                int cropH = maxY - minY + 1;
                double dR = 0, dG = 0, dB = 0, dW = 0;
                double cR = 0, cG = 0, cB = 0, cW = 0;

                for (int ly = 0; ly < lh; ly++)
                {
                    int sy = minY + Math.Clamp((int)((ly + 0.5) * cropH / lh), 0, cropH - 1);
                    float ny = (ly / (float)Math.Max(1, lh - 1)) - 0.5f;
                    for (int lx = 0; lx < lw; lx++)
                    {
                        int sx = minX + Math.Clamp((int)((lx + 0.5) * cropW / lw), 0, cropW - 1);
                        float nx = (lx / (float)Math.Max(1, lw - 1)) - 0.5f;
                        int si = ((sy * rw) + sx) * 4;
                        byte b = realBytes[si], g = realBytes[si + 1], r = realBytes[si + 2], a = realBytes[si + 3];
                        if (a < 24) continue;
                        byte maxC = Math.Max(r, Math.Max(g, b));
                        byte minC = Math.Min(r, Math.Min(g, b));
                        if (maxC < 20) continue;

                        float sat = (maxC - minC) / (float)maxC;
                        float val = maxC / 255f;
                        float nR = r / (float)maxC, nG = g / (float)maxC, nB = b / (float)maxC;
                        float w = (a / 255f) * (0.12f + (sat * 2.4f)) * (0.25f + (val * 0.75f));
                        colorGrid[(ly * lw) + lx] = (nR, nG, nB, w);

                        float dw = (a / 255f) * (0.04f + (sat * sat * 4.0f)) * val;
                        dR += nR * dw; dG += nG * dw; dB += nB * dw; dW += dw;

                        if ((nx * nx) + (ny * ny) <= 0.08f && sat > 0.25f)
                        {
                            cR += nR * dw; cG += nG * dw; cB += nB * dw; cW += dw;
                        }
                    }
                }

                if (dW > 0.01)
                {
                    domR = (float)(dR / dW); domG = (float)(dG / dW); domB = (float)(dB / dW);
                    float fMax = Math.Max(domR, Math.Max(domG, domB));
                    float fMin = Math.Min(domR, Math.Min(domG, domB));
                    if (fMax > 0.01f) { domR /= fMax; domG /= fMax; domB /= fMax; }
                    if (fMax - fMin < 0.12f) { domR = 0.42f; domG = 0.88f; domB = 1.0f; }
                }

                if (cW > 0.01)
                {
                    centerR = (float)(cR / cW); centerG = (float)(cG / cW); centerB = (float)(cB / cW);
                    float cMax = Math.Max(centerR, Math.Max(centerG, centerB));
                    if (cMax > 0.01f) { centerR /= cMax; centerG /= cMax; centerB /= cMax; }
                }
                else
                {
                    centerR = 0.96f; centerG = 0.98f; centerB = 1.0f;
                }
            }
        }

        // Check if this card has a dark inner well (e.g. Media Player) or multi-tone layers.
        byte minT = 255, maxT = 0;
        for (int i = 0; i < lw * lh; i++)
        {
            if (monoPx[(i * 4) + 3] < 200) continue;
            byte t = Math.Max(monoPx[i * 4], Math.Max(monoPx[(i * 4) + 1], monoPx[(i * 4) + 2]));
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }

        bool hasThreeToneWell = (maxT - minT) >= 40;
        const int radius = 5;

        for (int y = 0; y < lh; y++)
        {
            float ny = (y / (float)Math.Max(1, lh - 1)) - 0.5f;
            for (int x = 0; x < lw; x++)
            {
                int bi = ((y * lw) + x) * 4;
                byte cardA = monoPx[bi + 3];
                if (cardA == 0) continue;

                float nx = (x / (float)Math.Max(1, lw - 1)) - 0.5f;
                float rDist = MathF.Sqrt((nx * nx) + (ny * ny));
                byte cardMax = Math.Max(monoPx[bi], Math.Max(monoPx[bi + 1], monoPx[bi + 2]));
                float cardTone = Math.Clamp(cardMax / (float)Grey.B, 0f, 1.15f);

                if (hasThreeToneWell && cardTone < 0.46f)
                {
                    // Dark inner well: rich deep shade of the dominant color.
                    outPx[bi] = (byte)Math.Clamp(Math.Round(domB * 58f), 18, 70);
                    outPx[bi + 1] = (byte)Math.Clamp(Math.Round(domG * 46f), 14, 58);
                    outPx[bi + 2] = (byte)Math.Clamp(Math.Round(domR * 58f), 18, 70);
                    outPx[bi + 3] = cardA;
                    continue;
                }

                if (hasThreeToneWell && rDist <= 0.25f && cardTone >= 0.65f)
                {
                    // Inner high-contrast center emblem (e.g. Media Player's coral-orange '►' play triangle).
                    outPx[bi] = (byte)Math.Clamp(Math.Round(centerB * 248f), 0, 255);
                    outPx[bi + 1] = (byte)Math.Clamp(Math.Round(centerG * 248f), 0, 255);
                    outPx[bi + 2] = (byte)Math.Clamp(Math.Round(centerR * 248f), 0, 255);
                    outPx[bi + 3] = cardA;
                    continue;
                }

                float sumR = 0, sumG = 0, sumB = 0, sumW = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int cy = y + dy;
                    if ((uint)cy >= (uint)lh) continue;
                    int gRow = cy * lw;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int cx = x + dx;
                        if ((uint)cx >= (uint)lw) continue;
                        var cell = colorGrid[gRow + cx];
                        if (cell.Weight <= 0) continue;
                        float w = cell.Weight / (1f + (dx * dx) + (dy * dy));
                        sumR += cell.R * w; sumG += cell.G * w; sumB += cell.B * w; sumW += w;
                    }
                }

                float nR = domR, nG = domG, nB = domB;
                if (sumW > 0.05f)
                {
                    float lR = sumR / sumW, lG = sumG / sumW, lB = sumB / sumW;
                    float lMax = Math.Max(lR, Math.Max(lG, lB));
                    float lMin = Math.Min(lR, Math.Min(lG, lB));
                    if (lMax > 0.01f) { lR /= lMax; lG /= lMax; lB /= lMax; }
                    float localBlend = Math.Clamp(((lMax - lMin) - 0.12f) / 0.25f, 0f, 1f);
                    nR = (domR * (1f - localBlend)) + (lR * localBlend);
                    nG = (domG * (1f - localBlend)) + (lG * localBlend);
                    nB = (domB * (1f - localBlend)) + (lB * localBlend);
                    float bMax = Math.Max(nR, Math.Max(nG, nB));
                    if (bMax > 0.01f) { nR /= bMax; nG /= bMax; nB /= bMax; }
                }

                float peak = Math.Clamp(242f * cardTone, 0f, 255f);
                outPx[bi] = (byte)Math.Clamp(Math.Round(nB * peak), 0, 255);
                outPx[bi + 1] = (byte)Math.Clamp(Math.Round(nG * peak), 0, 255);
                outPx[bi + 2] = (byte)Math.Clamp(Math.Round(nR * peak), 0, 255);
                outPx[bi + 3] = cardA;
            }
        }

        // If the synthesized card has transparent interior cutout holes, fill them with crisp white
        // so the dock background colour never bleeds through the symbol.
        if (CyberIconAnatomy.GetCutoutStencil(monoLogo) is { Parts.Count: >= 1 } stencil)
        {
            foreach (var part in stencil.Parts)
            {
                if (!part.IsCutoutHole || !CopyStraightBgra32(part.Bitmap, out _, out _, out var pBytes)) continue;
                for (int i = 0; i < lw * lh; i++)
                {
                    int bi = i * 4;
                    byte pa = pBytes[bi + 3];
                    if (pa == 0) continue;
                    double t = pa / 255.0;
                    outPx[bi] = Mix(outPx[bi], 0xFF, t);
                    outPx[bi + 1] = Mix(outPx[bi + 1], 0xF8, t);
                    outPx[bi + 2] = Mix(outPx[bi + 2], 0xF4, t);
                    if (pa > outPx[bi + 3]) outPx[bi + 3] = pa;
                }
            }
        }
    }

    private static byte[] ExtractBgra32(ImageSource icon, out int width, out int height)
    {
        BitmapSource source;
        if (icon is BitmapSource bmp && bmp.PixelWidth is > 0 and <= 256 && bmp.PixelHeight is > 0 and <= 256)
        {
            source = bmp.Format == PixelFormats.Bgra32 ? bmp : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        }
        else
        {
            const int renderSize = 128;
            var scene = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(scene, BitmapScalingMode.HighQuality);
            using (var dc = scene.RenderOpen()) dc.DrawImage(icon, new Rect(0, 0, renderSize, renderSize));
            var target = new RenderTargetBitmap(renderSize, renderSize, 96, 96, PixelFormats.Pbgra32);
            target.Render(scene);
            source = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        }

        width = source.PixelWidth;
        height = source.PixelHeight;
        int stride = width * 4;
        var bytes = new byte[stride * height];
        source.CopyPixels(bytes, stride, 0);
        return bytes;
    }

    private static ImageSource? TryLoadCachedCard(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(cachePath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            if (image.PixelWidth != CardPixels || image.PixelHeight != CardPixels) return null;
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveCachedCard(string cachePath, BitmapSource bitmap)
    {
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(ForgedCacheDir);
            tmp = Path.Combine(ForgedCacheDir, $"{Path.GetFileNameWithoutExtension(cachePath)}.{Guid.NewGuid():N}.tmp");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch
        {
            if (tmp is not null)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* Best effort cleanup. */ }
            }
        }
    }

    private static byte Mix(byte from, byte to, double amount) => (byte)(from + ((to - from) * amount));
}
