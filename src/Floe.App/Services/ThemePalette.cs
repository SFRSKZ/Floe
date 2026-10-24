using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The colours of the Tron themes, shared by the dock and the OLED panel. Legacy is the 2010 film: ice-cyan light
/// lines and white heat on black, with an orange accent. Ares is the 2025 one: red light lines on black.
/// The static properties follow the saved theme; <see cref="For"/> gives any theme's colours (render threads and
/// previews hold their own).
/// </summary>
public sealed record ThemePalette(Color Line, Color Hot, Color Dim, Color Accent)
{
    private static readonly ThemePalette LegacyColors = new(
        Color.FromRgb(0x5F, 0xF4, 0xFF), Color.FromRgb(0xE8, 0xFF, 0xFF), Color.FromRgb(0x05, 0x3A, 0x48), Color.FromRgb(0xFF, 0x9A, 0x2E));

    private static readonly ThemePalette AresColors = new(
        Color.FromRgb(0xFF, 0x2A, 0x1C), Color.FromRgb(0xFF, 0xB0, 0x9C), Color.FromRgb(0x5A, 0x06, 0x04), Color.FromRgb(0xF4, 0xF4, 0xF4));

    /// <summary>Ares for Ares, Legacy for everything else.</summary>
    public static ThemePalette For(DockTheme theme) => theme == DockTheme.TronAres ? AresColors : LegacyColors;

    private static ThemePalette Current => For(GlassSettings.Theme);

    /// <summary>The light lines: rims, edges, the panel's lit dots.</summary>
    public static Color LineColor => Current.Line;

    /// <summary>Where a line runs hottest: its core, the brightest dots.</summary>
    public static Color HotColor => Current.Hot;

    /// <summary>The line's dim glow, and the faint grid.</summary>
    public static Color DimColor => Current.Dim;

    /// <summary>A sparing second colour: Legacy's orange, Ares's white.</summary>
    public static Color AccentColor => Current.Accent;

    /// <summary>Every icon glow in a Tron theme, whatever the aura palette: line, hot, dim.</summary>
    public static Color[] Glows => new[] { Current.Line, Current.Hot, Current.Dim };
}
