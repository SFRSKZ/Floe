using System.Windows;
using System.Windows.Media;

namespace Floe.App.Effects.Visualizer;

/// <summary>Music visualizer of glass cubes stacked into columns along the bottom edge of <see cref="Capsule"/>.</summary>
public sealed class CubeVisualizer : FrameworkElement
{
    /// <summary>Bounds in this element's coordinates; the cubes stand on its bottom edge.</summary>
    public Rect Capsule { get; set; } = Rect.Empty;

    /// <summary>On samples audio and draws; off fades out (~400 ms) and then unhooks the render callback.</summary>
    public bool IsActive { get; set; }

    /// <summary>Offscreen renders: 0..1 paints that fixed level without audio; -1 (default) uses the live meter.</summary>
    internal double DebugLevel { get; set; } = -1;

    /// <summary>Offscreen renders: the animation clock in seconds to draw at; -1 (default) uses the real clock.</summary>
    internal double DebugTime { get; set; } = -1;
}
