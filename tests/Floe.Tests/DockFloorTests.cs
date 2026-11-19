using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.Tests;

public class DockFloorTests
{
    private static ScreenInput.Rect R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    // physical pixels of a 1920x1080 panel at 100/150/200 %: the taskbar is 48 DIP tall
    public static TheoryData<int, int, int> Monitors => new()
    {
        { 1920, 1080, 48 },
        { 2880, 1620, 72 },
        { 3840, 2160, 96 },
    };

    [Theory]
    [MemberData(nameof(Monitors))]
    public void VisibleTaskbarLiftsTheFloorAboveIt(int width, int height, int taskbar)
    {
        var floor = DockFloor.Resolve(R(0, 0, width, height), R(0, 0, width, height - taskbar), default, taskbarGone: false, DockEdge.Bottom);
        Assert.Equal(height - taskbar, floor.Bottom);
        Assert.Equal(0, floor.Left);
        Assert.Equal(width, floor.Right); // the effect keeps its full width
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void HiddenTaskbarLeavesTheWholeEdge(int width, int height, int taskbar)
    {
        // a hidden taskbar still holds its work area: the floor must ignore it
        var floor = DockFloor.Resolve(R(0, 0, width, height), R(0, 0, width, height - taskbar), default, taskbarGone: true, DockEdge.Bottom);
        Assert.Equal(height, floor.Bottom);
    }

    [Fact]
    public void AppBarStripWins()
    {
        var strip = R(0, 980, 1920, 1032);
        Assert.Equal(strip, DockFloor.Resolve(R(0, 0, 1920, 1080), R(0, 0, 1920, 980), strip, taskbarGone: false, DockEdge.Bottom));
    }

    [Fact]
    public void SecondMonitorKeepsItsOwnOrigin()
    {
        // a monitor to the left of the primary, at 150 %
        var floor = DockFloor.Resolve(R(-2880, 0, 0, 1620), R(-2880, 0, 0, 1548), default, taskbarGone: false, DockEdge.Bottom);
        Assert.Equal(-2880, floor.Left);
        Assert.Equal(0, floor.Right);
        Assert.Equal(1548, floor.Bottom);
    }

    [Fact]
    public void MonitorWithoutTaskbarUsesItsEdge()
    {
        var floor = DockFloor.Resolve(R(1920, 0, 3840, 1080), R(1920, 0, 3840, 1080), default, taskbarGone: false, DockEdge.Bottom);
        Assert.Equal(1080, floor.Bottom);
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void BottomTaskbarDoesNotMoveADockOnAnotherEdge(DockEdge edge)
    {
        var monitor = R(0, 0, 2880, 1620);
        var floor = DockFloor.Resolve(monitor, R(0, 0, 2880, 1548), default, taskbarGone: false, edge);
        Assert.Equal(monitor, floor);
    }

    [Theory]
    [InlineData(DockEdge.Top, 0, 72, 2880, 1620)]
    [InlineData(DockEdge.Left, 72, 0, 2880, 1620)]
    [InlineData(DockEdge.Right, 0, 0, 2808, 1620)]
    public void TaskbarOnTheDocksEdgeLiftsItsFloor(DockEdge edge, int l, int t, int r, int b)
    {
        // Windows' taskbar moved to the same edge as the dock, at 150 %
        var work = edge switch
        {
            DockEdge.Top => R(0, 72, 2880, 1620),
            DockEdge.Left => R(72, 0, 2880, 1620),
            _ => R(0, 0, 2808, 1620),
        };
        Assert.Equal(R(l, t, r, b), DockFloor.Resolve(R(0, 0, 2880, 1620), work, default, taskbarGone: false, edge));
        Assert.Equal(R(0, 0, 2880, 1620), DockFloor.Resolve(R(0, 0, 2880, 1620), work, default, taskbarGone: true, edge));
    }

    [Fact]
    public void SideDockOnASecondMonitorKeepsItsOrigin()
    {
        var floor = DockFloor.Resolve(R(-1920, -300, 0, 780), R(-1920, -300, 0, 732), default, taskbarGone: false, DockEdge.Right);
        Assert.Equal(R(-1920, -300, 0, 780), floor);
    }

    [Fact]
    public void BrokenWorkAreaFallsBackToTheMonitor()
    {
        var floor = DockFloor.Resolve(R(0, 0, 1920, 1080), default, default, taskbarGone: false, DockEdge.Bottom);
        Assert.Equal(1080, floor.Bottom);
    }
}
