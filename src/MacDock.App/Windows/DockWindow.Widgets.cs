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
        if (_weatherWidget is not null)
        {
            return;
        }

        try
        {
            _weatherWidget = new WeatherWidgetWindow();
            _mediaWidget = new MediaWidgetWindow();
            _weatherWidget.Show();
            _mediaWidget.Show();
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
            return;
        }

        _weatherWidget.Show();
        _mediaWidget.Show();
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

        SnapWidget(_weatherWidget, "weather", true, geometry, gap);
        SnapWidget(_mediaWidget, "media", true, geometry, gap);
    }

    private (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) WidgetGeometry()
    {
        var capLeft = Left + _capsuleLeft;
        var capRight = capLeft + _capsuleWidth;
        var restLeft = Left + _restCapsuleLeft;
        return (Top + WindowHeight, capLeft, capRight, restLeft, restLeft + _restCapsuleWidth);
    }

    private static double TargetLeft(string id, bool defaultRight, double width, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        var saved = WidgetLayout.Get(id);
        var baseLeft = saved is { } s
            ? s.Left
            : (defaultRight ? geometry.RestRight + gap : geometry.RestLeft - gap - width);

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

    private static void SnapWidget(Window widget, string id, bool defaultRight, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return;
        }

        if (widget.ActualWidth <= 0) { widget.UpdateLayout(); }
        widget.Left = TargetLeft(id, defaultRight, widget.ActualWidth, geometry, gap);
        widget.Top = geometry.Bottom - widget.ActualHeight;
    }

    private bool UpdateWidgetPositions()
    {
        if (_weatherWidget is null || _mediaWidget is null)
        {
            return false;
        }

        var geometry = WidgetGeometry();
        const double gap = 12;

        var moved = SpringWidget(_weatherWidget, "weather", true, geometry, gap);
        moved |= SpringWidget(_mediaWidget, "media", true, geometry, gap);
        return moved;
    }

    private static bool SpringWidget(Window widget, string id, bool defaultRight, (double Bottom, double CapLeft, double CapRight, double RestLeft, double RestRight) geometry, double gap)
    {
        if (!widget.IsVisible)
        {
            return false;
        }

        if (widget.ActualWidth <= 0)
        {
            widget.UpdateLayout();
        }

        var target = TargetLeft(id, defaultRight, widget.ActualWidth, geometry, gap);
        var moved = false;

        if (widget is not IBubbleWidget { IsBubbleDragging: true } && Math.Abs(widget.Left - target) > 0.5)
        {
            widget.Left = target;
            moved = true;
        }

        var top = geometry.Bottom - widget.ActualHeight;
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
