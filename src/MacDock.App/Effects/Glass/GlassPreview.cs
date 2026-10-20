using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.Glass;

/// <summary>Diagnostic frames only: RenderTargetBitmap cannot execute ps_3_0.</summary>
internal static class GlassPreview
{
    internal static byte[] Backdrop(string path, double left, double top, double logicalWidth, double logicalHeight, out int width, out int height)
    {
        width = Math.Max(1, (int)Math.Ceiling(logicalWidth / 4));
        height = Math.Max(1, (int)Math.Ceiling(logicalHeight / 4));
        var source = new BitmapImage(new Uri(System.IO.Path.GetFullPath(path)));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(width / logicalWidth, height / logicalHeight));
            dc.DrawImage(source, new Rect(-left, -top, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight));
        }
        var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        frame.Render(visual);
        var pixels = new byte[width * height * 4];
        frame.CopyPixels(pixels, width * 4, 0);
        Blur(pixels, width, height, Services.BlurSettings.Radius * width / logicalWidth);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
            double luminance = r * 0.2126 + g * 0.7152 + b * 0.0722;
            double lifted = 0.06 + luminance * 0.88;
            pixels[i] = Channel(lifted + (b - luminance) * 1.8);
            pixels[i + 1] = Channel(lifted + (g - luminance) * 1.8);
            pixels[i + 2] = Channel(lifted + (r - luminance) * 1.8);
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static void Blur(byte[] pixels, int width, int height, double radius)
    {
        int reach = Math.Max(1, (int)Math.Ceiling(radius));
        double sigma = Math.Max(0.5, radius / 3), sum = 0;
        var kernel = new double[2 * reach + 1];
        for (int i = -reach; i <= reach; i++) sum += kernel[i + reach] = Math.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        var temp = new byte[pixels.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            var input = pass == 0 ? pixels : temp;
            var output = pass == 0 ? temp : pixels;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            for (int c = 0; c < 3; c++)
            {
                double value = 0;
                for (int k = -reach; k <= reach; k++)
                {
                    int sx = pass == 0 ? Math.Clamp(x + k, 0, width - 1) : x;
                    int sy = pass == 0 ? y : Math.Clamp(y + k, 0, height - 1);
                    value += input[(sy * width + sx) * 4 + c] * kernel[k + reach];
                }
                output[(y * width + x) * 4 + c] = (byte)Math.Clamp(Math.Round(value), 0, 255);
            }
        }
    }

    internal static BitmapSource WarpRim(BitmapSource source, double width, double height, double hover, double bulge, Point stretch, double plateau, double sigma)
    {
        int w = (int)Math.Ceiling(width + 48), h = (int)Math.Ceiling(height + 80);
        var input = new byte[source.PixelWidth * source.PixelHeight * 4];
        var output = new byte[w * h * 4];
        source.CopyPixels(input, source.PixelWidth * 4, 0);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Same inverse mapping as RimWarp.hlsl, including padded coordinates.
            double px = x + 0.5 - 24, py = y + 0.5 - 56;
            double vertical = Math.Clamp((height - py) / height, 0, 1);
            vertical = vertical * vertical * (3 - 2 * vertical);
            px += (px < width * 0.5 ? stretch.X : -stretch.Y) * vertical;
            double d = Math.Max(0, Math.Abs(Math.Clamp(px, 22, width - 22) - hover) - plateau);
            double lift = bulge * Math.Exp(-d * d / (2 * sigma * sigma));
            py += lift * Math.Clamp((height - 22 - py) / (height - 44 + lift), 0, 1);
            double sx = px + 24 - 0.5, sy = py + 24 - 0.5;
            int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
            if (ix < 0 || iy < 0 || ix + 1 >= source.PixelWidth || iy + 1 >= source.PixelHeight) continue;
            double fx = sx - ix, fy = sy - iy;
            int index = (iy * source.PixelWidth + ix) * 4;
            for (int c = 0; c < 4; c++)
                output[(y * w + x) * 4 + c] = (byte)Math.Clamp(Math.Round(
                    input[index + c] * (1 - fx) * (1 - fy) + input[index + 4 + c] * fx * (1 - fy) +
                    input[index + source.PixelWidth * 4 + c] * (1 - fx) * fy + input[index + source.PixelWidth * 4 + 4 + c] * fx * fy), 0, 255);
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, output, w * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
