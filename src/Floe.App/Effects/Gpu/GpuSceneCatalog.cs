using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>The particle looks that live only on the GPU: the dust engine drawing other shapes.</summary>
public static class GpuSceneCatalog
{
    public static bool Has(VisualizerStyle look) => look is VisualizerStyle.Chladni or VisualizerStyle.Fireflies or VisualizerStyle.Ferrofluid or VisualizerStyle.CyberSignal or VisualizerStyle.CyberBlackwall or VisualizerStyle.CyberCircuit or VisualizerStyle.CyberGlyph or VisualizerStyle.CyberRain or VisualizerStyle.CyberSamurai
        or VisualizerStyle.CyberBraindance or VisualizerStyle.CyberSandevistan or VisualizerStyle.CyberBreach or VisualizerStyle.CyberCollar;

    public static IGpuScene Create(VisualizerStyle look) => look switch
    {
        VisualizerStyle.Chladni => new StardustGpuScene { Shape = DustShape.Chladni },
        VisualizerStyle.Fireflies => new StardustGpuScene { Shape = DustShape.Fireflies },
        VisualizerStyle.Ferrofluid => new StardustGpuScene { Shape = DustShape.Ferro },
        VisualizerStyle.CyberSignal => new SignalGpuScene(),
        VisualizerStyle.CyberBlackwall => new BlackwallGpuScene(),
        VisualizerStyle.CyberCircuit => new CircuitGpuScene(),
        VisualizerStyle.CyberGlyph => new GlyphGpuScene(),
        VisualizerStyle.CyberRain => new RainGpuScene(false),
        VisualizerStyle.CyberSamurai => new RainGpuScene(true),
        VisualizerStyle.CyberBraindance => new BraindanceGpuScene(),
        VisualizerStyle.CyberSandevistan => new SandevistanGpuScene(),
        VisualizerStyle.CyberBreach => new BreachGpuScene(),
        VisualizerStyle.CyberCollar => new CollarGpuScene(),
        _ => throw new ArgumentOutOfRangeException(nameof(look)),
    };
}
