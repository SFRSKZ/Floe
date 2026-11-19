using System;
using System.IO;
using System.Windows.Media;
using Floe.App.Services;
using Floe.Core.Models;

namespace Floe.App.ViewModels;

public sealed class DockItemViewModel
{
    /// <param name="processName">The running process, for an app whose exe could not be read: the name is all there is.</param>
    /// <param name="runningKey">The key a running, unpinned app is grouped under (<see cref="Floe.Core.Services.AppIdentity.RunningKey"/>).</param>
    public DockItemViewModel(DockApp app, bool isPinned = true, IntPtr window = default, string? processName = null, string? runningKey = null)
    {
        App = app;
        IsPinned = isPinned;
        var resolved = AppLauncher.ResolveTarget(app);
        ExePath = resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(resolved) ? resolved : null;
        var extracted = ExtractProcessName(resolved);
        ProcessName = extracted.Length > 0 ? extracted : processName ?? string.Empty;
        Key = isPinned
            ? "P:" + Floe.Core.Services.AppIdentity.PinnedKey(app.Target, ProcessName)
            : "R:" + (string.IsNullOrEmpty(runningKey) ? Floe.Core.Services.AppIdentity.PinnedKey(app.Target, ProcessName) : runningKey);

        ReloadIcon();
        if (Icon is null && window != IntPtr.Zero)
        {
            Icon = IconService.GetWindowIcon(window) ?? IconService.GetFallbackIcon();
        }

        Glow = IconService.GetGlowColor(app);
    }

    public DockApp App { get; }

    /// <summary>Reads the icon again after a new one was picked for this app.</summary>
    public void ReloadIcon()
    {
        var file = IconChoices.File(App);
        Icon = (file is null ? null : IconService.GetIcon(new DockApp { Target = App.Target, IconPath = file })) ?? IconService.GetIcon(App);
    }

    public string Name => App.Name;

    public ImageSource? Icon { get; private set; }

    public Color Glow { get; }

    public bool IsPinned { get; }

    public string ProcessName { get; }

    /// <summary>The exe the item launches, when its target resolves to one.</summary>
    public string? ExePath { get; }

    /// <summary>What the item is known by in the dock: unique per pinned target and per running app, never empty.</summary>
    public string Key { get; }

    public bool IsRunning { get; set; }

    public IReadOnlyList<IntPtr> Windows { get; set; } = Array.Empty<IntPtr>();

    private int _cycle;

    public IntPtr NextWindow()
    {
        if (Windows.Count == 0)
        {
            return IntPtr.Zero;
        }

        var handle = Windows[_cycle % Windows.Count];
        _cycle++;
        return handle;
    }

    public IntPtr PreviousWindow()
    {
        if (Windows.Count == 0)
        {
            return IntPtr.Zero;
        }

        // _cycle points one past the last returned window; step back two to land on the one before it.
        _cycle = ((_cycle - 2) % Windows.Count + Windows.Count) % Windows.Count;
        var handle = Windows[_cycle];
        _cycle++;
        return handle;
    }

    private static string ExtractProcessName(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var bang = target.LastIndexOf('!');
            if (bang >= 0 && bang < target.Length - 1)
            {
                return target[(bang + 1)..];
            }

            var segment = target.TrimEnd('\\');
            var slash = segment.LastIndexOf('\\');
            var last = slash >= 0 ? segment[(slash + 1)..] : segment;
            var underscore = last.IndexOf('_');
            if (underscore > 0)
            {
                last = last[..underscore];
            }

            return last;
        }

        return Path.GetFileNameWithoutExtension(target);
    }
}
