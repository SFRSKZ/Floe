using System.Windows.Media;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>
/// The colours of the flat, hard-edged themes. Body is the dock's own colour, which the dock colour setting replaces;
/// Ink is whatever reads on it, and Accent marks the hovered and running icons.
/// </summary>
public sealed record CyberLook(Color Body, Color Ink, Color Accent, Color Strip)
{
    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);

    /// <summary>Rajdhani, bundled with the app (OFL): every word the cyber themes put on screen.</summary>
    public static readonly FontFamily Font = new(new Uri($"pack://application:,,,/{Product.Exe};component/"), "./Assets/Fonts/#Rajdhani");

    public static CyberLook Current => For(GlassSettings.DockColor, GlassSettings.CyberAccent, GlassSettings.CyberStrip, GlassSettings.CyberInk);

    /// <summary>A look from the studio's four colours; whichever is left null takes its default.</summary>
    public static CyberLook For(Color? body, Color? accent, Color? strip, Color? ink)
    {
        var plate = body ?? Yellow;
        return new CyberLook(plate, ink ?? InkOn(plate), accent ?? Red, strip ?? Yellow);
    }

    /// <summary>The font of the small windows: Rajdhani in a cyber theme, Segoe UI otherwise.</summary>
    public static FontFamily UiFont => GlassSettings.Cyber ? Font : new FontFamily("Segoe UI");

    /// <summary>The menus and panels stay dark in every cyber theme, so their text reads whatever the dock's colour.</summary>
    public static Color PanelInk => Color.FromRgb(0x0A, 0x0A, 0x10);

    /// <summary>The colour as chosen when it stands out on <paramref name="on"/>, otherwise the ink for that background.</summary>
    public static Color Readable(Color wanted, Color on) => Contrast(wanted, on) >= 3 ? wanted : InkOn(on);

    /// <summary>The WCAG contrast ratio of two colours, 1 to 21.</summary>
    public static double Contrast(Color a, Color b)
    {
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= .03928 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }

        return (.2126 * Linear(color.R)) + (.7152 * Linear(color.G)) + (.0722 * Linear(color.B));
    }

    /// <summary>The darker or the lighter ink, whichever has the higher contrast on the colour.</summary>
    public static Color InkOn(Color color)
    {
        var dark = Color.FromRgb(0x0A, 0x0A, 0x10);
        var light = Color.FromRgb(0xF4, 0xF4, 0xF0);
        return Contrast(dark, color) >= Contrast(light, color) ? dark : light;
    }
}
