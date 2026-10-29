using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>The particle looks that live only on the GPU: the dust engine drawing other shapes.</summary>
public static class GpuSceneCatalog
{
    public static bool Has(VisualizerStyle look) => look is VisualizerStyle.Chladni or VisualizerStyle.Fireflies or VisualizerStyle.Ferrofluid;

    public static IGpuScene Create(VisualizerStyle look) => look switch
    {
        VisualizerStyle.Chladni => new StardustGpuScene { Shape = DustShape.Chladni },
        VisualizerStyle.Fireflies => new StardustGpuScene { Shape = DustShape.Fireflies },
        VisualizerStyle.Ferrofluid => new StardustGpuScene { Shape = DustShape.Ferro },
        _ => throw new ArgumentOutOfRangeException(nameof(look)),
    };
}
