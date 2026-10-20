using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace MacDock.App.Services;

public enum DesktopButtonStyle
{
    Ring,
    Windows,
    Monitor,
    Orb,
}

public static class DesktopButtonSettings
{
    public static event Action? Changed;

    public static IReadOnlyList<(string Name, Color Color)> Colors { get; } = new[]
    {
        ("Beyaz", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        ("Buz", Color.FromRgb(0xCF, 0xE8, 0xFF)),
        ("Mor", Color.FromRgb(0xC9, 0xB6, 0xFF)),
        ("Nane", Color.FromRgb(0xA8, 0xF0, 0xC6)),
        ("Amber", Color.FromRgb(0xFF, 0xD9, 0xA0)),
        ("Gül", Color.FromRgb(0xFF, 0xB6, 0xD5)),
    };

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MacDock",
        "desktop-button.json");

    public static double Size { get; private set; } = 0.72;

    public static double Brightness { get; private set; } = 1.0;

    public static int ColorIndex { get; private set; } = 1;

    public static Color Color => Colors[Math.Clamp(ColorIndex, 0, Colors.Count - 1)].Color;

    public static DesktopButtonStyle Style { get; private set; } = DesktopButtonStyle.Ring;

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

            Size = model.Size <= 0 ? 0.72 : Math.Clamp(model.Size, 0.4, 1.6);
            Brightness = model.Brightness <= 0 ? 1.0 : Math.Clamp(model.Brightness, 0.3, 2.2);
            ColorIndex = Math.Clamp(model.ColorIndex, 0, Colors.Count - 1);
            Style = Enum.TryParse<DesktopButtonStyle>(model.Style, true, out var style) ? style : DesktopButtonStyle.Ring;
        }
        catch
        {
            // defaults
        }
    }

    public static void SetSize(double size)
    {
        size = Math.Clamp(size, 0.4, 1.6);
        if (Math.Abs(Size - size) < 0.01)
        {
            return;
        }

        Size = size;
        Save();
        Changed?.Invoke();
    }

    public static void SetBrightness(double brightness)
    {
        brightness = Math.Clamp(brightness, 0.3, 2.2);
        if (Math.Abs(Brightness - brightness) < 0.01)
        {
            return;
        }

        Brightness = brightness;
        Save();
        Changed?.Invoke();
    }

    public static void SetColorIndex(int index)
    {
        index = Math.Clamp(index, 0, Colors.Count - 1);
        if (ColorIndex == index)
        {
            return;
        }

        ColorIndex = index;
        Save();
        Changed?.Invoke();
    }

    public static void SetStyle(DesktopButtonStyle style)
    {
        if (Style == style)
        {
            return;
        }

        Style = style;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            MacDock.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Size = Size,
                Brightness = Brightness,
                ColorIndex = ColorIndex,
                Style = Style.ToString(),
            }));
        }
        catch
        {
            // best effort
        }
    }

    private sealed class Model
    {
        public double Size { get; set; } = 0.72;

        public double Brightness { get; set; } = 1.0;

        public int ColorIndex { get; set; } = 1;

        public string Style { get; set; } = nameof(DesktopButtonStyle.Ring);
    }
}
