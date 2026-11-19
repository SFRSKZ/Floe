using Floe.App.Effects.Gpu.Cp77;
using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>The particle looks that live only on the GPU: the dust engine drawing other shapes.</summary>
public static class GpuSceneCatalog
{
    public static bool Has(VisualizerStyle look) => look is VisualizerStyle.Chladni or VisualizerStyle.Fireflies or VisualizerStyle.Ferrofluid or VisualizerStyle.CyberSignal or VisualizerStyle.CyberBlackwall or VisualizerStyle.CyberCircuit or VisualizerStyle.CyberGlyph or VisualizerStyle.CyberRain or VisualizerStyle.CyberSamurai
        or VisualizerStyle.CyberSandevistan or VisualizerStyle.CyberBreach or VisualizerStyle.CyberCollar
        or VisualizerStyle.CyberPulse or VisualizerStyle.CyberRift
        or VisualizerStyle.CyberZiggurat or VisualizerStyle.CyberNetdive
        or VisualizerStyle.CyberRedline
        or VisualizerStyle.CyberJaws
        or VisualizerStyle.CyberBlackwallCinder or VisualizerStyle.CyberArasakaOs or VisualizerStyle.CyberBraindanceSuite
        or VisualizerStyle.CyberApogee or VisualizerStyle.CyberCantoDaemon or VisualizerStyle.CyberNightCity
        or VisualizerStyle.CyberTraumaTeam or VisualizerStyle.CyberLiquidChrome or VisualizerStyle.CyberDogtownFlir
        or VisualizerStyle.CyberMonowire or VisualizerStyle.CyberStrands;

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
        VisualizerStyle.CyberSandevistan => new SandevistanGpuScene(),
        VisualizerStyle.CyberBreach => new BreachGpuScene(),
        VisualizerStyle.CyberCollar => new CollarGpuScene(),
        VisualizerStyle.CyberPulse => new PulseGpuScene(),
        VisualizerStyle.CyberRift => new RiftGpuScene(),
        VisualizerStyle.CyberZiggurat => new ZigguratGpuScene(),
        VisualizerStyle.CyberNetdive => new NetdiveGpuScene(),
        VisualizerStyle.CyberRedline => new MaterialGpuScene(MaterialGpuScene.Material.Redline),
        VisualizerStyle.CyberJaws => new JawsGpuScene(),
        VisualizerStyle.CyberBlackwallCinder => new BlackwallCinderGpuScene(),
        VisualizerStyle.CyberArasakaOs => new ArasakaTacticalGpuScene(),
        VisualizerStyle.CyberBraindanceSuite => new BraindanceTimelineGpuScene(),
        VisualizerStyle.CyberApogee => new MilitechApogeeGpuScene(),
        VisualizerStyle.CyberCantoDaemon => new CantoBreachGpuScene(),
        VisualizerStyle.CyberNightCity => new NightCityRainGpuScene(),
        VisualizerStyle.CyberTraumaTeam => new TraumaBiometricGpuScene(),
        VisualizerStyle.CyberLiquidChrome => new LiquidChromeGpuScene(),
        VisualizerStyle.CyberDogtownFlir => new DogtownThermalGpuScene(),
        VisualizerStyle.CyberMonowire => new MonowireArcGpuScene(),
        VisualizerStyle.CyberStrands => new StrandCurtainGpuScene(),
        _ => throw new ArgumentOutOfRangeException(nameof(look)),
    };
}
