using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class VeilWindow : Window
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    // the monitor the veil covers (physical pixels): the one the foreground window is on, not always the primary
    private ScreenInput.Rect _monitor;

    public VeilWindow()
    {
        InitializeComponent();
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        _timer.Tick += (_, _) => UpdateHole();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    public void ShowVeil()
    {
        if (!IsVisible)
        {
            Show();
        }

        UpdateHole();
        _timer.Start();
    }

    public void HideVeil()
    {
        _timer.Stop();
        Hide();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            HideVeil();
        }
        else
        {
            ShowVeil();
        }
    }

    private void UpdateHole()
    {
        var hwnd = ScreenInput.GetForegroundHandle();
        var self = new WindowInteropHelper(this).Handle;
        FollowMonitor(hwnd != self ? hwnd : IntPtr.Zero, self);

        // one monitor, one DPI: physical pixels relative to its corner, scaled once
        var scale = ScreenInput.GetDpiScale(self);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0,
            (_monitor.Right - _monitor.Left) / scale, (_monitor.Bottom - _monitor.Top) / scale)));

        if (hwnd != IntPtr.Zero && hwnd != self && ScreenInput.TryGetWindowRect(hwnd, out var rect) && rect.Right > rect.Left)
        {
            geometry.Children.Add(new RectangleGeometry(new Rect(
                (rect.Left - _monitor.Left) / scale,
                (rect.Top - _monitor.Top) / scale,
                (rect.Right - rect.Left) / scale,
                (rect.Bottom - rect.Top) / scale)));
        }

        Shade.Data = geometry;
    }

    private void FollowMonitor(IntPtr foreground, IntPtr self)
    {
        var monitor = ScreenInput.GetMonitorForWindow(foreground);
        if (!ScreenInput.TryGetMonitorInfo(monitor, out var info)) return;
        var bounds = info.Monitor;
        if (bounds.Left == _monitor.Left && bounds.Top == _monitor.Top && bounds.Right == _monitor.Right && bounds.Bottom == _monitor.Bottom) return;
        _monitor = bounds;
        if (self != IntPtr.Zero)
        {
            SetWindowPos(self, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                SwpNoZOrder | SwpNoActivate);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
