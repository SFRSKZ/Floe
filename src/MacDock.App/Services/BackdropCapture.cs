using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MacDock.Core.Interop;

namespace MacDock.App.Services;

/// <summary>Captures at native resolution, publishing only changed frames to the UI.</summary>
internal sealed class BackdropCapture : IDisposable
{
    private const int ActiveIntervalMs = 66;
    private const int IdleIntervalMs = 250;
    private const int IdlePausedIntervalMs = 600;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Action<byte[], int, int, int> _present;
    private readonly Func<bool>? _isActive;
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
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
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).Handle;
        _present = present;
        _isActive = isActive;
        _presentLatest = PresentLatest;
        _visible = window.IsVisible;
        window.IsVisibleChanged += OnVisibilityChanged;
        window.Closed += OnClosed;
        new Thread(CaptureLoop) { IsBackground = true, Name = "MacDockBackdrop" }.Start();
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
            while (!_disposed)
            {
                if (_visible && ScreenInput.TryGetWindowRect(_hwnd, out var rect))
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    var pixels = capture.Capture(rect.Left, rect.Top, width, height, out var stride, out var changed);
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

                int delay;
                if (!_visible)
                {
                    delay = Timeout.Infinite;
                }
                else if (_isActive?.Invoke() ?? true)
                {
                    delay = ActiveIntervalMs;
                }
                else
                {
                    delay = ScreenInput.GetIdleMilliseconds() > 2000 ? IdlePausedIntervalMs : IdleIntervalMs;
                }

                _wake.WaitOne(delay);
            }
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
        _window.Closed -= OnClosed;
    }
}
