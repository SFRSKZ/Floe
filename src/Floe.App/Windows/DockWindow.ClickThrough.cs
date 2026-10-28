using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // A layered window lets clicks through wherever it draws nothing; a window DWM blends by its alpha takes every
    // click on its rectangle, the empty room above the capsule included. So the DWM dock turns itself
    // click-through (WS_EX_TRANSPARENT) whenever the cursor is off the glass and off everything it draws to be
    // clicked, and solid again the moment the cursor comes back over them. A click-through window gets no mouse
    // input at all, so the cursor is checked on a timer rather than from mouse events.

    private DispatcherTimer? _clickThroughTimer;
    private bool _clickThrough;

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

        // the glass, as drawn at the moment (it grows and shrinks with the hover)
        if (_glass.Data is { } shape && PresentationSource.FromVisual(_glass) is not null
            && shape.FillContains(TranslatePoint(point, _glass), 1, ToleranceType.Absolute)) return true;

        // the icons and buttons, magnified ones above the capsule included; the layer itself has no background
        return IconLayer.InputHitTest(TranslatePoint(point, IconLayer)) is not null;
    }

    private void SetClickThrough(bool through)
    {
        if (through == _clickThrough) return;
        _clickThrough = through;
        WindowStyling.SetClickThrough(_hwnd, through);
    }
}
