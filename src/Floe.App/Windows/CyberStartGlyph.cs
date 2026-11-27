using System;
using System.Windows.Media;

namespace Floe.App.Windows;

/// <summary>
/// The Cyberpunk Start button in hairline: four window panes outlined two device pixels thick with a dark rim. Hover
/// steps the panes a pixel apart and turns them to the accent; press pulls them back in and lights a point in each.
/// </summary>
public sealed class CyberStartGlyph : HairlineGlyph
{
    // pane edges in a 28 DIP box: two 8 DIP panes a side with a two DIP gutter, drawn two device pixels thick
    private const double Design = 28, A0 = 5, M00 = 13, M10 = 15, B0 = 23;
    private const int Weight = 2;

    public CyberStartGlyph(double size)
        : base(size, size)
    {
    }

    protected override void Draw(Painter p)
    {
        double hover = Hover, press = Press;
        // the 28 DIP layout scaled to the glyph's size (larger on the dock and the taskbar)
        double s = Width / Design, A = A0 * s, M0 = M00 * s, M1 = M10 * s, B = B0 * s;
        var line = Mix(Mix(Ink, Accent, hover), Ink, press);

        // apart by one DIP on hover, outer edges in by one on press: whole pixels, so the step stays sharp
        double o = hover >= .5 ? 1 : 0;
        double k = press >= .5 ? 1 : 0;
        p.Frame(A - o + k, A - o + k, M0 - o, M0 - o, line, Weight);
        p.Frame(M1 + o, A - o + k, B + o - k, M0 - o, line, Weight);
        p.Frame(A - o + k, M1 + o, M0 - o, B + o - k, line, Weight);
        p.Frame(M1 + o, M1 + o, B + o - k, B + o - k, line, Weight);

        if (press > 0.01)
        {
            var dot = Fade(Accent, press);
            foreach (var (x, y) in new[] { (A + 3, A + 3), (M1 + 3, A + 3), (A + 3, M1 + 3), (M1 + 3, M1 + 3) })
            {
                p.Fill(x, y, x + 1.5, y + 1.5, dot);
            }
        }
    }
}
