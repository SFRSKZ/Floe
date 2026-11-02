using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using Floe.Core.Services;

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

/// <summary>A set of studio colours saved under a name; a null colour keeps Yellow HUD's own.</summary>
public sealed record CyberPreset(string Name, Color? Body, Color? Accent, Color? Strip, Color? Ink);

public enum AppLanguage
{
    Auto,
    Turkish,
    English,
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
    CyberSignal,
    CyberBlackwall,
    CyberCircuit,
    CyberGlyph,
    CyberRain,
    CyberSamurai,
    // the second wave of cyber looks, each a GPU feedback scene of its own
    CyberBraindance,
    CyberSandevistan,
    CyberBreach,
    CyberCollar,
}

/// <summary>A theme dresses the whole dock (glass, rim, glows) and its visualizer in one look.</summary>
public enum DockIconSet
{
    Contour,
    Cards,
    Real,
}

public enum DockTheme
{
    None,
    TronLegacy,
    TronAres,
    CyberHud,
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
        Product.Folder,
        "glass.json");

    public static event Action? Changed;

    public static RimStyle Rim { get; private set; } = RimStyle.Plain;

    public static bool Visualizer { get; private set; } = true;

    /// <summary>Stardust and the dust wave drawn by Direct3D in their own layer instead of WPF (experimental).</summary>
    public static bool GpuVisualizer { get; private set; }

    /// <summary>The city the weather comes from; null until one is picked in the settings.</summary>
    public static string? WeatherCity { get; private set; }

    public static double WeatherLatitude { get; private set; }

    public static double WeatherLongitude { get; private set; }

    public static event Action? WeatherCityChanged;

    /// <summary>The OLED panel lights in the user's colours, and glass drops swell on it with the beat.</summary>
    public static bool OledColour { get; private set; }

    /// <summary>Every other look is shown through a colour LED screen of RGB dots.</summary>
    public static bool LedOverlay { get; private set; }

    public static BulgeStyle Bulge { get; private set; } = BulgeStyle.Soft;

    public static DockSurface Surface { get; private set; } = DockSurface.Classic;

    /// <summary>The dock shrinks while the cursor is away and grows back under it; off, it stays full size.</summary>
    public static bool Shrink { get; private set; } = true;

    public static bool FilmGrain { get; private set; }

    public static DockIconSet IconSet { get; private set; } = DockIconSet.Real;

    public static AppLanguage Language { get; private set; }

    public static bool GlyphNegative { get; private set; }

    public static bool VisualizerThemeColours { get; private set; }

    public static bool SignalEqualizer { get; private set; }

    public static DockTheme Theme { get; private set; }

    /// <summary>Either Tron theme: black glass edged in neon light lines, with the OLED panel lit in the same colour.</summary>
    public static bool Tron => Theme is DockTheme.TronLegacy or DockTheme.TronAres;

    /// <summary>The flat themes: hard-edged plates in solid colours instead of the glass capsule.</summary>
    public static bool Cyber => Theme == DockTheme.CyberHud;

    /// <summary>The dock's own colour, or null for what the surface and theme give it. Tron keeps its black glass.</summary>
    public static Color? DockColor { get; private set; }

    /// <summary>The studio's colours beside <see cref="DockColor"/>; null keeps Yellow HUD's own.</summary>
    public static Color? CyberAccent { get; private set; }

    public static Color? CyberStrip { get; private set; }

    public static Color? CyberInk { get; private set; }

    /// <summary>The colour of the glitch mark on the app in front; null: black or white, whichever reads on the dock.</summary>
    public static Color? GlitchColor { get; private set; }

    /// <summary>How deep the plates' corners are cut: 1 is the design, 0 square.</summary>
    public static double CyberCut { get; private set; } = 1;

    public static double GrainAmount { get; private set; } = 1;

    public static IReadOnlyList<CyberPreset> CyberPresets { get; private set; } = Array.Empty<CyberPreset>();

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
            WeatherCity = model.WeatherCity;
            WeatherLatitude = model.WeatherLatitude;
            WeatherLongitude = model.WeatherLongitude;
            OledColour = model.OledColour;
            LedOverlay = model.LedOverlay;
            // a theme that is gone (Mordor) falls back to none, as does its visualizer (the eye flame)
            Theme = Enum.TryParse<DockTheme>(model.Theme, true, out var theme) ? theme : DockTheme.None;
            Bulge = Enum.TryParse<BulgeStyle>(model.Bulge, true, out var bulge) ? bulge : BulgeStyle.Soft;
            Surface = Enum.TryParse<DockSurface>(model.Surface, true, out var surface) ? surface : DockSurface.Classic;
            Shrink = model.Shrink;
            FilmGrain = model.FilmGrain;
            // the outline and black sets are gone from the settings: whoever had one gets the apps' own icons
            IconSet = Enum.TryParse<DockIconSet>(model.IconSet, true, out var iconSet) && iconSet != DockIconSet.Contour ? iconSet : DockIconSet.Real;
            Language = Enum.TryParse<AppLanguage>(model.Language, true, out var language) ? language : AppLanguage.Auto;
            GlyphNegative = model.GlyphNegative;
            VisualizerThemeColours = model.VisualizerThemeColours;
            SignalEqualizer = model.SignalEqualizer;
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
            if (TryParseColor(model.CyberAccent, out var cyberAccent)) CyberAccent = cyberAccent;
            if (TryParseColor(model.CyberStrip, out var cyberStrip)) CyberStrip = cyberStrip;
            if (TryParseColor(model.CyberInk, out var cyberInk)) CyberInk = cyberInk;
            if (TryParseColor(model.GlitchColor, out var glitchColor)) GlitchColor = glitchColor;
            CyberCut = Math.Clamp(model.CyberCut, 0, 2);
            GrainAmount = Math.Clamp(model.GrainAmount, 0.3, 2);
            CyberPresets = (model.CyberPresets ?? new List<PresetModel>())
                .Select(saved => new CyberPreset(saved.Name, Parsed(saved.Body), Parsed(saved.Accent), Parsed(saved.Strip), Parsed(saved.Ink)))
                .ToList();
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
            // each product only keeps its own themes, including the defaults a missing file leaves behind;
            // this runs before the arguments below so a preview can still try any theme on
            NormalizeTheme();
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
            if (args.Contains("--filmgrain", StringComparer.OrdinalIgnoreCase)) FilmGrain = true;
            if (args.Contains("--english", StringComparer.OrdinalIgnoreCase)) Language = AppLanguage.English;
            if (args.Contains("--negative", StringComparer.OrdinalIgnoreCase)) GlyphNegative = true;
            if (args.Contains("--no-negative", StringComparer.OrdinalIgnoreCase)) GlyphNegative = false;
            if (args.Contains("--themed", StringComparer.OrdinalIgnoreCase)) VisualizerThemeColours = true;
            if (args.Contains("--equalizer", StringComparer.OrdinalIgnoreCase)) SignalEqualizer = true;
            var gpuArg = args.FirstOrDefault(a => a.StartsWith("--gpu-viz=", StringComparison.OrdinalIgnoreCase));
            if (gpuArg is not null) GpuVisualizer = gpuArg[10..] is "1" or "on" or "true";
        }
    }

    // The Cyber code stays compiled in both builds; this keeps each product on its own half of it.
    private static void NormalizeTheme()
    {
#if CP77
        Theme = DockTheme.CyberHud; // the only theme it has; a removed one lands here too
        if (!IsCyberLook(VisualizerLook)) VisualizerLook = VisualizerStyle.CyberBlackwall;
        GpuVisualizer = true; // its looks only exist on the GPU
#else
        if (Theme == DockTheme.CyberHud) Theme = DockTheme.None;
        if (IsCyberLook(VisualizerLook)) VisualizerLook = VisualizerStyle.Stardust;
#endif
    }

    public static bool IsCyberLook(VisualizerStyle look) => look is VisualizerStyle.CyberSignal or VisualizerStyle.CyberBlackwall or VisualizerStyle.CyberCircuit or VisualizerStyle.CyberGlyph or VisualizerStyle.CyberRain or VisualizerStyle.CyberSamurai
        or VisualizerStyle.CyberBraindance or VisualizerStyle.CyberSandevistan or VisualizerStyle.CyberBreach or VisualizerStyle.CyberCollar;

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

    public static void SetGlyphNegative(bool enabled)
    {
        if (GlyphNegative == enabled)
        {
            return;
        }

        GlyphNegative = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetVisualizerThemeColours(bool enabled)
    {
        if (VisualizerThemeColours == enabled)
        {
            return;
        }

        VisualizerThemeColours = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetSignalEqualizer(bool enabled)
    {
        if (SignalEqualizer == enabled)
        {
            return;
        }

        SignalEqualizer = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetLanguage(AppLanguage language)
    {
        if (Language == language)
        {
            return;
        }

        Language = language;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Shows a set in a --render frame without touching the saved settings.</summary>
    internal static void PreviewIconSet(DockIconSet set) => IconSet = set;

    public static void SetIconSet(DockIconSet set)
    {
        if (IconSet == set)
        {
            return;
        }

        IconSet = set;
        Save();
        Changed?.Invoke();
    }

    public static void SetFilmGrain(bool enabled)
    {
        if (FilmGrain == enabled)
        {
            return;
        }

        FilmGrain = enabled;
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

    public static void SetCyberColours(Color? body, Color? accent, Color? strip, Color? ink)
    {
        if (DockColor == body && CyberAccent == accent && CyberStrip == strip && CyberInk == ink) return;
        DockColor = body;
        CyberAccent = accent;
        CyberStrip = strip;
        CyberInk = ink;
        SaveSoon();
        Changed?.Invoke();
    }

    // the mark reads it on its next step, so the dock is not repainted for it
    public static void SetGlitchColor(Color? color)
    {
        if (GlitchColor == color) return;
        GlitchColor = color;
        SaveSoon();
    }

    public static void SetCyberPresets(IEnumerable<CyberPreset> presets)
    {
        CyberPresets = presets.ToList();
        SaveSoon();
    }

    private static Color? Parsed(string? text) => TryParseColor(text, out var color) ? color : null;

    public static void SetCyberCut(double cut)
    {
        if (CyberCut == cut) return;
        CyberCut = cut;
        SaveSoon();
        Changed?.Invoke();
    }

    public static void SetGrainAmount(double amount)
    {
        if (GrainAmount == amount) return;
        GrainAmount = amount;
        SaveSoon();
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

    public static void SetLedOverlay(bool enabled)
    {
        if (LedOverlay == enabled) return;
        LedOverlay = enabled;
        Save();
        Changed?.Invoke();
    }

    public static void SetWeatherCity(string name, double latitude, double longitude)
    {
        WeatherCity = name;
        WeatherLatitude = latitude;
        WeatherLongitude = longitude;
        Save();
        WeatherCityChanged?.Invoke();
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
                WeatherCity = WeatherCity,
                WeatherLatitude = WeatherLatitude,
                WeatherLongitude = WeatherLongitude,
                OledColour = OledColour,
                LedOverlay = LedOverlay,
                Theme = Theme.ToString(),
                Bulge = Bulge.ToString(),
                Surface = Surface.ToString(),
                Shrink = Shrink,
                FilmGrain = FilmGrain,
                IconSet = IconSet.ToString(),
                Language = Language.ToString(),
                GlyphNegative = GlyphNegative,
                VisualizerThemeColours = VisualizerThemeColours,
                SignalEqualizer = SignalEqualizer,
                VisualizerLook = VisualizerLook.ToString(),
                HologramTint = HologramTint.ToString(),
                Intro = Intro.ToString(),
                OledEffect = OledEffect,
                CubeSize = CubeSize,
                DustColors = DustColors.ToString(),
                CustomTint = Hex(CustomTint),
                DockColor = DockColor is { } dockColor ? Hex(dockColor) : null,
                CyberAccent = CyberAccent is { } cyberAccent ? Hex(cyberAccent) : null,
                CyberStrip = CyberStrip is { } cyberStrip ? Hex(cyberStrip) : null,
                CyberInk = CyberInk is { } cyberInk ? Hex(cyberInk) : null,
                GlitchColor = GlitchColor is { } glitchColor ? Hex(glitchColor) : null,
                CyberCut = CyberCut,
                GrainAmount = GrainAmount,
                CyberPresets = CyberPresets.Select(preset => new PresetModel
                {
                    Name = preset.Name,
                    Body = preset.Body is { } body ? Hex(body) : null,
                    Accent = preset.Accent is { } accent ? Hex(accent) : null,
                    Strip = preset.Strip is { } strip ? Hex(strip) : null,
                    Ink = preset.Ink is { } ink ? Hex(ink) : null,
                }).ToList(),
                CustomPalette = CustomPalette.Select(Hex).ToArray(),
            }));
        }
        catch
        {
            // settings are best effort
        }
    }

    private sealed class PresetModel
    {
        public string Name { get; set; } = string.Empty;

        public string? Body { get; set; }

        public string? Accent { get; set; }

        public string? Strip { get; set; }

        public string? Ink { get; set; }
    }

    private sealed class Model
    {
        public string Rim { get; set; } = nameof(RimStyle.Plain);

        public bool Visualizer { get; set; } = true;

        public bool GpuVisualizer { get; set; }

        public string? WeatherCity { get; set; }

        public double WeatherLatitude { get; set; }

        public double WeatherLongitude { get; set; }

        public bool OledColour { get; set; }

        public bool LedOverlay { get; set; }


        public string Theme { get; set; } = nameof(DockTheme.None);

        public string Bulge { get; set; } = nameof(BulgeStyle.Soft);

        public string Surface { get; set; } = nameof(DockSurface.Classic);

        public bool Shrink { get; set; } = true;

        public bool FilmGrain { get; set; }

        public string? IconSet { get; set; }

        // what came before IconSet: off meant the real icons
        public bool ContourIcons { get; set; } = true;

        public string Language { get; set; } = nameof(AppLanguage.Auto);

        public bool GlyphNegative { get; set; }

        public bool VisualizerThemeColours { get; set; }

        public bool SignalEqualizer { get; set; }

        public string VisualizerLook { get; set; } = nameof(VisualizerStyle.Stardust);

        public double CubeSize { get; set; } = 17;

        public string HologramTint { get; set; } = nameof(HologramColor.Blue);

        public string Intro { get; set; } = nameof(NowPlayingIntro.Drop);

        public int OledEffect { get; set; } = -1;

        public string DustColors { get; set; } = nameof(DustPalette.Tint);

        public string? CustomTint { get; set; }

        public string? DockColor { get; set; }

        public string? CyberAccent { get; set; }

        public string? CyberStrip { get; set; }

        public string? CyberInk { get; set; }

        public string? GlitchColor { get; set; }

        public double CyberCut { get; set; } = 1;

        public double GrainAmount { get; set; } = 1;

        public List<PresetModel>? CyberPresets { get; set; }

        public string[]? CustomPalette { get; set; }
    }
}
