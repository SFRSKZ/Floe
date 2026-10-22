using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.Core.Interop;
using Floe.Core.Models;

namespace Floe.App.Services;

public static class IconService
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const int WmGetIcon = 0x007F;
    private const int GclpHIcon = -14;
    private const int GclpHIconSm = -34;

    private const long WindowIconLifetimeMs = 30_000;

    private static readonly Dictionary<(IntPtr Hwnd, int Box), (ImageSource? Icon, uint ProcessId, long Stamp)> WindowIconCache = new();

    public static ImageSource? GetWindowIcon(IntPtr hwnd, int box = 58)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        var key = (hwnd, box);
        // negative results are cached too: an iconless window would otherwise repeat the WM_GETICON roundtrip every refresh
        if (WindowIconCache.TryGetValue(key, out var cached) &&
            cached.ProcessId == processId &&
            Environment.TickCount64 - cached.Stamp < WindowIconLifetimeMs &&
            IsWindow(hwnd))
        {
            return cached.Icon;
        }

        var icon = ResolveWindowIcon(hwnd, box);
        if (WindowIconCache.Count >= 256)
        {
            WindowIconCache.Clear();
        }

        WindowIconCache[key] = (icon, processId, Environment.TickCount64);
        return icon;
    }

    private static ImageSource? ResolveWindowIcon(IntPtr hwnd, int box)
    {
        var handle = SendMessage(hwnd, WmGetIcon, (IntPtr)1, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            handle = SendMessage(hwnd, WmGetIcon, (IntPtr)2, IntPtr.Zero);
        }

        if (handle == IntPtr.Zero)
        {
            handle = GetClassLongPtr(hwnd, GclpHIcon);
        }

        if (handle == IntPtr.Zero)
        {
            handle = GetClassLongPtr(hwnd, GclpHIconSm);
        }

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // window/class icons are owned by the window; do not destroy
            var bitmap = FromHIcon(handle);
            return Normalize(bitmap, box);
        }
        catch
        {
            return null;
        }
    }

    private static readonly Dictionary<string, Color> GlowCache = new();

    public static Color GetGlowColor(DockApp app)
    {
        var key = $"glow:{app.Target}#{app.IconPath}";
        if (GlowCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? raw = TryLoadImageFile(app.IconPath) ?? TryLoadImageFile(app.Target);
        var target = Environment.ExpandEnvironmentVariables(app.Target ?? string.Empty).Trim();

        if (raw is null && target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var hbm = ShellImage.GetHBitmap(target, 128);
            if (hbm != IntPtr.Zero)
            {
                try
                {
                    raw = FromHBitmap(hbm);
                }
                finally
                {
                    ShellImage.DeleteHBitmap(hbm);
                }
            }
        }

        if (raw is null)
        {
            var (file, index) = ResolveIconSource(app);
            if (file is not null)
            {
                raw = LoadRawIcon(file, index);
            }
        }

        var color = raw is null ? Colors.White : DominantColor(raw);
        GlowCache[key] = color;
        return color;
    }

    private static Color DominantColor(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        var counts = new int[24];
        var rSum = new long[24];
        var gSum = new long[24];
        var bSum = new long[24];
        var best = -1;
        var bestCount = 0;

        var bounds = AlphaBounds(pixels, width, height, stride);
        var yTop = bounds.Y;
        var yBottom = Math.Min(height, bounds.Y + Math.Max(1, (int)(bounds.H * 0.5)));

        for (var y = yTop; y < yBottom; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = PixelAt(pixels, stride, x, y);
                if (p.A < 180)
                {
                    continue;
                }

                int max = Math.Max(p.R, Math.Max(p.G, p.B));
                int min = Math.Min(p.R, Math.Min(p.G, p.B));
                if (max < 70)
                {
                    continue;
                }

                var saturation = (max - min) / (double)max;
                if (saturation < 0.30)
                {
                    continue;
                }

                var hue = Hue(p.R, p.G, p.B);
                var bucket = (int)(hue / 15.0) % 24;
                counts[bucket]++;
                rSum[bucket] += p.R;
                gSum[bucket] += p.G;
                bSum[bucket] += p.B;

                if (counts[bucket] > bestCount)
                {
                    bestCount = counts[bucket];
                    best = bucket;
                }
            }
        }

        if (best < 0)
        {
            return Color.FromRgb(0xF0, 0xF0, 0xF5);
        }

        return Color.FromRgb(
            (byte)(rSum[best] / counts[best]),
            (byte)(gSum[best] / counts[best]),
            (byte)(bSum[best] / counts[best]));
    }

    private static double Hue(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var delta = max - min;
        if (delta <= 0)
        {
            return 0;
        }

        double hue;
        if (max == rd)
        {
            hue = 60 * (((gd - bd) / delta) % 6);
        }
        else if (max == gd)
        {
            hue = 60 * (((bd - rd) / delta) + 2);
        }
        else
        {
            hue = 60 * (((rd - gd) / delta) + 4);
        }

        return hue < 0 ? hue + 360 : hue;
    }

    public static ImageSource? GetIcon(DockApp app, int box = 58)
    {
        var key = $"{app.Target}#{app.IconPath}#{box}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ImageSource? result = null;
        var target = Environment.ExpandEnvironmentVariables(app.Target ?? string.Empty).Trim();

        var overrideImage = TryLoadImageFile(app.IconPath) ?? TryLoadImageFile(app.Target);
        if (overrideImage is not null)
        {
            result = Normalize(overrideImage, box);
            Cache[key] = result;
            return result;
        }

        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var hbm = ShellImage.GetHBitmap(target, 256);
            if (hbm != IntPtr.Zero)
            {
                try
                {
                    result = Normalize(FromHBitmap(hbm), box);
                }
                finally
                {
                    ShellImage.DeleteHBitmap(hbm);
                }
            }
        }

        if (result is null)
        {
            var (file, index) = ResolveIconSource(app);
            if (file is not null)
            {
                var raw = LoadRawIcon(file, index);
                if (raw is null)
                {
                    var hbm = ShellImage.GetHBitmap(file, 256);
                    if (hbm != IntPtr.Zero)
                    {
                        try
                        {
                            raw = FromHBitmap(hbm);
                        }
                        finally
                        {
                            ShellImage.DeleteHBitmap(hbm);
                        }
                    }
                }

                if (raw is not null)
                {
                    result = Normalize(raw, box);
                }
            }
        }

        Cache[key] = result;
        return result;
    }

    private static BitmapSource FromHBitmap(IntPtr hBitmap)
        => Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static BitmapSource? TryLoadImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cleaned = Environment.ExpandEnvironmentVariables(path.Split(',')[0].Trim().Trim('"'));
        var extension = Path.GetExtension(cleaned).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico") || !File.Exists(cleaned))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(cleaned);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadRawIcon(string file, int index)
    {
        foreach (var size in new[] { 256, 128, 64, 48, 32 })
        {
            var icons = new IntPtr[1];
            try
            {
                var count = PrivateExtractIcons(file, index, size, size, icons, null!, 1, 0);
                if (count > 0 && icons[0] != IntPtr.Zero)
                {
                    return FromHIcon(icons[0]);
                }
            }
            catch
            {
                // try the next size
            }
            finally
            {
                if (icons[0] != IntPtr.Zero)
                {
                    DestroyIcon(icons[0]);
                }
            }
        }

        var large = new IntPtr[1];
        try
        {
            var n = ExtractIconEx(file, index, large, null, 1);
            if (n > 0 && large[0] != IntPtr.Zero)
            {
                return FromHIcon(large[0]);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large[0] != IntPtr.Zero)
            {
                DestroyIcon(large[0]);
            }
        }

        return null;
    }

    private static BitmapSource FromHIcon(IntPtr hIcon)
        => Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

    private static BitmapSource Normalize(BitmapSource source, int box)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        var bounds = AlphaBounds(pixels, width, height, stride);
        var (bx, by, bw, bh) = bounds;

        var modified = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        modified.Freeze();

        var cropped = new CroppedBitmap(modified, new Int32Rect(bx, by, bw, bh));
        cropped.Freeze();
        return cropped;
    }

    private static void RemoveDominantBackground(byte[] pixels, int width, int height, int stride, int bx, int by, int bw, int bh)
    {
        if (bw <= 0 || bh <= 0)
        {
            return;
        }

        // only strip a solid background when it wraps the icon (bbox corners are the background);
        // this leaves logos that merely contain white (e.g. ChatGPT) untouched.
        var corners = new[]
        {
            PixelAt(pixels, stride, bx, by),
            PixelAt(pixels, stride, bx + bw - 1, by),
            PixelAt(pixels, stride, bx, by + bh - 1),
            PixelAt(pixels, stride, bx + bw - 1, by + bh - 1),
        };
        foreach (var corner in corners)
        {
            if (corner.A < 200 || corner.R < 200 || corner.G < 200 || corner.B < 200)
            {
                return;
            }
        }

        var opaqueArea = 0;
        for (var y = by; y < by + bh; y++)
        {
            for (var x = bx; x < bx + bw; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] >= 200)
                {
                    opaqueArea++;
                }
            }
        }

        if (opaqueArea <= 0)
        {
            return;
        }

        var counts = new Dictionary<int, int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = PixelAt(pixels, stride, x, y);
                if (p.A < 200)
                {
                    continue;
                }

                var key = ((p.R >> 4) << 8) | ((p.G >> 4) << 4) | (p.B >> 4);
                counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
            }
        }

        if (counts.Count == 0)
        {
            return;
        }

        var bestKey = -1;
        var bestCount = 0;
        foreach (var pair in counts)
        {
            if (pair.Value > bestCount)
            {
                bestCount = pair.Value;
                bestKey = pair.Key;
            }
        }

        var r4 = (bestKey >> 8) & 0xF;
        var g4 = (bestKey >> 4) & 0xF;
        var b4 = bestKey & 0xF;

        // only act when the dominant colour is near-white and covers a large share
        if (r4 < 13 || g4 < 13 || b4 < 13)
        {
            return;
        }

        if ((double)bestCount / opaqueArea < 0.20)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                if (pixels[offset + 3] >= 200 && pixels[offset + 2] > 205 && pixels[offset + 1] > 205 && pixels[offset] > 205)
                {
                    pixels[offset + 3] = 0;
                }
            }
        }
    }

    private static (int X, int Y, int W, int H) AlphaBounds(byte[] pixels, int width, int height, int stride)
    {
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] <= 8)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
        {
            return (0, 0, width, height);
        }

        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(byte[] pixels, int stride, int x, int y)
    {
        var offset = (y * stride) + (x * 4);
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    public static Color[] GetIconColors(ImageSource? source, int count)
    {
        if (source is not BitmapSource bitmap || count <= 0)
        {
            return DefaultAuraColors(count);
        }

        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return DefaultAuraColors(count);
        }

        var stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        var counts = new int[24];
        var rSum = new long[24];
        var gSum = new long[24];
        var bSum = new long[24];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = PixelAt(pixels, stride, x, y);
                if (p.A < 160)
                {
                    continue;
                }

                var max = Math.Max(p.R, Math.Max(p.G, p.B));
                var min = Math.Min(p.R, Math.Min(p.G, p.B));
                if (max < 70 || (max - min) / (double)max < 0.30)
                {
                    continue;
                }

                var bucket = (int)(Hue(p.R, p.G, p.B) / 15.0) % 24;
                counts[bucket]++;
                rSum[bucket] += p.R;
                gSum[bucket] += p.G;
                bSum[bucket] += p.B;
            }
        }

        var picked = new List<Color>();
        foreach (var bucket in Enumerable.Range(0, 24).OrderByDescending(i => counts[i]))
        {
            if (picked.Count >= count || counts[bucket] == 0)
            {
                break;
            }

            var color = Color.FromRgb(
                (byte)(rSum[bucket] / counts[bucket]),
                (byte)(gSum[bucket] / counts[bucket]),
                (byte)(bSum[bucket] / counts[bucket]));

            var hue = Hue(color.R, color.G, color.B);
            if (picked.Any(c => HueDistance(Hue(c.R, c.G, c.B), hue) < 45))
            {
                continue;
            }

            picked.Add(color);
        }

        if (picked.Count == 0)
        {
            // neutral icon (gray/black/white): use a bright monochrome glow from its own brightness
            var (luminance, tintHue, tintSat) = AverageColor(pixels, stride, width, height);
            var saturation = Math.Min(tintSat, 0.12);
            var value = luminance < 140 ? 1.0 : 0.88;
            var neutral = new List<Color>();
            for (var i = 0; i < count; i++)
            {
                neutral.Add(FromHsv(tintHue, saturation, Math.Clamp(value - (i * 0.14), 0.5, 1.0)));
            }

            return neutral.ToArray();
        }

        // missing colors: stay in the icon's own color family (analogous hues), not random triads
        var found = picked.ToArray();
        var variations = new[] { 28.0, -28.0, 52.0, -52.0 };
        var step = 0;
        while (picked.Count < count)
        {
            var (h, s, v) = ToHsv(found[picked.Count % found.Length]);
            picked.Add(FromHsv(h + variations[step % variations.Length], Math.Max(s, 0.62), Math.Max(v, 0.92)));
            step++;
        }

        for (var i = 0; i < picked.Count; i++)
        {
            var (h, s, v) = ToHsv(picked[i]);
            picked[i] = FromHsv(h, Math.Max(s, 0.6), Math.Max(v, 0.9));
        }

        return picked.ToArray();
    }

    private static (double Luminance, double Hue, double Saturation) AverageColor(byte[] pixels, int stride, int width, int height)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        long n = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = PixelAt(pixels, stride, x, y);
                if (p.A < 160)
                {
                    continue;
                }

                r += p.R;
                g += p.G;
                b += p.B;
                n++;
            }
        }

        if (n == 0)
        {
            return (128, 0, 0);
        }

        var avg = Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        var luminance = (0.299 * avg.R) + (0.587 * avg.G) + (0.114 * avg.B);
        var (h, s, _) = ToHsv(avg);
        return (luminance, h, s);
    }

    private static Color[] DefaultAuraColors(int count)
    {
        var defaults = new[]
        {
            Color.FromRgb(0x5B, 0xC8, 0xFF),
            Color.FromRgb(0xA8, 0x55, 0xF7),
            Color.FromRgb(0x34, 0xE0, 0x7A),
        };

        return defaults.Take(Math.Max(0, count)).ToArray();
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        return (Hue(color.R, color.G, color.B), max <= 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - c;
        double r, g, b;

        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (string? File, int Index) ResolveIconSource(DockApp app)
    {
        var icon = app.IconPath;
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var comma = icon.LastIndexOf(',');
            string pathPart;
            var index = 0;

            if (comma > 0 && int.TryParse(icon[(comma + 1)..].Trim(), out var parsed))
            {
                pathPart = icon[..comma];
                index = parsed;
            }
            else
            {
                pathPart = icon;
            }

            pathPart = Environment.ExpandEnvironmentVariables(pathPart.Trim().Trim('"'));
            if (!string.IsNullOrWhiteSpace(pathPart) && File.Exists(pathPart))
            {
                return (pathPart, index);
            }
        }

        var target = Environment.ExpandEnvironmentVariables(app.Target ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
        {
            return (target, 0);
        }

        return (null, 0);
    }
}
