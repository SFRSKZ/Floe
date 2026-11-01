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

    public static DockEdge Edge { get; private set; } = DockEdge.Bottom;

    public static bool HideWindowsTaskbar { get; private set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var model = JsonSerializer.Deserialize<Model>(File.ReadAllText(ConfigPath));
            if (model is null) return;
            Edge = Enum.TryParse<DockEdge>(model.Edge, true, out var edge) ? edge : DockEdge.Bottom;
            HideWindowsTaskbar = model.HideWindowsTaskbar;
        }
        catch
        {
            // Bad or temporarily unavailable settings must not prevent the dock from starting.
        }
    }

    public static void SetEdge(DockEdge edge)
    {
        if (Edge == edge) return;
        Edge = edge;
        Save();
        Changed?.Invoke();
    }

    public static void SetHideWindowsTaskbar(bool hide)
    {
        if (HideWindowsTaskbar == hide) return;
        HideWindowsTaskbar = hide;
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        try
        {
            Floe.Core.Services.AtomicFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model
            {
                Edge = Edge.ToString(),
                HideWindowsTaskbar = HideWindowsTaskbar,
            }));
        }
        catch
        {
            // Settings are best effort; the in-memory choice still applies for this run.
        }
    }

    private sealed class Model
    {
        public string? Edge { get; set; }

        public bool HideWindowsTaskbar { get; set; }
    }
}
