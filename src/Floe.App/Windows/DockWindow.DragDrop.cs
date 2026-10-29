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

    private void DragStart(IconSlot slot, MouseButtonEventArgs e)
    {
        if (!slot.Item.IsPinned)
        {
            return;
        }

        _dragSlot = slot;
        _dragStart = e.GetPosition(this);
        _isDragging = false;
        Log($"dragStart {slot.Item.Name}");
        _tooltip.IsOpen = false;
        slot.Button.CaptureMouse();
    }

    private void DragMove(MouseEventArgs e)
    {
        if (_dragSlot is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_isDragging)
        {
            if (Math.Abs(position.X - _dragStart.X) < 6 && Math.Abs(position.Y - _dragStart.Y) < 6)
            {
                return;
            }

            _isDragging = true;
        }

        e.Handled = true;
        _dragX = position.X;
        _dragY = position.Y;
        var current = _pinnedList.IndexOf(_dragSlot.Item);
        var target = ComputeDropIndex(position.X);
        // pulled down far enough to unpin: no gap, the others close up
        _dragGap = position.Y > IconBottom + (IconBox * 0.5) || current < 0 ? -1 : target > current ? target - 1 : target;
        Canvas.SetZIndex(_dragSlot.Button, 1000);
        _dragSlot.Button.Opacity = _dragGap < 0 ? 0.45 : 0.9;
        EnsureAnimation();
    }

    private void DragEnd(MouseButtonEventArgs e)
    {
        if (_dragSlot is null)
        {
            return;
        }

        var slot = _dragSlot;
        _dragSlot = null;
        slot.Button.ReleaseMouseCapture();
        Canvas.SetZIndex(slot.Button, 0);
        slot.Button.Opacity = 1;
        _dragGap = -1;

        if (!_isDragging)
        {
            ActivateItem(slot.Item);
            return;
        }

        _isDragging = false;
        var position = e.GetPosition(this);

        if (position.Y > IconBottom + (IconBox * 0.5))
        {
            Unpin(slot.Item);
            return;
        }

        var current = _pinnedList.IndexOf(slot.Item);
        var target = ComputeDropIndex(position.X);
        if (current >= 0 && target != current && target != current + 1)
        {
            _pinnedList.RemoveAt(current);
            if (target > current)
            {
                target--;
            }

            target = Math.Clamp(target, 0, _pinnedList.Count);
            _pinnedList.Insert(target, slot.Item);
            SavePinned();
        }

        RefreshRunning();
    }

    private int ComputeDropIndex(double x)
    {
        var index = 0;
        var pinnedCount = Math.Min(_pinnedList.Count, _slots.Count);
        for (var i = 0; i < pinnedCount; i++)
        {
            if (x > _restCapsuleLeft + _slots[i].CenterLocal)
            {
                index = i + 1;
            }
        }

        return index;
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effects = PreferredEffect(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        e.Effects = PreferredEffect(e);
        if (e.Effects != DragDropEffects.None)
        {
            _targetBulge = MaxBulge;
            _targetDockScale = 1;
            EnsureAnimation();

            var index = FindIndexAt(e.GetPosition(this).X);
            if (index != _dropIndex)
            {
                _dropIndex = index;
                UpdateDropRing();
                RestartSpringLoad();
            }
        }

        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _springTimer?.Stop();
        _dropIndex = -1;
        _dropRing.Visibility = Visibility.Collapsed;
    }

    // Spring-loading: holding a dragged file over a running app's icon brings its window
    // forward, so the file can be dropped straight into that window.
    private void RestartSpringLoad()
    {
        _springTimer ??= CreateSpringTimer();
        _springTimer.Stop();
        if (_dropIndex >= 0)
        {
            _springTimer.Start();
        }
    }

    private DispatcherTimer CreateSpringTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_dropIndex < 0 || _dropIndex >= _slots.Count)
            {
                return;
            }

            var slot = _slots[_dropIndex];
            if (slot.Item.Windows.Count == 0)
            {
                return;
            }

            var hwnd = slot.Item.Windows[0];
            var activated = WindowActivator.Activate(hwnd);
            Log($"spring-load {slot.Item.Name} -> {hwnd} ok={activated}");
            Bounce(slot);
        };
        return timer;
    }

    private int FindIndexAt(double windowX)
    {
        var index = -1;
        var best = double.MaxValue;
        for (var i = 0; i < _slots.Count; i++)
        {
            var distance = Math.Abs(windowX - (_restCapsuleLeft + _slots[i].CenterLocal));
            if (distance < best && distance <= IconBox)
            {
                best = distance;
                index = i;
            }
        }

        return index;
    }

    private void UpdateDropRing()
    {
        if (_dropIndex < 0 || _dropIndex >= _slots.Count)
        {
            _dropRing.Visibility = Visibility.Collapsed;
            return;
        }

        var slot = _slots[_dropIndex];
        Canvas.SetLeft(_dropRing, slot.Center - (_dropRing.Width / 2));
        Canvas.SetTop(_dropRing, (IconBottom - (IconBox / 2)) - (_dropRing.Height / 2));
        _dropRing.Visibility = Visibility.Visible;
    }

    private static DragDropEffects PreferredEffect(DragEventArgs e)
    {
        if (!HasDroppableData(e.Data))
        {
            return DragDropEffects.None;
        }

        var allowed = e.AllowedEffects & (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        if (allowed == DragDropEffects.None)
        {
            return DragDropEffects.None;
        }

        return (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : allowed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;

        var paths = new List<string>();

        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] fileDrop)
        {
            paths.AddRange(fileDrop);
        }

        if (paths.Count == 0)
        {
            var raw = e.Data.GetData("Shell IDList Array");
            var bytes = raw switch
            {
                byte[] b => b,
                System.IO.MemoryStream ms => ms.ToArray(),
                _ => null,
            };

            if (bytes is not null)
            {
                paths.AddRange(ShellDropHelper.ParseIdListArray(bytes));
            }
        }

        Log($"drop: count={paths.Count} index={_dropIndex} formats={string.Join(",", e.Data.GetFormats())}");

        var dropItem = _dropIndex >= 0 && _dropIndex < _slots.Count ? _slots[_dropIndex].Item : null;
        _springTimer?.Stop();
        _dropIndex = -1;
        _dropRing.Visibility = Visibility.Collapsed;

        // dropped on a specific icon: open the files with that app
        if (dropItem is not null && paths.Count > 0)
        {
            var target = AppLauncher.ResolveTarget(dropItem.App);
            const string appsFolder = "shell:AppsFolder\\";
            if (target.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
            {
                var ok = StoreAppActivator.ActivateForFiles(target[appsFolder.Length..], paths);
                Log($"drop: store app {dropItem.Name} ok={ok}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in paths)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = target, Arguments = $"\"{path}\"", UseShellExecute = true });
                    }
                    catch
                    {
                        // ignore individual failures
                    }
                }

                return;
            }
        }

        foreach (var path in paths)
        {
            PinPath(path);
        }
    }

    private static string DeriveAumidName(string aumid)
    {
        var core = aumid.Split('!')[0];
        var parts = core.Split('.');
        var last = parts.Length > 0 ? parts[^1] : core;
        var underscore = last.IndexOf('_');
        if (underscore > 0)
        {
            last = last[..underscore];
        }

        return string.IsNullOrWhiteSpace(last) ? aumid : last;
    }

    private static bool HasDroppableData(System.Windows.IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
           || data.GetDataPresent("Shell IDList Array")
           || data.GetDataPresent("FileNameW");

    private void PinPath(string path)
    {
        DockApp? app = null;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".lnk")
        {
            app = ShortcutReader.Read(path);

            // Store apps resolve to a protected WindowsApps exe that cannot be launched/iconed
            // directly; use the shortcut itself instead.
            var resolved = app?.Target ?? string.Empty;
            if (app is null
                || string.IsNullOrWhiteSpace(resolved)
                || resolved.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
            {
                app = new DockApp { Name = System.IO.Path.GetFileNameWithoutExtension(path), Target = path, IconPath = path };
            }
        }
        else if (System.IO.File.Exists(path))
        {
            app = new DockApp
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Target = path,
                IconPath = path,
            };
        }
        else if (path.Length > 0 && !path.Contains('\\') && !path.Contains('/') && !path.Contains(':'))
        {
            // Start menu / UWP drag yields an AppUserModelID
            var shellTarget = "shell:AppsFolder\\" + path;
            app = new DockApp
            {
                Name = DeriveAumidName(path),
                Target = shellTarget,
                IconPath = shellTarget,
            };
        }

        Log($"pinpath '{path}' target='{app?.Target}'");

        if (app is null || string.IsNullOrWhiteSpace(app.Target))
        {
            return;
        }

        var vm = new DockItemViewModel(app, isPinned: true);
        if (string.IsNullOrWhiteSpace(vm.ProcessName) || _pinned.ContainsKey(vm.ProcessName))
        {
            Log($"pinpath skipped proc='{vm.ProcessName}'");
            return;
        }

        _pinnedList.Add(vm);
        _pinned[vm.ProcessName] = vm;
        SavePinned();
        RefreshRunning();
    }

}
