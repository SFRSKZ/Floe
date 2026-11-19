using System;
using System.Collections.Generic;
using System.Windows;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// The dock laid out as a taskbar: one bar along the whole bottom edge, as tall as the Windows taskbar.
/// The dock keeps its own measures and is scaled down to the bar's height; its capsule reaches past the window on
/// both sides so that, scaled about the bottom middle, it covers the monitor from edge to edge.
/// Strip: start and icons together in the middle (as Windows 11 centres them), the song, weather, tray and clock at the right.
/// Crown: start and icons at the left, the song and the rest at the right, the middle left to the light.
/// The visualizer strip lies over the bar, its light kept off the start button, the icons, the song and the clock.
/// </summary>
public partial class DockWindow
{
    private const double TaskbarHeight = 48;

    private static double TaskbarScale => TaskbarHeight / RestBarHeight;

    // the glass reaches this far past the monitor on both sides, so its lit side edges stay off the screen
    private const double TaskbarBleed = 24;

    // the song's buttons on the 48 DIP bar: at the dock's size they shrink to fiddly targets
    private const double TaskbarMediaButtonScale = 1.35;

    // the bar's visible ends, in root coordinates: the capsule's, less the bleed on the taskbar
    private double BarLeft => _capsuleLeft + (TaskbarOn ? TaskbarBleed : 0);
    private double BarRight => _capsuleLeft + _capsuleWidth - (TaskbarOn ? TaskbarBleed : 0);

    private static bool TaskbarOn => DockSettings.TaskbarOn;

    private static bool Crown => DockSettings.Taskbar == TaskbarLayout.Crown;

    private TaskbarLayout _appliedTaskbar = DockSettings.Taskbar;

    // whether the bar was really on: a layout picked while the dock sits on another edge leaves it off
    private bool _appliedTaskbarOn = DockSettings.TaskbarOn;

    // the icon row and where the right hand group begins, in root coordinates, as the last layout put them
    private double _taskbarIconsLeft;
    private double _taskbarIconsRight;
    private double _taskbarRightStart;
    private double _taskbarMediaLeft;

    /// <summary>The monitor's width in DIP: the taskbar window spans exactly that.</summary>
    private double TaskbarWindowWidth()
    {
        var monitor = _monitorInfo.Monitor;
        var width = monitor.Right > monitor.Left ? (monitor.Right - monitor.Left) / DockScale : SystemParameters.PrimaryScreenWidth;
        return Math.Max(200, width);
    }

    private void ApplyTaskbarGeometry()
    {
        _windowWidth = TaskbarWindowWidth();
        _capsuleWidth = (_windowWidth / TaskbarScale) + (2 * TaskbarBleed);
        _capsuleLeft = (_windowWidth - _capsuleWidth) / 2;
        _restCapsuleLeft = _capsuleLeft;
        _restCapsuleWidth = _capsuleWidth;
        _backdrop.Width = _windowWidth;
    }

    // the start button and its divider, in front of the icon row
    private const double StartRun = StartWidth + StartGap;

    /// <summary>
    /// Where the icon row begins: in the strip the start button and the icons are centred as one group, the icons
    /// right after the start button; in the crown both sit at the left.
    /// </summary>
    private double TaskbarIconStart(double total) => Crown ? BarLeft + LeadX : ((_windowWidth - total - StartRun) / 2) + StartRun;

    /// <summary>Where the start button stands: at the bar's left end, or in the strip just before the icons.</summary>
    private double StartButtonLeft => TaskbarOn && !Crown ? _taskbarIconsLeft - StartRun : BarLeft + PadX;

    /// <summary>
    /// How far the song card opens: as far as the player asks, unless on the taskbar the icon row would then run
    /// into the right hand group (many icons, a narrow monitor). The song gives way then, so the icons never
    /// overlap it and the visualizer keeps some free glass.
    /// </summary>
    private double RevealTarget => _mediaTarget > 0 && TaskbarOn && !TaskbarSongFits() ? 0 : _mediaTarget;

    private bool TaskbarSongFits()
    {
        var count = _slots.Count;
        var total = (Spacing * Math.Max(0, count - 1)) + (HasSeparator ? SeparatorExtra : 0) + (IconBox * count);
        return TaskbarIconStart(total) + total + ClockGap <= BarRight - TrailWidth(1);
    }

    /// <summary>The song card's width and button size follow the mode.</summary>
    private void ApplyTaskbarMedia()
    {
        _media.Width = MediaWidth;
        _mediaButtonsColumn.Width = new GridLength(MediaButton * 3 * MediaButtonScale);
        _mediaButtonsScale.ScaleX = _mediaButtonsScale.ScaleY = MediaButtonScale;
    }

    /// <summary>A new layout, or the taskbar turned on or off: everything is measured and placed again.</summary>
    private void ApplyTaskbarChange()
    {
        // the edge counts too: moving a taskbar dock to a side turns the bar off without a new layout
        if (_appliedTaskbar == DockSettings.Taskbar && _appliedTaskbarOn == TaskbarOn) return;
        var wasOn = _appliedTaskbarOn;
        _appliedTaskbar = DockSettings.Taskbar;
        _appliedTaskbarOn = TaskbarOn;
        Log($"taskbar {DockSettings.Taskbar}");
        _targetBulge = _currentBulge = _bulgeVel = 0;
        _targetDockScale = CompactScale;
        ApplyTaskbarMedia();
        RebuildIcons();
        Reposition();
        if (wasOn && !TaskbarOn)
        {
            // the taskbar's say on the looks ends here: the crown handed the GPU looks an inverted span (1 → 0, the
            // whole strip) and PlaceTaskbarVisualizer is not called off the bar, so the span is cleared here and the
            // dock's own rect takes over again; the last span handed over must not pass for this frame's in
            // FollowDockGpu, or its guard would leave the strip unmasked
            _placedGpuDock = Rect.Empty;
            _visualizerWindow?.SetGpuDock(null, null);
            _visualizerWindow?.SetClear(Array.Empty<float>());
        }

        ApplyVisualizerState();
        EnsureAnimation();
    }

    /// <summary>The monitor may have changed under a taskbar: its width follows.</summary>
    private void FitTaskbarToMonitor()
    {
        if (TaskbarOn && Math.Abs(TaskbarWindowWidth() - _windowWidth) > 0.5) RebuildIcons();
    }

    /// <summary>The visualizer strip over the bar, its gap and the spans it leaves clear.</summary>
    private void PlaceTaskbarVisualizer()
    {
        if (_visualizerWindow is null) return;
        var scale = DockScale;
        // full width, on the bar's own floor (above a Windows taskbar that shows)
        var area = AppBarBounds;
        double left = area.Left / scale, width = Math.Max(1, (area.Right - area.Left) / scale);
        var bounds = new Rect(left, (area.Bottom / scale) - TaskbarHeight, width, TaskbarHeight);

        // the gap the looks are built around: the icon row, or in the crown the song at the right (the group's
        // start while nothing plays), so the light spreads over the free middle
        var song = _mediaReveal > 0.5;
        // the strip's gap holds the start button too: it sits right before the icons
        double gapLeft = Crown ? (song ? _taskbarMediaLeft : _taskbarRightStart) - 6 : StartButtonLeft - 8;
        double gapRight = Crown ? (song ? _taskbarMediaLeft + MediaWidth : _taskbarRightStart) + 6 : _taskbarIconsRight + 8;
        double screenGapLeft = ToScreenX(gapLeft), screenGapRight = ToScreenX(gapRight);
        var gap = new Rect(screenGapLeft, bounds.Top, Math.Max(1, screenGapRight - screenGapLeft), bounds.Height);
        // Crown: the GPU looks draw the whole strip: an inverted span (1 → 0) leaves them nothing to skip, so the
        // light runs on under the icons and the spans below (SetClear) clear it off them. Any other bar hands no
        // span over and the looks follow the gap Place is given.
        _visualizerWindow.SetGpuDock(Crown ? 1f : null, Crown ? 0f : null);
        // no dock to stand under: the strip stays over the bar, and the bar goes under it below
        _visualizerWindow.Place(bounds, scale, IntPtr.Zero, gap);

        float At(double rootX) => (float)Math.Clamp((ToScreenX(rootX) - left) / width, 0, 1);
        var spans = new List<float>(6);
        if (Crown)
        {
            // start and icons in one run from the left edge; the song is in the right hand group
            spans.AddRange(new[] { 0f, At(_taskbarIconsRight + 10) });
        }
        else
        {
            spans.AddRange(new[] { At(gapLeft - 4), At(gapRight + 4) });
        }

        spans.AddRange(new[] { At(_taskbarRightStart - 8), 1f });
        _visualizerWindow.SetClear(spans.ToArray());
    }

    private (double, double, double, double, bool) _placedTaskbarLayout;

    /// <summary>
    /// The song card and the icons move while they animate: the strip's gap and the spans it keeps clear follow
    /// them in the same frame instead of waiting for the next visualizer poll.
    /// </summary>
    private void FollowTaskbarLayout()
    {
        if (!TaskbarOn || _visualizerWindow is not { Showing: true }) return;
        static double Q(double v) => Math.Round(v * 2) / 2;
        var layout = (Q(_taskbarIconsLeft), Q(_taskbarIconsRight), Q(_taskbarMediaLeft), Q(_taskbarRightStart), _mediaReveal > 0.5);
        if (layout == _placedTaskbarLayout) return;
        _placedTaskbarLayout = layout;
        PlaceTaskbarVisualizer();
    }

    /// <summary>On the taskbar the light lies over the bar: the dock goes just under the visualizer's top window.</summary>
    private void PutDockUnderVisualizer()
    {
        if (_visualizerWindow is { Showing: true } visualizer && visualizer.TopHandle != IntPtr.Zero)
            WindowStyling.PlaceBelow(_hwnd, visualizer.TopHandle);
    }
}
