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
        var monitorBottom = MonitorBottomPx;

        var physicalWidth = rect.Right - rect.Left;
        var pxPerDip = _windowWidth > 0 ? physicalWidth / _windowWidth : 1;
        var centerDip = _capsuleLeft + (_capsuleWidth / 2);
        var halfDip = (_capsuleWidth / 2) * _dockScale;
        var left = rect.Left + ((centerDip - halfDip) * pxPerDip);
        var right = rect.Left + ((centerDip + halfDip) * pxPerDip);

        if (!_shown)
        {
            var inTrigger = cursorY >= monitorBottom - TriggerPx && cursorX >= left - 4 && cursorX <= right + 4;
            if (inTrigger)
            {
                ShowDock();
            }

            return;
        }

        var inside = cursorX >= left - 12 && cursorX <= right + 12 && cursorY >= rect.Top - 12 && cursorY <= rect.Bottom + 12;
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
        _outsideTicks = 0;
        AnimateTop(ShownTop);
    }

    private void HideDock()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        UpdateEffectVisibility();
        AnimateTop(HiddenTop);
    }

    private void AnimateTop(double target)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        BeginAnimation(TopProperty, animation);
    }

    private double ShownTop => (MonitorBottomPx / DockScale) - Height - BottomMargin + Sink;

    private double HiddenTop => (MonitorBottomPx / DockScale) - TriggerShow - RestTop;

    private double DockScale
    {
        get
        {
            var scale = ScreenInput.GetDpiScale(_hwnd);
            return scale > 0 ? scale : 1.0;
        }
    }

    private int MonitorBottomPx
        => _monitorInfo.Monitor.Bottom > _monitorInfo.Monitor.Top
            ? _monitorInfo.Monitor.Bottom
            : ScreenInput.PrimaryScreenHeight;

    private bool CursorOverDock()
    {
        if (_hwnd == IntPtr.Zero || !ScreenInput.TryGetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var physicalWidth = rect.Right - rect.Left;
        var pxPerDip = _windowWidth > 0 ? physicalWidth / _windowWidth : 1;
        var centerDip = _capsuleLeft + (_capsuleWidth / 2);
        var halfDip = (_capsuleWidth / 2) * _dockScale;
        var left = rect.Left + ((centerDip - halfDip) * pxPerDip);
        var right = rect.Left + ((centerDip + halfDip) * pxPerDip);


        var (x, y) = ScreenInput.GetCursorPosition();
        return x >= left - 6 && x <= right + 6 && y >= rect.Top - 10 && y <= rect.Bottom + 6;
    }

    private void Reposition()
    {
        UpdateMonitor();

        BeginAnimation(TopProperty, null);
        CenterOnMonitor();
        Top = _shown ? ShownTop : HiddenTop;

        EnsureAppBar();
    }

    // The window is wider than the capsule and the capsule sits in its middle, so centring the window centres
    // the dock. Width, not ActualWidth: right after a relayout ActualWidth still holds the old size.
    private void CenterOnMonitor()
    {
        var scale = DockScale;
        var area = _monitorInfo.Monitor;
        if (area.Right <= area.Left) return; // no monitor read yet; Reposition centres once there is
        var width = double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width;
        Left = (area.Left / scale) + ((((area.Right - area.Left) / scale) - width) / 2);
    }

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
        var height = (int)Math.Round(Math.Max(RestBarHeight + BottomMargin, restTop) * DockScale);
        _appBar.SetPosition(_monitorInfo.Monitor, height);
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
        var fullscreen = ScreenInput.IsForegroundFullscreen(_monitor)
            || (_appbarFullscreen && ScreenInput.GetForegroundMonitor() == _monitor);
        if (!ScreenInput.IsForegroundFullscreen())
        {
            _appbarFullscreen = false;
        }

        if (fullscreen == _fullscreenHidden)
        {
            return;
        }

        _fullscreenHidden = fullscreen;


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
