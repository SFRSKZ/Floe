using System;
using System.IO;
using System.Windows.Media;
using Floe.App.Services;
using Floe.Core.Models;

namespace Floe.App.ViewModels;

public sealed class DockItemViewModel
{
    public DockItemViewModel(DockApp app, bool isPinned = true, IntPtr window = default)
    {
        App = app;
        IsPinned = isPinned;
        ProcessName = ExtractProcessName(AppLauncher.ResolveTarget(app));

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
