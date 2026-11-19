using Floe.Core.Interop;
using Floe.Core.Models;

namespace Floe.Core.Services;

/// <summary>
/// What the dock, its visualizer and its slide-in trigger stand on, in physical pixels. One answer for all of them,
/// so the effect never sits on the screen's physical bottom while the dock stands above a visible taskbar.
/// </summary>
public static class DockFloor
{
    /// <param name="monitor">The dock's monitor.</param>
    /// <param name="work">That monitor's work area.</param>
    /// <param name="appBar">The strip the dock's app bar holds; empty when it holds none (auto-hide, no reservation).</param>
    /// <param name="taskbarGone">Windows' taskbar is hidden (by the dock or anyone else).</param>
    public static ScreenInput.Rect Resolve(ScreenInput.Rect monitor, ScreenInput.Rect work, ScreenInput.Rect appBar, bool taskbarGone, DockEdge edge)
    {
        if (appBar.Right > appBar.Left && appBar.Bottom > appBar.Top) return appBar;
        if (taskbarGone || work.Right <= work.Left || work.Bottom <= work.Top) return monitor;

        // a taskbar that shows on the dock's own edge pushes the floor in to the work area there
        switch (edge)
        {
            case DockEdge.Bottom when work.Bottom > monitor.Top && work.Bottom < monitor.Bottom: monitor.Bottom = work.Bottom; break;
            case DockEdge.Top when work.Top < monitor.Bottom && work.Top > monitor.Top: monitor.Top = work.Top; break;
            case DockEdge.Left when work.Left < monitor.Right && work.Left > monitor.Left: monitor.Left = work.Left; break;
            case DockEdge.Right when work.Right > monitor.Left && work.Right < monitor.Right: monitor.Right = work.Right; break;
        }

        return monitor;
    }
}
