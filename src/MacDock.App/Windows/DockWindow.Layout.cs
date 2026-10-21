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

    private void RebuildIcons()
    {
        _slots.Clear();
        IconLayer.Children.Clear();
        IconLayer.Children.Add(_separator);

        var count = Items.Count;

        _separatorIndex = -1;
        for (var i = 1; i < count; i++)
        {
            if (!Items[i].IsPinned)
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
        _windowWidth = LeadX + maxContent + TrailWidth(1) + (2 * EdgeMargin);
        _restCapsuleLeft = (_windowWidth - _capsuleWidth) / 2;
        _restCapsuleWidth = _capsuleWidth;
        _capsuleLeft = _restCapsuleLeft;
        _backdrop.Width = _windowWidth;
        _backdrop.Height = WindowHeight;

        Width = _windowWidth;
        Height = WindowHeight;

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
            grid.Children.Add(dot);

            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
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
                Template = CreateButtonTemplate(),
                RenderTransformOrigin = new Point(0.5, 1.0),
                RenderTransform = transformGroup,
            };
            var slot = new IconSlot(button, dot, item, centerLocal, scale, translate);
            _slots.Add(slot);
            IconLayer.Children.Add(button);

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
        return template;
    }

    private double[] ComputeScales(double hoverWindow)
    {
        var count = _slots.Count;
        var result = new double[count];
        if (count == 0)
        {
            return result;
        }

        var falloffs = new double[count];
        var max = 0.0;
        for (var i = 0; i < count; i++)
        {
            var distance = Math.Abs(hoverWindow - (_restCapsuleLeft + _slots[i].CenterLocal));
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
        if (count == 0)
        {
            return;
        }

        var total = Spacing * (count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
        }

        var startX = _capsuleLeft + LeadX;
        var x = startX;
        var separatorX = double.NaN;
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
            Canvas.SetLeft(slot.Button, center - (IconBox / 2));
            Canvas.SetTop(slot.Button, IconBottom - IconBox);

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

        if (_activeIndex >= 0 && _activeIndex < count && _auraGrid.Visibility == Visibility.Visible)
        {
            PositionAura(_slots[_activeIndex]);
        }

        if (_activeIndex >= 0 && _activeIndex < count && _spectralLayer.Visibility == Visibility.Visible)
        {
            PositionSpectral(_slots[_activeIndex]);
        }
    }

    private void UpdateCapsule(IReadOnlyList<double> scales)
    {
        var count = _slots.Count;
        if (count == 0)
        {
            _capsuleWidth = 1;
            _capsuleLeft = (_windowWidth - 1) / 2;
            return;
        }

        var total = Spacing * (count - 1) + (HasSeparator ? SeparatorExtra : 0);
        for (var i = 0; i < count; i++)
        {
            total += IconBox * scales[i];
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
