using System.Windows;
using System.Windows.Media;
using Floe.App.Effects.Derezz;
using Floe.App.Services;
using Floe.Core.Interop;
using Floe.Core.Models;

namespace Floe.App.Windows;

/// <summary>
/// The particle style of the sleep and the autohide: the dock flows away as a river of points and gathers back
/// from it. One field, played forwards to leave and backwards to come back.
/// </summary>
public partial class DockWindow
{
    private const double ParticleWakeRate = 3.0;
    private const double ParticleShowRate = 4.0;
    private const double ParticleHideRate = 1.4;

    // the points flow in their own window, out past the dock window's right edge to the screen's
    private ParticleOverlayWindow? _particleOverlay;
    private ParticleDissolve? _particleRun;
    // the last field grabbed: a hidden dock gathers back from it
    private ParticleDissolve? _particleField;
    private double _particleTime;
    private double _particleRate;
    private int _particleDirection;
    private Action? _particleEnd;
    private bool _particleHooked;
    private double _particleLast = -1;
    // an autohide is dissolving the dock (grabbing or playing), or has hidden it that way
    private bool _particleHiding;
    private bool _particleHidden;

    // the glass dock's style only: the CP77 dock sleeps with its relic failure
#if CP77
    private static bool ParticlesOn => false;
#else
    private static bool ParticlesOn => AuraSettings.DerezzParticles && !DozeAsFade;
#endif

    private void InitializeParticles()
    {
        Closed += (_, _) =>
        {
            StopParticles();
            _particleOverlay?.Close();
            _particleOverlay = null;
        };
    }

    private async Task<ParticleDissolve?> GrabParticles()
    {
        CaptureReveal.Reveal(TimeSpan.FromMilliseconds(400));
        await Task.Delay(RevealSettle);
        var rect = CapsuleRect();
        var topLeft = Root.PointToScreen(rect.TopLeft);
        var bottomRight = Root.PointToScreen(rect.BottomRight);
        var area = new Int32Rect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
        double corner = GlassSettings.Cyber ? 0 : CornerRadius * area.Width / Math.Max(1, rect.Width);
        var snapshot = ScreenGrab.Capture(area, corner);
        return snapshot is null ? null : new ParticleDissolve(snapshot, rect, Environment.TickCount & 0xFFFF);
    }

    // ---- sleep ----

    private async void ParticleSleep()
    {
        CancelFxSettle(restoreArtwork: true);
        var field = await GrabParticles();
        if (!_dozing && !_dozePreview) return;
        if (field is null)
        {
            if (_dozePreview) _dozePreview = false;
            else HideDozedDock();
            return;
        }

        _particleField = field;
        PlayParticles(field, 0, 1, AuraSettings.DerezzSpeed, () =>
        {
            HideDozedDock();
            SinkBelowVisualizer();
            if (_dozePreview) ParticleWake();
        });
    }

    private void ParticleWake()
    {
        if (_particleRun is not null)
        {
            if (_particleDirection > 0) TurnParticles(ParticleWakeRate * AuraSettings.DerezzSpeed, EndParticleWake);
            return;
        }

        if (!_dozeHidden) return;
        _dozeHidden = false;
        Root.Opacity = 1;
        if (_particleField is null)
        {
            EndParticleWake();
            return;
        }

        PlayParticles(_particleField, ParticleDissolve.Duration, -1, ParticleWakeRate * AuraSettings.DerezzSpeed, EndParticleWake);
    }

    private void EndParticleWake()
    {
        Root.OpacityMask = null;
        _dozePreview = false;
    }

    // ---- autohide ----

    /// <summary>Hides the dock by dissolving it; false when the slide should do it instead.</summary>
    private bool ParticleHide()
    {
        if (!ParticlesOn || _dozing || _dozeHidden || _particleRun is not null || _particleHiding || DockSettings.Edge != DockEdge.Bottom) return false;
        _particleHiding = true;
        _ = DissolveAway();
        return true;
    }

    private async Task DissolveAway()
    {
        CancelFxSettle(restoreArtwork: true);
        var field = await GrabParticles();
        if (_shown || !_particleHiding)
        {
            // it came back while the picture was taken: nothing moved yet
            _particleHiding = false;
            return;
        }

        if (field is null)
        {
            _particleHiding = false;
            AnimateDockToEdge(shown: false);
            return;
        }

        _particleField = field;
        PlayParticles(field, 0, 1, ParticleHideRate * AuraSettings.DerezzSpeed, () =>
        {
            _particleHiding = false;
            _particleHidden = true;
            Root.Opacity = 0;
            Root.OpacityMask = null;
            SnapToEdge(shown: false);
        });
    }

    /// <summary>Shows a dock the particles hid or are hiding; false when the slide should bring it back.</summary>
    private bool ParticleShow()
    {
        if (_particleHiding)
        {
            _particleHiding = false;
            // still grabbing: the dock never left
            if (_particleRun is null) return true;
            TurnParticles(ParticleShowRate * AuraSettings.DerezzSpeed, () => Root.OpacityMask = null);
            return true;
        }

        if (!_particleHidden) return false;
        _particleHidden = false;
        SnapToEdge(shown: true);
        Root.Opacity = 1;
        if (_particleField is null || !ParticlesOn)
        {
            Root.OpacityMask = null;
            return true;
        }

        PlayParticles(_particleField, ParticleDissolve.Duration, -1, ParticleShowRate * AuraSettings.DerezzSpeed, () => Root.OpacityMask = null);
        return true;
    }

    private void SnapToEdge(bool shown)
    {
        var horizontal = DockSettings.Edge is DockEdge.Bottom or DockEdge.Top;
        var property = horizontal ? TopProperty : LeftProperty;
        BeginAnimation(property, null);
        SetValue(property, EdgeOffset(shown));
    }

    // ---- the run ----

    private void PlayParticles(ParticleDissolve field, double from, int direction, double rate, Action end)
    {
        _particleRun = field;
        _particleTime = from;
        _particleDirection = direction;
        _particleRate = rate;
        _particleEnd = end;
        Root.OpacityMask = ParticleMask(field, from);
        CoverParticles();
        if (_particleHooked) return;
        _particleHooked = true;
        _particleLast = -1;
        Effects.Visualizer.VisualizerPacing.DockAnimating = true;
        CompositionTarget.Rendering += RenderParticles;
    }

    // plays the running field the other way from where it is now
    private void TurnParticles(double rate, Action end)
    {
        _particleDirection = -_particleDirection;
        _particleRate = rate;
        _particleEnd = end;
    }

    private void StopParticles()
    {
        if (_particleHooked) CompositionTarget.Rendering -= RenderParticles;
        if (_particleHooked) Effects.Visualizer.VisualizerPacing.DockAnimating = _renderingHooked;
        _particleHooked = false;
        _particleRun = null;
        _particleEnd = null;
        if (_particleOverlay is not null)
        {
            _particleOverlay.Layer.Clear();
            _particleOverlay.Hide();
        }
    }

    private void RenderParticles(object? sender, EventArgs e)
    {
        if (_particleRun is not { } field)
        {
            StopParticles();
            return;
        }

        double stamp = e is RenderingEventArgs args ? args.RenderingTime.TotalSeconds : Environment.TickCount64 / 1000.0;
        if (stamp <= _particleLast) return;
        double dt = _particleLast < 0 ? 0 : Math.Min(.05, stamp - _particleLast);
        _particleLast = stamp;
        _particleTime += dt * _particleRate * _particleDirection;
        if ((_particleDirection > 0 && _particleTime >= ParticleDissolve.Duration) || (_particleDirection < 0 && _particleTime <= 0))
        {
            var end = _particleEnd;
            StopParticles();
            end?.Invoke();
            return;
        }

        Root.OpacityMask = ParticleMask(field, _particleTime);
        _particleOverlay?.Layer.Draw(field, _particleTime);
    }

    // lays the overlay over the dock window, stretched to the monitor's right edge
    private void CoverParticles()
    {
        _particleOverlay ??= new ParticleOverlayWindow();
        double right = Left + ActualWidth;
        var monitor = _monitorInfo.Monitor;
        if (monitor.Right > monitor.Left) right = Math.Max(right, monitor.Right / DockScale);
        _particleOverlay.Cover(Left, Top, right, ActualHeight);
        // the dock's own scale about the same point, so the points leave from where the icons stand
        var origin = Root.RenderTransformOrigin;
        _particleOverlay.Layer.RenderTransform = new ScaleTransform(_rootScale.ScaleX, _rootScale.ScaleY,
            origin.X * Root.ActualWidth, origin.Y * Root.ActualHeight);
    }

    // the live dock only to the right of the front; everything it has passed is points now
    private Brush ParticleMask(ParticleDissolve field, double time)
    {
        double width = Math.Max(1, Root.ActualWidth);
        double at = Math.Clamp(field.FrontX(time) / width, 0, 1);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, at));
        brush.GradientStops.Add(new GradientStop(Colors.White, at));
        brush.Freeze();
        return brush;
    }
}
