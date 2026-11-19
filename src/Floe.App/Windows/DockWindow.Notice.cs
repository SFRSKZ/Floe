using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Floe.App.Services;
using Floe.App.ViewModels;
using Floe.Core.Models;

namespace Floe.App.Windows;

// The glass edition's small moves, taken from the cyber dock in its own language: a card that names the app asking
// for attention, the minute rolling in, the seconds rising along the clock's divider and a new track sliding in.
public partial class DockWindow
{
    private Popup? _notice;
    private Image? _noticeIcon;
    private TextBlock? _noticeName;
    private Border? _noticeCard;
    private TranslateTransform? _noticeLift;
    private System.Windows.Threading.DispatcherTimer? _noticeTimer;

    private static readonly Duration Soft = new(TimeSpan.FromMilliseconds(320));
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>A glass card rises over the dock's first icon with the name of the app that asks for attention.</summary>
    private void ShowGlassNotice(DockItemViewModel item)
    {
        if (!IsVisible || !_shown || _fullscreenHidden || _slots.Count == 0) return;
        if (_notice is null)
        {
            _noticeIcon = new Image { Width = 22, Height = 22, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_noticeIcon, BitmapScalingMode.HighQuality);
            _noticeName = new TextBlock { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI"), MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis };
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(new TextBlock { Text = Loc.T("Yeni bildirim"), Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), FontSize = 10.5, FontFamily = new FontFamily("Segoe UI") });
            lines.Children.Add(_noticeName);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_noticeIcon);
            row.Children.Add(lines);
            _noticeLift = new TranslateTransform();
            // the tooltip's glass card, a size up
            _noticeCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1B, 0x1C, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 7, 14, 7),
                Child = row,
                RenderTransform = _noticeLift,
            };
            _notice = new Popup
            {
                AllowsTransparency = true, Placement = PlacementMode.Top, StaysOpen = true, IsHitTestVisible = false,
                VerticalOffset = -14, Child = _noticeCard,
            };
            _noticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.6) };
            _noticeTimer.Tick += (_, _) => HideGlassNotice();
            Closed += (_, _) => { _noticeTimer.Stop(); _notice.IsOpen = false; };
        }

        _noticeIcon!.Source = item.Icon;
        _noticeName!.Text = item.Name;
        if (DockSettings.Edge != DockEdge.Bottom) ShowUprightPopup(_notice, DockAnchor(_slots[0].Center), 14);
        else
        {
            _notice.Placement = PlacementMode.Top;
            _notice.HorizontalOffset = 0;
            _notice.VerticalOffset = -14;
            _notice.PlacementTarget = _slots[0].Button;
            _notice.IsOpen = true;
        }

        _noticeCard!.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Soft) { EasingFunction = EaseOut });
        _noticeLift!.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, Soft) { EasingFunction = EaseOut });
        _noticeTimer!.Stop();
        _noticeTimer.Start();
    }

    private void HideGlassNotice()
    {
        _noticeTimer?.Stop();
        if (_notice is not { IsOpen: true }) return;
        var fade = new DoubleAnimation(0, Soft) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => { if (_noticeCard!.Opacity < .01) _notice.IsOpen = false; };
        _noticeCard!.BeginAnimation(OpacityProperty, fade);
        _noticeLift!.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, Soft) { EasingFunction = EaseOut });
    }

    /// <summary>The new minute rises into place instead of switching.</summary>
    private void RollClock()
    {
        if (_clockTime.RenderTransform is not TranslateTransform lift)
        {
            lift = new TranslateTransform();
            _clockTime.RenderTransform = lift;
        }

        lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(7, 0, Soft) { EasingFunction = EaseOut });
        _clockTime.BeginAnimation(OpacityProperty, new DoubleAnimation(.15, 1, Soft) { EasingFunction = EaseOut });
    }

    private LinearGradientBrush? _secondsFill;

    /// <summary>The divider beside the clock fills from the bottom as the minute runs, and drains when it turns.</summary>
    private void ShowGlassSeconds(DateTime now)
    {
        if (_secondsFill is null)
        {
            var lit = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);
            var dim = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            _secondsFill = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            _secondsFill.GradientStops.Add(new GradientStop(lit, 0));
            _secondsFill.GradientStops.Add(new GradientStop(lit, 0));
            _secondsFill.GradientStops.Add(new GradientStop(dim, 0));
            _secondsFill.GradientStops.Add(new GradientStop(dim, 1));
        }

        // a theme change paints the divider again; take it back
        if (!ReferenceEquals(_clockDivider.Fill, _secondsFill)) _clockDivider.Fill = _secondsFill;

        double to = (now.Second + 1) / 60.0;
        double from = now.Second / 60.0;
        // one second's sweep, so it climbs smoothly; the turn of the minute drops it back to empty
        foreach (var stop in new[] { _secondsFill.GradientStops[1], _secondsFill.GradientStops[2] })
            stop.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(1)));
    }

    // the cyber themes paint their own divider, so only the brush is let go
    private void ClearGlassSeconds() => _secondsFill = null;

    /// <summary>A new track's title and artist slide in from the side.</summary>
    private void SlideTrackIn()
    {
        foreach (var text in new[] { _mediaTitle, _mediaArtist })
        {
            if (text.RenderTransform is not TranslateTransform shift)
            {
                shift = new TranslateTransform();
                text.RenderTransform = shift;
            }

            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, Soft) { EasingFunction = EaseOut });
            text.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Soft) { EasingFunction = EaseOut });
        }
    }
}
