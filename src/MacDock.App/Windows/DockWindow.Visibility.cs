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
using MacDock.App.Services;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

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
        inside |= CursorOverDesktopButton(cursorX, cursorY);
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
        _outsideTicks = 0;
        AnimateTop(ShownTop);
        PositionDesktopButton();
    }

    private void HideDock()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        AnimateTop(HiddenTop);
        PositionDesktopButton();
    }

    private void AnimateTop(double target)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        BeginAnimation(TopProperty, animation);
    }

    private double ShownTop => (MonitorBottomPx / DockScale) - Height - BottomMargin;

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

        // include the desktop button (separate window) to the right
        right += 100 * pxPerDip;

        var (x, y) = ScreenInput.GetCursorPosition();
        return x >= left - 6 && x <= right + 6 && y >= rect.Top - 10 && y <= rect.Bottom + 6;
    }

    private bool CursorOverDesktopButton(int cursorX, int cursorY)
    {
        if (_desktopButton is not { IsVisible: true } button)
        {
            return false;
        }

        var buttonHwnd = new WindowInteropHelper(button).Handle;
        if (buttonHwnd == IntPtr.Zero || !ScreenInput.TryGetWindowRect(buttonHwnd, out var rect))
        {
            return false;
        }

        return cursorX >= rect.Left - 12 && cursorX <= rect.Right + 12
            && cursorY >= rect.Top - 12 && cursorY <= rect.Bottom + 12;
    }

    private void Reposition()
    {
        UpdateMonitor();

        var scale = DockScale;
        var area = _monitorInfo.Monitor;
        var monitorLeft = area.Left / scale;
        var monitorWidth = (area.Right - area.Left) / scale;
        var width = ActualWidth > 0 ? ActualWidth : Width;

        BeginAnimation(TopProperty, null);
        Left = monitorLeft + ((monitorWidth - width) / 2);
        Top = _shown ? ShownTop : HiddenTop;

        EnsureAppBar();
        PositionWidgets();
        PositionDesktopButton();
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
        var height = (int)Math.Round((RestBarHeight + BottomMargin) * DockScale);
        _appBar.SetPosition(_monitorInfo.Monitor, height);
    }


    private void PositionDesktopButton()
    {
        _desktopButton ??= CreateDesktopButton();

        // use the rest capsule width so it does not have to follow hover magnification
        var right = ToScreenX(_restCapsuleLeft + _restCapsuleWidth) + 16;
        var centerY = Top + RestTop + (RestBarHeight / 2);
        var top = centerY - (_desktopButton.Height / 2);

        if (double.IsNaN(_desktopButtonX)
            || double.IsNaN(_desktopButtonY)
            || Math.Abs(_desktopButtonX - right) > 0.5
            || Math.Abs(_desktopButtonY - top) > 0.5)
        {
            _desktopButton.Left = right;
            _desktopButton.Top = top;
            _desktopButtonX = right;
            _desktopButtonY = top;
        }

        if (_shown && !_fullscreenHidden && IsVisible)
        {
            if (!_desktopButton.IsVisible)
            {
                _desktopButton.Show();
            }
        }
        else if (_desktopButton.IsVisible)
        {
            _desktopButton.Hide();
        }
    }

    private DesktopButtonWindow CreateDesktopButton()
    {
        var button = new DesktopButtonWindow();
        button.Clicked += DesktopController.ShowDesktop;
        button.Show();
        button.Hide();
        return button;
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

        PositionDesktopButton();

        if (fullscreen)
        {
            _widgetsHiddenForFullscreen = _weatherWidget?.IsVisible ?? false;
            _weatherWidget?.Hide();
            _mediaWidget?.Hide();
            Hide();
        }
        else
        {
            Show();
            Reposition();
            if (_widgetsHiddenForFullscreen)
            {
                _weatherWidget?.Show();
                _mediaWidget?.Show();
                ArrangeWidgets();
            }
        }
    }

}
