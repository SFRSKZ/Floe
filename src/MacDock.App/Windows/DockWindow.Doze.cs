using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MacDock.App.Effects.Derezz;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class DockWindow
{
    private static readonly TimeSpan DozeAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AwakePoll = TimeSpan.FromSeconds(1);
    // A derezzed dock is fully transparent and gets no mouse input, so the cursor is polled instead.
    private static readonly TimeSpan DozingPoll = TimeSpan.FromMilliseconds(100);
    // DWM needs a couple of composed frames before a window it excluded from capture shows up in a grab.
    private static readonly TimeSpan RevealSettle = TimeSpan.FromMilliseconds(120);
    private const double RezzSpeed = 1.8;
    private const double RewindSpeed = 2.2;

    private DispatcherTimer? _dozeTimer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _dozing;
    private bool _dozeHidden;
    private BitmapSource? _dozeSnapshot;
    private DerezzField? _dozeField;
    private double _dozeTime;
    private int _dozeDirection;
    private bool _dozeRezz;
    // A derezz the settings asked for: it rezzes straight back instead of waiting for the cursor.
    private bool _dozePreview;

    private void InitializeDoze()
    {
        if (_glassPreview) return;
        InitializeDerezz();
        _dozeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AwakePoll };
        _dozeTimer.Tick += (_, _) => DozeTick();
        _dozeTimer.Start();
        AuraSettings.DerezzPreviewRequested += PreviewDoze;
        Closed += (_, _) =>
        {
            AuraSettings.DerezzPreviewRequested -= PreviewDoze;
            _dozeTimer.Stop();
            StopDerezzLoop();
        };
    }

    private void DozeTick()
    {
        if (_menuOpen || _settingsOpen || _isDragging || !IsVisible || CursorOverDock())
        {
            WakeDock();
            return;
        }
        if (!AuraSettings.Derezz && !_dozing)
        {
            _lastActivity = DateTime.UtcNow;
            return;
        }
        // An autohidden dock has nothing on screen to derezz; the grab would catch whatever covers it.
        if (!_shown && !_dozing)
        {
            _lastActivity = DateTime.UtcNow;
            return;
        }
        if (!_dozing && DateTime.UtcNow - _lastActivity >= DozeAfter)
        {
            _dozing = true;
            _dozeTimer!.Interval = DozingPoll;
            Derezz();
            SinkBelowVisualizer();
        }
    }

    private void PreviewDoze()
    {
        if (_dozing || _dozePreview || _dozeHidden || _dozeField is not null || !IsVisible || !_shown) return;
        _dozePreview = true;
        Derezz();
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
        _dozeTimer!.Interval = AwakePoll;
        Rezz();
        _visualizerWindow?.PlaceBelowDock(_hwnd);
    }

    private void SinkBelowVisualizer()
    {
        if (_dozing && _visualizerWindow is { IsVisible: true } visualizer)
            WindowStyling.PlaceBelow(_hwnd, visualizer.Handle);
    }

    private Rect CapsuleRect() => new(_capsuleLeft, RestTop, _capsuleWidth, RestBarHeight);

    // The dock breaks into falling cubes and is gone where each one leaves.
    // The dock is excluded from screen capture, so it is revealed for a few frames before the grab.
    private async void Derezz()
    {
        CaptureReveal.Reveal(TimeSpan.FromMilliseconds(400));
        await Task.Delay(RevealSettle);
        if (!_dozing && !_dozePreview) return;

        var rect = CapsuleRect();
        var topLeft = Root.PointToScreen(rect.TopLeft);
        var bottomRight = Root.PointToScreen(rect.BottomRight);
        var area = new Int32Rect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
            (int)Math.Round(bottomRight.X - topLeft.X), (int)Math.Round(bottomRight.Y - topLeft.Y));
        _dozeSnapshot = ScreenGrab.Capture(area, CornerRadius * area.Width / Math.Max(1, rect.Width));
        if (_dozeSnapshot is null)
        {
            if (_dozePreview) _dozePreview = false;
            else HideDozedDock();
            return;
        }

        _dozeField = new DerezzField(_dozeSnapshot, rect, AuraSettings.DerezzCell, new Point(rect.X + (rect.Width / 2), rect.Bottom), WindowHeight, rezz: false,
            speed: AuraSettings.DerezzSpeed)
        {
            DrawIntact = false,
        };
        _dozeTime = 0;
        _dozeDirection = 1;
        _dozeRezz = false;
        StartDerezzLoop();
    }

    // Waking during the derezz rewinds it; waking a hidden dock rezzes it back from under the cursor.
    private void Rezz()
    {
        if (_dozeField is not null && !_dozeRezz && _dozeDirection == 1)
        {
            _dozeDirection = -1;
            return;
        }

        if (!_dozeHidden) return;
        _dozeHidden = false;
        Root.Opacity = 1;
        if (_dozeSnapshot is null)
        {
            Root.OpacityMask = null;
            return;
        }

        var rect = CapsuleRect();
        var (cursorX, _) = ScreenInput.GetCursorPosition();
        double x = Math.Clamp(Root.PointFromScreen(new Point(cursorX, 0)).X, rect.Left, rect.Right);
        _dozeField = new DerezzField(_dozeSnapshot, rect, AuraSettings.DerezzCell, new Point(x, rect.Bottom), WindowHeight, rezz: true,
            speed: RezzSpeed * AuraSettings.DerezzSpeed, seed: Environment.TickCount & 0xFF);
        _dozeTime = 0;
        _dozeDirection = 1;
        _dozeRezz = true;
        Root.OpacityMask = RootMask(_dozeField, 0, 0);
        StartDerezzLoop();
    }

    private void HideDozedDock()
    {
        _dozeHidden = true;
        Root.Opacity = 0;
        Root.OpacityMask = null;
    }

    /// <summary>Advances the dock's own derezz; false once there is nothing left to draw.</summary>
    private bool AdvanceDoze(double dt, List<(DerezzField, double)> runs)
    {
        if (_dozeField is not { } field) return false;
        _dozeTime += dt * (_dozeDirection < 0 ? -RewindSpeed : 1);
        bool rezz = _dozeRezz;
        if (_dozeDirection > 0 && _dozeTime >= field.Duration)
        {
            _dozeField = null;
            if (rezz)
            {
                Root.OpacityMask = null;
                _dozePreview = false;
                return false;
            }

            HideDozedDock();
            if (_dozePreview)
            {
                Rezz();
                return _dozeField is not null;
            }

            return false;
        }

        if (_dozeDirection < 0 && _dozeTime <= 0)
        {
            _dozeField = null;
            Root.OpacityMask = null;
            return false;
        }

        // The rim and shadow outside the cells go with the cells, not with the cubes still falling.
        Root.OpacityMask = RootMask(field, _dozeTime, field.ShownShare(_dozeTime));
        runs.Add((field, _dozeTime));
        return true;
    }

    // Shows the live dock only in the cells still in place; the shadow around the capsule fades with them.
    private DrawingBrush RootMask(DerezzField field, double time, double outside)
    {
        var bounds = new Rect(0, 0, Math.Max(1, Root.ActualWidth), Math.Max(1, Root.ActualHeight));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(bounds)));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, field.ShownRegion(time)));
        if (outside > .004)
        {
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb((byte)(255 * outside), 255, 255, 255)),
                null, field.OutsideRegion(bounds)));
        }

        var brush = new DrawingBrush(group)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = bounds,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = bounds,
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }
}
