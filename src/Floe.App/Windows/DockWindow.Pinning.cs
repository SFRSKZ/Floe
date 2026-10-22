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
        if (item.IsPinned)
        {
            return;
        }

        var pinned = new DockItemViewModel(item.App, isPinned: true);
        _pinnedList.Add(pinned);
        _pinned[pinned.ProcessName] = pinned;
        SavePinned();
        RefreshRunning();
    }

    private void Unpin(DockItemViewModel item)
    {
        var target = _pinnedList.FirstOrDefault(v =>
            string.Equals(v.ProcessName, item.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _pinnedList.Remove(target);
        _pinned.Remove(target.ProcessName);
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
