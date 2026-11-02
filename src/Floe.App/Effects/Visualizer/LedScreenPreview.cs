using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Visualizer;

public static class LedScreenPreview
{
    public static bool TryRender(string[] args)
    {
        const string option = "--render-led-wpf=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            var folder = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(folder);
            if (!LedScreenEffect.IsAvailable) throw new InvalidOperationException("LedScreen ps_3_0 compilation failed.");
            if (!LedScreenEffect.IsSoftwareAvailable) throw new InvalidOperationException("LedScreen ps_2_0 compilation failed.");
            const int width = 600, height = 80;
            const double pitch = 6;

            var picture = Draw(width, height);
            var plain = Render(width, height, picture, null);
            var screen = Render(width, height, picture, new LedScreenEffect(software: true)
            {
                Size = new Point(width, height),
                Pitch = pitch,
            });

            // Both frames share the dark backing, so any difference comes from the shader alone.
            var plainPixels = Pixels(plain);
            var screenPixels = Pixels(screen);
            int differing = 0, lit = 0;
            for (int i = 0; i < screenPixels.Length; i += 4)
            {
                if (screenPixels[i] > 32 || screenPixels[i + 1] > 32 || screenPixels[i + 2] > 32) lit++;
                if (plainPixels[i] != screenPixels[i] || plainPixels[i + 1] != screenPixels[i + 1]
                    || plainPixels[i + 2] != screenPixels[i + 2] || plainPixels[i + 3] != screenPixels[i + 3]) differing++;
            }

            if (lit == 0) throw new InvalidOperationException("LED render is empty.");
            if (differing == 0) throw new InvalidOperationException("LED render matches the plain one; the shader did not run.");

            Save(plain, Path.Combine(folder, "led-plain.png"));
            Save(screen, Path.Combine(folder, "led-effect.png"));
            // The triads are six pixels wide, so a magnified crop is the only way to see them.
            Save(Zoom(screen, 150, 4), Path.Combine(folder, "led-zoom.png"));
            File.WriteAllText(Path.Combine(folder, "render-info.txt"), FormattableString.Invariant(
                $"ps_3_0 available; ps_2_0 build runs in RenderTargetBitmap; {width}x{height}, pitch {pitch}; {differing} of {width * height} pixels differ from the plain picture; {lit} lit.\n"));
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"LED screen preview failed: {ex}");
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static RenderTargetBitmap Draw(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.35) };
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(255, 64, 96), 0.00));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(255, 190, 50), 0.22));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(70, 240, 130), 0.45));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(40, 190, 255), 0.68));
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb(150, 90, 255), 1.00));
            dc.DrawRoundedRectangle(rainbow, null, new Rect(0, 0, width, height), height / 2.0, height / 2.0);
            // Bright and coloured dots sit on the bar so the triads have something to separate.
            dc.DrawEllipse(Brushes.White, null, new Point(64, 40), 17, 17);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 214, 70)), null, new Point(142, 28), 11, 11);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(30, 226, 255)), null, new Point(214, 52), 13, 13);
            dc.DrawEllipse(Brushes.White, null, new Point(292, 38), 9, 9);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(350, 26, 96, 28), 7, 7);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 60, 120)), null, new Point(506, 40), 16, 16);
        }
        var source = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        source.Render(visual);
        source.Freeze();
        return source;
    }

    // Host the picture in a real element tree: an Effect only runs once the element is measured,
    // arranged and rendered, which a lone DrawingVisual never does.
    private static RenderTargetBitmap Render(int width, int height, BitmapSource picture, LedScreenEffect? effect)
    {
        var panel = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x1F)),
            Child = new Border { Background = new ImageBrush(picture) { Stretch = Stretch.Fill } },
            Effect = effect,
        };
        panel.Measure(new Size(width, height));
        panel.Arrange(new Rect(0, 0, width, height));
        panel.UpdateLayout();
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(panel);
        target.Freeze();
        return target;
    }

    private static RenderTargetBitmap Zoom(BitmapSource source, int cropWidth, int scale)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, cropWidth * scale, source.PixelHeight * scale)));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth * scale, source.PixelHeight * scale));
            dc.Pop();
        }
        var target = new RenderTargetBitmap(cropWidth * scale, source.PixelHeight * scale, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
