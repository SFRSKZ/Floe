using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SceneLab.Render;

/// <summary>PNG writing, the dock's backdrop composite, a contact sheet with labels, and the mp4.</summary>
public static class Frames
{
    /// <summary>The dock's backdrop, so a transparent strip looks like it does on screen (FLOE_GPU_BACKDROP=RRGGBB).</summary>
    public static readonly int Backdrop = int.TryParse(Environment.GetEnvironmentVariable("FLOE_GPU_BACKDROP"),
        System.Globalization.NumberStyles.HexNumber, null, out var backdrop) ? backdrop : 0x0B0E14;

    public static int BackdropR => (Backdrop >> 16) & 0xFF;
    public static int BackdropG => (Backdrop >> 8) & 0xFF;
    public static int BackdropB => Backdrop & 0xFF;

    /// <summary>Premultiplied BGRA over the backdrop colour, opaque result.</summary>
    public static void Composite(byte[] pixels)
    {
        int r = BackdropR, g = BackdropG, b = BackdropB;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            int inverse = 255 - alpha;
            pixels[i] = (byte)Math.Min(255, pixels[i] + (((b * inverse) + 127) / 255));
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + (((g * inverse) + 127) / 255));
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + (((r * inverse) + 127) / 255));
            pixels[i + 3] = 255;
        }
    }

    public static BitmapSource ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(byte[] pixels, int width, int height, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(pixels, width, height)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Mean luminance 0..1 and mean absolute difference against a previous frame, for the motion report.</summary>
    public static (double Luma, double Change) Measure(byte[] pixels, byte[]? previous)
    {
        double luma = 0, change = 0;
        int x4 = pixels.Length / 4;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            luma += (pixels[i] + (2.0 * pixels[i + 1]) + pixels[i + 2]) / 1020.0;
            if (previous is not null)
            {
                change += (Math.Abs(pixels[i] - previous[i]) + Math.Abs(pixels[i + 1] - previous[i + 1]) + Math.Abs(pixels[i + 2] - previous[i + 2])) / 765.0;
            }
        }

        return (luma / x4, previous is null ? 0 : change / x4);
    }

    /// <summary>
    /// The tone spread of a frame: how much of it sits in deep shadow, in the mid tones, and in the highlights.
    /// A flat pixel-art look is nearly all shadow plus a few saturated highlights; a graded, cinematic frame keeps
    /// a large mid-tone share and rolls its highlights off instead of clipping them.
    /// </summary>
    public static (double Shadow, double Mid, double Highlight) Tone(byte[] pixels)
    {
        int shadow = 0, mid = 0, highlight = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double luma = (pixels[i] + (2.0 * pixels[i + 1]) + pixels[i + 2]) / 1020.0;
            if (luma < 0.06) shadow++;
            else if (luma > 0.92) highlight++;
            else mid++;
        }

        double total = Math.Max(1, pixels.Length / 4);
        return (shadow / total, mid / total, highlight / total);
    }

    /// <summary>A vertical contact sheet: each shot scaled to a readable width, labelled, over the backdrop.</summary>
    public static void SaveContactSheet(IReadOnlyList<(string Label, BitmapSource Image)> shots, string path, int width = 1920)
    {
        double scale = width / (double)shots[0].Image.PixelWidth;
        int stripHeight = (int)Math.Round(shots[0].Image.PixelHeight * scale);
        int label = 26;
        int gap = 10;
        int total = (stripHeight + label + gap) * shots.Count;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var backdrop = Color.FromRgb((byte)BackdropR, (byte)BackdropG, (byte)BackdropB);
            dc.DrawRectangle(new SolidColorBrush(backdrop), null, new Rect(0, 0, width, total));
            var typeface = new Typeface("Segoe UI");
            double y = 0;
            foreach (var (text, image) in shots)
            {
                dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 15, Brushes.White, 1.0),
                    new Point(12, y + 4));
                y += label;
                dc.DrawImage(image, new Rect(0, y, width, stripHeight));
                y += stripHeight + gap;
            }
        }

        var render = new RenderTargetBitmap(width, total, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Encodes a PNG sequence to H.264 with the bundled ffmpeg; returns false when ffmpeg is not found.</summary>
    public static bool EncodeMp4(string frameDirectory, int fps, string output, out string log)
    {
        string? ffmpeg = FindFfmpeg();
        log = ffmpeg is null ? "ffmpeg not found" : "";
        if (ffmpeg is null) return false;

        var arguments = $"-y -framerate {fps} -i \"{Path.Combine(frameDirectory, "f%05d.png")}\" "
            + $"-c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -movflags +faststart \"{output}\"";
        var startInfo = new System.Diagnostics.ProcessStartInfo(ffmpeg, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        log = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static string? FindFfmpeg()
    {
        var dsh = @"C:\Users\Haluk\.dsh\tools\ffmpeg\bin\ffmpeg.exe";
        if (File.Exists(dsh)) return dsh;
        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var candidate = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // a broken PATH entry is not worth a failure
            }
        }

        return null;
    }
}
