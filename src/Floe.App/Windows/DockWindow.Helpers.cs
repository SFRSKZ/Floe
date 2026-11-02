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

    private Point ToScreenDips(double localX, double localY)
    {
        // a window with no screen yet has no PointToScreen; the dock scales about the middle of its bottom edge
        if (PresentationSource.FromVisual(Root) is null)
        {
            return new Point(Left + (ActualWidth / 2) + ((localX - (ActualWidth / 2)) * _dockScale), Top + ActualHeight + ((localY - ActualHeight) * _dockScale));
        }

        var point = Root.PointToScreen(new Point(localX, localY));
        var scale = DockScale;
        return new Point(point.X / scale, point.Y / scale);
    }

    private double ToScreenX(double localX)
        => ToScreenDips(localX, RestTop + (RestBarHeight / 2)).X;

    private double ToScreenY(double localY)
        => ToScreenDips(_capsuleLeft + (_capsuleWidth / 2), localY).Y;

    private Point DockAnchor(double localX)
    {
        var point = ToScreenDips(localX, RestTop + (RestBarHeight / 2));
        var bounds = CapsuleScreenBounds();
        var scale = DockScale;
        var left = bounds.Left / scale;
        var top = bounds.Top / scale;
        var right = bounds.Right / scale;
        var bottom = bounds.Bottom / scale;
        return DockSettings.Edge switch
        {
            DockEdge.Top => new Point(point.X, bottom),
            DockEdge.Left => new Point(right, point.Y),
            DockEdge.Right => new Point(left, point.Y),
            _ => new Point(point.X, top),
        };
    }

    private void PlacePopover(Window window, Point anchor, Rect work, double gap = 10)
    {
        var edge = DockSettings.Edge;
        var left = edge switch
        {
            DockEdge.Left => anchor.X + gap,
            DockEdge.Right => anchor.X - window.ActualWidth - gap,
            _ => anchor.X - (window.ActualWidth / 2),
        };
        var top = edge switch
        {
            DockEdge.Bottom => anchor.Y - window.ActualHeight - gap,
            DockEdge.Top => anchor.Y + gap,
            _ => anchor.Y - (window.ActualHeight / 2),
        };
        window.Left = Math.Clamp(left, work.Left + 8, Math.Max(work.Left + 8, work.Right - window.ActualWidth - 8));
        window.Top = Math.Clamp(top, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - window.ActualHeight - 8));
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floe.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics must never crash the dock
        }
    }

}
