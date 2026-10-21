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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeNonActivatingToolWindow(_hwnd);
        DesktopController.RegisterHotKey(_hwnd, DesktopController.HotkeyIdVeil, DesktopController.ModControl | DesktopController.ModAlt, DesktopController.VkV);
        if (BlurBackdrop)
        {
            if (!NoFx.Contains("exclude")) WindowAffinity.ExcludeFromCapture(_hwnd);
        }

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        _taskbarCreatedMessage = TrayNotification.TaskbarCreatedMessage;
        DpiChanged += OnDpiChanged;

        // react to foreground changes instead of waiting for the 1 s fullscreen poll
        _fullscreenHook = new EventHooks.Hook(
            EventHooks.EventSystemForeground,
            EventHooks.EventSystemForeground,
            _ => UpdateFullscreenState());

        // hwnd exists now: join the shell's appbar list so the work area ends above the bar
        if (ReserveScreenSpace)
        {
            _appBar = new AppBar();
            _appBar.Register(_hwnd);
        }
    }

    private void OnDpiChanged(object? sender, DpiChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(Reposition), DispatcherPriority.Render);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == DesktopController.HotkeyIdVeil)
        {
            ToggleVeil();
            handled = true;
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            // Explorer restarted: the tray icon and the appbar registration belong to the old shell
            RestoreShellIntegration();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == AppBar.CallbackMessage)
        {
            var notification = wParam.ToInt32();
            if (notification == AppBar.AbnPosChanged)
            {
                ApplyAppBarPosition();
            }
            else if (notification == AppBar.AbnFullscreenApp)
            {
                _appbarFullscreen = lParam != IntPtr.Zero;
                UpdateFullscreenState();
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (msg == 0x007E) // WM_DISPLAYCHANGE
        {
            Reposition();
        }
        else if (msg == 0x001A && wParam.ToInt64() == 0x002F) // WM_SETTINGCHANGE / SPI_SETWORKAREA
        {
            Reposition();
        }
        else if (msg == 0x02E0) // WM_DPICHANGED - WPF has not applied the new DPI transform yet
        {
            Dispatcher.BeginInvoke(new Action(Reposition), DispatcherPriority.Render);
        }

        if (msg == TrayNotification.CallbackMessage)
        {
            var evt = lParam.ToInt32() & 0xFFFF;
            if (evt == TrayNotification.WmLeftButtonUp)
            {
                ToggleDockVisible();
                handled = true;
            }
            else if (evt == TrayNotification.WmRightButtonUp)
            {
                ShowTrayMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void RestoreShellIntegration()
    {
        if (_trayIcon == IntPtr.Zero)
        {
            _trayIcon = TrayNotification.LoadAppIcon(Environment.ProcessPath ?? string.Empty);
        }

        TrayNotification.Add(_hwnd, "MacDock", _trayIcon);

        // ABM_REMOVE is a no-op against the fresh shell, but it clears our stale registration state
        if (_appBar is not null)
        {
            _appBar.Remove();
            _appBar.Register(_hwnd);
        }
        Reposition();
    }

    private void ToggleVeil()
    {
        _veil ??= new VeilWindow();
        _veil.Toggle();

        if (_veil.IsVisible)
        {
            WindowStyling.BringToTop(_hwnd);
        }
    }

    private void ToggleDockVisible()
    {
        if (IsVisible)
        {
            Hide();
            _weatherWidget?.Hide();
            _mediaWidget?.Hide();
        }
        else
        {
            Show();
            Reposition();
            _weatherWidget?.Show();
            _mediaWidget?.Show();
            ArrangeWidgets();
        }
    }

    private void ShowTrayMenu()
    {
        var (x, y) = ScreenInput.GetCursorPosition();
        var actions = new List<(string Label, Action Run)>
        {
            (IsVisible ? "Dock'u gizle" : "Dock'u göster", ToggleDockVisible),
            ("Masaüstünü göster", DesktopController.ShowDesktop),
            (_veil?.IsVisible == true ? "Perde modunu kapat" : "Perde modu (Ctrl+Alt+V)", ToggleVeil),
            (AutoStart.IsEnabled() ? "Açılışta başlatmayı kapat" : "Açılışta başlat", AutoStart.Toggle),
            ("-", () => { }),
            ("Çıkış", () => Application.Current.Shutdown()),
        };

        var index = TrayNotification.ShowMenu(_hwnd, x, y, actions.Select(action => action.Label).ToList());
        if (index >= 0 && index < actions.Count && actions[index].Label != "-")
        {
            actions[index].Run();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAnimation();
        StopWidgetRendering();
        AuraSettings.Changed -= OnAuraSettingsChanged;
        _settings?.Close();
        for (var k = 0; k < _auraEffects.Length; k++)
        {
            _auraEffects[k].BeginAnimation(DropShadowEffect.DirectionProperty, null);
        }

        for (var i = 0; i < _spectralWisps.Count; i++)
        {
            _spectralWisps[i].Spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        _spectralWaveSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        _spectralRim.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        _spectralLayer.BeginAnimation(OpacityProperty, null);

        DesktopController.UnregisterHotKey(_hwnd, DesktopController.HotkeyIdVeil);
        _veil?.HideVeil();
        _foregroundHook?.Dispose();
        _fullscreenHook?.Dispose();
        _windowHook?.Dispose();
        _alertHook?.Dispose();
        _ambientTimer.Stop();
        _backdropCapture?.Dispose();
        TrayNotification.Remove(_hwnd, "MacDock", _trayIcon);
        if (_trayIcon != IntPtr.Zero)
        {
            TrayNotification.DestroyAppIcon(_trayIcon);
        }

        _source?.RemoveHook(WndProc);
        DpiChanged -= OnDpiChanged;
        _appBar?.Dispose();
        _appBar = null;
        base.OnClosed(e);
    }

}
