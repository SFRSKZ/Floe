namespace Floe.App.Effects.Gpu.Desktop;

/// <summary>The scenes the desktop wallpaper can play, by name.</summary>
public static class DesktopScenes
{
    public static readonly string[] Names = { "Strands", "Blackwall", "Relic" };

    public static IGpuScene? Create(string name) => name.ToLowerInvariant() switch
    {
        "strands" => new StrandCurtainGpuScene(desktop: true),
        "blackwall" => new BlackwallDesktopScene(),
        "relic" => new RelicDesktopScene(),
        _ => null,
    };
}
