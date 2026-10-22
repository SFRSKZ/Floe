using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Services;

/// <summary>Captures at quarter resolution, publishing only changed frames to the UI.</summary>
internal sealed class BackdropCapture : IDisposable
{
    private static readonly object FreezeGate = new();
    // Signalled while not frozen, so a frozen worker sleeps instead of polling every tick.
    private static readonly ManualResetEvent Unfrozen = new(true);
    private static bool _frozen;
    public static bool Frozen
    {
        get { lock (FreezeGate) return _frozen; }
        set
        {
            lock (FreezeGate)
            {
                _frozen = value;
                if (value)
                {
                    Unfrozen.Reset();
                }
                else
                {
                    Unfrozen.Set();
                }
            }
        }
    }

    private const int ActiveIntervalMs = 33;
    private const int IdleIntervalMs = 250;
    private const int IdlePausedIntervalMs = 600;

    private readonly FrameworkElement _window;
    private readonly IntPtr _hwnd;
    private readonly Action<byte[], int, int, int> _present;
    private readonly Func<bool>? _isActive;
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly WaitHandle[] _waits;
    private readonly Action _presentLatest;
    private volatile bool _visible;
    private volatile bool _disposed;
    private bool _workerStopped;
    private bool _pending;
    private byte[]? _pixels;
    private int _width;
    private int _height;
    private int _stride;

    public BackdropCapture(Window window, Action<byte[], int, int, int> present, Func<bool>? isActive = null)
        : this(window, new WindowInteropHelper(window).Handle, present, isActive)
    {
        window.Closed += OnClosed;
    }

    /// <summary>Captures behind <paramref name="hwnd"/> (a window or a popup) while <paramref name="owner"/> is shown.</summary>
    public BackdropCapture(FrameworkElement owner, IntPtr hwnd, Action<byte[], int, int, int> present, Func<bool>? isActive = null)
    {
        _window = owner;
        _hwnd = hwnd;
        _present = present;
        _isActive = isActive;
        _presentLatest = PresentLatest;
        _waits = new WaitHandle[] { _wake, Unfrozen };
        _visible = owner.IsVisible;
        owner.IsVisibleChanged += OnVisibilityChanged;
        new Thread(CaptureLoop) { IsBackground = true, Name = "FloeBackdrop" }.Start();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        lock (_sync)
        {
            _visible = _window.IsVisible;
            if (!_workerStopped)
            {
                _wake.Set();
            }
        }
    }

    private void CaptureLoop()
    {
        try
        {
            using var capture = new ScreenCapture.Session();
            int unchangedFrames = 0;
            while (!_disposed)
            {
                bool frozen = false;
                try
                {
                    lock (FreezeGate)
                    {
                        frozen = _frozen;
                        if (!frozen && _visible && ScreenInput.TryGetWindowRect(_hwnd, out var rect))
                        {
                            var sourceWidth = rect.Right - rect.Left;
                            var sourceHeight = rect.Bottom - rect.Top;
                            var width = Math.Max(1, (sourceWidth + 3) / 4);
                            var height = Math.Max(1, (sourceHeight + 3) / 4);
                            var pixels = capture.CaptureScaled(rect.Left, rect.Top, sourceWidth, sourceHeight, width, height, out var stride, out var changed);
                            unchangedFrames = changed ? 0 : Math.Min(unchangedFrames + 1, 4);
                            if (pixels is not null && changed)
                            {
                                lock (_sync)
                                {
                                    if (_disposed)
                                    {
                                        break;
                                    }

                                    if (_pixels is null || _pixels.Length != pixels.Length)
                                    {
                                        _pixels = new byte[pixels.Length];
                                    }

                                    // Separate storage keeps the capture thread from overwriting a frame
                                    // while WritePixels reads it. At most one UI callback is queued.
                                    pixels.CopyTo(_pixels, 0);
                                    _width = width;
                                    _height = height;
                                    _stride = stride;
                                    if (!_pending && !_window.Dispatcher.HasShutdownStarted)
                                    {
                                        _pending = true;
                                        _window.Dispatcher.BeginInvoke(DispatcherPriority.Render, _presentLatest);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    unchangedFrames = 4;
                    // A failed grab (session lock, RDP, display-mode change) or a dispatcher
                    // that is shutting down must not kill the process; retry next tick.
                }

                if (frozen)
                {
                    // Frozen popups are part of the capture, so the grab is paused; sleep until
                    // the unfreeze signal or a visibility/dispose wake instead of polling.
                    WaitHandle.WaitAny(_waits);
                    continue;
                }

                int delay;
                var active = _isActive?.Invoke() ?? false;
                if (!_visible)
                {
                    delay = Timeout.Infinite;
                }
                else if (active && unchangedFrames < 4)
                {
                    delay = ActiveIntervalMs;
                }
                else
                {
                    // A settled dock can still see changes (video, cursor) but 30 Hz is wasted
                    // work there; the idle cadence keeps it fresh and only active frames run hot.
                    delay = !active && ScreenInput.GetIdleMilliseconds() > 2000 ? IdlePausedIntervalMs : IdleIntervalMs;
                }

                _wake.WaitOne(delay);
            }
        }
        catch (Exception)
        {
            // Background thread: an unhandled exception here would terminate the dock.
        }
        finally
        {
            lock (_sync)
            {
                _workerStopped = true;
                _wake.Dispose();
                _pixels = null;
            }
        }
    }

    private void PresentLatest()
    {
        lock (_sync)
        {
            _pending = false;
            if (!_disposed && _pixels is not null)
            {
                _present(_pixels, _width, _height, _stride);
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_workerStopped)
            {
                _wake.Set();
            }
        }

        _window.IsVisibleChanged -= OnVisibilityChanged;
        if (_window is Window window)
        {
            window.Closed -= OnClosed;
        }
    }
}
