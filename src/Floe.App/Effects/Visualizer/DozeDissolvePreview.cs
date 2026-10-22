using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.Glass;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Visualizer;

public static class DozeDissolvePreview
{
    public static bool TryRender(string[] args)
    {
        const string prefix = "--render-dissolve=";
        const string samplePrefix = "--dissolve-sample=";
        var arg = Array.Find(args, a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        try
        {
            var sampleArg = Array.Find(args, a => a.StartsWith(samplePrefix, StringComparison.OrdinalIgnoreCase));
            var path = Path.GetFullPath(sampleArg is null ? ".ds-tasks/silo/dock_sample.png" : sampleArg[samplePrefix.Length..]);
            var sample = new BitmapImage();
            sample.BeginInit();
            sample.CacheOption = BitmapCacheOption.OnLoad;
            sample.UriSource = new Uri(path);
            sample.EndInit();
            sample.Freeze();
            // Native pixel bounds of the capsule in the supplied reference screenshot.
            // An already cropped optional sample is also accepted.
            var bounds = sample.PixelWidth >= 1034 && sample.PixelHeight >= 176
                ? new Int32Rect(7, 111, 1027, 65)
                : new Int32Rect(0, 0, sample.PixelWidth, sample.PixelHeight);
            var crop = new CroppedBitmap(sample, bounds);
            crop.Freeze();
            int width = crop.PixelWidth, height = crop.PixelHeight;
            const int labelHeight = 28, gap = 16;
            int rowHeight = height + labelHeight + gap;
            if (!DozeGhostEffect.IsAvailable) throw new InvalidOperationException("DozeGhost ps_3_0 compilation failed.");
            double[] lines = { 0, .3, .6, 1.02, .5, 0 };
            string[] labels = { "AWAKE / Line 0", "DOZE / Line 0.30", "DOZE / Line 0.60", "GHOST / Line 1.02", "WAKE / Line 0.50", "AWAKE / Line 0" };
            var stage = new DrawingVisual();
            using (var dc = stage.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 16, 24)), null, new Rect(0, 0, width, rowHeight * 6));
                for (int i = 0; i < lines.Length; i++)
                {
                    var dock = new DrawingVisual { Effect = new DozeGhostEffect(software: true)
                        { Line = lines[i], Size = new Point(width, height) } };
                    using (var input = dock.RenderOpen())
                    {
                        // Remove the screenshot background outside the rounded capsule.
                        input.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 22, 22));
                        input.DrawImage(crop, new Rect(0, 0, width, height));
                        input.Pop();
                    }
                    var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    frame.Render(dock);
                    frame.Freeze();
                    dc.DrawText(new FormattedText(labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 12, Brushes.LightSteelBlue, 1), new Point(8, i * rowHeight + 6));
                    dc.DrawImage(frame, new Rect(0, i * rowHeight + labelHeight, width, height));
                }
            }
            var output = new RenderTargetBitmap(width, rowHeight * 6, 96, 96, PixelFormats.Pbgra32);
            output.Render(stage);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));
            var outputPath = Path.GetFullPath(arg[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var file = File.Create(outputPath);
            encoder.Save(file);
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Doze ghost preview failed: {ex}");
            Environment.ExitCode = 1;
        }
        return true;
    }
}
