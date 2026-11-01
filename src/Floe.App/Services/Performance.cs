using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>What the settings offer: let the dock decide, or pin it to looks or to smoothness.</summary>
public enum PerformanceMode
{
    Auto,
    Quality,
    Smooth,
}

/// <summary>How much decoration the dock can afford. Motion (hover, magnify, slide) never degrades; decoration does.</summary>
public enum PerformanceLevel
{
    Full,
    Balanced,
    Lite,
}

/// <summary>
/// One place that decides how rich the dock may be on this machine. In Auto the dock's own frames are
/// the measure: when they arrive late the level steps down, after a long clean stretch it steps back up.
/// Effects read the knobs below instead of deciding on their own, and listen to <see cref="Changed"/>.
/// </summary>
public static class Performance
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Product.Folder,
        "perf.json");

    // A frame counts as late past 1.7 frames; gaps over a quarter second are pauses, not load.
    private const double LateFactor = 1.7;
    private const double PauseGap = 0.25;
    // Judge over this many animating frames: step down when a fifth are late, up after a long clean run.
    private const int DownWindow = 90;
    private const double DownShare = 0.2;
    private const int UpWindow = 2400;
    private const double UpShare = 0.03;
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(4);

    private static PerformanceLevel _autoLevel;
    private static PerformanceLevel _startLevel;
    private static int _frames;
    private static int _late;
    private static DateTime _lastStep = DateTime.MinValue;

    /// <summary>Raised on the UI thread when <see cref="Level"/> or <see cref="Mode"/> changes.</summary>
    public static event Action? Changed;

    public static PerformanceMode Mode { get; private set; } = PerformanceMode.Auto;

    public static PerformanceLevel Level => Mode switch
    {
        PerformanceMode.Quality => PerformanceLevel.Full,
        PerformanceMode.Smooth => PerformanceLevel.Lite,
        _ => _autoLevel,
    };

    /// <summary>The monitor's refresh rate in Hz (60 when unknown).</summary>
    public static double RefreshRate { get; private set; } = 60;

    /// <summary>Highest rate the dock's own render loop draws at, in Hz; 0 means every monitor frame.</summary>
    public static double DockFrameCap => Level == PerformanceLevel.Full ? 0 : 60;

    /// <summary>Whether idle decoration moves at all: active-icon shaders, the metal rim's drift, chromatic faces.</summary>
    public static bool AmbientMotion => Level != PerformanceLevel.Lite;

    /// <summary>Tick rate for idle decoration, in Hz; 0 when it holds still.</summary>
    public static double AmbientFps => Level switch
    {
        PerformanceLevel.Full => 30,
        PerformanceLevel.Balanced => 15,
        _ => 0,
    };

    /// <summary>Highest rate the music visualizer repaints at, in Hz; 0 means its own pace, unlimited.</summary>
    public static double VisualizerFps => Level switch
    {
        PerformanceLevel.Full => 0,
        PerformanceLevel.Balanced => 30,
        _ => 15,
    };

    /// <summary>Multiplier on the sleep derezz cube size: bigger cubes, fewer of them.</summary>
    public static double DerezzCellScale => Level == PerformanceLevel.Balanced ? 1.5 : 1;

    /// <summary>In Lite the dock dozes off with a plain fade instead of the cube derezz.</summary>
    public static bool DerezzAsFade => Level == PerformanceLevel.Lite;

    /// <summary>In Lite a click skips the liquid-glass drop.</summary>
    public static bool RichClick => Level != PerformanceLevel.Lite;

    public static void Load()
    {
        RefreshRate = ReadRefreshRate();
        _startLevel = WeakHardware() ? PerformanceLevel.Balanced : PerformanceLevel.Full;
        _autoLevel = _startLevel;
        try
        {
            if (File.Exists(ConfigPath)
                && JsonSerializer.Deserialize<Model>(File.ReadAllText(ConfigPath)) is { } model
                && Enum.TryParse<PerformanceMode>(model.Mode, true, out var mode))
            {
                Mode = mode;
            }
        }
        catch
        {
            // best effort: Auto
        }

        Log($"perf level start={Level} mode={Mode} refresh={RefreshRate:F0}Hz tier={RenderCapability.Tier >> 16} cores={Environment.ProcessorCount}");
    }

    public static void SetMode(PerformanceMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        ResetWindow();
        Save();
        Log($"perf mode={mode} level={Level}");
        Changed?.Invoke();
    }

    /// <summary>
    /// Fed by the dock's render loop with the time since its previous frame, only while it animates.
    /// <paramref name="expected"/> is the frame time the loop aims for (seconds).
    /// </summary>
    public static void ReportFrame(double gap, double expected)
    {
        if (Mode != PerformanceMode.Auto || gap <= 0 || gap > PauseGap || expected <= 0)
        {
            return;
        }

        _frames++;
        if (gap > expected * LateFactor)
        {
            _late++;
        }

        if (DateTime.UtcNow - _lastStep < Cooldown)
        {
            return;
        }

        if (_frames >= DownWindow && _late > _frames * DownShare && _autoLevel < PerformanceLevel.Lite)
        {
            Step(_autoLevel + 1, $"late {_late}/{_frames}");
        }
        else if (_frames >= UpWindow && _late < _frames * UpShare && _autoLevel > PerformanceLevel.Full)
        {
            Step(_autoLevel - 1, $"clean {_late}/{_frames}");
        }
        else if (_frames >= UpWindow)
        {
            ResetWindow();
        }
    }

    private static void Step(PerformanceLevel level, string why)
    {
        _autoLevel = level;
        _lastStep = DateTime.UtcNow;
        ResetWindow();
        Log($"perf level={level} ({why})");
        Changed?.Invoke();
    }

    private static void ResetWindow()
    {
        _frames = 0;
        _late = 0;
    }

    private static bool WeakHardware() =>
        RenderCapability.Tier >> 16 < 2
        || RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly
        || Environment.ProcessorCount <= 2;

    private static double ReadRefreshRate()
    {
        try
        {
            var dc = GetDC(IntPtr.Zero);
            var hz = GetDeviceCaps(dc, 116); // VREFRESH
            ReleaseDC(IntPtr.Zero, dc);
            return hz > 1 ? hz : 60;
        }
        catch
        {
            return 60;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new Model { Mode = Mode.ToString() }));
        }
        catch
        {
            // best effort
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "floe.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // logging must never throw
        }
    }

    private sealed class Model
    {
        public string Mode { get; set; } = nameof(PerformanceMode.Auto);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
}
