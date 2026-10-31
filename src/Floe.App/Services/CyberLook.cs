using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The colours of the flat, hard-edged themes. Body is the dock's own colour, which the dock colour setting replaces;
/// Ink is whatever reads on it, and Accent marks the hovered and running icons.
/// </summary>
public sealed record CyberLook(Color Body, Color Ink, Color Accent, Color Strip)
{
    private static readonly Color Yellow = Color.FromRgb(0xFC, 0xEE, 0x0A);
    private static readonly Color Paper = Color.FromRgb(0xEC, 0xEC, 0xE6);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x00, 0x3C);

    /// <summary>Rajdhani, bundled with the app (OFL): every word the cyber themes put on screen.</summary>
    public static readonly FontFamily Font = new(new Uri("pack://application:,,,/Floe;component/"), "./Assets/Fonts/#Rajdhani");

    public static CyberLook Current
    {
        get
        {
            var body = GlassSettings.DockColor ?? (GlassSettings.Theme == DockTheme.CyberCorporate ? Paper : Yellow);
            return new CyberLook(body, InkOn(body), Red, Yellow);
        }
    }

    /// <summary>The font of the small windows: Rajdhani in a cyber theme, Segoe UI otherwise.</summary>
    public static FontFamily UiFont => GlassSettings.Cyber ? Font : new FontFamily("Segoe UI");

    /// <summary>The menus and panels stay dark in every cyber theme, so their text reads whatever the dock's colour.</summary>
    public static Color PanelInk => Color.FromRgb(0x0A, 0x0A, 0x10);

    /// <summary>Near black on a light colour, near white on a dark one.</summary>
    public static Color InkOn(Color color)
    {
        var luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
        return luminance > 0.45 ? Color.FromRgb(0x0A, 0x0A, 0x10) : Color.FromRgb(0xF4, 0xF4, 0xF0);
    }
}
