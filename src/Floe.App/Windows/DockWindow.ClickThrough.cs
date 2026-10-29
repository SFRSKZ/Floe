using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // A layered window lets clicks through wherever it draws nothing; a window DWM blends by its alpha takes every
    // click on its rectangle, the empty room above the capsule included, and WS_EX_TRANSPARENT does nothing for a
    // window that is not layered. So whenever the cursor is off the glass and off everything it draws to be clicked,
    // the DWM dock cuts its window down to a region round the bar (it is compact then, so nothing is drawn above
    // it), and takes the whole rectangle back the moment the cursor comes over them. Off the region the window
    // gets no mouse input at all, so the cursor is checked on a timer rather than from mouse events.

    private DispatcherTimer? _clickThroughTimer;
    private bool _clickThrough;
    private Int32Rect _clickRegion;

    private void StartClickThrough()
    {
        if (!DwmDock || _clickThroughTimer is not null) return;
        _clickThroughTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _clickThroughTimer.Tick += (_, _) => UpdateClickThrough();
        _clickThroughTimer.Start();
        UpdateClickThrough();
    }

    private void UpdateClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;
        // a drag, a held button or an open menu keep the window solid until they end
        bool solid = !IsVisible || _isDragging || _menuOpen || Mouse.Captured is not null || CursorOverSolid();
        SetClickThrough(!solid);
    }

    private bool CursorOverSolid()
    {
        if (!ScreenInput.TryGetWindowRect(_hwnd, out var rect)) return false;
        var (x, y) = ScreenInput.GetCursorPosition();
        bool near = x >= rect.Left - 40 && x < rect.Right + 40 && y >= rect.Top - 40 && y < rect.Bottom + 40;
        // far from the dock a slow check is enough; near it, every frame, so the first click already lands
        _clickThroughTimer!.Interval = TimeSpan.FromMilliseconds(near ? 16 : 120);
        if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom) return false;

        Point point;
        try { point = PointFromScreen(new Point(x, y)); }
        catch (InvalidOperationException) { return false; } // not connected to a source yet
        return IsSolidAt(point);
    }

    // whether a point in window coordinates is on something the dock draws to be clicked
    private bool IsSolidAt(Point point)
    {
        // the glass, as drawn at the moment, but only the bar itself: the bulge it lifts toward the cursor is
        // decoration, and counting it let the dock swallow clicks meant for the window just above it
        var local = TranslatePoint(point, Root);
        if (local.Y >= RestTop - 2 && _glass.Data is { } shape && PresentationSource.FromVisual(_glass) is not null
            && shape.FillContains(TranslatePoint(point, _glass), 1, ToleranceType.Absolute)) return true;

        // the icons and buttons, magnified ones above the capsule included; the layer itself has no background
        if (IconLayer.InputHitTest(TranslatePoint(point, IconLayer)) is not null) return true;

        // the narrow gaps between magnified icons, so sliding along their tops does not drop the hover
        foreach (var slot in _slots)
        {
            var bounds = slot.Button.TransformToAncestor(Root).TransformBounds(new Rect(slot.Button.RenderSize));
            bounds.Inflate(6, 0);
            if (bounds.Contains(local)) return true;
        }
        return false;
    }

    private void SetClickThrough(bool through)
    {
        if (!through)
        {
            if (!_clickThrough) return;
            _clickThrough = false;
            _clickRegion = default;
            WindowRegion.Clear(_hwnd);
            return;
        }

        // the bar as drawn now, a little room round it for its glow, down to the window's bottom for the shadow
        if (_glass.Data is not { } shape || PresentationSource.FromVisual(_glass) is null) return;
        var bar = _glass.TransformToAncestor(this).TransformBounds(shape.Bounds);
        var region = new Int32Rect(
            (int)Math.Floor((bar.Left - 16) * _dpi),
            (int)Math.Floor((bar.Top - 4) * _dpi),
            0, 0);
        region.Width = (int)Math.Ceiling((bar.Right + 16) * _dpi) - region.X;
        region.Height = (int)Math.Ceiling(ActualHeight * _dpi) - region.Y;
        if (_clickThrough && region == _clickRegion) return;
        _clickThrough = true;
        _clickRegion = region;
        WindowRegion.SetRect(_hwnd, region.X, region.Y, region.X + region.Width, region.Y + region.Height);
    }
}
