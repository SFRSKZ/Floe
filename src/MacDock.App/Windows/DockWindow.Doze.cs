using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MacDock.App.Effects.Glass;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    private static readonly TimeSpan DozeAfter = TimeSpan.FromSeconds(30);
    private DispatcherTimer? _dozeTimer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _dozing;
    private DozeGhostEffect? _dozeGhost;
    private readonly Stopwatch _dozeClock = new();
    private bool _dozeRendering;
    private double _dozeFrom, _dozeTarget, _dozeDuration;

    private void InitializeDoze()
    {
        if (_glassPreview) return;
        // Root has no effect; effects on its children remain part of the live input.
        Root.SizeChanged += (_, _) => UpdateDozeSize();
        DpiChanged += (_, _) => UpdateDozeSize();
        _dozeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _dozeTimer.Tick += (_, _) => DozeTick();
        _dozeTimer.Start();
        Closed += (_, _) =>
        {
            _dozeTimer.Stop();
            StopDozeRendering();
            Root.Effect = null;
        };
    }

    private void DozeTick()
    {
        if (_menuOpen || _settingsOpen || _isDragging || !IsVisible || CursorOverDock())
        {
            WakeDock();
            return;
        }
        if (!_dozing && DateTime.UtcNow - _lastActivity >= DozeAfter)
        {
            _dozing = true;
            TransitionDoze(1.02);
            SinkBelowVisualizer();
        }
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        WakeDock();
    }

    private void WakeDock()
    {
        _lastActivity = DateTime.UtcNow;
        if (!_dozing) return;
        _dozing = false;
        TransitionDoze(0);
        _visualizerWindow?.PlaceBelowDock(_hwnd);
    }

    private void SinkBelowVisualizer()
    {
        if (_dozing && _visualizerWindow is { IsVisible: true } visualizer)
            WindowStyling.PlaceBelow(_hwnd, visualizer.Handle);
    }

    private void UpdateDozeSize()
    {
        if (_dozeGhost is null) return;
        var dpi = VisualTreeHelper.GetDpi(Root);
        _dozeGhost.Size = new Point(Math.Max(1, Root.ActualWidth * dpi.DpiScaleX),
            Math.Max(1, Root.ActualHeight * dpi.DpiScaleY));
    }

    private void TransitionDoze(double target)
    {
        _dozeGhost ??= new DozeGhostEffect();
        // Read the displayed position, including when reversing an unfinished sweep.
        _dozeFrom = _dozeGhost.Line;
        _dozeTarget = target;
        _dozeDuration = (target > _dozeFrom ? 1.1 : .3) * Math.Abs(target - _dozeFrom) / 1.02;
        UpdateDozeSize();
        Root.Effect = _dozeGhost;
        _dozeClock.Restart();
        if (!_dozeRendering)
        {
            CompositionTarget.Rendering += RenderDoze;
            _dozeRendering = true;
        }
    }

    private void RenderDoze(object? sender, EventArgs e)
    {
        if (_dozeGhost is null) return;
        double progress = _dozeDuration <= 0 ? 1 : Math.Clamp(_dozeClock.Elapsed.TotalSeconds / _dozeDuration, 0, 1);
        double eased = progress * progress * (3 - 2 * progress);
        _dozeGhost.Line = _dozeFrom + (_dozeTarget - _dozeFrom) * eased;
        if (progress < 1) return;
        StopDozeRendering();
        if (_dozeTarget == 0) Root.Effect = null;
        // The settled shader stays on the real content. No timer updates Time or Line.
    }

    private void StopDozeRendering()
    {
        if (_dozeRendering) CompositionTarget.Rendering -= RenderDoze;
        _dozeRendering = false;
        _dozeClock.Stop();
    }
}
