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
        menu.Items.Add(MenuAction(Loc.T("Masaüstünü göster"), DesktopController.ShowDesktop));
        menu.Items.Add(MenuAction(Loc.T("Perde modu (Ctrl+Alt+V)"), ToggleVeil));
        menu.Items.Add(MenuAction(Loc.T("Görev Yöneticisi"), () => StartSafe("taskmgr.exe")));
        menu.Items.Add(MenuAction(Loc.T(AutoStart.IsEnabled() ? "Açılışta başlatmayı kapat" : "Açılışta başlat"), AutoStart.Toggle));
        menu.Items.Add(new Separator());
        if (Product.Cp77) menu.Items.Add(MenuAction(Loc.T("Stüdyo"), () => OpenSettings(studio: true)));
        menu.Items.Add(MenuAction(Loc.T(Product.QuitLabel), () => Application.Current.Shutdown()));
        return menu;
    }

    private void OpenIconMenu(IconSlot slot)
    {
        var item = slot.Item;
        var actions = new List<(string Label, Action Run)>
        {
            (Loc.T("Aç"), () => AppLauncher.LaunchOrActivate(item.App)),
            (Loc.T(item.IsPinned ? "Kaldır" : "Sabitle"), () =>
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
            (Loc.T("Dosya konumunu aç"), () => OpenLocation(item.App)),
            (Loc.T("İkonu değiştir"), () => OpenIconPicker(item)),
            (Loc.T("Kapat"), () => CloseApp(item)),
        };

        HidePreview();
        _tooltipTimer.Stop();
        _tooltip.IsOpen = false;

        if (_iconMenu is null)
        {
            _iconMenu = new IconMenuWindow();
            _iconMenu.Dismissed += () =>
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

        var work = SettingsWindow.GetWorkArea(this);
        var anchor = DockAnchor(slot.Center);
        if (DockSettings.Edge == DockEdge.Bottom) anchor.Y = Top + IconBottom - (IconBox * HoverMaxScale) + 2;
        _iconMenu.ShowFor(item.Name, item.Icon, actions, anchor, DockSettings.Edge, work);
    }

    private void OpenIconPicker(DockItemViewModel item) => new IconPickerWindow(item, () =>
    {
        item.ReloadIcon();
        RebuildIcons();
    }).Show();

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

        // a press can still land on the empty room above the bar (a touch, or before the click-through timer
        // caught up): that belongs to the window behind, so no dock menu there
        if (!IsSolidAt(e.GetPosition(this)))
        {
            e.Handled = true;
            if (DwmDock) UpdateClickThrough();
            return;
        }

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
            OpenIconMenu(slot);
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

    private void OpenSettings(bool studio = false)
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        var window = new SettingsWindow();
        if (studio) window.ShowStudio();
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
        PlacePopover(window, DockAnchor(Canvas.GetLeft(_clock) + (ClockWidth / 2)), work, 12);
        window.Activate();
    }

}
