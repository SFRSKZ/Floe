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

    private void LoadPinned()
    {
        foreach (var app in PinnedAppsProvider.Load())
        {
            var vm = new DockItemViewModel(app);
            _pinnedList.Add(vm);
            _pinned[vm.ProcessName] = vm;
            Items.Add(vm);
        }

        RebuildIcons();
    }

    private void RefreshRunning()
    {
        var self = Process.GetCurrentProcess().Id;
        var windows = AppWindows.Enumerate(self);

        var remaining = windows.Where(window => !IsHelperProcess(window.ProcessName)).ToList();

        foreach (var pinned in _pinnedList)
        {
            var matched = remaining.Where(window => AppMatcher.WindowMatchesItem(pinned.Name, pinned.ProcessName, pinned.App.Target, window)).ToList();
            if (matched.Count > 0)
            {
                pinned.IsRunning = true;
                pinned.Windows = matched.Select(window => window.Hwnd).ToList();
                var consumed = matched.Select(window => window.Hwnd).ToHashSet();
                remaining.RemoveAll(window => consumed.Contains(window.Hwnd));
            }
            else
            {
                pinned.IsRunning = false;
                pinned.Windows = Array.Empty<IntPtr>();
            }
        }

        var grouped = new Dictionary<string, (string Path, List<IntPtr> Windows)>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in remaining)
        {
            if (!grouped.TryGetValue(window.ProcessName, out var entry))
            {
                entry = (window.ProcessPath, new List<IntPtr>());
                grouped[window.ProcessName] = entry;
            }

            entry.Windows.Add(window.Hwnd);
        }

        var unpinned = new List<DockItemViewModel>();
        foreach (var name in grouped.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var entry = grouped[name];
            var vm = new DockItemViewModel(
                new DockApp { Name = name, Target = entry.Path },
                isPinned: false,
                window: entry.Windows.Count > 0 ? entry.Windows[0] : IntPtr.Zero);

            vm.Windows = entry.Windows;
            vm.IsRunning = true;

            if (vm.Icon is not null)
            {
                unpinned.Add(vm);
            }
        }

        var desired = _pinnedList.Select(vm => "P:" + vm.ProcessName)
            .Concat(unpinned.Select(vm => "R:" + vm.ProcessName))
            .ToList();

        if (!desired.SequenceEqual(_itemKeys, StringComparer.OrdinalIgnoreCase))
        {
            _itemKeys = desired;
            Items.Clear();
            foreach (var vm in _pinnedList)
            {
                Items.Add(vm);
            }

            foreach (var vm in unpinned)
            {
                Items.Add(vm);
            }

            RebuildIcons();
        }
        else
        {
            // Same app set: keep the existing slots (and their icons) but refresh the window
            // handles, otherwise new or recreated windows are never picked up.
            var fresh = unpinned.ToDictionary(vm => vm.ProcessName, StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items)
            {
                if (!item.IsPinned && fresh.TryGetValue(item.ProcessName, out var current))
                {
                    item.Windows = current.Windows;
                    item.IsRunning = true;
                }
            }
        }

        UpdateDots();
    }

    private void UpdateDots()
    {
        foreach (var slot in _slots)
        {
            slot.Dot.Visibility = slot.Item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static bool IsHelperProcess(string name)
    {
        var value = name.ToLowerInvariant();
        return value is "floe" or "conhost" or "dwm" or "taskhostw"
            || value.Contains("helper")
            || value.Contains("crashpad")
            || value.Contains("runtimebroker")
            || value.Contains("textinputhost")
            || value.Contains("searchhost")
            || value.Contains("startmenuexperiencehost")
            || value.Contains("shellexperiencehost")
            || value.Contains("widgetservice")
            || value.Contains("webview");
    }

    private void ScheduleRunningRefresh()
    {
        _runningDebounce.Stop();
        _runningDebounce.Start();
    }

}
