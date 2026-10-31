using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace Floe.App.Services;

public enum RimStyle
{
    Plain,
    MetalSilver,
    MetalGold,
    MetalChromatic,
}

/// <summary>
/// The dock's surface. Classic is the original glass; the others drop its shine for a calmer, flatter plate,
/// each with a thin metal outline in a tone that suits it.
/// </summary>
public enum DockSurface
{
    Classic,
    Mica,
    Frost,
    Graphite,
    Floating,
}

public enum VisualizerStyle
{
    Glow,
    Hologram,
    Cubes,
    Stardust,
    DustWave,
    SilkContour,
    Oled,
    Prism,
    Wheat,
    Ribbon,
    // the OLED panel's show on a colour LED wall: RGB triads, the user's colours, glass drops on the beat
    Led,
    // the dust wave on the GPU, its grains and trails swept by a MilkDrop flow
    DustMilk,
    // particle looks of their own, each a GPU scene (see Effects/Gpu/GpuSceneCatalog)
    Chladni,
    Fireflies,
    CoverDust,
    Ferrofluid,
    IconDust,
    GlassRain,
    Murmuration,
    StereoField,
    // the cyber themes' own hard edged strips (see Effects/Visualizer/CyberVisualizer)
    CyberBars,
    CyberPulse,
    CyberGrid,
    CyberCode,
    CyberSignal,
    CyberBlackwall,
}

/// <summary>A theme dresses the whole dock (glass, rim, glows) and its visualizer in one look.</summary>
public enum DockTheme
{
    None,
    TronLegacy,
    TronAres,
    CyberHud,
    CyberModules,
    CyberCorporate,
}

public enum HologramColor
{
    Blue,
    Cyan,
    Violet,
    Pink,
    Gold,
    Green,
    White,
    Custom,
}

/// <summary>Colours of the dust and contour visualizers: one tint, or a gradient of several.</summary>
public enum DustPalette
{
    Tint,
    Sunset,
    Aurora,
    Ocean,
    Neon,
    Rainbow,
    Custom,
    Silk,
}

public enum BulgeStyle
{
    Soft,
    Pedestal,
    Flat,
}

public enum NowPlayingIntro
{
    Drop,
    Cubes,
    Dust,
}

/// <summary>Dock glass look: rim style and the music visualizer glow.</summary>
public static class GlassSettings
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Floe",
        "glass.json");

    public static event Action? Changed;

    public static RimStyle Rim { get; private set; } = RimStyle.Plain;

    public static bool Visualizer { get; private set; } = true;

    /// <summary>Stardust and the dust wave drawn by Direct3D in their own layer instead of WPF (experimental).</summary>
    public static bool GpuVisualizer { get; private set; }

    /// <summary>The OLED panel lights in the user's colours, and glass drops swell on it with the beat.</summary>
    public static bool OledColour { get; private set; }

    /// <summary>Every other look is shown through a colour LED screen of RGB dots.</summary>
    public static bool LedOverlay { get; private set; }

    /// <summary>The dock as a window DWM blends on the GPU instead of a layered one; takes effect on restart.</summary>
    public static bool FastDock { get; private set; }

    public static BulgeStyle Bulge { get; private set; } = BulgeStyle.Soft;

    public static DockSurface Surface { get; private set; } = DockSurface.Classic;

    /// <summary>The dock shrinks while the cursor is away and grows back under it; off, it stays full size.</summary>
    public static bool Shrink { get; private set; } = true;

    public static DockTheme Theme { get; private set; }

    /// <summary>Either Tron theme: black glass edged in neon light lines, with the OLED panel lit in the same colour.</summary>
    public static bool Tron => Theme is DockTheme.TronLegacy or DockTheme.TronAres;

    /// <summary>The flat themes: hard-edged plates in solid colours instead of the glass capsule.</summary>
    public static bool Cyber => Theme is DockTheme.CyberHud or DockTheme.CyberModules or DockTheme.CyberCorporate;

    /// <summary>The dock's own colour, or null for what the surface and theme give it. Tron keeps its black glass.</summary>
    public static Color? DockColor { get; private set; }

    public static VisualizerStyle VisualizerLook { get; private set; } = VisualizerStyle.Stardust;

    // still in the code but no longer offered: glow, cubes, silk contour, prism, wheat field, light ribbon
    private static bool IsRetired(VisualizerStyle look) => look is VisualizerStyle.Glow or VisualizerStyle.Cubes
        or VisualizerStyle.SilkContour or VisualizerStyle.Prism or VisualizerStyle.Wheat or VisualizerStyle.Ribbon
        or VisualizerStyle.CoverDust or VisualizerStyle.IconDust or VisualizerStyle.GlassRain or VisualizerStyle.Murmuration or VisualizerStyle.StereoField;

    public static HologramColor HologramTint { get; private set; } = HologramColor.Blue;

    public static NowPlayingIntro Intro { get; private set; } = NowPlayingIntro.Drop;

    /// <summary>
    /// The OLED panel's one preset, or -1 to take turns: 0 tunnel, 1 river, 2 whirlpools, 3 smoke, 4 echo, 5 the grid
    /// floor, 6 spectrum, 7 sonar, 8 starfield, 9 rain, 10 plasma.
    /// </summary>
    public static int OledEffect { get; private set; } = -1;

    public static DustPalette DustColors { get; private set; } = DustPalette.Tint;

    /// <summary>The colour the <see cref="HologramColor.Custom"/> tint takes its hue from.</summary>
    public static Color CustomTint { get; private set; } = Color.FromRgb(0x4D, 0xE0, 0xC0);

    /// <summary>The four stops of the <see cref="DustPalette.Custom"/> palette, middle of the strip outwards.</summary>
    public static Color[] CustomPalette { get; } =
    {
        Color.FromRgb(0xFF, 0xE0, 0x8A), Color.FromRgb(0xFF, 0x6F, 0x91), Color.FromRgb(0x9B, 0x6B, 0xFF), Color.FromRgb(0x3F, 0xC8, 0xFF),
    };

    /// <summary>Goes up whenever any visualizer colour changes, so palettes know to rebuild.</summary>
    public static int ColorVersion { get; private set; }

    /// <summary>Edge of one cube of the Cubes visualizer, in DIP.</summary>
    public static double CubeSize { get; private set; } = 17;

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(ConfigPath));
            if (model is null)
            {
                return;
            }

            Rim = Enum.TryParse<RimStyle>(model.Rim, true, out var rim) ? rim : RimStyle.Plain;
            Visualizer = model.Visualizer;
            GpuVisualizer = model.GpuVisualizer;
            OledColour = model.OledColour;
            LedOverlay = model.LedOverlay;
            FastDock = model.FastDock;
            // a theme that is gone (Mordor) falls back to none, as does its visualizer (the eye flame)
            Theme = Enum.TryParse<DockTheme>(model.Theme, true, out var theme) ? theme : DockTheme.None;
            Bulge = Enum.TryParse<BulgeStyle>(model.Bulge, true, out var bulge) ? bulge : BulgeStyle.Soft;
            Surface = Enum.TryParse<DockSurface>(model.Surface, true, out var surface) ? surface : DockSurface.Classic;
            Shrink = model.Shrink;
            // looks taken off the picker (and "Lamella", the old metal wave) fall back to the stardust
            VisualizerLook = Enum.TryParse<VisualizerStyle>(model.VisualizerLook, true, out var look) && !IsRetired(look)
                ? look
                : VisualizerStyle.Stardust;
            HologramTint = Enum.TryParse<HologramColor>(model.HologramTint, true, out var tint) ? tint : HologramColor.Blue;
            Intro = Enum.TryParse<NowPlayingIntro>(model.Intro, true, out var intro) ? intro : NowPlayingIntro.Drop;
            OledEffect = Math.Clamp(model.OledEffect, -1, 10);
            DustColors = Enum.TryParse<DustPalette>(model.DustColors, true, out var dust) ? dust : DustPalette.Tint;
            if (TryParseColor(model.CustomTint, out var custom)) CustomTint = custom;
            if (TryParseColor(model.DockColor, out var dockColor)) DockColor = dockColor;
            for (int i = 0; i < CustomPalette.Length && i < (model.CustomPalette?.Length ?? 0); i++)
            {
                if (TryParseColor(model.CustomPalette![i], out var stop)) CustomPalette[i] = stop;
            }
            CubeSize = model.CubeSize <= 0 ? 17 : Math.Clamp(Math.Round(model.CubeSize), 8, 24);
        }
        catch
        {
            // fall back to defaults
        }
        finally
        {
            // previews can try the theme on without touching the saved settings
            var args = Environment.GetCommandLineArgs();
            var themeArg = args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
            if (themeArg is not null && Enum.TryParse<DockTheme>(themeArg[8..], true, out var previewTheme)) Theme = previewTheme;
            var dockColorArg = args.FirstOrDefault(a => a.StartsWith("--dockcolor=", StringComparison.OrdinalIgnoreCase));
            if (dockColorArg is not null && TryParseColor(dockColorArg[12..], out var previewColor)) DockColor = previewColor;
            var surfaceArg = args.FirstOrDefault(a => a.StartsWith("--surface=", StringComparison.OrdinalIgnoreCase));
            if (surfaceArg is not null && Enum.TryParse<DockSurface>(surfaceArg[10..], true, out var previewSurface)) Surface = previewSurface;
            var rimArg = args.FirstOrDefault(a => a.StartsWith("--rim=", StringComparison.OrdinalIgnoreCase));
            if (rimArg is not null && Enum.TryParse<RimStyle>(rimArg[6..], true, out var previewRim)) Rim = previewRim;
            var lookArg = args.FirstOrDefault(a => a.StartsWith("--look=", StringComparison.OrdinalIgnoreCase));
            if (lookArg is not null && Enum.TryParse<VisualizerStyle>(lookArg[7..], true, out var previewLook)) VisualizerLook = previewLook;
            var gpuArg = args.FirstOrDefault(a => a.StartsWith("--gpu-viz=", StringComparison.OrdinalIgnoreCase));
            if (gpuArg is not null) GpuVisualizer = gpuArg[10..] is "1" or "on" or "true";
        }
    }

    internal static void PreviewTheme(DockTheme theme)
    {
        Theme = theme;
        ColorVersion++;
    }

    internal static void PreviewCubeSize(double size)
    {
        CubeSize = Math.Clamp(Math.Round(size), 8, 24);
    }

    public static void SetRim(RimStyle rim)
    {
        if (Rim == rim)
        {
            return;
        }

        Rim = rim;
        Save();
        Changed?.Invoke();
    }

    public static void SetSurface(DockSurface surface)
    {
        if (Surface == surface)
        {
            return;
        }

        Surface = surface;
        Save();
        Changed?.Invoke();
    }

    public static void SetShrink(bool enabled)
    {
        if (Shrink == enabled)
        {
            return;
        }

        Shrink = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetVisualizer(bool enabled)
    {
        if (Visualizer == enabled)
        {
            return;
        }

        Visualizer = enabled;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Turning a theme on also turns on its visualizer (Tron lights the OLED panel); turning it off
    /// leaves the visualizer as it is.
    /// </summary>
    public static void SetTheme(DockTheme theme)
    {
        if (Theme == theme)
        {
            return;
        }

        Theme = theme;
        if (Tron)
        {
            Visualizer = true;
            VisualizerLook = VisualizerStyle.Oled;
        }

        Save();
        Changed?.Invoke();
    }

    public static void SetDockColor(Color? color)
    {
        if (DockColor == color) return;
        DockColor = color;
        SaveSoon();
        Changed?.Invoke();
    }

    public static void SetOledColour(bool enabled)
    {
        if (OledColour == enabled) return;
        OledColour = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetFastDock(bool enabled)
    {
        if (FastDock == enabled) return;
        FastDock = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetLedOverlay(bool enabled)
    {
        if (LedOverlay == enabled) return;
        LedOverlay = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetGpuVisualizer(bool enabled)
    {
        if (GpuVisualizer == enabled)
        {
            return;
        }

        GpuVisualizer = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetBulge(BulgeStyle bulge)
    {
        if (Bulge == bulge)
        {
            return;
        }

        Bulge = bulge;
        Save();
        Changed?.Invoke();
    }

    public static void SetVisualizerLook(VisualizerStyle look)
    {
        if (VisualizerLook == look)
        {
            return;
        }

        VisualizerLook = look;
        Save();
        Changed?.Invoke();
    }

    public static void SetHologramTint(HologramColor tint)
    {
        if (HologramTint == tint)
        {
            return;
        }

        HologramTint = tint;
        ColorVersion++;
        Save();
        Changed?.Invoke();
    }

    // Diagnostic override for offscreen renders; the user's choice is not persisted.
    internal static void PreviewTint(HologramColor tint)
    {
        HologramTint = tint;
        ColorVersion++;
    }

    public static void SetIntro(NowPlayingIntro intro)
    {
        if (Intro == intro)
        {
            return;
        }

        Intro = intro;
        Save();
        Changed?.Invoke();
    }

    public static void SetOledEffect(int effect)
    {
        effect = Math.Clamp(effect, -1, 10);
        if (OledEffect == effect)
        {
            return;
        }

        OledEffect = effect;
        Save();
        Changed?.Invoke();
    }

    public static void SetDustColors(DustPalette palette)
    {
        if (DustColors == palette)
        {
            return;
        }

        DustColors = palette;
        ColorVersion++;
        Save();
        Changed?.Invoke();
    }

    // Diagnostic override for offscreen renders; the user's choice is not persisted.
    internal static void PreviewDustColors(DustPalette palette)
    {
        DustColors = palette;
        ColorVersion++;
    }

    // The custom colours change live while a slider is dragged: no Changed (that re-lays the dock out),
    // the visualizers pick them up through ColorVersion, and the file is written once the dragging pauses.
    public static void SetCustomTint(Color color)
    {
        CustomTint = color;
        ColorVersion++;
        SaveSoon();
    }

    public static void SetCustomPaletteStop(int index, Color color)
    {
        CustomPalette[index] = color;
        ColorVersion++;
        SaveSoon();
    }

    private static System.Threading.Timer? _saveTimer;

    private static void SaveSoon()
    {
        _saveTimer ??= new System.Threading.Timer(_ => Save());
        _saveTimer.Change(400, System.Threading.Timeout.Infinite);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' || !int.TryParse(text[1..], System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static void SetCubeSize(double size)
    {
        size = Math.Clamp(Math.Round(size), 8, 24);
        if (CubeSize == size)
        {
            return;
        }

        CubeSize = size;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Rim = Rim.ToString(),
                Visualizer = Visualizer,
                GpuVisualizer = GpuVisualizer,
                OledColour = OledColour,
                LedOverlay = LedOverlay,
                FastDock = FastDock,
                Theme = Theme.ToString(),
                Bulge = Bulge.ToString(),
                Surface = Surface.ToString(),
                Shrink = Shrink,
                VisualizerLook = VisualizerLook.ToString(),
                HologramTint = HologramTint.ToString(),
                Intro = Intro.ToString(),
                OledEffect = OledEffect,
                CubeSize = CubeSize,
                DustColors = DustColors.ToString(),
                CustomTint = Hex(CustomTint),
                DockColor = DockColor is { } dockColor ? Hex(dockColor) : null,
                CustomPalette = CustomPalette.Select(Hex).ToArray(),
            }));
        }
        catch
        {
            // settings are best effort
        }
    }

    private sealed class Model
    {
        public string Rim { get; set; } = nameof(RimStyle.Plain);

        public bool Visualizer { get; set; } = true;

        public bool GpuVisualizer { get; set; }

        public bool OledColour { get; set; }

        public bool LedOverlay { get; set; }

        public bool FastDock { get; set; }


        public string Theme { get; set; } = nameof(DockTheme.None);

        public string Bulge { get; set; } = nameof(BulgeStyle.Soft);

        public string Surface { get; set; } = nameof(DockSurface.Classic);

        public bool Shrink { get; set; } = true;

        public string VisualizerLook { get; set; } = nameof(VisualizerStyle.Stardust);

        public double CubeSize { get; set; } = 17;

        public string HologramTint { get; set; } = nameof(HologramColor.Blue);

        public string Intro { get; set; } = nameof(NowPlayingIntro.Drop);

        public int OledEffect { get; set; } = -1;

        public string DustColors { get; set; } = nameof(DustPalette.Tint);

        public string? CustomTint { get; set; }

        public string? DockColor { get; set; }

        public string[]? CustomPalette { get; set; }
    }
}
