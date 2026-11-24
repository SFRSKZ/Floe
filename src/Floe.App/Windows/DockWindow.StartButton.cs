using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// The leading Start button. Shared by both products: the plain dock draws a dot-matrix glyph, while the
/// Cyberpunk edition draws a chamfered angular Start badge.
/// </summary>
public partial class DockWindow
{
    private const double StartWidth = 40;
    private const double StartGap = 14;
    private const double StartGlyph = 28;

    // icons begin after the start button and its divider
    private const double LeadX = PadX + StartWidth + StartGap;

    private readonly Grid _start = new() { Width = StartWidth, Height = StartWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly Image _startGlyph = new() { Width = StartGlyph, Height = StartGlyph, IsHitTestVisible = false };
    private readonly CyberStartGlyph _cyberStart = new(StartGlyph);
    private readonly Rectangle _startDivider = new() { Width = 1, Height = 34, RadiusX = 0.5, RadiusY = 0.5, IsHitTestVisible = false };
    private readonly ScaleTransform _startScale = new(1, 1);

    private void InitializeStartButton()
    {
        _start.ToolTip = Loc.T("Başlat");
        _start.RenderTransformOrigin = new Point(0.5, 0.5);
        _start.RenderTransform = _startScale;
        _start.Children.Add(_startGlyph);
        _start.Children.Add(_cyberStart);
        _start.MouseLeftButtonUp += (_, _) =>
        {
            _cyberStart.Pressed(false);
            DesktopController.OpenStartMenu();
        };
        _start.MouseEnter += (_, _) =>
        {
            AnimateStart(GlassSettings.Cyber ? 1 : 1.12, 1);
            if (GlassSettings.Cyber) _cyberStart.Engage(true);
        };
        _start.MouseLeave += (_, _) =>
        {
            AnimateStart(1, GlassSettings.Cyber ? 1 : 0.82);
            _cyberStart.Engage(false);
            _cyberStart.Pressed(false);
        };
        _start.MouseLeftButtonDown += (_, _) =>
        {
            if (GlassSettings.Cyber) _cyberStart.Pressed(true);
        };
        _startGlyph.Opacity = 0.82;
        RenderOptions.SetBitmapScalingMode(_startGlyph, BitmapScalingMode.HighQuality);

        _startDivider.Fill = _lineBrush;
        _clockLayer.Children.Add(_start);
        _clockLayer.Children.Add(_startDivider);

        UpdateStartGlyph();
        DesktopButtonSettings.Changed += OnStartSettingsChanged;
        DpiChanged += (_, _) => UpdateStartGlyph();
        Closed += (_, _) => DesktopButtonSettings.Changed -= OnStartSettingsChanged;
    }

    private void OnStartSettingsChanged() => Dispatcher.BeginInvoke(UpdateStartGlyph);

    private void UpdateStartGlyph()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        bool cyber = GlassSettings.Cyber;
        _cyberStart.Visibility = cyber ? Visibility.Visible : Visibility.Collapsed;
        _startGlyph.Visibility = cyber ? Visibility.Collapsed : Visibility.Visible;
        if (cyber)
        {
            var look = CyberLook.Current;
            _cyberStart.Paint(StartGlyphColor, CyberLook.Readable(look.Accent, look.Body));
            return;
        }

        _startGlyph.Source = DotMatrixIcons.Render("desktop", StartGlyph, StartGlyphColor, dpi);
        _startGlyph.Opacity = 0.82;
    }

    private void AnimateStart(double scale, double opacity)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _startScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        _startScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        _startGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
    }

    private void PositionStartButton()
    {
        var left = StartButtonLeft;
        Canvas.SetLeft(_start, left);
        Canvas.SetTop(_start, RestTop + ((RestBarHeight - StartWidth) / 2));
        Canvas.SetLeft(_startDivider, left + StartWidth + (StartGap / 2));
        Canvas.SetTop(_startDivider, RestTop + ((RestBarHeight - _startDivider.Height) / 2));
    }
}
