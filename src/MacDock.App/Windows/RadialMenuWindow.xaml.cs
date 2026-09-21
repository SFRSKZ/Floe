using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class RadialMenuWindow : Window
{
    private const double Size = 380;
    private const double CenterX = Size / 2;
    private const double CenterY = Size / 2;
    private const double Inner = 46;
    private const double Outer = 134;

    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public event Action? Dismissed;

    private void Dismiss()
    {
        _watchdog.Stop();
        Hide();
        Dismissed?.Invoke();
    }

    public RadialMenuWindow()
    {
        InitializeComponent();
        Width = Size;
        Height = Size;
        _watchdog.Tick += (_, _) => CheckCursor();
    }

    private void CheckCursor()
    {
        if (!IsVisible)
        {
            _watchdog.Stop();
            return;
        }

        // grace period: the cursor is still on the icon right after opening
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 220)
        {
            return;
        }

        var (cursorX, cursorY) = ScreenInput.GetCursorPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (cursorX / dpi.DpiScaleX) - (Left + CenterX);
        var dy = (cursorY / dpi.DpiScaleY) - (Top + CenterY);

        if (Math.Sqrt((dx * dx) + (dy * dy)) > Outer + 16)
        {
            Dismiss();
        }
    }

    public static double Radius => Size / 2;

    public static double HitRadius => Outer;

    private DateTime _shownAt;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
    }

    public void ShowFor(ImageSource? icon, IReadOnlyList<(string Label, Action Run)> items)
    {
        Root.Children.Clear();
        Root.ReleaseMouseCapture();

        var count = Math.Max(1, items.Count);
        var step = 360.0 / count;
        var start = -90.0 - (step / 2);

        for (var i = 0; i < items.Count; i++)
        {
            var angle0 = start + (i * step) + 2;
            var angle1 = start + ((i + 1) * step) - 2;
            var slice = new Path
            {
                Data = PieSlice(angle0, angle1),
                Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1C)),
                Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
            };

            var action = items[i];
            slice.MouseEnter += (_, _) => slice.Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0x24, 0x2A, 0x38));
            slice.MouseLeave += (_, _) => slice.Fill = new SolidColorBrush(Color.FromArgb(0xE6, 0x14, 0x16, 0x1C));
            slice.MouseLeftButtonUp += (_, _) =>
            {
                Dismiss();
                action.Run();
            };

            Root.Children.Add(slice);

            var mid = ((angle0 + angle1) / 2) * Math.PI / 180.0;
            var radius = (Inner + Outer) / 2;
            var label = new TextBlock
            {
                Text = action.Label,
                Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                TextAlignment = TextAlignment.Center,
                Width = 96,
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, CenterX + (Math.Cos(mid) * radius) - (label.DesiredSize.Width / 2));
            Canvas.SetTop(label, CenterY + (Math.Sin(mid) * radius) - (label.DesiredSize.Height / 2));
            Root.Children.Add(label);
        }

        var hub = new Ellipse
        {
            Width = (Inner * 2) - 6,
            Height = (Inner * 2) - 6,
            Fill = new SolidColorBrush(Color.FromArgb(0xF2, 0x0E, 0x10, 0x14)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(hub, CenterX - (hub.Width / 2));
        Canvas.SetTop(hub, CenterY - (hub.Height / 2));
        Root.Children.Add(hub);

        if (icon is not null)
        {
            var image = new Image { Source = icon, Width = 40, Height = 40, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            Canvas.SetLeft(image, CenterX - 20);
            Canvas.SetTop(image, CenterY - 20);
            Root.Children.Add(image);
        }

        Show();
        UpdateLayout();
        _shownAt = DateTime.UtcNow;
        _watchdog.Start();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        var position = e.GetPosition(Root);
        var dx = position.X - CenterX;
        var dy = position.Y - CenterY;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (position.X < 0 || position.Y < 0 || position.X > Size || position.Y > Size || distance < Inner || distance > Outer)
        {
            Dismiss();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Dismiss();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _watchdog.Stop();
        if (Mouse.Captured == Root)
        {
            Root.ReleaseMouseCapture();
        }

        base.OnClosed(e);
    }

    private static Geometry PieSlice(double angle0, double angle1)
    {
        var p0 = Polar(angle0, Inner);
        var p1 = Polar(angle0, Outer);
        var p2 = Polar(angle1, Outer);
        var p3 = Polar(angle1, Inner);
        var large = Math.Abs(angle1 - angle0) > 180;

        var figure = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new ArcSegment(p2, new Size(Outer, Outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p3, true));
        figure.Segments.Add(new ArcSegment(p0, new Size(Inner, Inner), 0, large, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Point Polar(double degrees, double radius)
    {
        var radians = degrees * Math.PI / 180.0;
        return new Point(CenterX + (Math.Cos(radians) * radius), CenterY + (Math.Sin(radians) * radius));
    }
}

