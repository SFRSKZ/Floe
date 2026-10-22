using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Effects.Glass;
using MacDock.App.Effects.MetalRim;

namespace MacDock.App.Effects.Derezz;

/// <summary>
/// --render-derezz=&lt;dir&gt; writes a 60 fps PNG sequence of the dock dozing off into a derezz and
/// rezzing back in, composed from the reference screenshot like the dissolve preview.
/// </summary>
public static class DerezzPreview
{
    private const int Fps = 60;
    private const double Scale = 2;
    private const double Margin = 40, Above = 52, Below = 24;

    public static bool TryRender(string[] args)
    {
        const string prefix = "--render-derezz=";
        var arg = Array.Find(args, a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        try
        {
            var dir = Path.GetFullPath(arg[prefix.Length..]);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "f*.png")) File.Delete(old);

            var sample = Load(Path.GetFullPath(".ds-tasks/silo/dock_sample.png"));
            var capsulePx = new Int32Rect(7, 111, 1027, 65);
            var capsule = new Rect(Margin, Above, capsulePx.Width, capsulePx.Height);
            var stage = new Rect(0, 0, capsule.Width + (2 * Margin), Above + capsule.Height + Below);
            var awake = Awake(sample, capsulePx);
            var backdrop = Backdrop(sample, capsulePx, capsule, stage);

            double floor = stage.Bottom;
            var doze = new DerezzField(awake, capsule, 6, new Point(capsule.X + (capsule.Width / 2), capsule.Bottom), floor, rezz: false);
            var wake = new DerezzField(awake, capsule, 6, new Point(capsule.X + (capsule.Width * .3), capsule.Bottom), floor, rezz: true, speed: 1.8, seed: 3);

            int frame = 0;
            void Emit(Action<DrawingContext, DrawingContext> draw) =>
                Save(Frame(stage, Scale, (dc, glow) =>
                {
                    dc.DrawImage(backdrop, stage);
                    draw(dc, glow);
                }), dir, frame++);

            for (int i = 0; i < Fps * .4; i++) Emit((dc, _) => dc.DrawImage(awake, capsule));
            for (double t = 0; t <= doze.Duration + (1.0 / Fps); t += 1.0 / Fps)
            {
                double now = t;
                Emit((dc, glow) =>
                {
                    doze.Render(dc, glow, now);
                });
            }

            for (int i = 0; i < Fps * .7; i++) Emit((_, _) => { });
            for (double t = 0; t <= wake.Duration + (1.0 / Fps); t += 1.0 / Fps)
            {
                double now = t;
                Emit((dc, glow) =>
                {
                    dc.PushClip(wake.LiveRegion(now, stage));
                    dc.DrawImage(awake, capsule);
                    dc.Pop();
                    wake.Render(dc, glow, now);
                });
            }

            for (int i = 0; i < Fps * .5; i++) Emit((dc, _) => dc.DrawImage(awake, capsule));
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Derezz preview failed: {ex}");
            Environment.ExitCode = 1;
        }

        return true;
    }

    // Draws a frame with its glow layer blurred on top, the bloom the dock gets from a BlurEffect.
    private static BitmapSource Frame(Rect stage, double scale, Action<DrawingContext, DrawingContext> draw)
    {
        var sharp = new DrawingVisual();
        var glow = new DrawingVisual { Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 5, KernelType = System.Windows.Media.Effects.KernelType.Gaussian } };
        using (var dc = sharp.RenderOpen())
        using (var g = glow.RenderOpen())
        {
            draw(dc, g);
        }

        int w = (int)(stage.Width * scale), h = (int)(stage.Height * scale);
        var bloom = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bloom.Render(glow);
        bloom.Freeze();
        var composed = new DrawingVisual();
        using (var dc = composed.RenderOpen())
        {
            dc.DrawDrawing(sharp.Drawing);
            dc.DrawImage(bloom, stage);
        }

        var bitmap = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(composed);
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string dir, int frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, $"f{frame:0000}.png"));
        encoder.Save(file);
    }

    private static BitmapSource Awake(BitmapSource sample, Int32Rect px)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, px.Width, px.Height), 22, 22));
            dc.DrawImage(new CroppedBitmap(sample, px), new Rect(0, 0, px.Width, px.Height));
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(px.Width, px.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // The screenshot around the capsule, with the capsule itself painted over by the strip just above it.
    private static BitmapSource Backdrop(BitmapSource sample, Int32Rect px, Rect capsule, Rect stage)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(6, 14, 30)), null, stage);
            dc.DrawImage(sample, new Rect(capsule.X - px.X, capsule.Y - px.Y, sample.PixelWidth, sample.PixelHeight));
            var strip = new CroppedBitmap(sample, new Int32Rect(px.X, px.Y - 12, px.Width, 8));
            dc.DrawImage(strip, new Rect(capsule.X - 2, capsule.Y - 2, capsule.Width + 4, capsule.Height + 4));
        }

        var bitmap = new RenderTargetBitmap((int)(stage.Width * Scale), (int)(stage.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
