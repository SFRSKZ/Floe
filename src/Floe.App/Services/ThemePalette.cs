using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The colours of the Tron themes, shared by the dock and the OLED panel. Legacy is the 2010 film: ice-cyan light
/// lines and white heat on black, with an orange accent. Ares is the 2025 one: red light lines on black.
/// </summary>
public static class ThemePalette
{
    /// <summary>The light lines: rims, edges, the panel's lit dots.</summary>
    public static Color Line => GlassSettings.Theme == DockTheme.TronAres ? Color.FromRgb(0xFF, 0x2A, 0x1C) : Color.FromRgb(0x5F, 0xF4, 0xFF);

    /// <summary>Where a line runs hottest: its core, the brightest dots.</summary>
    public static Color Hot => GlassSettings.Theme == DockTheme.TronAres ? Color.FromRgb(0xFF, 0xB0, 0x9C) : Color.FromRgb(0xE8, 0xFF, 0xFF);

    /// <summary>The line's dim glow, and the faint grid.</summary>
    public static Color Dim => GlassSettings.Theme == DockTheme.TronAres ? Color.FromRgb(0x5A, 0x06, 0x04) : Color.FromRgb(0x05, 0x3A, 0x48);

    /// <summary>A sparing second colour: Legacy's orange, Ares's white.</summary>
    public static Color Accent => GlassSettings.Theme == DockTheme.TronAres ? Color.FromRgb(0xF4, 0xF4, 0xF4) : Color.FromRgb(0xFF, 0x9A, 0x2E);

    /// <summary>Every icon glow in a Tron theme, whatever the aura palette: line, hot, dim.</summary>
    public static Color[] Glows => new[] { Line, Hot, Dim };
}
