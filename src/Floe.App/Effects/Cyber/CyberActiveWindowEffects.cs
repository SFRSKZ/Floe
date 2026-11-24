using System.Windows;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Effects.Cyber;

/// <summary>The 5 selectable Cyberpunk 2077 active (focused) window indicator styles on the dock.</summary>
public enum CyberActiveWindowStyle
{
    /// <summary>#1 Kiroshi Target Lock: 4 corner target brackets + orbiting perimeter laser comet + bottom [ACT] power rail.</summary>
    KiroshiTargetLock = 1,

    /// <summary>#2 Arasaka Tactical Chamber: Dark #0A0A10 recessed chamber plate + left rarity charge bar + right live signal meter.</summary>
    ArasakaTacticalChamber = 2,

    /// <summary>#3 Sandevistan Chrono-Echo: Twin cyan/accent silhouette afterimages + upward quantum scanline + [>>>] chevron bar.</summary>
    SandevistanChronoEcho = 3,

    /// <summary>#4 Netrunner Uplink Beacons: Twin vertical laser columns with ascending data packets + top [LINK] bridge + pedestal.</summary>
    NetrunnerUplinkBeacons = 4,

    /// <summary>#5 Relic Bio-Pulse Halo: Counter-rotating segmented telemetry rings + high-contrast horizontal signal streaks.</summary>
    RelicBioPulseHalo = 5,
}

/// <summary>
/// Renders the 5 Cyberpunk 2077 active window indicator animations around the currently focused dock slot.
/// </summary>
public static class CyberActiveWindowEffects
{
    private static readonly Color DarkInk = Color.FromRgb(0x0A, 0x0A, 0x10);
    private static readonly ColorHudCache Cache = new();

    /// <summary>
    /// Draws the active window effect <paramref name="style"/> for a slot at <paramref name="slotRect"/>.
    /// </summary>
    public static void DrawActiveSlot(
        DrawingContext dc,
        CyberActiveWindowStyle style,
        Rect slotRect,
        ImageSource? iconLogo,
        double timeSec,
        double lockAgeSec,
        Color bodyColor,
        Color accentColor,
        Color cyanColor)
    {
        double lockT = Math.Clamp(lockAgeSec / 0.34, 0.0, 1.0);
        double lockEase = 1.0 - Math.Pow(1.0 - lockT, 3.0);
        var frameRect = Rect.Inflate(slotRect, 5.0, 5.0);

        switch (style)
        {
            case CyberActiveWindowStyle.KiroshiTargetLock:
                DrawKiroshiTargetLock(dc, slotRect, frameRect, iconLogo, timeSec, lockEase, bodyColor, accentColor, cyanColor);
                break;

            case CyberActiveWindowStyle.ArasakaTacticalChamber:
                DrawArasakaTacticalChamber(dc, slotRect, frameRect, iconLogo, timeSec, lockAgeSec, lockEase, bodyColor, accentColor, cyanColor);
                break;

            case CyberActiveWindowStyle.SandevistanChronoEcho:
                DrawSandevistanChronoEcho(dc, slotRect, frameRect, iconLogo, timeSec, lockEase, bodyColor, accentColor, cyanColor);
                break;

            case CyberActiveWindowStyle.NetrunnerUplinkBeacons:
                DrawNetrunnerUplinkBeacons(dc, slotRect, frameRect, iconLogo, timeSec, lockEase, bodyColor, accentColor, cyanColor);
                break;

            case CyberActiveWindowStyle.RelicBioPulseHalo:
                DrawRelicBioPulseHalo(dc, slotRect, frameRect, iconLogo, timeSec, lockAgeSec, lockEase, bodyColor, accentColor, cyanColor);
                break;
        }
    }

    // #1 KIROSHI TARGET LOCK: 4 breathing corner brackets + orbiting perimeter laser comet + bottom dual power rail.
    private static void DrawKiroshiTargetLock(
        DrawingContext dc, Rect slotRect, Rect frameRect, ImageSource? iconLogo,
        double t, double lockEase, Color body, Color accent, Color cyan)
    {
        if (iconLogo is not null) dc.DrawImage(iconLogo, slotRect);

        double spread = ((1.0 - lockEase) * 10.0) + (Math.Sin(t * 4.2) * 1.3);
        var r = Rect.Inflate(frameRect, spread, spread);
        const double len = 10.0;

        var backPen = Cache.Pen(DarkInk, 4.0);
        var cyanPen = Cache.Pen(cyan, 2.0);
        var accentPen = Cache.Pen(accent, 2.0);

        DrawCornerBracket(dc, r.Left, r.Top, 1, 1, len, backPen, cyanPen);
        DrawCornerBracket(dc, r.Right, r.Top, -1, 1, len, backPen, accentPen);
        DrawCornerBracket(dc, r.Right, r.Bottom, -1, -1, len, backPen, cyanPen);
        DrawCornerBracket(dc, r.Left, r.Bottom, 1, -1, len, backPen, accentPen);

        // Orbiting perimeter laser trace comet.
        double perim = (r.Width + r.Height) * 2.0;
        double headDist = (t * 115.0) % perim;
        for (int s = 0; s < 12; s++)
        {
            var p1 = PointOnPerimeter(r, headDist - (s * 2.6), perim);
            var p2 = PointOnPerimeter(r, headDist - ((s + 1) * 2.6), perim);
            byte alpha = (byte)(255 * (1.0 - (s / 12.0)) * lockEase);
            var col = s < 2 ? Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF) : Color.FromArgb(alpha, cyan.R, cyan.G, cyan.B);
            dc.DrawLine(Cache.Pen(col, s < 2 ? 2.6 : 2.0), p1, p2);
        }

        // Bottom active power bar [ACT].
        double barW = slotRect.Width * 0.72;
        double barX = slotRect.Left + ((slotRect.Width - barW) * 0.5);
        double barY = frameRect.Bottom + 1.5;
        dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(barX - 1, barY - 1, barW + 2, 5.5));
        dc.DrawRectangle(Cache.Brush(accent), null, new Rect(barX, barY, barW * 0.62, 3.5));
        dc.DrawRectangle(Cache.Brush(cyan), null, new Rect(barX + (barW * 0.66), barY, barW * 0.34, 3.5));
    }

    // #2 ARASAKA TACTICAL CHAMBER: Recessed dark #0A0A10 plate + left rarity power bar + right live signal meter.
    private static void DrawArasakaTacticalChamber(
        DrawingContext dc, Rect slotRect, Rect frameRect, ImageSource? iconLogo,
        double t, double lockAge, double lockEase, Color body, Color accent, Color cyan)
    {
        double cx = frameRect.Left + (frameRect.Width * 0.5);
        double openW = frameRect.Width * (0.25 + (0.75 * lockEase));
        var chRect = new Rect(cx - (openW * 0.5), frameRect.Top, openW, frameRect.Height);

        bool flash = lockAge < 0.22;
        var outline = CyberFxLayer.Plate(chRect, 8.0);
        dc.DrawGeometry(Cache.Brush(DarkInk), Cache.Pen(flash ? accent : cyan, 1.5), outline);

        if (iconLogo is not null)
        {
            // Render icon in bright silver #ABB2C0 inside the dark chamber for instant contrast.
            CyberFxLayer.Silhouette(dc, iconLogo, slotRect, Cache.Brush(Color.FromRgb(0xCA, 0xD2, 0xE2)));
        }

        // Left 4.5px pulsing Accent Rarity / Power Rail with animated charge notches.
        dc.DrawRectangle(Cache.Brush(accent), null, new Rect(chRect.Left - 1, chRect.Top, 4.5, chRect.Height));
        int skipNotch = (int)(t * 6.0) % 3;
        for (int n = 0; n < 3; n++)
        {
            if (n == skipNotch) continue;
            dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(chRect.Left, chRect.Top + 6.0 + (n * 4.0), 3.5, 1.5));
        }

        // Right 5-segment vertical live cyan signal meter.
        const int segs = 5;
        int activeSegs = 2 + (int)((0.5 + (0.5 * Math.Sin(t * 6.5))) * 3.5);
        for (int s = 0; s < segs; s++)
        {
            double sy = chRect.Bottom - 12.0 - (s * 6.5);
            var col = s < activeSegs ? (s == segs - 1 ? accent : cyan) : Color.FromRgb(0x1E, 0x24, 0x30);
            dc.DrawRectangle(Cache.Brush(col), null, new Rect(chRect.Right - 4.8, sy, 3.0, 4.5));
        }
    }

    // #3 SANDEVISTAN CHRONO-ECHO: Chromatic twin silhouette afterimages + upward quantum scanline + [>>>] chevron bar.
    private static void DrawSandevistanChronoEcho(
        DrawingContext dc, Rect slotRect, Rect frameRect, ImageSource? iconLogo,
        double t, double lockEase, Color body, Color accent, Color cyan)
    {
        if (iconLogo is not null)
        {
            double arrivalSpread = (1.0 - lockEase) * 13.0;
            double echoShift = arrivalSpread + 3.0 + (1.8 * Math.Sin(t * 4.8));

            var leftRect = slotRect;
            leftRect.Offset(-echoShift, -0.8);
            CyberFxLayer.Silhouette(dc, iconLogo, leftRect, Cache.Brush(Color.FromArgb(205, cyan.R, cyan.G, cyan.B)));

            var rightRect = slotRect;
            rightRect.Offset(echoShift, 0.8);
            CyberFxLayer.Silhouette(dc, iconLogo, rightRect, Cache.Brush(Color.FromArgb(205, accent.R, accent.G, accent.B)));

            dc.DrawImage(iconLogo, slotRect);
        }

        // Upward quantum scanline slice across the icon.
        double scanU = (t * 0.95) % 1.0;
        double scanY = (slotRect.Bottom - 4.0) - (scanU * (slotRect.Height - 8.0));
        dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(slotRect.Left + 2, scanY - 1.5, slotRect.Width - 4, 3.5));
        dc.DrawRectangle(Cache.Brush(cyan), null, new Rect(slotRect.Left + 3, scanY - 0.8, (slotRect.Width - 6) * 0.62, 2.0));
        dc.DrawRectangle(Cache.Brush(accent), null, new Rect(slotRect.Left + 3 + ((slotRect.Width - 6) * 0.66), scanY - 0.8, (slotRect.Width - 6) * 0.34, 2.0));

        // Bottom triple-pulse indicator bar.
        double pillW = slotRect.Width * 0.66;
        double pillX = slotRect.Left + ((slotRect.Width - pillW) * 0.5);
        double pillY = frameRect.Bottom - 1.0;
        dc.DrawGeometry(Cache.Brush(DarkInk), null, CyberFxLayer.Plate(new Rect(pillX, pillY, pillW, 7.0), 2.5));

        int litStep = (int)(t * 7.0) % 3;
        double cx = slotRect.Left + (slotRect.Width * 0.5);
        for (int c = 0; c < 3; c++)
        {
            var col = c == litStep ? accent : cyan;
            dc.DrawRectangle(Cache.Brush(col), null, new Rect(cx - 10.5 + (c * 7.5), pillY + 2.0, 5.5, 3.0));
        }
    }

    // #4 NETRUNNER UPLINK BEACONS: Twin vertical data columns with ascending packets + top HUD bridge + bottom pedestal.
    private static void DrawNetrunnerUplinkBeacons(
        DrawingContext dc, Rect slotRect, Rect frameRect, ImageSource? iconLogo,
        double t, double lockEase, Color body, Color accent, Color cyan)
    {
        if (iconLogo is not null) dc.DrawImage(iconLogo, slotRect);

        double railH = frameRect.Height * lockEase;
        double leftX = frameRect.Left - 2.0;
        double rightX = frameRect.Right - 2.0;

        // Dark rail backing + cyan laser rails.
        dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(leftX - 1, frameRect.Top, 5.0, railH));
        dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(rightX - 1, frameRect.Top, 5.0, railH));
        dc.DrawRectangle(Cache.Brush(cyan), null, new Rect(leftX + 0.5, frameRect.Top + 2, 2.0, Math.Max(1, railH - 4)));
        dc.DrawRectangle(Cache.Brush(cyan), null, new Rect(rightX + 0.5, frameRect.Top + 2, 2.0, Math.Max(1, railH - 4)));

        // Ascending data packets along both vertical rails.
        for (int p = 0; p < 3; p++)
        {
            double u1 = ((t * 1.35) + (p * 0.33)) % 1.0;
            double py1 = (frameRect.Bottom - 6.0) - (u1 * (frameRect.Height - 12.0));
            var c = p == 0 ? Colors.White : accent;
            dc.DrawRectangle(Cache.Brush(c), null, new Rect(leftX, py1, 3.0, 4.5));

            double u2 = ((t * 1.35) + (p * 0.33) + 0.16) % 1.0;
            double py2 = (frameRect.Bottom - 6.0) - (u2 * (frameRect.Height - 12.0));
            dc.DrawRectangle(Cache.Brush(c), null, new Rect(rightX, py2, 3.0, 4.5));
        }

        // Top HUD Bridge Tag.
        double tagW = slotRect.Width * 0.66;
        double tagX = slotRect.Left + ((slotRect.Width - tagW) * 0.5);
        var tagRect = new Rect(tagX, frameRect.Top - 5.0, tagW, 8.0);
        dc.DrawGeometry(Cache.Brush(DarkInk), Cache.Pen(cyan, 1.0), CyberFxLayer.Plate(tagRect, 2.5));

        var ledCol = ((int)(t * 5.0) % 2 == 0) ? accent : cyan;
        dc.DrawRectangle(Cache.Brush(ledCol), null, new Rect(tagRect.Left + 4.0, tagRect.Top + 2.5, 4.0, 3.0));
        dc.DrawRectangle(Cache.Brush(cyan), null, new Rect(tagRect.Left + 10.5, tagRect.Top + 3.0, Math.Max(2, tagRect.Width - 14.5), 2.0));

        // Bottom Uplink Pedestal.
        dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(frameRect.Left + 2, frameRect.Bottom - 1, frameRect.Width - 4, 5.0));
        dc.DrawRectangle(Cache.Brush(accent), null, new Rect(frameRect.Left + 4, frameRect.Bottom + 0.5, frameRect.Width - 8, 2.5));
    }

    // #5 RELIC BIO-PULSE HALO: Counter-rotating segmented telemetry ring + high-contrast horizontal signal streaks.
    private static void DrawRelicBioPulseHalo(
        DrawingContext dc, Rect slotRect, Rect frameRect, ImageSource? iconLogo,
        double t, double lockAge, double lockEase, Color body, Color accent, Color cyan)
    {
        double cx = slotRect.Left + (slotRect.Width * 0.5);
        double cy = slotRect.Top + (slotRect.Height * 0.5);
        var center = new Point(cx, cy);
        double haloR = (slotRect.Width * 0.50) * (0.72 + (0.28 * lockEase));

        // Dark contrast backing ring so the cyan/accent telemetry segments pop over red or yellow bars.
        dc.DrawEllipse(null, Cache.Pen(DarkInk, 5.2), center, haloR, haloR);

        var cyanPen = Cache.Pen(cyan, 2.2);
        for (int i = 0; i < 4; i++)
        {
            DrawArc(dc, center, haloR, (t * 52.0) + (i * 90.0), 50.0, cyanPen);
        }

        var accentPen = Cache.Pen(accent, 1.8);
        for (int i = 0; i < 3; i++)
        {
            DrawArc(dc, center, haloR - 3.4, -(t * 78.0) + (i * 120.0), 38.0, accentPen);
        }

        if (iconLogo is not null) dc.DrawImage(iconLogo, slotRect);

        // Periodic Relic side signal streaks in high-contrast dark backing + cyan/accent cores.
        double burstCycle = (t * 1.4) % 1.0;
        double burst = (burstCycle < 0.28 || lockAge < 0.30) ? 1.0 : 0.42;
        int seed = (int)(t * 9.0);
        ReadOnlySpan<double> yOffsets = [-0.28, -0.06, 0.18, 0.34];

        for (int i = 0; i < yOffsets.Length; i++)
        {
            double y = cy + (yOffsets[i] * slotRect.Height);
            double len = (6.0 + (((seed * (i + 3) * 17) & 0x7FFFFFFF) % 13)) * burst;
            bool leftSide = ((seed + i) % 2) == 0;
            double sx = leftSide ? (cx - haloR - len - 2.0) : (cx + haloR + 2.0);

            dc.DrawRectangle(Cache.Brush(DarkInk), null, new Rect(sx - 1.0, y - 1.0, len + 2.0, 4.0));
            dc.DrawRectangle(Cache.Brush(i % 2 == 0 ? cyan : accent), null, new Rect(sx, y, len, 2.0));
        }
    }

    private static void DrawCornerBracket(
        DrawingContext dc, double x, double y, double dx, double dy, double len, Pen backPen, Pen fgPen)
    {
        var p1 = new Point(x + (dx * len), y);
        var p0 = new Point(x, y);
        var p2 = new Point(x, y + (dy * len));
        dc.DrawLine(backPen, p1, p0);
        dc.DrawLine(backPen, p0, p2);
        dc.DrawLine(fgPen, p1, p0);
        dc.DrawLine(fgPen, p0, p2);
    }

    private static Point PointOnPerimeter(Rect r, double dist, double perim)
    {
        double md = ((dist % perim) + perim) % perim;
        if (md < r.Width) return new Point(r.Left + md, r.Top);
        if (md < r.Width + r.Height) return new Point(r.Right, r.Top + (md - r.Width));
        if (md < (r.Width * 2) + r.Height) return new Point(r.Right - (md - (r.Width + r.Height)), r.Bottom);
        return new Point(r.Left, r.Bottom - (md - ((r.Width * 2) + r.Height)));
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double startDeg, double sweepDeg, Pen pen)
    {
        double r0 = startDeg * Math.PI / 180.0;
        double r1 = (startDeg + sweepDeg) * Math.PI / 180.0;
        var p0 = new Point(center.X + (Math.Cos(r0) * radius), center.Y + (Math.Sin(r0) * radius));
        var p1 = new Point(center.X + (Math.Cos(r1) * radius), center.Y + (Math.Sin(r1) * radius));
        var seg = new ArcSegment(p1, new Size(radius, radius), 0, Math.Abs(sweepDeg) > 180, sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true);
        var fig = new PathFigure(p0, new PathSegment[] { seg }, false);
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private sealed class ColorHudCache
    {
        private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
        private readonly Dictionary<(Color, double), Pen> _pens = new();

        public SolidColorBrush Brush(Color c)
        {
            if (_brushes.TryGetValue(c, out var b)) return b;
            if (_brushes.Count > 128) _brushes.Clear();
            b = new SolidColorBrush(c);
            b.Freeze();
            return _brushes[c] = b;
        }

        public Pen Pen(Color c, double thickness)
        {
            var key = (c, thickness);
            if (_pens.TryGetValue(key, out var p)) return p;
            if (_pens.Count > 128) _pens.Clear();
            p = new Pen(Brush(c), thickness);
            p.Freeze();
            return _pens[key] = p;
        }
    }
}
