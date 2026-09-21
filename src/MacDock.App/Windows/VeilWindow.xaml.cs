using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class VeilWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

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
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, Width, Height)));

        var hwnd = ScreenInput.GetForegroundHandle();
        var self = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && hwnd != self && ScreenInput.TryGetWindowRect(hwnd, out var rect) && rect.Right > rect.Left)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            geometry.Children.Add(new RectangleGeometry(new Rect(
                rect.Left / dpi.DpiScaleX,
                rect.Top / dpi.DpiScaleY,
                (rect.Right - rect.Left) / dpi.DpiScaleX,
                (rect.Bottom - rect.Top) / dpi.DpiScaleY)));
        }

        Shade.Data = geometry;
    }
}
