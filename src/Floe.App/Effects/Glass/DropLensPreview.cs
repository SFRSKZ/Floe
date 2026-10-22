using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.ActiveIcon;
using Floe.App.Effects.MetalRim;
using Floe.App.Windows;

namespace Floe.App.Effects.Glass;

/// <summary>Hidden deterministic drop diagnostic; executes the real shader through D3D11 WARP.</summary>
internal static class DropLensPreview
{
    private const int Width = 560, Height = 290;

    internal static bool TryRender(string[] args)
    {
        const string option = "--renderdrop=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        string folder = Path.GetFullPath(argument[option.Length..]);
        try
        {
            Directory.CreateDirectory(folder);
            if (ShaderCompiler.LoadShader("Floe.DropLens.hlsl") is null)
                throw new InvalidOperationException("DropLens ps_3_0 compilation failed.");
            var backdrop = Backdrop();
            Save(backdrop, Path.Combine(folder, "backdrop.png"));
            var sheet = new DrawingVisual();
            var info = new StringBuilder("ps_3_0 compiled; same HLSL executed with D3D11 WARP using ps_4_0 bindings.\nSeed 173; physics steps <= 1/120 s; frames 560x290; crops enlarged 3x.\n");
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 1600, 1280));
                double[] times = { .08, .22, .40, .55, .69, .80, .94, 1.25 };
                for (int i = 0; i < times.Length; i++)
                {
                    var maskVisual = Pose(times[i], mask: true);
                    var mask = Raster(maskVisual, Width, Height);
                    var center = maskVisual.LensCenter;
                    using var renderer = new ClickGlassRenderer(mask, backdrop);
                    var lens = renderer.Render(new float[]
                    {
                        Width, Height, 0, 0,
                        (float)center.X, (float)center.Y, 0, 0,
                        (float)maskVisual.LensRadius, 0, 0, 0,
                    });
                    var validation = Validate(mask, lens, backdrop);
                    info.AppendLine(FormattableString.Invariant($"t={times[i]:0.00}: {validation}"));
                    var decoration = Raster(Pose(times[i], mask: false), Width, Height);
                    var composition = new DrawingVisual();
                    using (var frame = composition.RenderOpen())
                    {
                        frame.DrawImage(backdrop, new Rect(0, 0, Width, Height));
                        frame.DrawImage(lens, new Rect(0, 0, Width, Height));
                        frame.DrawImage(decoration, new Rect(0, 0, Width, Height));
                    }
                    var bitmap = Raster(composition, Width, Height);
                    string name = "drop-" + times[i].ToString("0.00", CultureInfo.InvariantCulture) + ".png";
                    Save(bitmap, Path.Combine(folder, name));
                    Save(lens, Path.Combine(folder, "lens-" + name));
                    int x = (i % 2) * 800, y = (i / 2) * 320;
                    dc.DrawText(Text($"{times[i]:0.00}s — full frame / 3x crop", 16), new Point(x + 8, y + 4));
                    dc.DrawImage(bitmap, new Rect(x, y + 30, Width, Height));
                    int cropY = (int)Math.Clamp(center.Y - 40, 0, Height - 80);
                    var crop = new CroppedBitmap(bitmap, new Int32Rect(240, cropY, 80, 80));
                    dc.DrawImage(crop, new Rect(x + Width, y + 30, 240, 240));
                }
            }
            Save(Raster(sheet, 1600, 1280), Path.Combine(folder, "contact-sheet.png"));
            File.WriteAllText(Path.Combine(folder, "render-info.txt"), info.ToString());
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Drop preview failed: {ex}");
            try { File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString()); } catch { }
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static LiquidDropVisual Pose(double time, bool mask)
    {
        var visual = new LiquidDropVisual(173) { MaskOnly = mask, DecorationOnly = !mask };
        visual.Reset(Width / 2.0);
        for (double elapsed = 0; elapsed < time;)
        {
            double step = Math.Min(1.0 / 120, time - elapsed);
            visual.Advance(step, Width / 2.0);
            elapsed += step;
        }
        visual.Measure(new Size(Width, Height));
        visual.Arrange(new Rect(0, 0, Width, Height));
        visual.UpdateLayout();
        return visual;
    }

    private static BitmapSource Backdrop()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush(new GradientStopCollection
            {
                new(Colors.DarkSlateBlue, 0), new(Colors.Coral, .3),
                new(Colors.Gold, .5), new(Colors.Teal, .7), new(Colors.MidnightBlue, 1),
            }, 20);
            dc.DrawRectangle(brush, null, new Rect(0, 0, Width, Height));
            for (int x = 0; x < Width; x += 6)
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 15, 25, 55)), 2), new Point(x, 0), new Point(x, Height));
            for (int y = 0; y < Height; y += 26)
            {
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1), new Point(0, y), new Point(Width, y));
                dc.DrawText(Text("GLASS  0123456789  REFRACTION  0123456789", 13), new Point(125, y + 3));
            }
        }
        return Raster(visual, Width, Height);
    }

    private static string Validate(BitmapSource mask, BitmapSource lens, BitmapSource backdrop)
    {
        byte[] m = new byte[Width * Height * 4], l = new byte[m.Length], b = new byte[m.Length];
        mask.CopyPixels(m, Width * 4, 0); lens.CopyPixels(l, Width * 4, 0); backdrop.CopyPixels(b, Width * 4, 0);
        int outside = 0, invalid = 0, changed = 0, opaque = 0;
        for (int i = 0; i < m.Length; i += 4)
        {
            if (m[i + 3] == 0 && (l[i] != 0 || l[i + 1] != 0 || l[i + 2] != 0 || l[i + 3] != 0)) outside++;
            if (l[i] > l[i + 3] || l[i + 1] > l[i + 3] || l[i + 2] > l[i + 3]) invalid++;
            if (m[i + 3] > 250 && l[i + 3] > 250)
            {
                opaque++;
                if (Math.Abs(l[i] - b[i]) + Math.Abs(l[i + 1] - b[i + 1]) + Math.Abs(l[i + 2] - b[i + 2]) > 30) changed++;
            }
        }
        if (outside != 0 || invalid != 0)
            throw new InvalidOperationException($"Invalid lens output: outside-mask={outside}, non-premultiplied={invalid}.");
        return $"outside-mask={outside}, non-premultiplied={invalid}, changed opaque pixels={changed}/{opaque}";
    }

    private static FormattedText Text(string text, double size) => new(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.White, 1);

    private static BitmapSource Raster(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
