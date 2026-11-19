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

    private void Pin(DockItemViewModel item)
    {
        // an app whose exe could not be read has nothing to launch it by
        if (item.IsPinned || string.IsNullOrWhiteSpace(item.App.Target))
        {
            return;
        }

        var pinned = new DockItemViewModel(item.App, isPinned: true);
        if (_pinned.ContainsKey(pinned.Key))
        {
            return;
        }

        _pinnedList.Add(pinned);
        _pinned[pinned.Key] = pinned;
        SavePinned();
        RefreshRunning();
    }

    private void Unpin(DockItemViewModel item)
    {
        // by its key: two packaged apps may share a process name, and only the one asked for goes
        var target = _pinnedList.FirstOrDefault(v => ReferenceEquals(v, item))
            ?? _pinnedList.FirstOrDefault(v => string.Equals(v.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _pinnedList.Remove(target);
        _pinned.Remove(target.Key);
        SavePinned();
        RefreshRunning();
    }

    private void SavePinned()
        => PinnedAppsProvider.Save(PinnedAppsProvider.DefaultConfigPath, _pinnedList.Select(v => v.App).ToList());

    private static void OpenLocation(DockApp app)
    {
        var target = AppLauncher.ResolveTarget(app);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", System.IO.File.Exists(target) ? $"/select,\"{target}\"" : $"\"{target}\"");
        }
        catch
        {
            // ignore
        }
    }

    private static void CloseApp(DockItemViewModel item)
    {
        // the item's own windows: another app with the same process name (two different app.exe) stays open
        if (item.Windows.Count > 0)
        {
            foreach (var hwnd in item.Windows)
            {
                AppWindows.RequestClose(hwnd);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(item.ProcessName))
        {
            return;
        }

        foreach (var process in Process.GetProcessesByName(item.ProcessName))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void StartSafe(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

}
