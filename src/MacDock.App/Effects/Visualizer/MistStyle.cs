using System.Windows.Media;

namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// One look of the mist visualizer: how its strands are drawn (thin lines or
/// columns of dust), how dense and tall the field is, how much fog and glow
/// sits around it, and its colours.
/// </summary>
internal sealed record MistStyle(
    string Name,
    double StrandWidth, // 1 = the hologram's pen stack
    double Dust,        // share of strands drawn as dust columns, 0..1
    double Spacing,     // strand spacing as a multiple of the hologram's
    double Height,      // field height multiplier
    int Veils,
    double VeilAlpha,
    double Haze,        // bloom behind the clusters
    int Sparks,
    Color Core,
    Color Body,
    Color Edge)
{
    private static readonly Color IceCore = Color.FromRgb(0xEA, 0xF6, 0xFF);
    private static readonly Color IceBody = Color.FromRgb(0x6F, 0xB8, 0xFF);
    private static readonly Color IceEdge = Color.FromRgb(0x2A, 0x6F, 0xD6);

    public static readonly MistStyle[] All =
    {
        new("İnce", 0.45, 0.0, 1.4, 1.35, 5, 1.0, 0.8, 20, IceCore, IceBody, IceEdge),
        new("Toz", 0.55, 1.0, 1.1, 1.35, 5, 1.0, 0.7, 40, IceCore, IceBody, IceEdge),
        new("İnce toz", 0.45, 0.5, 1.2, 1.35, 5, 1.0, 0.75, 30, IceCore, IceBody, IceEdge),
        new("Kıvılcım", 0.40, 0.4, 1.8, 1.20, 4, 0.8, 0.6, 120, IceCore, IceBody, IceEdge),
        new("Duman", 0.45, 0.4, 2.4, 1.00, 7, 1.6, 0.9, 20, IceCore, IceBody, IceEdge),
        new("Kristal", 0.35, 0.0, 1.6, 1.45, 3, 0.5, 0.35, 15,
            Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xCF, 0xE9, 0xFF), Color.FromRgb(0x7F, 0xB6, 0xE8)),
        new("Okyanus", 0.45, 0.3, 1.4, 1.30, 5, 1.1, 0.9, 25,
            Color.FromRgb(0xE0, 0xFF, 0xFB), Color.FromRgb(0x4F, 0xD8, 0xD0), Color.FromRgb(0x1A, 0x7F, 0x9A)),
        new("Aurora", 0.45, 0.3, 1.4, 1.30, 5, 1.0, 0.9, 25,
            Color.FromRgb(0xF0, 0xE8, 0xFF), Color.FromRgb(0x7F, 0xA8, 0xFF), Color.FromRgb(0x8A, 0x4F, 0xE0)),
        new("Orman", 0.40, 0.2, 0.9, 1.50, 4, 0.9, 1.0, 30, IceCore, IceBody, IceEdge),
        new("Minimal", 0.35, 0.8, 3.2, 0.90, 2, 0.6, 0.4, 12, IceCore, IceBody, IceEdge),
    };
}
