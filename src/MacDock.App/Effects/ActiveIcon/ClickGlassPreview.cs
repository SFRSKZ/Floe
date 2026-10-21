using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.ActiveIcon;

internal static class ClickGlassPreview
{
    internal static bool TryRender(string[] args)
    {
        const string option = "--renderclick=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            var folder = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(folder);
            if (!ClickGlassEffect.IsAvailable) throw new InvalidOperationException("ClickGlass ps_3_0 compilation failed.");
            const double size = 64;
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRoundedRectangle(new LinearGradientBrush(Colors.Coral, Colors.MediumVioletRed, 70), null,
                    new Rect(0, 0, size, size), 15, 15);
                dc.DrawEllipse(Brushes.White, null, new Point(23, 43), 8, 5);
                dc.DrawEllipse(Brushes.White, null, new Point(43, 38), 8, 5);
                dc.DrawLine(new Pen(Brushes.White, 4), new Point(29, 43), new Point(29, 18));
                dc.DrawLine(new Pen(Brushes.White, 4), new Point(49, 38), new Point(49, 13));
                dc.DrawLine(new Pen(Brushes.White, 6), new Point(29, 19), new Point(49, 14));
            }
            var source = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            source.Render(drawing);
            source.Freeze();
            using var renderer = new ClickGlassRenderer(source, (int)size);
            foreach (var palette in new[] { 0, 1, 2 })
            foreach (var tone in new[] { 0, 1 })
            foreach (var progress in new[] { 0.0, 0.1, 0.25, 0.4, 0.6, 0.8, 1.0 })
            {
                var surface = renderer.Render(progress, tone, palette, Colors.Coral, Colors.MediumVioletRed);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(42, 46, 54)), null, new Rect(0, 0, 128, 128));
                    dc.DrawImage(source, new Rect(32, 32, size, size));
                    dc.DrawImage(surface, new Rect(20, 20, surface.PixelWidth, surface.PixelHeight));
                }
                var target = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                using var output = File.Create(Path.Combine(folder, $"p{palette}-{(tone == 0 ? "dark" : "light")}-{progress:0.00}.png"));
                encoder.Save(output);
            }
            File.WriteAllText(Path.Combine(folder, "render-info.txt"),
                "ps_3_0 compiled; same HLSL executed with D3D11 WARP (ps_4_0 bindings), real icon and SDF textures; WPF composition; 64 DIP icon.\n");
        }
        catch (Exception ex)
        {
            MetalRim.ShaderCompiler.Log($"Click preview failed: {ex}");
            Environment.ExitCode = 1;
        }
        return true;
    }

}
