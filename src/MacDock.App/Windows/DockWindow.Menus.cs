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
        menu.Items.Add(MenuAction("MacDock'u kapat", () => Application.Current.Shutdown()));
        return menu;
    }

    private ContextMenu BuildItemMenu(DockItemViewModel item)
    {
        var menu = new ContextMenu();
        ApplyMenuStyle(menu);
        menu.Items.Add(MenuAction("Aç", () => AppLauncher.LaunchOrActivate(item.App)));
        menu.Items.Add(MenuAction(item.IsPinned ? "Dock'tan kaldır" : "Dock'a sabitle", () =>
        {
            if (item.IsPinned)
            {
                Unpin(item);
            }
            else
            {
                Pin(item);
            }
        }));
        menu.Items.Add(MenuAction("Dosya konumunu aç", () => OpenLocation(item.App)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Pencereyi kapat", () => CloseApp(item)));

        var target = AppLauncher.ResolveTarget(item.App);
        var verbs = ShellVerbs.Get(target);
        if (verbs.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var verb in verbs)
            {
                menu.Items.Add(MenuAction(verb.Name, verb.Invoke));
            }
        }

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
            _radial.Dismissed += () => _menuOpen = false;
        }

        _menuOpen = true;

        var radius = RadialMenuWindow.Radius;
        var hit = RadialMenuWindow.HitRadius + 16;
        var centerX = Math.Clamp(ToScreenX(slot.Center), hit, SystemParameters.PrimaryScreenWidth - hit);
        var centerY = Math.Clamp(Top + IconBottom - (IconBox / 2), hit, SystemParameters.PrimaryScreenHeight - hit);
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
            OpenSettings();
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

            if (previousForeground != IntPtr.Zero)
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

        if (slot is not null)
        {
            menu.Placement = PlacementMode.Top;
            menu.PlacementTarget = slot.Button;
            menu.VerticalOffset = -8;
        }
        else
        {
            menu.Placement = PlacementMode.MousePoint;
        }

        menu.IsOpen = true;

        if (slot is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (menu.IsOpen)
                {
                    menu.HorizontalOffset = -(menu.ActualWidth - slot.Button.ActualWidth) / 2;
                }
            });
        }

        e.Handled = true;
    }

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
        window.Left = Math.Max(8, Math.Min(
            ToScreenX(Canvas.GetLeft(_clock) + (ClockWidth / 2)) - (window.ActualWidth / 2),
            SystemParameters.PrimaryScreenWidth - window.ActualWidth - 8));
        window.Top = Math.Max(8, Top + RestTop - window.ActualHeight - 12);
        window.Activate();
    }

}
