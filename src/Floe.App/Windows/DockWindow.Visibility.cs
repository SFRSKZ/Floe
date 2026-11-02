using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        if (!AutoHide || _menuOpen || _settingsOpen || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            return;
        }

        var (cursorX, cursorY) = ScreenInput.GetCursorPosition();

        if (!_shown)
        {
            if (CursorAtDockEdge(cursorX, cursorY, AppBarBounds))
            {
                ShowDock();
            }

            return;
        }

        var bar = DockHoverBounds(rect);
        var inside = cursorX >= bar.Left - 12 && cursorX <= bar.Right + 12
            && cursorY >= bar.Top - 12 && cursorY <= bar.Bottom + 12;
        if (inside)
        {
            _outsideTicks = 0;
            return;
        }

        _outsideTicks++;
        if (_outsideTicks >= 5)
        {
            HideDock();
        }
    }

    private void ShowDock()
    {
        if (_shown)
        {
            return;
        }

        _shown = true;
        UpdateEffectVisibility();
        _visualizerWindow?.PlaceBelowDock(_hwnd);
        _tronLayer?.PlaceBelowDock(_hwnd);
        _outsideTicks = 0;
        AnimateDockToEdge(shown: true);
    }

    private void HideDock()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        Log("autohide slid the dock away");
        UpdateEffectVisibility();
        AnimateDockToEdge(shown: false);
    }

    private void AnimateDockToEdge(bool shown)
    {
        var horizontal = DockSettings.Edge is DockEdge.Bottom or DockEdge.Top;
        var animation = new System.Windows.Media.Animation.DoubleAnimation(EdgeOffset(shown), TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        BeginAnimation(horizontal ? TopProperty : LeftProperty, animation);
    }

    private double DockScale
    {
        get
        {
            var scale = ScreenInput.GetDpiScale(_hwnd);
            return scale > 0 ? scale : 1.0;
        }
    }

    private bool CursorOverDock()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var (x, y) = ScreenInput.GetCursorPosition();
        var bar = DockHoverBounds(rect);
        return x >= bar.Left - 6 && x <= bar.Right + 6 && y >= bar.Top - 10 && y <= bar.Bottom + 6;
    }

    private void Reposition()
    {
        UpdateMonitor();

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        ApplyDockOrientation();
        EnsureAppBar();
        UpdateLayout();
        PositionWindowAtEdge(_shown);
        if (BenchTop is { } benchTop) Top = benchTop;
        if (BenchLeft is { } benchLeft) Left = benchLeft;
    }

    private void CenterOnMonitor()
    {
        if (_monitorInfo.Monitor.Right <= _monitorInfo.Monitor.Left) return;
        ApplyDockOrientation();
        UpdateLayout();
        PositionWindowAtEdge(_shown);
    }

    private void ApplyDockOrientation()
    {
        var edge = DockSettings.Edge;

        // this runs on every relayout, and a new transform would measure the whole dock again
        if (_orientedEdge != edge)
        {
            _orientedEdge = edge;
            Root.LayoutTransform = edge switch
            {
                DockEdge.Left => new RotateTransform(90),
                DockEdge.Right => new RotateTransform(270),
                _ => Transform.Identity,
            };
            Root.RenderTransformOrigin = edge switch
            {
                DockEdge.Top => new Point(0.5, RestTop / WindowHeight),
                DockEdge.Left => new Point(0, 0.5),
                DockEdge.Right => new Point(1, 0.5),
                _ => new Point(0.5, 1.0),
            };
        }

        Root.Width = _windowWidth;
        Root.Height = WindowHeight;
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        Width = vertical ? WindowHeight : _windowWidth;
        Height = vertical ? _windowWidth : WindowHeight;
    }

    private void PositionWindowAtEdge(bool shown)
    {
        var monitor = _monitorInfo.Monitor;
        if (monitor.Right <= monitor.Left || monitor.Bottom <= monitor.Top) return;

        var edge = DockSettings.Edge;
        var scale = DockScale;
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        if (vertical)
        {
            Top = (monitor.Top / scale) + (((monitor.Bottom - monitor.Top) / scale - Height) / 2);
            Left = EdgeOffset(shown);
        }
        else
        {
            Left = (monitor.Left / scale) + (((monitor.Right - monitor.Left) / scale - Width) / 2);
            Top = EdgeOffset(shown);
        }
    }

    // Where the window stands across its edge. The bottom edge keeps the dock's own measures, so the free room under
    // the bar stays and the scale of a compact dock does not move it; the other edges keep a margin off the screen.
    private double EdgeOffset(bool shown)
    {
        var anchor = AppBarBounds;
        var bar = CapsuleWindowBounds();
        var scale = DockScale;
        var edge = DockSettings.Edge;
        if (edge == DockEdge.Bottom)
        {
            var bottom = anchor.Bottom / scale;
            return shown ? bottom - Height - BottomMargin + Sink : bottom - TriggerShow - RestTop;
        }

        return (edge, shown) switch
        {
            (DockEdge.Top, true) => (anchor.Top / scale) - bar.Top + BottomMargin,
            (DockEdge.Top, false) => (anchor.Top / scale) + TriggerShow - bar.Bottom,
            (DockEdge.Left, true) => (anchor.Left / scale) - bar.Left + BottomMargin,
            (DockEdge.Left, false) => (anchor.Left / scale) + TriggerShow - bar.Right,
            (DockEdge.Right, true) => (anchor.Right / scale) - bar.Right - BottomMargin,
            _ => (anchor.Right / scale) - TriggerShow - bar.Left,
        };
    }

    // The capsule along the edge and the whole window across it: icons bulge out of the capsule on hover.
    private ScreenInput.Rect DockHoverBounds(ScreenInput.Rect window)
    {
        var bar = CapsuleScreenBounds();
        return DockSettings.Edge is DockEdge.Left or DockEdge.Right
            ? new ScreenInput.Rect { Left = window.Left, Right = window.Right, Top = bar.Top, Bottom = bar.Bottom }
            : new ScreenInput.Rect { Left = bar.Left, Right = bar.Right, Top = window.Top, Bottom = window.Bottom };
    }

    private bool CursorAtDockEdge(int cursorX, int cursorY, ScreenInput.Rect monitor)
    {
        var half = (int)Math.Ceiling((_capsuleWidth * DockScale) / 2) + 8;
        var centerX = monitor.Left + ((monitor.Right - monitor.Left) / 2);
        var centerY = monitor.Top + ((monitor.Bottom - monitor.Top) / 2);
        return DockSettings.Edge switch
        {
            DockEdge.Top => cursorY <= monitor.Top + TriggerPx && Math.Abs(cursorX - centerX) <= half,
            DockEdge.Left => cursorX <= monitor.Left + TriggerPx && Math.Abs(cursorY - centerY) <= half,
            DockEdge.Right => cursorX >= monitor.Right - TriggerPx && Math.Abs(cursorY - centerY) <= half,
            _ => cursorY >= monitor.Bottom - TriggerPx && Math.Abs(cursorX - centerX) <= half,
        };
    }

    private Rect CapsuleWindowBounds()
    {
        var geometry = _glass.Data;
        if (geometry is null || geometry.Bounds.IsEmpty)
        {
            return new Rect(_capsuleLeft, RestTop, _capsuleWidth, RestBarHeight);
        }

        try
        {
            return _glass.TransformToAncestor(this).TransformBounds(geometry.Bounds);
        }
        catch (InvalidOperationException)
        {
            return new Rect(_capsuleLeft, RestTop, _capsuleWidth, RestBarHeight);
        }
    }

    private ScreenInput.Rect CapsuleScreenBounds()
    {
        try
        {
            var bounds = CapsuleWindowBounds();
            var topLeft = PointToScreen(bounds.TopLeft);
            var bottomRight = PointToScreen(bounds.BottomRight);
            return new ScreenInput.Rect
            {
                Left = (int)Math.Floor(topLeft.X),
                Top = (int)Math.Floor(topLeft.Y),
                Right = (int)Math.Ceiling(bottomRight.X),
                Bottom = (int)Math.Ceiling(bottomRight.Y),
            };
        }
        catch (InvalidOperationException)
        {
            return _monitorInfo.Monitor;
        }
    }

    private ScreenInput.Rect AppBarBounds
    {
        get
        {
            var applied = _appBar?.AppliedRect ?? default;
            return applied.Right > applied.Left && applied.Bottom > applied.Top ? applied : _monitorInfo.Monitor;
        }
    }

    /// <summary>The benchmark parks its dock here (DIP), off every screen, so it never covers the real one.</summary>
    internal static double? BenchLeft { get; set; }

    internal static double? BenchTop { get; set; }

    /// <summary>
    /// The fast dock, now the only one: a plain window DWM blends by its alpha instead of a layered one, so frames
    /// stay on the GPU; clicks off the glass pass through (DockWindow.ClickThrough.cs). FLOE_DWM_DOCK=0 brings back
    /// the old layered window. Read once, when the dock is built: a window cannot switch between the two.
    /// </summary>
    internal static bool DwmDock { get; private set; }

    private static bool WantsDwmDock() => Environment.GetEnvironmentVariable("FLOE_DWM_DOCK") != "0";

    private void UpdateMonitor()
    {
        _monitor = ScreenInput.GetMonitorForWindow(_hwnd);
        if (ScreenInput.TryGetMonitorInfo(_monitor, out var info))
        {
            _monitorInfo = info;
            return;
        }

        _monitorInfo = new ScreenInput.MonitorInfo
        {
            Monitor = new ScreenInput.Rect
            {
                Left = 0,
                Top = 0,
                Right = ScreenInput.PrimaryScreenWidth,
                Bottom = ScreenInput.PrimaryScreenHeight,
            },
        };
    }

    private void EnsureAppBar()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (ReserveScreenSpace)
        {
            _appBar ??= new AppBar();
            ApplyAppBarPosition();
        }
    }

    private void ApplyAppBarPosition()
    {
        if (_appBar is null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!_appBar.IsRegistered && !_appBar.Register(_hwnd))
        {
            return;
        }

        if (_monitorInfo.Monitor.Bottom <= _monitorInfo.Monitor.Top)
        {
            UpdateMonitor();
        }

        // Keep the reservation while a fullscreen app runs: fullscreen windows size to the
        // monitor rect anyway, and dropping it would re-layout every maximized window.
        // reserve up to the capsule's top as it rests: full size when the dock does not shrink, so maximized
        // windows end above it instead of under it
        var restTop = ((ShadowRoom + RestBarHeight) * CompactScale) + BottomMargin - Sink + 4;
        var height = (int)Math.Round(Math.Max((RestBarHeight + BottomMargin) * FullScale, restTop) * DockScale);
        _appBar.SetPosition(_monitorInfo.Monitor, height, DockSettings.Edge, DockSettings.HideWindowsTaskbar || TaskbarController.IsHidden());
        if (IsLoaded)
        {
            UpdateLayout();
            PositionWindowAtEdge(_shown);
        }
    }

    private void OnDockSettingsChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnDockSettingsChanged));
            return;
        }

        // turning the full-screen hiding off brings a hidden dock back at once
        UpdateFullscreenState();

        // a new size: the dock grows or shrinks to it, and the visualizer strip follows
        _targetDockScale = IsMouseOver || _menuOpen ? FullScale : CompactScale;
        EnsureAnimation();
        ApplyVisualizerState();

        // the taskbar choice moves the reserved strip too, without a new edge
        if (_lastDockEdge == DockSettings.Edge)
        {
            ApplyAppBarPosition();
            return;
        }

        _lastDockEdge = DockSettings.Edge;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        ApplyDockOrientation();
        if (_appBar is not null && _hwnd != IntPtr.Zero)
        {
            _appBar.Remove();
            _appBar.Register(_hwnd);
        }
        UpdateShape();
        Reposition();
    }


    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Window_PreviewMouseMove(this, e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Window_MouseLeave(this, e);
    }

    private void UpdateFullscreenState()
    {
        if (!IsLoaded)
        {
            return;
        }

        // ABN_FULLSCREENAPP is desktop-wide, so only honour it when the foreground app is on our monitor
        var fullscreen = DockSettings.HideOnFullscreen
            && (ScreenInput.IsForegroundFullscreen(_monitor)
                || (_appbarFullscreen && ScreenInput.GetForegroundMonitor() == _monitor));
        if (!ScreenInput.IsForegroundFullscreen())
        {
            _appbarFullscreen = false;
        }

        if (fullscreen == _fullscreenHidden)
        {
            return;
        }

        _fullscreenHidden = fullscreen;
        var foreground = ScreenInput.GetForegroundInfo();
        Log($"fullscreen hide={fullscreen} appbar={_appbarFullscreen} fg='{foreground.ProcessName}' title='{foreground.Title}'");

        if (fullscreen)
        {
            Hide();
        }
        else
        {
            Show();
            Reposition();
        }
    }

}
