using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Derezz;

/// <summary>
/// --render-particles=sample.png;outdir[;scale] writes the particle dissolve frame by frame (30 fps) over a dark
/// backdrop, the live dock standing to the right of the front, then the same field played back to gather it.
/// </summary>
internal static class ParticlePreview
{
    public static bool TryRender(string[] args)
    {
        const string prefix = "--render-particles=";
        var arg = Array.Find(args, a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        var parts = arg[prefix.Length..].Split(';');
        var sample = new BitmapImage();
        sample.BeginInit();
        sample.CacheOption = BitmapCacheOption.OnLoad;
        sample.UriSource = new Uri(Path.GetFullPath(parts[0]));
        sample.EndInit();
        var snapshot = new FormatConvertedBitmap(sample, PixelFormats.Pbgra32, null, 0);
        var outDir = Path.GetFullPath(parts.Length > 1 ? parts[1] : "particles");
        double scale = parts.Length > 2 ? double.Parse(parts[2], CultureInfo.InvariantCulture) : 1.5;
        Directory.CreateDirectory(outDir);

        // the dock window: the capsule sits 52 units from its top, 144 tall, with room on both sides
        double capW = snapshot.PixelWidth / scale, capH = snapshot.PixelHeight / scale;
        const double margin = 260;
        var capsule = new Rect(margin, 52, capW, capH);
        int width = (int)Math.Ceiling((capW + (2 * margin)) * scale), height = (int)Math.Ceiling(144 * scale);
        var field = new ParticleDissolve(snapshot, capsule);
        var live = new uint[snapshot.PixelWidth * snapshot.PixelHeight];
        snapshot.CopyPixels(live, snapshot.PixelWidth * 4, 0);
        var buffer = new uint[width * height];
        int capX = (int)(capsule.Left * scale), capY = (int)(capsule.Top * scale);
        const uint backdrop = 0xFF0F0F14;

        void Write(double t, int index)
        {
            Array.Clear(buffer);
            var image = new uint[width * height];
            Array.Fill(image, backdrop);
            int front = (int)(field.FrontX(t) * scale);
            for (int y = 0; y < snapshot.PixelHeight; y++)
            {
                for (int x = Math.Max(0, front - capX); x < snapshot.PixelWidth; x++)
                {
                    uint p = live[(y * snapshot.PixelWidth) + x];
                    if ((p >> 24) > 0) image[((capY + y) * width) + capX + x] = Over(p, backdrop);
                }
            }

            field.Render(buffer, width, height, t);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != 0) image[i] = Over(buffer[i], image[i]);
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, image, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outDir, $"f{index:D3}.png"));
            encoder.Save(stream);
        }

        int n = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (double t = 0; t <= ParticleDissolve.Duration + .001; t += 1 / 30.0) Write(t, n++);
        for (double t = ParticleDissolve.Duration; t >= 0; t -= 3.0 / 30.0) Write(t, n++);
        var cost = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 60; i++) field.Render(buffer, width, height, .3 + (i * .02));
        File.WriteAllText(Path.Combine(outDir, "info.txt"),
            $"points={field.Count} frames={n} render={cost.Elapsed.TotalMilliseconds / 60:F2}ms size={width}x{height}");
        return true;
    }

    // premultiplied source over an opaque backdrop
    private static uint Over(uint s, uint d)
    {
        uint a = s >> 24;
        uint r = ((s >> 16) & 255) + ((((d >> 16) & 255) * (255 - a)) / 255);
        uint g = ((s >> 8) & 255) + ((((d >> 8) & 255) * (255 - a)) / 255);
        uint b = (s & 255) + (((d & 255) * (255 - a)) / 255);
        return 0xFF000000u | (Math.Min(255, r) << 16) | (Math.Min(255, g) << 8) | Math.Min(255, b);
    }
}
