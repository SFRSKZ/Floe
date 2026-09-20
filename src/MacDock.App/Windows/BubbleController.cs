using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

/// <summary>
/// Gives a small glass window the same behaviour as the main dock:
/// a spring scale between the passive (compact) and hovered height, plus
/// a live blurred backdrop captured behind the window (excluding itself).
/// </summary>
public sealed class BubbleController
{
    private const double CompactScale = 0.62;

    private readonly Window _window;
    private readonly Rectangle _backdrop;
    private readonly ImageBrush _brush;
    private readonly ScaleTransform _scale;
    private WriteableBitmap? _bitmap;
    private BackdropCapture? _capture;

    private bool _hooked;
    private long _lastTick;
    private double _current = CompactScale;
    private double _target = CompactScale;
    private double _velocity;
    private IntPtr _hwnd;

    public BubbleController(Window window, Rectangle backdrop, ImageBrush brush, ScaleTransform scale)
    {
        _window = window;
        _backdrop = backdrop;
        _brush = brush;
        _scale = scale;
        _backdrop.Effect = new BlurEffect { Radius = 16, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    }

    public bool Blur { get; set; } = true;

    public bool IsDragging => _dragging;

    public bool Activatable { get; set; }

    public string LayoutId { get; set; } = string.Empty;

    private bool _dragging;
    private Point _dragOffset;

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractive(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var cursor = ScreenInput.GetCursorPosition();
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        _dragOffset = new Point((cursor.X / scale) - _window.Left, (cursor.Y / scale) - _window.Top);
        _dragging = true;
        _window.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var cursor = ScreenInput.GetCursorPosition();
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        _window.Left = (cursor.X / scale) - _dragOffset.X;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _window.ReleaseMouseCapture();
        if (!string.IsNullOrEmpty(LayoutId))
        {
            WidgetLayout.Set(LayoutId, _window.Left, _window.Top);
        }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or TextBoxBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static void Log(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macdock.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} bubble: {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    private string _name = "bubble";

    public void Start()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _name = _window.Title;
        if (!Activatable)
        {
            WindowStyling.MakeNonActivatingToolWindow(_hwnd);
        }
        if (Blur)
        {
            WindowAffinity.ExcludeFromCapture(_hwnd);
        }

        Log($"start {_name} blur={Blur} hwnd={_hwnd}");

        _scale.ScaleX = _current;
        _scale.ScaleY = _current;

        _window.MouseEnter += (_, _) => SetTarget(1);
        _window.MouseLeave += (_, _) => SetTarget(CompactScale);
        _window.SizeChanged += (_, _) => UpdateClip();
        _window.PreviewMouseLeftButtonDown += OnDragStart;
        _window.PreviewMouseMove += OnDragMove;
        _window.PreviewMouseLeftButtonUp += OnDragEnd;

        if (_window.IsLoaded)
        {
            OnLoaded();
        }
        else
        {
            _window.Loaded += (_, _) => OnLoaded();
        }
    }

    private void OnLoaded()
    {
        Log($"loaded {_name}");
        UpdateClip();
        EnsureAnimation();
        if (Blur)
        {
            _capture = new BackdropCapture(_window, PresentBackdrop, () => _window.IsMouseOver || _dragging);
            Log($"capture started {_name}");
        }
    }

    private void SetTarget(double target)
    {
        _target = target;
        EnsureAnimation();
    }

    private void UpdateClip()
    {
        var width = _window.ActualWidth;
        var height = _window.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _backdrop.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 16, 16);
    }

    public void Stop()
    {
        _capture?.Dispose();
        _capture = null;
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }

    private void PresentBackdrop(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _brush.ImageSource = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }

    private void EnsureAnimation()
    {
        if (!_hooked)
        {
            _hooked = true;
            _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = (now - _lastTick) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastTick = now;
        if (dt <= 0 || dt > 0.1)
        {
            dt = 1.0 / 60.0;
        }

        const double stiffness = 260;
        const double damping = 27;
        var accel = (stiffness * (_target - _current)) - (damping * _velocity);
        _velocity += accel * dt;
        _current += _velocity * dt;

        _scale.ScaleX = _current;
        _scale.ScaleY = _current;

        if (Math.Abs(_current - _target) < 0.002 && Math.Abs(_velocity) < 0.02)
        {
            _current = _target;
            _velocity = 0;
            _scale.ScaleX = _current;
            _scale.ScaleY = _current;
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }
}


