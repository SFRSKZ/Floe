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
        var screenHeight = ScreenInput.PrimaryScreenHeight;

        var physicalWidth = rect.Right - rect.Left;
        var pxPerDip = _windowWidth > 0 ? physicalWidth / _windowWidth : 1;
        var centerDip = _capsuleLeft + (_capsuleWidth / 2);
        var halfDip = (_capsuleWidth / 2) * _dockScale;
        var left = rect.Left + ((centerDip - halfDip) * pxPerDip);
        var right = rect.Left + ((centerDip + halfDip) * pxPerDip);

        if (!_shown)
        {
            var inTrigger = cursorY >= screenHeight - TriggerPx && cursorX >= left - 4 && cursorX <= right + 4;
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

    private double ShownTop => SystemParameters.PrimaryScreenHeight - Height - BottomMargin;

    private double HiddenTop => SystemParameters.PrimaryScreenHeight - TriggerShow - RestTop;

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

    private void Reposition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = _shown ? ShownTop : HiddenTop;
        PositionWidgets();
        PositionDesktopButton();
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
        var fullscreen = ScreenInput.IsForegroundFullscreen();
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
