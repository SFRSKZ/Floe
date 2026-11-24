using System.Windows;
using System.Windows.Media;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// Renders Cyberpunk 2077 quickhack / datashard execution visuals for icon clicks and launches:
/// hard-edged locking brackets, dual chamfered telemetry rings, and a compact <c>EXEC</c> uplink micro-tag.
/// </summary>
public static class CyberLaunchEffect
{
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();
    private static readonly Dictionary<(Color Color, double Thickness), Pen> PenCache = new();

    /// <summary>Draws a sharp chamfered shockwave ring around <paramref name="frame"/> for instant click feedback.</summary>
    public static void DrawClickRing(DrawingContext dc, Rect frame, double p, Color accent, Color cyan)
    {
        if (p is <= 0 or >= 1) return;

        double eased = EaseOutCubic(p);
        double fade = StepFade(1.0 - p, steps: 5);
        if (fade <= 0) return;

        var ring = Rect.Inflate(frame, 18.0 * eased, 18.0 * eased);
        var ringColor = Color.FromArgb((byte)(235 * fade), cyan.R, cyan.G, cyan.B);
        dc.DrawGeometry(null, CachedPen(ringColor, 1.5), CyberFxLayer.SlotOutline(ring));

        if (p < 0.35)
        {
            double flash = StepFade(1.0 - (p / 0.35), steps: 3);
            var tickPen = CachedPen(Color.FromArgb((byte)(255 * flash), accent.R, accent.G, accent.B), 2.0);
            const double len = 7.0;
            dc.DrawLine(tickPen, new Point(ring.Right - len, ring.Top), new Point(ring.Right, ring.Top));
            dc.DrawLine(tickPen, new Point(ring.Right, ring.Top), new Point(ring.Right, ring.Top + len));
            dc.DrawLine(tickPen, new Point(ring.Left, ring.Bottom - len), new Point(ring.Left, ring.Bottom));
            dc.DrawLine(tickPen, new Point(ring.Left, ring.Bottom), new Point(ring.Left + len, ring.Bottom));
        }
    }

    /// <summary>
    /// Draws the complete 3-phase Cyberpunk datashard execution sequence (locking corners, dual telemetry rings,
    /// uplink rail + <c>EXEC</c> / <c>OK</c> tag with 4-segment upload bar) anchored cleanly above <paramref name="frame"/>.
    /// </summary>
    public static void DrawLaunch(
        DrawingContext dc,
        Rect frame,
        double windowWidth,
        double p,
        Color accent,
        Color cyan,
        Color strip,
        Color ink,
        Action<DrawingContext, string, double, double, double, Brush> drawText)
    {
        if (p is < 0 or > 1) return;

        // Phase 1: 4 locking targeting brackets closing cleanly onto the icon corners
        if (p <= 0.48)
        {
            double lockMove = EaseOutCubic(CyberFxLayer.Segment(p, 0.00, 0.24));
            double expand = 14.0 * (1.0 - lockMove);
            double alpha = p <= 0.32 ? 1.0 : StepFade(1.0 - CyberFxLayer.Segment(p, 0.32, 0.48), steps: 4);
            if (alpha > 0)
            {
                bool lockFlash = p is >= 0.22 and <= 0.30;
                Color bracketColor = lockFlash ? Color.FromRgb(0xF8, 0xFC, 0xFF) : cyan;
                var pen = CachedPen(Color.FromArgb((byte)(255 * alpha), bracketColor.R, bracketColor.G, bracketColor.B), lockFlash ? 2.2 : 1.6);
                CyberFxLayer.Corners(dc, Rect.Inflate(frame, expand, expand), 10.0, pen);
            }
        }

        // Phase 2: Dual expanding chamfered telemetry rings
        DrawTelemetryRing(dc, frame, p, from: 0.06, to: 0.56, maxExpand: 20.0, color: accent, thickness: 1.6);
        DrawTelemetryRing(dc, frame, p, from: 0.16, to: 0.72, maxExpand: 32.0, color: cyan, thickness: 1.2);

        // Phase 3: Vertical data-uplink rail + compact EXEC / OK tag above the frame
        if (p is >= 0.08 and <= 0.96)
        {
            double cx = frame.X + (frame.Width * 0.5);
            double stalkGrow = EaseOutCubic(CyberFxLayer.Segment(p, 0.08, 0.20));
            double tagAlpha = p <= 0.84 ? 1.0 : StepFade(1.0 - CyberFxLayer.Segment(p, 0.84, 0.96), steps: 4);

            const double tagW = 76.0;
            const double tagH = 23.0;
            const double stalkH = 14.0;
            double tagX = Math.Clamp(cx - (tagW * 0.5), 8.0, Math.Max(8.0, windowWidth - tagW - 8.0));
            double tagY = Math.Max(4.0, frame.Top - stalkH - tagH - 1.0);
            var tagRect = new Rect(tagX, tagY, tagW, tagH);

            if (stalkGrow > 0.05)
            {
                double topY = Math.Max(tagRect.Bottom, frame.Top - 1.0 - (stalkH * stalkGrow));
                var stalkPen = CachedPen(Color.FromArgb((byte)(235 * tagAlpha), cyan.R, cyan.G, cyan.B), 1.5);
                dc.DrawLine(stalkPen, new Point(cx, frame.Top - 1.0), new Point(cx, topY));
            }

            if (p >= 0.12)
            {
                double open = CyberFxLayer.Segment(p, 0.12, 0.24);
                double close = 1.0 - CyberFxLayer.Segment(p, 0.84, 0.96);
                double clipW = Math.Max(1.0, tagW * Math.Min(open, close));

                dc.PushClip(new RectangleGeometry(new Rect(tagRect.X, tagRect.Y - 1, clipW, tagRect.Height + 2)));
                dc.DrawGeometry(CachedBrush(ink), CachedPen(cyan, 1.0), CyberFxLayer.Plate(tagRect, 5.0));
                dc.DrawRectangle(CachedBrush(accent), null, new Rect(tagRect.X + 6.0, tagRect.Y + 4.0, 3.5, tagRect.Height - 8.0));

                bool done = p >= 0.72;
                string label = done ? "OK" : CyberFxLayer.Decode("EXEC", CyberFxLayer.Segment(p, 0.14, 0.38), Math.Floor(p * 30.0));
                drawText(dc, label, tagRect.X + 14.0, tagRect.Y + 1.0, 11.0, CachedBrush(done ? cyan : strip));

                double fillProgress = CyberFxLayer.Segment(p, 0.20, 0.72);
                const int segments = 4;
                const double blockW = 11.0;
                const double blockGap = 2.5;
                const double blockH = 3.5;
                double barY = tagRect.Y + 15.0;
                for (int i = 0; i < segments; i++)
                {
                    double bx = tagRect.X + 14.0 + (i * (blockW + blockGap));
                    bool lit = fillProgress >= (i + 1) / (double)segments;
                    Brush fill = lit
                        ? CachedBrush(i == segments - 1 && done ? accent : cyan)
                        : CachedBrush(Color.FromArgb(0x36, cyan.R, cyan.G, cyan.B));
                    dc.DrawRectangle(fill, null, new Rect(bx, barY, blockW, blockH));
                }

                dc.Pop();
            }
        }
    }

    private static void DrawTelemetryRing(
        DrawingContext dc, Rect frame, double p,
        double from, double to, double maxExpand,
        Color color, double thickness)
    {
        if (p < from || p > to) return;
        double t = CyberFxLayer.Segment(p, from, to);
        double eased = EaseOutCubic(t);
        double fade = StepFade(1.0 - t, steps: 5);
        if (fade <= 0) return;

        var ring = Rect.Inflate(frame, maxExpand * eased, maxExpand * eased);
        var c = Color.FromArgb((byte)(235 * fade), color.R, color.G, color.B);
        dc.DrawGeometry(null, CachedPen(c, thickness), CyberFxLayer.SlotOutline(ring));
    }

    private static SolidColorBrush CachedBrush(Color c)
    {
        if (BrushCache.TryGetValue(c, out var known)) return known;
        if (BrushCache.Count > 256) BrushCache.Clear();
        var b = new SolidColorBrush(c);
        b.Freeze();
        return BrushCache[c] = b;
    }

    private static Pen CachedPen(Color c, double thickness)
    {
        var key = (c, thickness);
        if (PenCache.TryGetValue(key, out var known)) return known;
        if (PenCache.Count > 256) PenCache.Clear();
        var p = new Pen(CachedBrush(c), thickness);
        p.Freeze();
        return PenCache[key] = p;
    }

    private static double StepFade(double v, int steps) => Math.Floor(Math.Clamp(v, 0, 1) * steps) / steps;

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - Math.Clamp(t, 0, 1), 3.0);
}
