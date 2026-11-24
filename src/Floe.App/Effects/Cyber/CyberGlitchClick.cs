using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// Renders Cyberpunk 2077 pixel-level icon click effects directly on an icon or card snapshot:
/// Relic glitch slices, vertical column datamosh, and stepped mosaic bit-crush.
/// </summary>
public static class CyberGlitchClick
{
    private static readonly ConditionalWeakTable<ImageSource, Dictionary<int, Brush?[]>> Samples = new();

    public static Brush?[] Sample(ImageSource src, int n)
    {
        var map = Samples.GetOrCreateValue(src);
        lock (map)
        {
            if (map.TryGetValue(n, out var cached)) return cached;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(src, new Rect(0, 0, n, n));
            }

            var rtb = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var pixels = new byte[n * n * 4];
            rtb.CopyPixels(pixels, n * 4, 0);

            var brushes = new Brush?[n * n];
            var palette = new Dictionary<Color, Brush>();
            for (int i = 0; i < brushes.Length; i++)
            {
                int offset = i * 4;
                byte a = pixels[offset + 3];
                if (a < 8) continue;

                byte r = (byte)Math.Clamp((pixels[offset + 2] * 255) / a, 0, 255);
                byte g = (byte)Math.Clamp((pixels[offset + 1] * 255) / a, 0, 255);
                byte b = (byte)Math.Clamp((pixels[offset] * 255) / a, 0, 255);
                var color = Color.FromArgb(a, r, g, b);
                if (!palette.TryGetValue(color, out var brush))
                {
                    var solid = new SolidColorBrush(color);
                    solid.Freeze();
                    palette[color] = brush = solid;
                }

                brushes[i] = brush;
            }

            return map[n] = brushes;
        }
    }
    public static void Draw(
        DrawingContext dc,
        ImageSource snapshot,
        Rect at,
        Rect frame,
        double p,
        Color cyan,
        Color red,
        Color ink,
        Action<DrawingContext, string, double, double, double, Brush> drawText)
    {
        if (p is < 0 or > 1 || at.Width <= 0 || at.Height <= 0) return;

        double tick = Math.Floor(p * 28.0);
        double fade = StepFade(1.0 - p, steps: 5);

        // 5 hard-stepped horizontal slices shifting across the icon
        const int slices = 5;
        double sliceH = at.Height / slices;
        double amp = at.Width * 0.24 * (1.0 - p);
        double clipLeft = at.X - at.Width;
        double clipWidth = at.Width * 3.0;
        for (int s = 0; s < slices; s++)
        {
            double shift = (CyberFxLayer.Random((s * 13.0) + tick) - 0.5) * 2.0 * amp;
            var clipRect = new Rect(clipLeft, at.Y + (s * sliceH), clipWidth, sliceH + 0.5);
            dc.PushClip(new RectangleGeometry(clipRect));
            dc.DrawImage(snapshot, new Rect(at.X + shift, at.Y, at.Width, at.Height));
            dc.Pop();
        }

        if (fade <= 0) return;

        // 6 cyan / red horizontal tear blocks around the icon
        double scale = at.Width / 38.0;
        double cx = frame.X + (frame.Width * 0.5);
        double cy = frame.Y + (frame.Height * 0.5);
        double tearAreaW = frame.Width + 18.0;
        double tearAreaH = frame.Height + 8.0;
        byte tearAlpha = (byte)(0xFF * 0.85 * fade);
        for (int i = 0; i < 6; i++)
        {
            double gx = cx + ((CyberFxLayer.Random((i * 19.0) + tick) - 0.5) * tearAreaW);
            double gy = cy + ((CyberFxLayer.Random((i * 31.0) + tick) - 0.5) * tearAreaH);
            double gw = (8.0 + (CyberFxLayer.Random((i * 7.0) + tick) * 14.0)) * scale;
            const double gh = 2.5;
            var baseColor = i % 2 == 0 ? cyan : red;
            var brush = CyberFxLayer.Solid(Color.FromArgb(tearAlpha, baseColor.R, baseColor.G, baseColor.B));
            dc.DrawRectangle(brush, null, new Rect(gx - (gw * 0.5), gy, gw, gh));
        }

        // Double jittered chamfered frame (red + cyan)
        byte alpha = (byte)(255.0 * fade);
        Color redFaded = Color.FromArgb(alpha, red.R, red.G, red.B);
        Color cyanFaded = Color.FromArgb(alpha, cyan.R, cyan.G, cyan.B);
        Color inkFaded = Color.FromArgb(alpha, ink.R, ink.G, ink.B);

        double jx = (CyberFxLayer.Random(tick * 11.0) - 0.5) * 6.0;
        var redFrame = frame;
        redFrame.Offset(jx, -1.0);
        var cyanFrame = frame;
        cyanFrame.Offset(-jx, 1.0);

        dc.DrawGeometry(null, CyberFxLayer.CachedPen(redFaded, 1.3), CyberFxLayer.SlotOutline(redFrame));
        dc.DrawGeometry(null, CyberFxLayer.CachedPen(cyanFaded, 1.3), CyberFxLayer.SlotOutline(cyanFrame));

        // Compact RELIC! micro-tag anchored above the slot frame
        const double tagW = 68.0;
        const double tagH = 15.0;
        double tagX = Math.Max(4.0, cx - (tagW * 0.5));
        double tagY = Math.Max(2.0, frame.Top - tagH - 5.0);
        var tagRect = new Rect(tagX, tagY, tagW, tagH);

        if (frame.Top > tagY + tagH)
        {
            dc.DrawLine(CyberFxLayer.CachedPen(cyanFaded, 1.2), new Point(cx, frame.Top), new Point(cx, tagY + tagH));
        }

        dc.DrawGeometry(CyberFxLayer.Solid(inkFaded), CyberFxLayer.CachedPen(cyanFaded, 1.0), CyberFxLayer.Plate(tagRect, 4.0));
        dc.DrawRectangle(CyberFxLayer.Solid(redFaded), null, new Rect(tagX + 2.0, tagY + 2.0, 2.5, tagH - 4.0));
        drawText(dc, "RELIC!", tagX + 7.0, tagY + 1.5, 9.0, CyberFxLayer.Solid(Color.FromArgb(alpha, 0xF8, 0xFA, 0xFC)));

        double fillProgress = 1.0 - p;
        for (int i = 0; i < 4; i++)
        {
            bool lit = fillProgress >= (i + 1) / 4.0;
            Color cellColor = lit
                ? (i == 3 ? redFaded : cyanFaded)
                : Color.FromArgb((byte)(0x38 * fade), cyan.R, cyan.G, cyan.B);
            dc.DrawRectangle(CyberFxLayer.Solid(cellColor), null, new Rect(tagX + 43.0 + (i * 5.5), tagY + 5.0, 4.0, 5.0));
        }
    }

    public static void DrawDatamosh(DrawingContext dc, ImageSource img, Brush?[] grid48, Rect at, double p)
    {
        if (p is < 0 or > 1 || at.Width <= 0 || at.Height <= 0) return;

        const int cols = 14;
        double tick = Math.Floor(p * 18.0);
        double decay = StepFade(1.0 - CyberFxLayer.Segment(p, 0.35, 0.98), 5);
        double cw = at.Width / cols;
        double gridStepW = at.Width / 48.0;

        for (int c = 0; c < cols; c++)
        {
            double x0 = at.X + (c * cw);
            bool active = CyberFxLayer.Random((c * 23.0) + Math.Floor(tick / 3.0)) > 0.42 && decay > 0.0;
            if (!active)
            {
                dc.PushClip(new RectangleGeometry(new Rect(x0, at.Y, cw + 0.3, at.Height)));
                dc.DrawImage(img, at);
                dc.Pop();
                continue;
            }

            double cutFrac = 0.25 + (CyberFxLayer.Random((c * 13.0) + 7.0) * 0.45);
            double cutY = at.Y + (cutFrac * at.Height);
            double topH = Math.Max(0.0, cutY - at.Y);
            if (topH > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(x0, at.Y, cw + 0.3, topH)));
                dc.DrawImage(img, at);
                dc.Pop();
            }

            double extra = decay * at.Width * 0.26 * CyberFxLayer.Random((c * 31.0) + tick);
            double smearH = Math.Max(0.0, at.Bottom - cutY + extra);
            int row = Math.Clamp((int)(cutFrac * 48.0), 0, 47);
            for (int gx = 0; gx < 48; gx++)
            {
                double center = (((gx + 0.5) / 48.0) * at.Width) + at.X;
                if (center < x0 || center >= x0 + cw) continue;
                if (grid48[(row * 48) + gx] is { } brush)
                {
                    dc.DrawRectangle(brush, null, new Rect(at.X + (gx * gridStepW), cutY, gridStepW + 0.5, smearH));
                }
            }
        }
    }

    public static void DrawBitCrush(DrawingContext dc, ImageSource img, Func<int, Brush?[]> sample, Rect at, double p)
    {
        if (p is < 0 or > 1 || at.Width <= 0 || at.Height <= 0) return;

        ReadOnlySpan<int> levels = stackalloc int[] { 16, 8, 4, 4, 8, 8, 12, 16, 24, 0 };
        int stage = Math.Clamp((int)Math.Floor(p * 10.0), 0, 9);
        int res = levels[stage];
        if (res == 0)
        {
            dc.DrawImage(img, at);
            return;
        }

        var cells = sample(res);
        double cw = at.Width / res;
        double ch = at.Height / res;
        for (int r = 0; r < res; r++)
        {
            for (int c = 0; c < res; c++)
            {
                if (cells[(r * res) + c] is { } brush)
                {
                    dc.DrawRectangle(brush, null, new Rect(at.X + (c * cw), at.Y + (r * ch), cw + 0.35, ch + 0.35));
                }
            }
        }
    }

    private static double StepFade(double value, int steps) => Math.Floor(Math.Clamp(value, 0.0, 1.0) * steps) / steps;
}
