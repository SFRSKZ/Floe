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

    private void OnWidgetRendering(object? sender, EventArgs e)
    {
        if (!UpdateWidgetPositions())
        {
            StopWidgetRendering();
        }
    }

    private void EnsureWidgetRendering()
    {
        if (!_widgetRenderingHooked)
        {
            _widgetRenderingHooked = true;
            CompositionTarget.Rendering += OnWidgetRendering;
        }
    }

    private void StopWidgetRendering()
    {
        if (_widgetRenderingHooked)
        {
            _widgetRenderingHooked = false;
            CompositionTarget.Rendering -= OnWidgetRendering;
        }
    }

    private void EnsureWidgets()
    {
        if (_weatherWidget is not null || NoFx.Contains("widgets"))
        {
            return;
        }

        var visible = WidgetLayout.GetVisible();
        try
        {
            _weatherWidget = new WeatherWidgetWindow { Blur = BlurBackdrop };
            _mediaWidget = new MediaWidgetWindow { Blur = BlurBackdrop };

            if (visible)
            {
                _weatherWidget.Show();
                _mediaWidget.Show();
            }

            PositionWidgets();
        }
        catch (Exception ex)
        {
            Log($"widgets error: {ex.GetType().Name} {ex.Message}");

            // Drop the half-built pair so the next toggle retries instead of using a null widget.
            _weatherWidget?.Close();
            _mediaWidget?.Close();
            _weatherWidget = null;
            _mediaWidget = null;
        }
    }

    private void ToggleWidgets()
    {
        EnsureWidgets();
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return;
        }

        if (_weatherWidget.IsVisible)
        {
            _weatherWidget.Hide();
            _mediaWidget.Hide();
            WidgetLayout.SetVisible(false);
            return;
        }

        _weatherWidget.Show();
        _mediaWidget.Show();
        WidgetLayout.SetVisible(true);
        PositionWidgets();
    }

    private void PositionWidgets() => ArrangeWidgets();

    private void ArrangeWidgets()
    {
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return;
        }

        var geometry = WidgetGeometry();
        const double gap = 12;

        SnapWidget(_weatherWidget, "weather", geometry, gap);
        SnapWidget(_mediaWidget, "media", geometry, gap);
    }

    private (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) WidgetGeometry()
    {
        var capLeft = Left + _capsuleLeft;
        var capRight = capLeft + _capsuleWidth;
        var restLeft = Left + _restCapsuleLeft;
        return (Top + WindowHeight, capLeft, capRight, restLeft, restLeft + _restCapsuleWidth);
    }

    private static double DefaultLeft(string id, double width, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        var mediaLeft = geometry.RestRight + gap;

        // media is the rightmost widget, weather sits to its left with the same gap
        return id == "media" ? mediaLeft : mediaLeft - gap - width;
    }

    private static double TargetLeft(string id, (double Left, double Top)? saved, double width, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        var baseLeft = saved is { } s
            ? s.Left
            : DefaultLeft(id, width, geometry, gap);

        var restCenter = (geometry.RestLeft + geometry.RestRight) / 2;
        if ((baseLeft + (width / 2)) >= restCenter)
        {
            var expansion = Math.Max(0, geometry.CapRight - geometry.RestRight);
            var overlap = Math.Max(0, (geometry.CapRight + gap) - baseLeft);
            return baseLeft + Math.Min(overlap, expansion);
        }

        var expansionLeft = Math.Max(0, geometry.RestLeft - geometry.CapLeft);
        var overlapLeft = Math.Max(0, (baseLeft + width) - (geometry.CapLeft - gap));
        return baseLeft - Math.Min(overlapLeft, expansionLeft);
    }

    private static double TargetTop((double Left, double Top)? saved, double height, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry)
        => saved is { } s ? s.Top : geometry.Bottom - height;

    private static void SnapWidget(Window widget, string id, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return;
        }

        // never fight the user while a drag is in progress
        if (widget is IBubbleWidget { IsBubbleDragging: true })
        {
            return;
        }

        if (widget.ActualWidth <= 0) { widget.UpdateLayout(); }
        var saved = WidgetLayout.Get(id, widget.ActualWidth, widget.ActualHeight);
        widget.Left = TargetLeft(id, saved, widget.ActualWidth, geometry, gap);
        widget.Top = TargetTop(saved, widget.ActualHeight, geometry);
    }

    private bool UpdateWidgetPositions()
    {
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return false;
        }

        var geometry = WidgetGeometry();
        const double gap = 12;

        var moved = SpringWidget(_weatherWidget, "weather", geometry, gap);
        moved |= SpringWidget(_mediaWidget, "media", geometry, gap);
        return moved;
    }

    private static bool SpringWidget(Window widget, string id, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return false;
        }

        // the user owns both axes while dragging; keep the loop alive to re-apply afterwards
        if (widget is IBubbleWidget { IsBubbleDragging: true })
        {
            return true;
        }

        if (widget.ActualWidth <= 0)
        {
            widget.UpdateLayout();
        }

        var saved = WidgetLayout.Get(id, widget.ActualWidth, widget.ActualHeight);
        var target = TargetLeft(id, saved, widget.ActualWidth, geometry, gap);
        var top = TargetTop(saved, widget.ActualHeight, geometry);
        var moved = false;

        if (Math.Abs(widget.Left - target) > 0.5)
        {
            widget.Left = target;
            moved = true;
        }

        if (Math.Abs(widget.Top - top) > 0.5)
        {
            widget.Top = top;
            moved = true;
        }

        return moved;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _clockTime.Text = now.ToString("HH:mm");
        _clockDate.Text = now.ToString("ddd d MMM");
    }

}
