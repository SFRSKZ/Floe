using System.IO;
using System.Text.Json;
using Floe.Core.Models;

namespace Floe.App.Services;

public static class DockSettings
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Floe.Core.Services.Product.Folder,
        "dock.json");

    public static event Action? Changed;

    public static DockEdge Edge => DockEdge.Bottom;

    public static bool HideWindowsTaskbar { get; private set; }

    /// <summary>The dock slides off the edge when the cursor leaves it and comes back when it touches the edge.</summary>
    public static bool AutoHide { get; private set; }

    /// <summary>The dock's size at full scale, 0.6 .. 1.</summary>
    public static double Scale { get; private set; } = 1;

    public const double MinScale = 0.6;

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(ConfigPath));
            if (model is null) return;
            HideWindowsTaskbar = model.HideWindowsTaskbar;
            AutoHide = model.AutoHide;
            Scale = Math.Clamp(model.Scale, MinScale, 1);
        }
        catch
        {
            // Bad or temporarily unavailable settings must not prevent the dock from starting.
        }
    }

    public static void SetHideWindowsTaskbar(bool hide)
    {
        if (HideWindowsTaskbar == hide) return;
        HideWindowsTaskbar = hide;
        Save();
        Changed?.Invoke();
    }

    public static void SetAutoHide(bool hide)
    {
        if (AutoHide == hide) return;
        AutoHide = hide;
        Save();
        Changed?.Invoke();
    }

    public static void SetScale(double scale)
    {
        scale = Math.Clamp(scale, MinScale, 1);
        if (Math.Abs(Scale - scale) < 0.001) return;
        Scale = scale;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                HideWindowsTaskbar = HideWindowsTaskbar,
                AutoHide = AutoHide,
                Scale = Scale,
            }));
        }
        catch
        {
            // Settings are best effort; the in-memory choice still applies for this run.
        }
    }

    private sealed class Model
    {
        public bool HideWindowsTaskbar { get; set; }

        public bool AutoHide { get; set; }

        public double Scale { get; set; } = 1;
    }
}
