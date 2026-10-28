using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// An icon's right-click menu: a plain list on the dock's liquid glass, standing above the icon that opened it.
/// It never takes focus; it closes when the cursor wanders off it (the way from the icon to it keeps it open)
/// or when a row is clicked.
/// </summary>
internal sealed class IconMenuWindow : Window
{
    // room round the glass for its shadow
    private const double Room = 12;
    private const double AnchorReach = 44;

    private static readonly Brush RowHover = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Text = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush DimText = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush DangerText = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0x8A, 0x80)));
    private static readonly Brush Line = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));

    private readonly StackPanel _rows = new() { Margin = new Thickness(5) };
    private readonly GlassPanel _glass;
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private DateTime _shownAt;
    // where the cursor was when the menu opened (the icon), in DIP
    private Point _anchor;

    public event Action? Dismissed;

    /// <summary>For the screenshot: the cursor is elsewhere, and the list stays up anyway.</summary>
    internal bool KeepOpen { get; init; }

    public IconMenuWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Floe Menu";

        // a lighter shade than the settings panel: the list is short and the glass should read as glass
        _glass = new GlassPanel { CornerRadius = 12, ReadingShade = .25, MinWidth = 168 };
        _glass.Children.Add(_rows);
        Content = new Border
        {
            Margin = new Thickness(Room),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = .35, Color = Colors.Black },
            Child = _glass,
        };

        _watchdog.Tick += (_, _) => CheckCursor();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Fills the list and shows it centred over <paramref name="centerX"/> with its glass ending
    /// <paramref name="bottom"/> (screen DIP), kept inside <paramref name="work"/>.
    /// </summary>
    public void ShowFor(string title, ImageSource? icon, IReadOnlyList<(string Label, Action Run)> items, double centerX, double bottom, Rect work)
    {
        _rows.Children.Clear();
        _rows.Children.Add(Header(title, icon));
        _rows.Children.Add(Divider());
        for (var i = 0; i < items.Count; i++)
        {
            // the last action ends the app: set apart and in red
            bool last = i == items.Count - 1 && items.Count > 2;
            if (last) _rows.Children.Add(Divider());
            _rows.Children.Add(Row(items[i], last));
        }

        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        Left = Math.Clamp(centerX - (size.Width / 2), work.Left - Room, Math.Max(work.Left - Room, work.Right - size.Width + Room));
        Top = Math.Max(work.Top - Room, bottom - size.Height + Room);

        Show();
        var (anchorX, anchorY) = ScreenInput.GetCursorPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        _anchor = new Point(anchorX / dpi.DpiScaleX, anchorY / dpi.DpiScaleY);
        _shownAt = DateTime.UtcNow;
        if (!KeepOpen) _watchdog.Start();
    }

    private void Dismiss()
    {
        _watchdog.Stop();
        Hide();
        _rows.Children.Clear();
        Dismissed?.Invoke();
    }

    private void CheckCursor()
    {
        if (!IsVisible)
        {
            _watchdog.Stop();
            return;
        }

        // grace period: the cursor is still on the icon right after opening
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 220) return;

        var (cursorX, cursorY) = ScreenInput.GetCursorPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = cursorX / dpi.DpiScaleX;
        var y = cursorY / dpi.DpiScaleY;
        // the glass, with a little slack round it
        var glass = new Rect(Left + Room - 10, Top + Room - 10, ActualWidth - (2 * Room) + 20, ActualHeight - (2 * Room) + 20);
        if (glass.Contains(x, y)) return;

        // resting on the icon, or on the way from it up to the list, keeps it open
        var ax = x - _anchor.X;
        var ay = y - _anchor.Y;
        if (Math.Sqrt((ax * ax) + (ay * ay)) <= AnchorReach) return;
        if (x >= glass.Left && x <= glass.Right && y >= Math.Min(glass.Bottom, _anchor.Y) && y <= Math.Max(glass.Bottom, _anchor.Y)) return;

        Dismiss();
    }

    private UIElement Row((string Label, Action Run) action, bool danger)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = action.Label,
                Foreground = danger ? DangerText : Text,
                FontSize = 13,
                FontFamily = new FontFamily("Segoe UI"),
            },
        };
        row.MouseEnter += (_, _) => row.Background = RowHover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            Dismiss();
            action.Run();
        };
        return row;
    }

    private static UIElement Header(string title, ImageSource? icon)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 12, 6) };
        if (icon is not null)
        {
            panel.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = DimText,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 220,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    private static UIElement Divider() => new Border { Height = 1, Background = Line, Margin = new Thickness(8, 4, 8, 4) };

    protected override void OnClosed(EventArgs e)
    {
        _watchdog.Stop();
        base.OnClosed(e);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
