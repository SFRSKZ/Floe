using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The outline icon set of the cyber themes: black strokes on clear, used as a mask so the dock can paint them in any
/// colour. An app is matched by its name or process; one the set does not know keeps its own icon.
/// </summary>
public static class ContourIcons
{
    private static readonly IconCatalog Set = new("ContourIcons");
    private static readonly Dictionary<string, ImageBrush> Loaded = new();

    /// <summary>The whole set, in catalog order.</summary>
    public static IReadOnlyList<(string Name, string File)> All => Set.All;

    /// <summary>The icon as a mask to lay over a fill, or nothing when the set has no icon for the app.</summary>
    public static ImageBrush? Mask(string name, string? process = null) => Set.Match(name, process) is { } file ? Load(file) : null;

    /// <summary>One icon of the set by its file name, as a mask.</summary>
    public static ImageBrush Load(string file)
    {
        if (Loaded.TryGetValue(file, out var known)) return known;
        var brush = new ImageBrush(Set.Load(file)) { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return Loaded[file] = brush;
    }
}
