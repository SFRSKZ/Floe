namespace MacDock.App.Effects.Visualizer;

/// <summary>
/// Repaint rate for the visualizers. Every repaint re-presents the full-width visualizer strip,
/// and all windows share one render thread, so the light slows down while the dock animates.
/// </summary>
public static class VisualizerPacing
{
    public static bool DockAnimating { get; set; }

    public static double RepaintInterval => DockAnimating ? 1.0 / 15 : 1.0 / 30;
}
