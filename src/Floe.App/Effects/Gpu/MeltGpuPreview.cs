using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// --render-gpu-melt=&lt;dir&gt; writes 90 PNG frames (30 fps) of <see cref="MeltGpuScene"/> over the dock's
/// backdrop: a made-up 900x70 DIP dock melts away down a 1920x200 DIP strip, and --rezz runs the same frames
/// the other way, the liquid rising from under the strip back into the dock. --melt-image=&lt;png&gt; melts that
/// picture instead of the synthetic dock.
/// <para>
/// The synthetic dock is a WPF drawing rendered at 2 px/DIP, so the frames show the effect at the density the
/// real strip uses.
/// </para>
/// </summary>
public static class MeltGpuPreview
{
    private const int Width = 3840;
    private const int Height = 400;
    private const int Frames = 90;

    private static readonly Color[] IconColors =
    {
        Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40),
        Color.FromRgb(0x4B, 0x9B, 0xFF), Color.FromRgb(0xB0, 0x7C, 0xFF), Color.FromRgb(0xFF, 0x9F, 0x0A),
        Color.FromRgb(0x32, 0xD0, 0xC6), Color.FromRgb(0xFF, 0x6B, 0x9D), Color.FromRgb(0x7B, 0xD8, 0x8F),
    };

    public static bool TryRender(string[] args)
    {
        const string option = "--render-gpu-melt=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            bool rezz = args.Contains("--rezz", StringComparer.OrdinalIgnoreCase);
            const string imageOption = "--melt-image=";
            var image = args.FirstOrDefault(a => a.StartsWith(imageOption, StringComparison.OrdinalIgnoreCase));

            var snapshot = image is null ? SyntheticDock() : Load(Path.GetFullPath(image[imageOption.Length..]));
            int width = snapshot.PixelWidth;
            int height = snapshot.PixelHeight;
            // the dock's place: centred, its bottom 8 target pixels above the strip's
            var home = new Int32Rect(
                Math.Max(0, (Width - width) / 2),
                Math.Max(0, Height - height - 8),
                Math.Min(width, Width),
                Math.Min(height, Height));

            using var scene = new MeltGpuScene();
            scene.Start(snapshot, home, rezz);
            GpuOffscreen.RenderFrames(scene, Width, Height, 2, Frames, null, folder);

            File.WriteAllText(
                Path.Combine(folder, "render-info.txt"),
                $"MeltGpuScene: 2D FLIP fluid (6 px grid, 40 Jacobi, FLIP .95), {(rezz ? "rezz" : "derezz")}, "
                + $"{Width}x{Height} at 2 px/dip ({Width / 2}x{Height / 2} DIP), dock {width}x{height} px at "
                + $"({home.X},{home.Y}), {Frames} frames at 30 fps, composited over #0B0E14.\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Floe.Gpu] melt preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }

        return true;
    }

    private static BitmapSource Load(string path)
    {
        var decoder = new PngBitmapDecoder(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Pbgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        }

        source.Freeze();
        return source;
    }

    /// <summary>The dock as the preview shows it: dark glass with nine coloured icons, at 2 px/DIP.</summary>
    private static BitmapSource SyntheticDock()
    {
        const double dipWidth = 900, dipHeight = 70, iconSize = 48, margin = 22;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var glass = new SolidColorBrush(Color.FromArgb(0xCC, 0x1C, 0x22, 0x30));
            dc.DrawRoundedRectangle(glass, null, new Rect(0, 0, dipWidth, dipHeight), 17, 17);

            // a faint top edge, like the real dock's glass catching the light
            var highlight = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            dc.DrawRoundedRectangle(highlight, null, new Rect(6, 2, dipWidth - 12, 10), 6, 6);

            double gap = (dipWidth - (2 * margin) - (IconColors.Length * iconSize)) / (IconColors.Length - 1);
            for (int i = 0; i < IconColors.Length; i++)
            {
                double x = margin + (i * (iconSize + gap));
                double y = (dipHeight - iconSize) / 2;
                dc.DrawRoundedRectangle(new SolidColorBrush(IconColors[i]), null, new Rect(x, y, iconSize, iconSize), 11, 11);
            }
        }

        var bitmap = new RenderTargetBitmap((int)(dipWidth * 2), (int)(dipHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
