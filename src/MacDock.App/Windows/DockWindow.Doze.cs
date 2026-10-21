using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    // After this long without the pointer near it, the dock fades and sinks behind the visualizer.
    private static readonly TimeSpan DozeAfter = TimeSpan.FromSeconds(30);
    private const double DozeOpacity = 0.3;

    private DispatcherTimer? _dozeTimer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _dozing;

    private void InitializeDoze()
    {
        if (_glassPreview)
        {
            return;
        }

        _dozeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _dozeTimer.Tick += (_, _) => DozeTick();
        _dozeTimer.Start();
        Closed += (_, _) => _dozeTimer.Stop();
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
            AnimateDockOpacity(DozeOpacity, 1200);
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
        if (!_dozing)
        {
            return;
        }

        _dozing = false;
        AnimateDockOpacity(1, 180);
        _visualizerWindow?.PlaceBelowDock(_hwnd);
    }

    /// <summary>The visualizer strip re-stacks itself under the dock; while dozing the dock goes under it instead.</summary>
    private void SinkBelowVisualizer()
    {
        if (_dozing && _visualizerWindow is { IsVisible: true } visualizer)
        {
            WindowStyling.PlaceBelow(_hwnd, visualizer.Handle);
        }
    }

    private void AnimateDockOpacity(double target, int ms)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
