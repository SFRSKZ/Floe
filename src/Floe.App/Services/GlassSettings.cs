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

public enum VisualizerStyle
{
    Glow,
    Hologram,
    Cubes,
    Stardust,
    DustWave,
    SilkContour,
    EyeFlame,
    Oled,
}

/// <summary>A theme dresses the whole dock (glass, rim, glows) and its visualizer in one look.</summary>
public enum DockTheme
{
    None,
    Mordor,
    TronLegacy,
    TronAres,
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
    Hologram,
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

    public static BulgeStyle Bulge { get; private set; } = BulgeStyle.Soft;

    public static DockTheme Theme { get; private set; }

    /// <summary>The Mordor theme: black iron glass lit from below by embers, ember glows, and Sauron's eye.</summary>
    public static bool Mordor => Theme == DockTheme.Mordor;

    /// <summary>Either Tron theme: black glass edged in neon light lines, with the OLED panel lit in the same colour.</summary>
    public static bool Tron => Theme is DockTheme.TronLegacy or DockTheme.TronAres;

    public static VisualizerStyle VisualizerLook { get; private set; } = VisualizerStyle.Glow;

    public static HologramColor HologramTint { get; private set; } = HologramColor.Blue;

    public static NowPlayingIntro Intro { get; private set; } = NowPlayingIntro.Drop;

    /// <summary>The OLED panel's one preset (0 tunnel .. 4 echo, 5 the grid floor), or -1 to take turns.</summary>
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
            // before themes there was only the Mordor switch
            Theme = Enum.TryParse<DockTheme>(model.Theme, true, out var theme) ? theme : model.Mordor ? DockTheme.Mordor : DockTheme.None;
            Bulge = Enum.TryParse<BulgeStyle>(model.Bulge, true, out var bulge) ? bulge : BulgeStyle.Soft;
            // the eye flame replaced the wraith wind in its place in the list
            VisualizerLook = model.VisualizerLook == "WraithWind" ? VisualizerStyle.EyeFlame
                : Enum.TryParse<VisualizerStyle>(model.VisualizerLook, true, out var look) ? look : VisualizerStyle.Glow;
            HologramTint = Enum.TryParse<HologramColor>(model.HologramTint, true, out var tint) ? tint : HologramColor.Blue;
            Intro = Enum.TryParse<NowPlayingIntro>(model.Intro, true, out var intro) ? intro : NowPlayingIntro.Drop;
            OledEffect = Math.Clamp(model.OledEffect, -1, 5);
            DustColors = Enum.TryParse<DustPalette>(model.DustColors, true, out var dust) ? dust : DustPalette.Tint;
            if (TryParseColor(model.CustomTint, out var custom)) CustomTint = custom;
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
            if (args.Contains("--mordor", StringComparer.OrdinalIgnoreCase)) Theme = DockTheme.Mordor;
            var themeArg = args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
            if (themeArg is not null && Enum.TryParse<DockTheme>(themeArg[8..], true, out var previewTheme)) Theme = previewTheme;
        }
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
    /// Turning a theme on also turns on its visualizer (Mordor lights the eye, Tron the OLED panel); turning it off
    /// leaves the visualizer as it is.
    /// </summary>
    public static void SetTheme(DockTheme theme)
    {
        if (Theme == theme)
        {
            return;
        }

        Theme = theme;
        if (theme != DockTheme.None)
        {
            Visualizer = true;
            VisualizerLook = theme == DockTheme.Mordor ? VisualizerStyle.EyeFlame : VisualizerStyle.Oled;
        }

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
        effect = Math.Clamp(effect, -1, 5);
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
                Mordor = Mordor,
                Theme = Theme.ToString(),
                Bulge = Bulge.ToString(),
                VisualizerLook = VisualizerLook.ToString(),
                HologramTint = HologramTint.ToString(),
                Intro = Intro.ToString(),
                OledEffect = OledEffect,
                CubeSize = CubeSize,
                DustColors = DustColors.ToString(),
                CustomTint = Hex(CustomTint),
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

        public bool Mordor { get; set; }

        public string Theme { get; set; } = nameof(DockTheme.None);

        public string Bulge { get; set; } = nameof(BulgeStyle.Soft);

        public string VisualizerLook { get; set; } = nameof(VisualizerStyle.Glow);

        public double CubeSize { get; set; } = 17;

        public string HologramTint { get; set; } = nameof(HologramColor.Blue);

        public string Intro { get; set; } = nameof(NowPlayingIntro.Drop);

        public int OledEffect { get; set; } = -1;

        public string DustColors { get; set; } = nameof(DustPalette.Tint);

        public string? CustomTint { get; set; }

        public string[]? CustomPalette { get; set; }
    }
}
