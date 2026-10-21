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

    private readonly DispatcherTimer _previewHideTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        _targetHoverX = Math.Min(_windowWidth, Math.Max(0, position.X));
        _targetBulge = MaxBulge;
        _targetDockScale = 1;
        SetIconsDimmed(false);
        EnsureAnimation();
        UpdateHoveredIcon();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_menuOpen)
        {
            return;
        }

        _targetBulge = 0;
        _targetDockScale = CompactScale;
        SetIconsDimmed(true);
        _hoveredIndex = -1;
        _tooltipTimer.Stop();
        _tooltip.IsOpen = false;

        // the preview may still be under the cursor just above the dock
        StartPreviewHideTimer();
        EnsureAnimation();
    }

    private int FindHoveredIndex()
    {
        var index = -1;
        var best = double.MaxValue;
        for (var i = 0; i < _slots.Count; i++)
        {
            var distance = Math.Abs(_currentHoverX - (_restCapsuleLeft + _slots[i].CenterLocal));
            if (distance < best && distance <= IconBox)
            {
                best = distance;
                index = i;
            }
        }

        return index;
    }

    private void UpdateHoveredIcon()
    {
        var index = FindHoveredIndex();
        if (index == _hoveredIndex)
        {
            return;
        }

        _hoveredIndex = index;
        _tooltip.IsOpen = false;
        HidePreview();
        _tooltipTimer.Stop();

        if (index >= 0 && _radial?.IsVisible != true)
        {
            _tooltipTimer.Start();
        }
    }

    private void HidePreview()
    {
        _previewHideTimer.Stop();
        _preview?.Close3();
    }

    private void StartPreviewHideTimer()
    {
        if (_preview?.IsVisible != true)
        {
            return;
        }

        _previewHideTimer.Stop();
        _previewHideTimer.Start();
    }

    private void FinishPreviewHide()
    {
        _previewHideTimer.Stop();
        if (_preview?.IsVisible != true || CursorOverDock() || _preview.IsCursorOver)
        {
            return;
        }

        HidePreview();
    }

    private void ShowPreview(IconSlot slot)
    {
        if (_preview is null)
        {
            _preview = new PreviewWindow();
            _preview.MouseLeave += (_, _) => StartPreviewHideTimer();
            _preview.WindowActivated += HidePreview;
            _preview.SourcesGone += HidePreview;
            _previewHideTimer.Tick += (_, _) => FinishPreviewHide();
        }

        _previewHideTimer.Stop();
        _preview.Show();
        _preview.ShowFor(slot.Item.Windows);

        var left = ToScreenX(slot.Center) - (_preview.Width / 2);
        left = Math.Max(8, Math.Min(left, SystemParameters.PrimaryScreenWidth - _preview.Width - 8));
        _preview.Left = left;
        _preview.Top = Top + RestTop - _preview.Height - 10;
    }

    private void ShowTooltip()
    {
        _tooltipTimer.Stop();
        if (_hoveredIndex < 0 || _hoveredIndex >= _slots.Count || _radial?.IsVisible == true)
        {
            return;
        }

        var slot = _slots[_hoveredIndex];

        if (slot.Item.Windows.Count > 0)
        {
            ShowPreview(slot);
            return;
        }

        _tooltipText.Text = slot.Item.Name;
        _tooltip.PlacementTarget = slot.Button;
        _tooltip.IsOpen = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_tooltip.Child is FrameworkElement child)
            {
                _tooltip.HorizontalOffset = -(child.ActualWidth - slot.Button.ActualWidth) / 2;
            }
        });
    }

    public void PreviewHover(double capsuleX)
    {
        _dockScale = _targetDockScale = 1;
        _rootScale.ScaleX = 1;
        _rootScale.ScaleY = 1;
        _currentHoverX = _targetHoverX = _restCapsuleLeft + Math.Min(_capsuleWidth, Math.Max(0, capsuleX));
        _currentBulge = _targetBulge = MaxBulge;
        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        _hoveredIndex = FindHoveredIndex();
        if (_hoveredIndex >= 0)
        {
            _currentGlowColor = _targetGlowColor = _slots[_hoveredIndex].Item.Glow;
        }

        UpdateGlow();
    }

    public void PreviewAura(string processName)
    {
        _activeProcess = processName;
        UpdateAura();
        Log($"preview aura idx={_activeIndex} effect={AuraSettings.Effect}");
    }

}
