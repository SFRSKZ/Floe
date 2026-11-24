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
            _pinned[vm.Key] = vm;
            Items.Add(vm);
        }

        RebuildIcons();
    }

    private void RefreshRunning()
    {
        // this runs every few seconds for good: one pass over the windows per pinned app, without a Process
        // object or throwaway lists for the apps that are not running
        var windows = AppWindows.Enumerate(Environment.ProcessId);

        var remaining = windows.ToList();

        foreach (var pinned in _pinnedList)
        {
            List<IntPtr>? matched = null;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                if (!AppMatcher.Matches(pinned, remaining[i])) continue;
                (matched ??= new List<IntPtr>()).Add(remaining[i].Hwnd);
                remaining.RemoveAt(i);
            }

            if (matched is not null)
            {
                // walked backwards: put the windows back in the order they were listed
                matched.Reverse();
                pinned.IsRunning = true;
                pinned.Windows = matched;
            }
            else
            {
                pinned.IsRunning = false;
                pinned.Windows = Array.Empty<IntPtr>();
            }
        }

        // Windows' own shell windows are no app; a helper's windows (steamwebhelper) were taken by its pinned app
        // above, or are grouped under its app below - only the helpers named in AppIdentity, nothing by name pattern
        remaining.RemoveAll(window => AppIdentity.IsShellProcess(window.ProcessName) || IsOwnProduct(window.ProcessName));

        var grouped = new Dictionary<string, (string Name, string Path, string AppId, List<IntPtr> Windows)>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in remaining)
        {
            var key = AppIdentity.RunningKey(new WindowFacts(window.ProcessName, window.ProcessPath, window.AppId));
            if (key.Length == 0) continue;
            if (!grouped.TryGetValue(key, out var entry))
            {
                entry = (AppIdentity.OwnerProcess(window.ProcessName), window.ProcessPath, window.AppId, new List<IntPtr>());
                grouped[key] = entry;
            }
            else if ((entry.Path.Length == 0 && window.ProcessPath.Length > 0) || (entry.AppId.Length == 0 && window.AppId.Length > 0))
            {
                // a later window of the same app may have a readable exe or appId: the icon comes from it
                entry = (
                    entry.Name,
                    entry.Path.Length > 0 ? entry.Path : window.ProcessPath,
                    entry.AppId.Length > 0 ? entry.AppId : window.AppId,
                    entry.Windows);
                grouped[key] = entry;
            }

            entry.Windows.Add(window.Hwnd);
        }

        // an app already in the dock keeps its item: only a newcomer has its icon resolved
        var known = new Dictionary<string, DockItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            if (!item.IsPinned) known[item.Key] = item;
        }

        var unpinned = new List<DockItemViewModel>();
        foreach (var (key, entry) in grouped.OrderBy(pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase))
        {
            var processName = entry.Name;
            var shellAppTarget = entry.AppId.Contains('!') ? $"shell:AppsFolder\\{entry.AppId}" : null;
            var targetPath = entry.Path.Length > 0 ? entry.Path : (shellAppTarget ?? string.Empty);

            if (known.TryGetValue("R:" + key, out var existing) && (existing.App.Target.Length > 0 || targetPath.Length == 0))
            {
                existing.Windows = entry.Windows;
                existing.IsRunning = true;
                unpinned.Add(existing);
                continue;
            }

            var displayName = (shellAppTarget is not null ? ShellImage.GetDisplayName(shellAppTarget) : null) ?? processName;

            var vm = new DockItemViewModel(
                new DockApp { Name = displayName, Target = targetPath, IconPath = entry.Path.Length > 0 ? null : shellAppTarget },
                isPinned: false,
                window: entry.Windows.Count > 0 ? entry.Windows[0] : IntPtr.Zero,
                processName: processName,
                runningKey: key);

            vm.Windows = entry.Windows;
            vm.IsRunning = true;

            if (vm.Icon is not null)
            {
                unpinned.Add(vm);
            }
        }

        var desired = _pinnedList.Select(vm => vm.Key)
            .Concat(unpinned.Select(vm => vm.Key))
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
        else if (!unpinned.SequenceEqual(Items.Where(item => !item.IsPinned)))
        {
            // the same apps, one of them under a new item (say it restarted from elsewhere): keep the existing
            // slots and their icons but take the newcomer's windows, or recreated windows are never picked up
            var fresh = unpinned.ToDictionary(vm => vm.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var item in Items)
            {
                if (!item.IsPinned && fresh.TryGetValue(item.Key, out var current) && !ReferenceEquals(item, current))
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
            UpdateStack(slot);
        }
    }

    private const int StackDepth = 3;

    // a copy of the icon as it shows: the outline in the ink, or the full card/artwork (never an empty hover frame)
    private static void Dress(System.Windows.Shapes.Rectangle copy, IconSlot slot, Image artwork, System.Windows.Shapes.Rectangle line)
    {
        if (line.Visibility == Visibility.Visible)
        {
            copy.Fill = line.Fill;
            copy.OpacityMask = line.OpacityMask;
            copy.Margin = line.Margin;
        }
        else
        {
            var source = slot.Card is { } card ? card.OnFrame : (artwork.Source ?? slot.Item.Icon);
            copy.Fill = source is null ? null : new ImageBrush(source) { Stretch = Stretch.Uniform };
            copy.OpacityMask = null;
            copy.Margin = artwork.Margin;
        }
    }

    /// <summary>
    /// Turns the deck one card as the wheel changes window: forward, the front card slides to the back and fades
    /// while the next one steps up; backward, the last card comes round to the front. The copies all look alike,
    /// so only the motion is shown and every element ends where it started.
    /// </summary>
    private void ShuffleStack(IconSlot slot, bool forward)
    {
        if (slot.Flyer is not { } flyer || slot.Item.Windows.Count < 2
            || slot.Button.Content is not Grid { Children: [Image artwork, System.Windows.Shapes.Rectangle line, ..] }) return;
        var front = line.Visibility == Visibility.Visible ? (UIElement)line : artwork;
        if (!outlineOrArt(slot, artwork, line)) return;
        Dress(flyer, slot, artwork, line);

        int depth = Math.Min(StackDepth, slot.Item.Windows.Count - 1);
        double back = IconBox * .18 * depth, shrink = 1 - (.08 * depth);
        var duration = new Duration(TimeSpan.FromMilliseconds(260));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var move = new TranslateTransform();
        var size = new ScaleTransform();
        var turn = new TransformGroup();
        turn.Children.Add(size);
        turn.Children.Add(move);
        flyer.RenderTransform = turn;
        flyer.Visibility = Visibility.Visible;

        DoubleAnimation Run(double from, double to) => new(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var x = forward ? Run(0, back) : Run(back, 0);
        x.Completed += (_, _) => flyer.Visibility = Visibility.Collapsed;
        move.BeginAnimation(TranslateTransform.XProperty, x);
        move.BeginAnimation(TranslateTransform.YProperty, forward ? Run(0, -back) : Run(-back, 0));
        size.BeginAnimation(ScaleTransform.ScaleXProperty, forward ? Run(1, shrink) : Run(shrink, 1));
        size.BeginAnimation(ScaleTransform.ScaleYProperty, forward ? Run(1, shrink) : Run(shrink, 1));
        flyer.BeginAnimation(OpacityProperty, forward ? Run(1, .30) : Run(.45, 1));

        // the card left in front: forward it rises out of the stack, backward it is hidden until the flyer lands
        front.BeginAnimation(OpacityProperty, forward ? Run(.35, 1) : new DoubleAnimationUsingKeyFrames
        {
            Duration = duration,
            FillBehavior = FillBehavior.Stop,
            KeyFrames = { new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)), new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)) },
        });

        static bool outlineOrArt(IconSlot slot, Image artwork, System.Windows.Shapes.Rectangle line)
            => line.Visibility == Visibility.Visible || slot.Card is not null || artwork.Source is not null || slot.Item.Icon is not null;
    }

    /// <summary>
    /// Shows a copy of the icon behind it for each window past the first (up to three), each a step up and to the
    /// side and a little smaller and fainter, so the windows of one app read as a deck at a glance.
    /// </summary>
    private void UpdateStack(IconSlot slot)
    {
        if (slot.Stack.Length == 0 || slot.Button.Content is not Grid { Children: [Image artwork, System.Windows.Shapes.Rectangle line, ..] }) return;
        int extra = Math.Min(StackDepth, Math.Max(0, slot.Item.Windows.Count - 1));
        bool outline = line.Visibility == Visibility.Visible;
        bool hasArt = outline || slot.Card is not null || artwork.Source is not null || slot.Item.Icon is not null;
        double step = IconBox * .08;
        for (int layer = 0; layer < slot.Stack.Length; layer++)
        {
            var copy = slot.Stack[layer];
            if (layer >= extra || !hasArt)
            {
                copy.Visibility = Visibility.Collapsed;
                continue;
            }

            Dress(copy, slot, artwork, line);
            double k = layer + 1;
            double shrink = 1 - (.08 * k);
            var turn = new TransformGroup();
            turn.Children.Add(new ScaleTransform(shrink, shrink));
            turn.Children.Add(new TranslateTransform(step * k, -step * k));
            copy.RenderTransform = turn;
            copy.Opacity = .36 - (.10 * layer);
            copy.Visibility = Visibility.Visible;
        }
    }

    // the other edition's dock (Floe beside FloeCP77) is no app to switch to either
    private static bool IsOwnProduct(string name)
        => name.Equals("Floe", StringComparison.OrdinalIgnoreCase) || name.Equals("FloeCP77", StringComparison.OrdinalIgnoreCase);

    private void ScheduleRunningRefresh()
    {
        _runningDebounce.Stop();
        _runningDebounce.Start();
    }

}
