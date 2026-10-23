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

    private void ApplyMenuStyle(ContextMenu menu)
    {
        menu.Style = (Style)FindResource("GlassMenu");
        menu.Resources[typeof(MenuItem)] = (Style)FindResource("GlassMenuItem");
    }

    private ContextMenu BuildBackgroundMenu()
    {
        var menu = new ContextMenu();
        ApplyMenuStyle(menu);
        menu.Items.Add(MenuAction("Masaüstünü göster", DesktopController.ShowDesktop));
        menu.Items.Add(MenuAction("Perde modu (Ctrl+Alt+V)", ToggleVeil));
        menu.Items.Add(MenuAction("Görev Yöneticisi", () => StartSafe("taskmgr.exe")));
        menu.Items.Add(MenuAction(AutoStart.IsEnabled() ? "Açılışta başlatmayı kapat" : "Açılışta başlat", AutoStart.Toggle));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Floe'yu kapat", () => Application.Current.Shutdown()));
        return menu;
    }

    private void OpenRadialMenu(IconSlot slot)
    {
        var item = slot.Item;
        var actions = new List<(string Label, Action Run)>
        {
            ("Aç", () => AppLauncher.LaunchOrActivate(item.App)),
            (item.IsPinned ? "Kaldır" : "Sabitle", () =>
            {
                if (item.IsPinned)
                {
                    Unpin(item);
                }
                else
                {
                    Pin(item);
                }
            }),
            ("Konum", () => OpenLocation(item.App)),
            ("Kapat", () => CloseApp(item)),
        };

        HidePreview();
        _tooltipTimer.Stop();
        _tooltip.IsOpen = false;

        if (_radial is null)
        {
            _radial = new RadialMenuWindow();
            _radial.Dismissed += () =>
            {
                _menuOpen = false;

                if (!CursorOverDock())
                {
                    _targetBulge = 0;
                    _targetDockScale = CompactScale;
                }

                EnsureAnimation();
            };
        }

        _menuOpen = true;

        var radius = RadialMenuWindow.Radius;
        var hit = RadialMenuWindow.HitRadius + 16;
        var work = SettingsWindow.GetWorkArea(this);
        var centerX = Math.Clamp(ToScreenX(slot.Center), work.Left + hit, Math.Max(work.Left + hit, work.Right - hit));
        var centerY = Math.Clamp(Top + IconBottom - (IconBox / 2), work.Top + hit, Math.Max(work.Top + hit, work.Bottom - hit));
        _radial.Left = centerX - radius;
        _radial.Top = centerY - radius;
        _radial.ShowFor(item.Icon, actions);
    }

    private static MenuItem MenuAction(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (_clock.IsMouseOver || _clockDivider.IsMouseOver)
        {
            if (e.Delta > 0)
            {
                DesktopController.VolumeUp();
            }
            else
            {
                DesktopController.VolumeDown();
            }

            e.Handled = true;
            return;
        }

        var slot = _slots.FirstOrDefault(s => s.Button.IsMouseOver);
        if (slot is { Item.Windows.Count: > 0 })
        {
            var hwnd = e.Delta < 0 ? slot.Item.PreviousWindow() : slot.Item.NextWindow();

            WindowActivator.Activate(hwnd);
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);

        if (_clock.IsMouseOver || _clockDivider.IsMouseOver)
        {
            if (!_settingsClosedByClick) OpenSettings();
            _settingsClosedByClick = false;
            e.Handled = true;
            return;
        }

        var slot = _slots.FirstOrDefault(s => s.Button.IsMouseOver);
        if (slot is not null)
        {
            OpenRadialMenu(slot);
            e.Handled = true;
            return;
        }

        _menuOpen = true;
        _targetBulge = MaxBulge;
        _targetDockScale = 1;
        EnsureAnimation();

        var menu = BuildBackgroundMenu();
        var previousForeground = ScreenInput.GetForegroundHandle();
        WindowStyling.SetNoActivate(_hwnd, false);
        Activate();

        menu.Closed += (_, _) =>
        {
            WindowStyling.SetNoActivate(_hwnd, true);
            _menuOpen = false;

            // only hand focus back when we still own it; if the user clicked another
            // app while the menu was open, that app must keep the foreground
            var foreground = ScreenInput.GetForegroundHandle();
            using var currentProcess = Process.GetCurrentProcess();
            var ownsActivation = foreground == _hwnd
                || (foreground != IntPtr.Zero
                    && string.Equals(
                        ScreenInput.GetWindowInfo(foreground).ProcessName,
                        currentProcess.ProcessName,
                        StringComparison.OrdinalIgnoreCase));

            if (previousForeground != IntPtr.Zero && ownsActivation)
            {
                WindowActivator.Activate(previousForeground);
            }

            if (!CursorOverDock())
            {
                _targetBulge = 0;
                _targetDockScale = CompactScale;
            }

            EnsureAnimation();
        };

        menu.Placement = PlacementMode.MousePoint;

        menu.IsOpen = true;

        e.Handled = true;
    }

    // The dock never takes focus, so a click on it would not deactivate the settings; close them here too.
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        _settingsClosedByClick = _settings is not null;
        _settings?.Close();
    }

    // the right click on the clock that closed the settings must not open them again on release
    private bool _settingsClosedByClick;

    private void OpenSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        var window = new SettingsWindow();
        _settings = window;
        _settingsOpen = true;

        window.Closed += (_, _) =>
        {
            _settingsOpen = false;
            _settings = null;

            if (!CursorOverDock())
            {
                _targetBulge = 0;
                _targetDockScale = CompactScale;
            }

            EnsureAnimation();
        };

        window.Show();
        window.UpdateLayout();
        var work = SettingsWindow.GetWorkArea(this);
        window.Left = Math.Clamp(
            ToScreenX(Canvas.GetLeft(_clock) + (ClockWidth / 2)) - (window.ActualWidth / 2),
            work.Left + 8, Math.Max(work.Left + 8, work.Right - window.ActualWidth - 8));
        window.Top = Math.Clamp(Top + RestTop - window.ActualHeight - 12,
            work.Top + 8, Math.Max(work.Top + 8, work.Bottom - window.ActualHeight - 8));
        window.Activate();
    }

}
