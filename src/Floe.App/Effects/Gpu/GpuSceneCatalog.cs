using Floe.App.Effects.Gpu.BladeRunner;
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
        or VisualizerStyle.CyberMonowire or VisualizerStyle.CyberStrands
        or VisualizerStyle.CyberBrHorizon or VisualizerStyle.CyberBrLightBands or VisualizerStyle.CyberBrPulseLine or VisualizerStyle.CyberBrHangingFlames or VisualizerStyle.CyberBrWarmColumns or VisualizerStyle.CyberBrGlitchBlocks or VisualizerStyle.CyberBrFrostPanels or VisualizerStyle.CyberBrLedColumns or VisualizerStyle.CyberBrBarcode or VisualizerStyle.CyberBrLances or VisualizerStyle.CyberBrGreenOrbs or VisualizerStyle.CyberBrChevron;

    /// <summary>The looks the automatic round moves through: this theme's GPU looks that the settings offer.</summary>
    public static VisualizerStyle[] Round(bool cyber, Func<VisualizerStyle, bool> fits) =>
        Enum.GetValues<VisualizerStyle>()
            .Where(look => Has(look) && GlassSettings.IsCyberLook(look) == cyber && !GlassSettings.IsDroppedCyberLook(look) && !GlassSettings.IsRetiredLook(look) && fits(look))
            .ToArray();

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
        VisualizerStyle.CyberBrHorizon => new BrHorizon(),
        VisualizerStyle.CyberBrLightBands => new BrLightBands(),
        VisualizerStyle.CyberBrPulseLine => new BrPulseLine(),
        VisualizerStyle.CyberBrHangingFlames => new BrHangingFlames(),
        VisualizerStyle.CyberBrWarmColumns => new BrWarmColumns(),
        VisualizerStyle.CyberBrGlitchBlocks => new BrGlitchBlocks(),
        VisualizerStyle.CyberBrFrostPanels => new BrFrostPanels(),
        VisualizerStyle.CyberBrLedColumns => new BrLedColumns(),
        VisualizerStyle.CyberBrBarcode => new BrBarcode(),
        VisualizerStyle.CyberBrLances => new BrLances(),
        VisualizerStyle.CyberBrGreenOrbs => new BrGreenOrbs(),
        VisualizerStyle.CyberBrChevron => new BrChevron(),
        _ => throw new ArgumentOutOfRangeException(nameof(look)),
    };
}
