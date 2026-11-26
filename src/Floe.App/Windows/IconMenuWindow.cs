using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.Core.Interop;
using Floe.Core.Models;

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
    // the app's own Jump List, above Floe's actions; it scrolls when it is longer than the screen allows
    private readonly StackPanel _jump = new();
    private readonly ScrollViewer _jumpScroll;
    private IReadOnlyList<(string Label, Action Run)> _items = Array.Empty<(string, Action)>();
    private string _title = "";
    private ImageSource? _icon;
    private Point _placeAnchor;
    private Rect _work;
    private readonly GlassPanel _glass;
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private DateTime _shownAt;
    // where the cursor was when the menu opened (the icon), in DIP
    private Point _anchor;
    private DockEdge _edge;

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

        _jumpScroll = new ScrollViewer
        {
            Content = _jump,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
        };
        _watchdog.Tick += (_, _) => CheckCursor();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Fills the list and opens it beside the selected dock edge, kept inside <paramref name="work"/>.
    /// </summary>
    public void ShowFor(string title, ImageSource? icon, IReadOnlyList<(string Label, Action Run)> items, Point anchor, DockEdge edge, Rect work)
    {
        _title = title;
        _icon = icon;
        _items = items;
        _edge = edge;
        _placeAnchor = anchor;
        _work = work;
        _jump.Children.Clear();
        Build();
        Place();

        Show();
        var (anchorX, anchorY) = ScreenInput.GetCursorPosition();
        var dpi = VisualTreeHelper.GetDpi(this);
        _anchor = new Point(anchorX / dpi.DpiScaleX, anchorY / dpi.DpiScaleY);
        _shownAt = DateTime.UtcNow;
        if (!KeepOpen) _watchdog.Start();
    }

    /// <summary>Puts the app's Jump List above the actions of the open menu, which keeps its place by the icon.</summary>
    public void SetJumpList(IReadOnlyList<JumpGroup> groups, Action<int> run)
    {
        if (!IsVisible || groups.Count == 0) return;
        // the app's own "New window" stands in for Floe's
        var titles = new HashSet<string>(groups.SelectMany(g => g.Entries).Select(e => e.Title), StringComparer.CurrentCultureIgnoreCase);
        _items = _items.Where((item, i) => i == _items.Count - 1 || !titles.Contains(item.Label)).ToList();
        _jump.Children.Clear();
        foreach (var group in groups)
        {
            _jump.Children.Add(GroupTitle(group.Title));
            foreach (var entry in group.Entries)
            {
                var id = entry.Id;
                _jump.Children.Add(Row(entry.Title, entry.Icon, () => run(id), danger: false));
            }
        }

        Build();
        Place();
    }

    private void Build()
    {
        _rows.Children.Clear();
        _rows.Children.Add(Header(_title, _icon));
        _rows.Children.Add(Divider());
        if (_jump.Children.Count > 0)
        {
            ((Panel?)_jumpScroll.Parent)?.Children.Remove(_jumpScroll);
            _jumpScroll.MaxHeight = Math.Max(160, _work.Height - 260);
            _rows.Children.Add(_jumpScroll);
            _rows.Children.Add(Divider());
        }

        for (var i = 0; i < _items.Count; i++)
        {
            // the last action ends the app: set apart and in red
            bool last = i == _items.Count - 1 && _items.Count > 2;
            if (last) _rows.Children.Add(Divider());
            var item = _items[i];
            _rows.Children.Add(Row(item.Label, null, item.Run, last));
        }
    }

    private void Place()
    {
        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = content.DesiredSize;
        var anchor = _placeAnchor;
        var work = _work;
        var left = _edge switch
        {
            DockEdge.Left => anchor.X + 8 - Room,
            DockEdge.Right => anchor.X - size.Width - 8 + Room,
            _ => anchor.X - (size.Width / 2),
        };
        var top = _edge switch
        {
            DockEdge.Bottom => anchor.Y - size.Height - 8 + Room,
            DockEdge.Top => anchor.Y + 8 - Room,
            _ => anchor.Y - (size.Height / 2),
        };
        Left = Math.Clamp(left, work.Left - Room, Math.Max(work.Left - Room, work.Right - size.Width + Room));
        Top = Math.Clamp(top, work.Top - Room, Math.Max(work.Top - Room, work.Bottom - size.Height + Room));
    }

    private void Dismiss()
    {
        _watchdog.Stop();
        Hide();
        _rows.Children.Clear();
        _jump.Children.Clear();
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
        var corridor = _edge switch
        {
            DockEdge.Top => x >= glass.Left && x <= glass.Right && y >= Math.Min(glass.Top, _anchor.Y) && y <= Math.Max(glass.Top, _anchor.Y),
            DockEdge.Left => y >= glass.Top && y <= glass.Bottom && x >= Math.Min(glass.Right, _anchor.X) && x <= Math.Max(glass.Right, _anchor.X),
            DockEdge.Right => y >= glass.Top && y <= glass.Bottom && x >= Math.Min(glass.Left, _anchor.X) && x <= Math.Max(glass.Left, _anchor.X),
            _ => x >= glass.Left && x <= glass.Right && y >= Math.Min(glass.Bottom, _anchor.Y) && y <= Math.Max(glass.Bottom, _anchor.Y),
        };
        if (corridor) return;

        Dismiss();
    }

    private UIElement Row(string label, ImageSource? icon, Action run, bool danger)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is not null)
        {
            line.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        line.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = danger ? DangerText : Text,
            FontSize = 13,
            FontFamily = CyberLook.UiFont,
            MaxWidth = 260,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var row = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 6, 12, 6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = line,
        };
        row.MouseEnter += (_, _) => row.Background = RowHover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            Dismiss();
            run();
        };
        return row;
    }

    private static UIElement GroupTitle(string title) => new TextBlock
    {
        Text = title,
        Foreground = DimText,
        FontSize = 11.5,
        FontWeight = FontWeights.SemiBold,
        FontFamily = CyberLook.UiFont,
        Margin = new Thickness(12, 8, 12, 3),
        MaxWidth = 260,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

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
            FontFamily = CyberLook.UiFont,
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
