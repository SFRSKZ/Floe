using System;
using System.Windows;
using System.Windows.Media;

namespace Floe.App.Windows;

/// <summary>
/// The Cyber show-desktop button in hairline: a screen with a chamfered corner and two windows on it, and a one pixel
/// accent rail beside it. Peek slides the windows off the screen's edge and the desktop's icons show behind them;
/// press lights the screen's frame.
/// </summary>
public sealed class DesktopSlabGlyph : HairlineGlyph
{
    // layout in a 34 x 40 DIP box
    private const double Left = 3, Top = 9, Right = 27, Bottom = 30, Cut = 3;
    private const double RailX = 31, RailTop = 11, RailBottom = 28;
    private const double Travel = 26;

    public DesktopSlabGlyph(double width, double height)
        : base(width, height)
    {
    }

    protected override int HoverInMs => 420;

    protected override int HoverOutMs => 360;

    /// <summary>Kept for the caller's sake: the hairline screen has no fill, so the ground is not used.</summary>
    public void Paint(Color ink, Color accent, Color ground) => Paint(ink, accent);

    public void Peek(bool on) => Engage(on);

    protected override void Draw(Painter p)
    {
        double hover = Hover, press = Press;
        var dim = Mix(Ink, Color.FromArgb(Ink.A, 0x80, 0x70, 0xA0), .45);

        p.Box(Left, Top, Right, Bottom, Mix(Ink, Accent, press), Cut);

        // the desktop's icons, behind the windows
        if (hover > 0.01)
        {
            var icon = Fade(dim, Math.Clamp((hover - .3) / .7, 0, 1));
            foreach (var y in new[] { 13.0, 18, 23 }) p.Box(6, y, 8, y + 2, icon);
        }

        // the windows slide right and leave through the screen's edge, the front one first
        double front = Smooth(Math.Clamp(hover * 1.4, 0, 1)) * Travel;
        double back = Smooth(Math.Clamp((hover * 1.4) - .4, 0, 1)) * Travel;
        p.Clip(new Rect(Left + 1, Top + 1, Right - Left - 2, Bottom - Top - 2));
        Window(p, 6 + back, 14, 18 + back, 26, dim);
        Window(p, 13 + front, 12, 24 + front, 22, Ink);
        p.Clip(null);

        var rail = Mix(Fade(Accent, .8), Accent, hover);
        p.V(RailX, RailTop, RailBottom, rail);
        if (hover > 0.01) p.V(RailX + 1, RailTop, RailBottom, Fade(Accent, hover));
    }

    private static void Window(Painter p, double x0, double y0, double x1, double y1, Color c)
    {
        p.Box(x0, y0, x1, y1, c);
        p.H(x0, x1, y0 + 2, c);
    }

    private static double Smooth(double t) => t * t * (3 - (2 * t));
}
