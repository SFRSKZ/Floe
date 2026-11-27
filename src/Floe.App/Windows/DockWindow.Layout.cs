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

    private bool _roomForDecals;

    private void RebuildIcons()
    {
        SetFxHover(null);
        CancelFxSettle(restoreArtwork: false);
        _roomForDecals = GlassSettings.Cyber;
        _slots.Clear();
        IconLayer.Children.Clear();
        IconLayer.Children.Add(_separator);

        var count = Items.Count;

        _separatorIndex = -1;
        for (var i = 1; i < count; i++)
        {
            if (Items[i - 1].IsPinned && !Items[i].IsPinned)
            {
                _separatorIndex = i;
                break;
            }
        }

        var extra = HasSeparator ? SeparatorExtra : 0;

        var restContent = count == 0 ? 0 : (count * IconBox) + ((count - 1) * Spacing) + extra;
        _restContent = restContent;
        _capsuleWidth = LeadX + restContent + TrailWidth(_mediaReveal);

        var maxContent = count == 0 ? 0 : (count * IconBox * HoverMaxScale) + ((count - 1) * Spacing) + extra;
        _windowWidth = LeadX + maxContent + TrailWidth(1) + (2 * EdgeMargin) + (GlassSettings.Cyber ? 2 * DecalRoom : 0);
        _restCapsuleLeft = (_windowWidth - _capsuleWidth) / 2;
        _restCapsuleWidth = _capsuleWidth;
        _capsuleLeft = _restCapsuleLeft;
        if (TaskbarOn) ApplyTaskbarGeometry();
        ApplyBackdropTurn();
        UpdateBackdropUnscale();

        ApplyDockOrientation();
        // a pinned or closed app changes the width; grow or shrink from the middle, not the left edge
        if (_hwnd != IntPtr.Zero) CenterOnMonitor();

        for (var i = 0; i < count; i++)
        {
            var item = Items[i];
            var localExtra = HasSeparator && i >= _separatorIndex ? SeparatorExtra : 0;
            var centerLocal = LeadX + (i * (IconBox + Spacing)) + localExtra + (IconBox / 2);

            var image = new Image
            {
                Source = item.Icon,
                Width = IconBox,
                Height = IconBox,
                Stretch = Stretch.Uniform,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            MakeUpright(image);

            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = _runningDotBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                Visibility = Visibility.Collapsed,
            };

            var grid = new Grid { Width = IconBox, Height = IconBox };
            grid.Children.Add(image);
            // the cyber themes' outline icon, in the app artwork's place; the set leaves a wide margin around its drawings
            var outline = new Rectangle { Margin = new Thickness(-5), Visibility = Visibility.Collapsed };
            MakeUpright(outline);
            grid.Children.Add(outline);
            grid.Children.Add(dot);
            // an app with more windows shows as a small stack of itself; kept behind the artwork and after it in
            // the children, since the click effects find the artwork as the first child
            var stack = new Rectangle[StackDepth];
            for (int layer = 0; layer < StackDepth; layer++)
            {
                stack[layer] = new Rectangle { Visibility = Visibility.Collapsed, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
                Panel.SetZIndex(stack[layer], -1 - layer);
                grid.Children.Add(stack[layer]);
            }

            var pressScale = new ScaleTransform(1, 1, IconBox / 2.0, IconBox / 2.0);
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(pressScale);
            transformGroup.Children.Add(scale);
            transformGroup.Children.Add(translate);

            var button = new Button
            {
                Width = IconBox,
                Height = IconBox,
                Content = grid,
                DataContext = item,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Template = SharedButtonTemplate,
                RenderTransformOrigin = new Point(0.5, 1.0),
                RenderTransform = transformGroup,
            };
            // the card that flies to the back (or comes from it) when the wheel turns the deck
            var flyer = new Rectangle { Visibility = Visibility.Collapsed, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5) };
            Panel.SetZIndex(flyer, 5);
            grid.Children.Add(flyer);
            var slot = new IconSlot(button, dot, item, centerLocal, pressScale, scale, translate) { Stack = stack, Flyer = flyer };
            _slots.Add(slot);
            IconLayer.Children.Add(button);
            ApplyCyberIcon(slot);
            ApplyRestGlass(slot);

            if (item.IsPinned)
            {
                button.PreviewMouseLeftButtonDown += (_, e) => DragStart(slot, e);
                button.PreviewMouseMove += (_, e) => DragMove(e);
                button.PreviewMouseLeftButtonUp += (_, e) => DragEnd(e);
            }
            else
            {
                button.Click += DockIcon_Click;
            }
        }

        var scales = ComputeScales(_currentHoverX);
        UpdateCapsule(scales);
        ApplyLayout(scales);
        UpdateShape();
        UpdateAura();
    }

    private static readonly ControlTemplate SharedButtonTemplate = CreateButtonTemplate();

    private static ControlTemplate CreateButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        template.Seal();
        return template;
    }

    private double[] _scaleBuffer = Array.Empty<double>();
    private double[] _falloffBuffer = Array.Empty<double>();

    private double[] ComputeScales(double hoverWindow)
    {
        // called every frame while the dock moves: the two arrays are kept and only replaced when the icon count
        // changes; every caller uses the result before the next call
        var count = _slots.Count;
        if (_scaleBuffer.Length != count)
        {
            _scaleBuffer = new double[count];
            _falloffBuffer = new double[count];
        }

        var result = _scaleBuffer;
        if (count == 0)
        {
            return result;
        }

        var falloffs = _falloffBuffer;
        var max = 0.0;
        for (var i = 0; i < count; i++)
        {
            var distance = Math.Abs(hoverWindow - RestCenter(_slots[i]));
            falloffs[i] = Falloff(distance, FalloffRadius);
            max = Math.Max(max, falloffs[i]);
        }

        var norm = max > 0.001 ? 1.0 / max : 0.0;
        var intensity = MaxBulge > 0 ? Math.Max(0, Math.Min(1, _currentBulge / MaxBulge)) : 0.0;

        for (var i = 0; i < count; i++)
        {
            result[i] = 1 + ((HoverMaxScale - 1) * Math.Min(1.0, falloffs[i] * norm) * intensity);
        }

        return result;
    }

    private void ApplyLayout(IReadOnlyList<double> scales)
    {
        var count = _slots.Count;
        var total = Spacing * Math.Max(0, count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
        }

        var startX = TaskbarOn ? TaskbarIconStart(total) : _capsuleLeft + LeadX;
        _taskbarIconsLeft = startX;
        _taskbarIconsRight = startX + total;
        var x = startX;
        var separatorX = double.NaN;

        // a held icon leaves its place and the rest slide over to open a gap where it would land
        var held = _isDragging ? _dragSlot : null;
        var heldIndex = held is null ? -1 : _slots.IndexOf(held);
        var gapWidth = heldIndex < 0 ? 0 : (IconBox * scales[heldIndex]) + Spacing;
        var ease = Math.Min(1.0, _frameDt * 16);
        var order = 0;
        for (var i = 0; i < count; i++)
        {
            if (HasSeparator && i == _separatorIndex)
            {
                separatorX = x + (SeparatorExtra / 2);
                x += SeparatorExtra;
            }

            var width = IconBox * scales[i];
            var center = x + (width / 2);
            var slot = _slots[i];

            slot.Center = center;
            var shift = 0.0;
            if (heldIndex >= 0 && i != heldIndex)
            {
                shift = (i > heldIndex ? -gapWidth : 0) + (_dragGap >= 0 && order >= _dragGap ? gapWidth : 0);
                order++;
            }

            slot.Shift += (shift - slot.Shift) * ease;
            if (slot == held)
            {
                Canvas.SetLeft(slot.Button, _dragX - (IconBox / 2));
                // lifted a little off the shelf, and down with the cursor when pulled out to unpin
                Canvas.SetTop(slot.Button, IconBottom - IconBox - 6 + Math.Clamp(_dragY - (IconBottom - (IconBox / 2)), 0, IconBox));
            }
            else
            {
                Canvas.SetLeft(slot.Button, center - (IconBox / 2) + slot.Shift);
                Canvas.SetTop(slot.Button, IconBottom - IconBox);
            }

            slot.Scale.ScaleX = scales[i];
            slot.Scale.ScaleY = scales[i];

            x += width + Spacing;
        }

        if (HasSeparator && !double.IsNaN(separatorX))
        {
            _separator.Visibility = Visibility.Visible;
            Canvas.SetLeft(_separator, separatorX - (_separator.Width / 2));
            Canvas.SetTop(_separator, IconBottom - (IconBox / 2) - (_separator.Height / 2));
        }
        else
        {
            _separator.Visibility = Visibility.Collapsed;
        }

        PositionStartButton();
        PositionSections(startX + total);
        FollowTaskbarLayout();
        FollowDockGpu();
        UpdateCyberHover(scales);

        if (_activeIndex >= 0 && _activeIndex < count && _auraGrid.Visibility == Visibility.Visible)
        {
            PositionAura(_slots[_activeIndex]);
        }

        if (_activeIndex >= 0 && _activeIndex < count && _spectralLayer.Visibility == Visibility.Visible)
        {
            PositionSpectral(_slots[_activeIndex]);
        }

        UpdateEffectVisibility();
    }

    private void UpdateCapsule(IReadOnlyList<double> scales)
    {
        var count = _slots.Count;
        var total = Spacing * Math.Max(0, count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
        }

        if (TaskbarOn)
        {
            // the bar spans the monitor whatever it holds
            _capsuleWidth = (_windowWidth / TaskbarScale) + (2 * TaskbarBleed);
            _capsuleLeft = (_windowWidth - _capsuleWidth) / 2;
            return;
        }

        _capsuleWidth = LeadX + total + TrailWidth(_mediaReveal);
        _capsuleLeft = (_windowWidth - _capsuleWidth) / 2;
    }

    private void DockIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DockItemViewModel item)
        {
            Log("click (no item)");
            return;
        }

        ActivateItem(item);
    }

    private void ActivateItem(DockItemViewModel item)
    {
        // The click answers first: the sound is mixed on its own stream, so launching an app cannot delay it.
        var clicked = _slots.FirstOrDefault(s => ReferenceEquals(s.Item, item));
        var pan = clicked is null || _windowWidth <= 0
            ? 0
            : (((clicked.Center / _windowWidth) * 2) - 1) * 0.32;
        ClickSound.Play(AuraSettings.ClickTone, pan);

        PlayClickGlass(item);

        // the app already in front: its icon puts the window away, as the taskbar does
        if (WindowActivator.ForegroundAmong(item.Windows) is var front && front != IntPtr.Zero)
        {
            WindowActivator.Minimize(front);
            Log($"click {item.Name} -> minimize foreground {front}");
            return;
        }

        // group: cycle through the windows of the same app
        if (item.Windows.Count > 0)
        {
            var hwnd = item.NextWindow();
            var activated = WindowActivator.Activate(hwnd);

            // elevated windows (e.g. Task Manager) can't be restored by a non-elevated process;
            // relaunching a single-instance app brings its existing window back
            if (!activated || ScreenInput.IsWindowMinimized(hwnd))
            {
                var relaunch = AppLauncher.LaunchFresh(item.App);
                Log($"click {item.Name} -> activate failed (elevated?), relaunch: {relaunch.Message}");
                return;
            }

            Log($"click {item.Name} -> cycle window {hwnd} ok={activated} count={item.Windows.Count}");
            return;
        }

        var result = AppLauncher.LaunchOrActivate(item.App);
        Log($"click {item.Name} -> {result.Message}");

        if (result.Started && _slots.FirstOrDefault(s => ReferenceEquals(s.Item, item)) is { } slot)
        {
            Bounce(slot);
        }
    }

    private void Bounce(IconSlot slot)
    {
        if (GlassSettings.Cyber)
        {
            PlayCyberLaunch(slot);
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(700),
        };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-IconBox * 0.42, KeyTime.FromPercent(0.28)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.6)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-IconBox * 0.2, KeyTime.FromPercent(0.78)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        slot.Translate.BeginAnimation(TranslateTransform.YProperty, animation);
    }

}
