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

    private void UpdateForeground()
    {
        var info = ScreenInput.GetForegroundInfo();
        if (IsIgnoredForeground(info.ProcessName) || string.Equals(info.ProcessName, _activeProcess, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingForeground = info.ProcessName;
        _pendingTitle = info.Title;
        _pendingPath = info.Path;
        _foregroundDebounce.Stop();
        _foregroundDebounce.Start();
    }

    private void ApplyForeground()
    {
        _foregroundDebounce.Stop();
        if (string.IsNullOrEmpty(_pendingForeground)
            || string.Equals(_pendingForeground, _activeProcess, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeProcess = _pendingForeground;
        _activeTitle = _pendingTitle;
        _activePath = _pendingPath;

        if (_attentionItem is not null
            && AppMatcher.Matches(_attentionItem.Name, _attentionItem.ProcessName, _attentionItem.App.Target, _activeProcess, _activeTitle, _activePath))
        {
            _attentionItem = null;
        }

        UpdateAura();

        // a just-launched app may not be marked running yet - refresh immediately so the effect starts now
        if (_activeIndex < 0)
        {
            RefreshRunning();
        }

        Log($"fg='{_activeProcess}' title='{_activeTitle}' path='{_activePath}' auraIndex={_activeIndex} visible={_auraGrid.Visibility}");
    }

    private void OnSystemAlert(IntPtr hwnd)
    {
        var info = ScreenInput.GetWindowInfo(hwnd);
        if (string.IsNullOrEmpty(info.ProcessName))
        {
            return;
        }

        for (var i = 0; i < _slots.Count; i++)
        {
            var item = _slots[i].Item;
            if (item.IsRunning
                && AppMatcher.Matches(item.Name, item.ProcessName, item.App.Target, info.ProcessName, info.Title, info.Path))
            {
                if (!ReferenceEquals(_attentionItem, item))
                {
                    _attentionItem = item;
                    Log($"alert {item.Name}");
                    UpdateAura();
                }

                return;
            }
        }
    }

    private void UpdateAmbient()
    {
        var cpu = SystemMetrics.GetCpuUsage();
        _ambientLevel += (cpu - _ambientLevel) * 0.35;
        var opacity = 0.5 + (0.5 * (_ambientLevel / 100.0));

        // sub-2% moves are invisible on a 1 px line and would redraw it every tick
        if (Math.Abs(opacity - _separator.Opacity) > 0.02)
        {
            _separator.Opacity = opacity;
        }
    }

    private static bool IsIgnoredForeground(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        return name.Equals(Product.Exe, StringComparison.OrdinalIgnoreCase)
            || name.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase);
    }

}
