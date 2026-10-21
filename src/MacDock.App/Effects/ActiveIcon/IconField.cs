using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.ActiveIcon;

/// <summary>
/// Signed distance field of an icon's silhouette over the effect's padded area, as a brush
/// for <see cref="ActiveIconEffect.Field"/>. Grey level g encodes (g - 0.5) * 32 DIP, positive outside.
/// </summary>
public static class IconField
{
    private const int PixelsPerDip = 2;
    private const double Range = 32;
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<(double Size, bool Solid), ImageBrush>> Cache = new();

    /// <param name="solid">
    /// Field of the icon's outer outline instead of every stroke: gaps closed and holes filled, so
    /// line-art icons without a plate (ChatGPT, Codex) read as one shape.
    /// </param>
    public static ImageBrush For(ImageSource icon, double iconSize, bool solid = false)
    {
        var sizes = Cache.GetOrCreateValue(icon);
        if (sizes.TryGetValue((iconSize, solid), out var cached))
        {
            return cached;
        }

        var brush = Build(icon, iconSize, solid);
        sizes[(iconSize, solid)] = brush;
        return brush;
    }

    private static ImageBrush Build(ImageSource icon, double iconSize, bool solid)
    {
        var total = iconSize + (2 * ActiveIconEffect.Pad);
        int n = (int)Math.Round(total * PixelsPerDip);

        // the icon laid out the way the dock's Image shows it: uniform, centred
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var scale = Math.Min(iconSize / Math.Max(1, icon.Width), iconSize / Math.Max(1, icon.Height));
            var w = icon.Width * scale;
            var h = icon.Height * scale;
            var x = ActiveIconEffect.Pad + ((iconSize - w) / 2);
            var y = ActiveIconEffect.Pad + ((iconSize - h) / 2);
            dc.DrawImage(icon, new Rect(x * PixelsPerDip, y * PixelsPerDip, w * PixelsPerDip, h * PixelsPerDip));
        }

        var target = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[n * n * 4];
        target.CopyPixels(pixels, n * 4, 0);

        const double far = 1e20;
        var toInside = new double[n * n];
        var toOutside = new double[n * n];
        for (int i = 0; i < n * n; i++)
        {
            var inside = pixels[(i * 4) + 3] > 110;
            toInside[i] = inside ? 0 : far;
            toOutside[i] = inside ? far : 0;
        }

        if (solid)
        {
            Solidify(toInside, toOutside, n);
        }

        Transform(toInside, n);
        Transform(toOutside, n);

        var field = new byte[n * n * 4];
        for (int i = 0; i < n * n; i++)
        {
            var px = Math.Sqrt(toInside[i]) - Math.Sqrt(toOutside[i]);
            var dip = px / PixelsPerDip;
            var g = (byte)Math.Round(Math.Clamp((dip / Range) + 0.5, 0, 1) * 255);
            field[i * 4] = field[(i * 4) + 1] = field[(i * 4) + 2] = g;
            field[(i * 4) + 3] = 255;
        }

        var bitmap = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, field, n * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }

    /// <summary>Closes gaps narrower than ~12 DIP and fills enclosed holes, rewriting both seed grids.</summary>
    private static void Solidify(double[] toInside, double[] toOutside, int n)
    {
        const double far = 1e20;
        var radius = 6.0 * PixelsPerDip;
        var r2 = radius * radius;

        // closing: dilate by the radius, then erode by it
        var grow = (double[])toInside.Clone();
        Transform(grow, n);
        var shrink = new double[n * n];
        for (int i = 0; i < n * n; i++) shrink[i] = grow[i] <= r2 ? far : 0;
        Transform(shrink, n);
        var inside = new bool[n * n];
        for (int i = 0; i < n * n; i++) inside[i] = shrink[i] > r2 || toInside[i] == 0;

        // anything the outside can't reach from the border is a hole
        var reached = new bool[n * n];
        var stack = new Stack<int>();
        void Visit(int p)
        {
            if (!inside[p] && !reached[p])
            {
                reached[p] = true;
                stack.Push(p);
            }
        }

        var bottomRow = (n - 1) * n;
        for (int i = 0; i < n; i++)
        {
            Visit(i);
            Visit(bottomRow + i);
            Visit(i * n);
            Visit((i * n) + n - 1);
        }

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            int x = p % n;
            if (x > 0) Visit(p - 1);
            if (x < n - 1) Visit(p + 1);
            if (p >= n) Visit(p - n);
            if (p < bottomRow) Visit(p + n);
        }

        for (int i = 0; i < n * n; i++)
        {
            var solid = !reached[i];
            toInside[i] = solid ? 0 : far;
            toOutside[i] = solid ? far : 0;
        }
    }

    /// <summary>Exact squared Euclidean distance transform (Felzenszwalb and Huttenlocher), in place.</summary>
    private static void Transform(double[] grid, int n)
    {
        var f = new double[n];
        var d = new double[n];
        var v = new int[n];
        var z = new double[n + 1];
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < n; y++) f[y] = grid[(y * n) + x];
            Pass(f, d, v, z, n);
            for (int y = 0; y < n; y++) grid[(y * n) + x] = d[y];
        }

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++) f[x] = grid[(y * n) + x];
            Pass(f, d, v, z, n);
            for (int x = 0; x < n; x++) grid[(y * n) + x] = d[x];
        }
    }

    private static void Pass(double[] f, double[] d, int[] v, double[] z, int n)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                s = ((f[q] + (q * q)) - (f[v[k]] + (v[k] * v[k]))) / ((2 * q) - (2 * v[k]));
                if (s > z[k] || k == 0)
                {
                    break;
                }

                k--;
            }

            if (s <= z[k])
            {
                // only reachable at k == 0: the new parabola dominates everything
                v[0] = q;
                z[1] = double.PositiveInfinity;
                continue;
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            d[q] = ((q - v[k]) * (q - v[k])) + f[v[k]];
        }
    }
}
