using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using MacDock.App.ViewModels;

namespace MacDock.App.Services;

public enum AuraColorMode
{
    Palette,
    Icon,
}

public enum AuraEffect
{
    Glow,
    Burst,
}

public sealed class AuraPalette
{
    public AuraPalette(string name, Color first, Color second, Color third)
    {
        Name = name;
        Colors = new[] { first, second, third };
    }

    public string Name { get; }

    public Color[] Colors { get; }
}

public static class AuraSettings
{
    public static event Action? Changed;

    public static IReadOnlyList<AuraPalette> Palettes { get; } = new[]
    {
        new AuraPalette("Aurora", Rgb(0x5B, 0xC8, 0xFF), Rgb(0xA8, 0x55, 0xF7), Rgb(0x34, 0xE0, 0x7A)),
        new AuraPalette("Gün Batımı", Rgb(0xFF, 0x8A, 0x3D), Rgb(0xFF, 0x4D, 0x8D), Rgb(0xB1, 0x4D, 0xFF)),
        new AuraPalette("Okyanus", Rgb(0x22, 0xD3, 0xEE), Rgb(0x3B, 0x82, 0xF6), Rgb(0x63, 0x66, 0xF1)),
        new AuraPalette("Neon", Rgb(0xFF, 0x2D, 0x95), Rgb(0x00, 0xE5, 0xFF), Rgb(0xC6, 0xFF, 0x00)),
        new AuraPalette("Ateş", Rgb(0xFF, 0xD1, 0x66), Rgb(0xFF, 0x6B, 0x35), Rgb(0xEF, 0x2D, 0x56)),
        new AuraPalette("Buz", Rgb(0xE0, 0xF2, 0xFE), Rgb(0xA5, 0xB4, 0xFC), Rgb(0x7D, 0xD3, 0xFC)),
        new AuraPalette("Orman", Rgb(0x9A, 0xE6, 0x6A), Rgb(0x2E, 0xC4, 0x8B), Rgb(0x0E, 0x9F, 0x8C)),
        new AuraPalette("Mono", Rgb(0xFF, 0xFF, 0xFF), Rgb(0xC7, 0xC9, 0xD3), Rgb(0x8A, 0x8F, 0x9C)),
    };

    private static readonly Dictionary<string, Color[]> IconCache = new();
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MacDock",
        "aura.json");

    public static AuraColorMode Mode { get; private set; } = AuraColorMode.Palette;

    public static AuraEffect Effect { get; private set; } = AuraEffect.Glow;

    public static int PaletteIndex { get; private set; }

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

            Mode = Enum.TryParse<AuraColorMode>(model.Mode, true, out var mode) ? mode : AuraColorMode.Palette;
            Effect = Enum.TryParse<AuraEffect>(model.Effect, true, out var effect) ? effect : AuraEffect.Glow;
            PaletteIndex = Math.Clamp(model.PaletteIndex, 0, Palettes.Count - 1);
        }
        catch
        {
            // fall back to defaults
        }
    }

    public static void SetMode(AuraColorMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        Save();
        Changed?.Invoke();
    }

    public static void SetEffect(AuraEffect effect)
    {
        if (Effect == effect)
        {
            return;
        }

        Effect = effect;
        Save();
        Changed?.Invoke();
    }

    public static void SetPalette(int index)
    {
        index = Math.Clamp(index, 0, Palettes.Count - 1);
        if (PaletteIndex == index)
        {
            return;
        }

        PaletteIndex = index;
        Save();
        Changed?.Invoke();
    }

    public static Color[] GetColors(DockItemViewModel item)
    {
        if (Mode == AuraColorMode.Icon && item.Icon is not null)
        {
            if (!IconCache.TryGetValue(item.ProcessName, out var colors))
            {
                colors = IconService.GetIconColors(item.Icon, 3);
                IconCache[item.ProcessName] = colors;
            }

            return colors;
        }

        return Palettes[Math.Clamp(PaletteIndex, 0, Palettes.Count - 1)].Colors;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Mode = Mode.ToString(),
                Effect = Effect.ToString(),
                PaletteIndex = PaletteIndex,
            }));
        }
        catch
        {
            // settings persistence is best-effort
        }
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private sealed class Model
    {
        public string Mode { get; set; } = nameof(AuraColorMode.Palette);

        public string Effect { get; set; } = nameof(AuraEffect.Glow);

        public int PaletteIndex { get; set; }
    }
}
