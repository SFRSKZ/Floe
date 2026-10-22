using System.Diagnostics;
using System.Windows.Media;
using MacDock.App.Effects.Derezz;

namespace MacDock.App.Windows;

/// <summary>
/// One render loop drives the dock's derezz into an overlay layer.
/// The layer shares Root's scale so fields work in Root's own coordinates.
/// </summary>
public partial class DockWindow
{
    private const double FrameTime = 1 / 61.0;

    private readonly DerezzLayer _derezzLayer = new();
    private readonly Stopwatch _derezzClock = new();
    private readonly List<(DerezzField, double)> _derezzRuns = new();
    private double _derezzLast;
    private bool _derezzRendering;

    private void InitializeDerezz()
    {
        _derezzLayer.RenderTransformOrigin = Root.RenderTransformOrigin;
        _derezzLayer.RenderTransform = _rootScale;
        Stage.Children.Add(_derezzLayer);
    }

    private void StartDerezzLoop()
    {
        if (_derezzRendering) return;
        _derezzRendering = true;
        _derezzClock.Restart();
        _derezzLast = 0;
        CompositionTarget.Rendering += RenderDerezz;
    }

    private void StopDerezzLoop()
    {
        if (_derezzRendering) CompositionTarget.Rendering -= RenderDerezz;
        _derezzRendering = false;
        _derezzClock.Stop();
        _derezzLayer.Clear();
    }

    private void RenderDerezz(object? sender, EventArgs e)
    {
        double now = _derezzClock.Elapsed.TotalSeconds;
        // Rendering fires at the monitor's rate, 180 Hz on a fast one; the cubes need no more than 60 frames a second,
        // and a frame that changes nothing costs the layered window nothing.
        if (_derezzLast > 0 && now - _derezzLast < FrameTime) return;
        double dt = Math.Min(.05, now - _derezzLast);
        _derezzLast = now;
        _derezzRuns.Clear();
        if (!AdvanceDoze(dt, _derezzRuns))
        {
            StopDerezzLoop();
            return;
        }

        _derezzLayer.Draw(_derezzRuns);
    }
}
