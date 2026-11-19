using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Floe.App.Effects.Glass;
using Floe.App.Services;
using Floe.Core.Models;

namespace Floe.App.Windows;

/// <summary>
/// The dock on any screen edge. Everything is laid out as the bottom dock and the stage is turned onto the chosen
/// edge (mirrored for the top, a quarter turn for the left, folded over the diagonal for the right), so the bulge, the shadow, the hit tests and the effect
/// layers keep working unchanged. Icons and text are turned back one by one, so they read upright on every edge;
/// on a side the wide sections (the song, the weather) step aside and the clock shows only the time.
/// </summary>
public partial class DockWindow
{
    // the turn back, shared by every icon and text: identity on the bottom edge
    private readonly MatrixTransform _upright = new(Matrix.Identity);

    // the backdrop is a picture of the screen, so it is turned back as a whole onto the stage it sits in
    private readonly MatrixTransform _backdropTurn = new(Matrix.Identity);

    // the time alone, turned upright across a side dock's bar
    private const double SideClockWidth = 64;
    private const double SideClockSlot = 40;

    private static Transform EdgeTurn(DockEdge edge) => edge switch
    {
        DockEdge.Top => new ScaleTransform(1, -1),
        DockEdge.Left => new RotateTransform(90),
        // the right edge reads top to bottom like the left one: the bar is folded over the diagonal, not turned,
        // so the first icon stays at the top
        DockEdge.Right => new MatrixTransform(0, 1, 1, 0, 0, 0),
        _ => Transform.Identity,
    };

    private static Matrix UprightMatrix(DockEdge edge)
    {
        var matrix = Matrix.Identity;
        switch (edge)
        {
            case DockEdge.Top: matrix.Scale(1, -1); break;
            case DockEdge.Left: matrix.Rotate(-90); break;
            case DockEdge.Right: matrix = new Matrix(0, 1, 1, 0, 0, 0); break;
        }

        return matrix;
    }

    /// <summary>Turns an element back about its middle, so it reads upright whatever edge the dock is on.</summary>
    private void MakeUpright(UIElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = _upright;
    }

    private void ApplyUpright(DockEdge edge)
    {
        _upright.Matrix = UprightMatrix(edge);
        var vertical = edge is DockEdge.Left or DockEdge.Right;

        // the song and the weather are wide rows of text: on a side they would stand across the bar, so they go
        MakeUpright(_clock);
        MakeUpright(_media);
        MakeUpright(_weather);
        _weather.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        _clock.Width = vertical ? SideClockWidth : ClockWidth;
        _clockTime.FontSize = ClockTimeSize(GlassSettings.Cyber);
        _clockDate.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;

        // the cyber markings carry a word drawn as a path; turned with the bar it would read mirrored or sideways
        var wordShown = edge == DockEdge.Bottom ? Visibility.Visible : Visibility.Collapsed;
        _decalText.Visibility = _decalTextEdge.Visibility = wordShown;

        _fx.Upright = _upright.Matrix;
        _fx.Spine = edge switch
        {
            DockEdge.Left => Matrix.Identity,
            // the fold mirrors; a flip across the line turns that into the left edge's quarter turn
            DockEdge.Right => new Matrix(1, 0, 0, -1, 0, 0),
            _ => _upright.Matrix,
        };

        ApplyBackdropTurn();
    }

    /// <summary>The time's size: smaller down a side, where it stands across the bar.</summary>
    private static double ClockTimeSize(bool cyber) => DockSettings.Vertical ? (cyber ? 19 : 17) : cyber ? 31 : 26;

    /// <summary>
    /// Opens <paramref name="popup"/> beside <paramref name="anchor"/> (screen DIP, on the dock's inner side), away
    /// from the edge, at a screen point: tied to a turned element its content would be turned with it.
    /// </summary>
    private void ShowUprightPopup(Popup popup, Point anchor, double gap)
    {
        popup.PlacementTarget = null;
        popup.Placement = PlacementMode.Absolute;
        popup.IsOpen = true;
        void Place()
        {
            if (popup.Child is not FrameworkElement child) return;
            double width = child.ActualWidth, height = child.ActualHeight;
            var work = SettingsWindow.GetWorkArea(this);
            var (left, top) = DockSettings.Edge switch
            {
                DockEdge.Top => (anchor.X - (width / 2), anchor.Y + gap),
                DockEdge.Left => (anchor.X + gap, anchor.Y - (height / 2)),
                DockEdge.Right => (anchor.X - width - gap, anchor.Y - (height / 2)),
                _ => (anchor.X - (width / 2), anchor.Y - height - gap),
            };
            popup.HorizontalOffset = Math.Clamp(left, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8));
            popup.VerticalOffset = Math.Clamp(top, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8));
        }

        Place();
        // the child has its size once the popup has laid it out
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Place);
    }

    /// <summary>
    /// The backdrop holds the screen behind the window as the window lies on the screen; on another edge it is
    /// sized like the window and turned back by the inverse of the stage's turn, so it lands where it was captured.
    /// </summary>
    private void ApplyBackdropTurn()
    {
        var edge = DockSettings.Edge;
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        _backdrop.Width = vertical ? WindowHeight : _windowWidth;
        _backdrop.Height = vertical ? _windowWidth : WindowHeight;
        _backdropTurn.Matrix = edge switch
        {
            // window (x, y) -> stage (x, h - y)
            DockEdge.Top => new Matrix(1, 0, 0, -1, 0, WindowHeight),
            // window (x, y) -> stage (y, h - x)
            DockEdge.Left => new Matrix(0, -1, 1, 0, 0, WindowHeight),
            // window (x, y) -> stage (y, x)
            DockEdge.Right => new Matrix(0, 1, 1, 0, 0, 0),
            _ => Matrix.Identity,
        };

        // the liquid pass maps a horizontal capsule into the backdrop's pixels; off the bottom edge it would bend
        // the wrong place, so the plain vibrancy carries the glass there
        if (_liquid is null) return;
        if (edge != DockEdge.Bottom && ReferenceEquals(_backdrop.Effect, _liquid)) _backdrop.Effect = _edgeVibrancy ??= new VibrancyEffect();
        else if (edge == DockEdge.Bottom && _edgeVibrancy is not null && ReferenceEquals(_backdrop.Effect, _edgeVibrancy)) _backdrop.Effect = _liquid;
    }

    private VibrancyEffect? _edgeVibrancy;
}
