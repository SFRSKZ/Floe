using System.IO;
using System.Text.Json;
using Floe.Core.Models;

namespace Floe.App.Services;

/// <summary>The dock as a full-width taskbar: off, the glass strip (icons centred) or the crown (song in the middle).</summary>
public enum TaskbarLayout
{
    Off,
    Strip,
    Crown,
}

public static class DockSettings
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Floe.Core.Services.Product.Folder,
        "dock.json");

    public static event Action? Changed;

    /// <summary>The screen edge the dock stands on; saved, bottom unless another was picked.</summary>
    public static DockEdge Edge { get; private set; } = DockEdge.Bottom;

    /// <summary>The dock runs down a side of the screen: its icons stack, the song and the weather step aside.</summary>
    public static bool Vertical => Edge is DockEdge.Left or DockEdge.Right;

    public static bool HideWindowsTaskbar { get; private set; }

    /// <summary>The dock laid out as a taskbar along the whole bottom edge.</summary>
    public static TaskbarLayout Taskbar { get; private set; }

    /// <summary>The taskbar layout is a bottom-edge strip: on another edge the dock stays a dock (the choice is kept).</summary>
    public static bool TaskbarOn => Taskbar != TaskbarLayout.Off && Edge == DockEdge.Bottom;

    /// <summary>
    /// Whether the dock hides Windows' taskbar: only when that was chosen. The taskbar layout does not decide it;
    /// with Windows' taskbar showing, a Floe taskbar stands on top of it.
    /// </summary>
    public static bool HidesWindowsTaskbar => HideWindowsTaskbar;

    /// <summary>The dock steps aside while a full-screen app is in front; off, it always stays.</summary>
    public static bool HideOnFullscreen { get; private set; } = true;

    /// <summary>The dock slides off the screen edge until the pointer reaches that edge.</summary>
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
            HideOnFullscreen = model.HideOnFullscreen;
            AutoHide = model.AutoHide;
            Scale = Math.Clamp(model.Scale, MinScale, 1);
            Taskbar = Enum.IsDefined(model.Taskbar) ? model.Taskbar : TaskbarLayout.Off;
            Edge = Enum.IsDefined(model.Edge) ? model.Edge : DockEdge.Bottom;
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

    public static void SetHideOnFullscreen(bool hide)
    {
        if (HideOnFullscreen == hide) return;
        HideOnFullscreen = hide;
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

    public static void SetTaskbar(TaskbarLayout layout)
    {
        if (Taskbar == layout) return;
        Taskbar = layout;
        Save();
        Changed?.Invoke();
    }

    public static void SetEdge(DockEdge edge)
    {
        if (!Enum.IsDefined(edge) || Edge == edge) return;
        Edge = edge;
        Save();
        Changed?.Invoke();
    }

    /// <summary>For --render: an edge for this run only, never saved.</summary>
    internal static void PreviewEdge(DockEdge edge) => Edge = edge;

    /// <summary>For --render: a layout for this run only, never saved.</summary>
    internal static void PreviewTaskbar(TaskbarLayout layout) => Taskbar = layout;

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
                HideOnFullscreen = HideOnFullscreen,
                AutoHide = AutoHide,
                Scale = Scale,
                Taskbar = Taskbar,
                Edge = Edge,
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

        public bool HideOnFullscreen { get; set; } = true;

        public bool AutoHide { get; set; }

        public double Scale { get; set; } = 1;

        public TaskbarLayout Taskbar { get; set; }

        public DockEdge Edge { get; set; } = DockEdge.Bottom;
    }
}
