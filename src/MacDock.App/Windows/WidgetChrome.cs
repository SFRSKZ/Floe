using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MacDock.App.Effects.MetalRim;
using MacDock.App.Services;

namespace MacDock.App.Windows;

/// <summary>Gives the widget bubbles the dock's glass: tint, hairline rims, top sheen and the Metal rim option.</summary>
internal sealed class WidgetChrome
{
    public const double Radius = 22;

    private readonly Window _window;
    private readonly Border _panel;
    private readonly Border _outerRim;
    private readonly Brush _rimBrush;
    private readonly Border _metal = new() { IsHitTestVisible = false, Background = Brushes.Transparent, CornerRadius = new CornerRadius(Radius) };
    private MetalRimEffect? _metalEffect;

    private WidgetChrome(Window window, Grid root, Border panel)
    {
        _window = window;
        _panel = panel;

        panel.CornerRadius = new CornerRadius(Radius);
        panel.Margin = new Thickness(1);
        panel.BorderThickness = new Thickness(1);
        panel.Background = Frozen(Vertical(
            (Color.FromArgb(0x52, 0x25, 0x25, 0x25), 0.0),
            (Color.FromArgb(0x58, 0x1E, 0x1E, 0x1E), 0.08),
            (Color.FromArgb(0x63, 0x1A, 0x1A, 0x1A), 1.0)));
        _rimBrush = Frozen(Vertical(
            (Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 0.0),
            (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0)));
        panel.BorderBrush = _rimBrush;

        // faint dark outer hairline, like the dock's
        _outerRim = new Border
        {
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(Radius + 1),
            BorderThickness = new Thickness(1),
            BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0))),
        };

        // top sheen: a soft inner glow plus a bright hairline fading toward the ends
        var glow = new Border
        {
            IsHitTestVisible = false,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Background = Frozen(Vertical(
                (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.0),
                (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.4))),
        };
        var highlight = new Border
        {
            IsHitTestVisible = false,
            Height = 1,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Radius, 1, Radius, 0),
            Background = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0),
                new(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 0.5),
                new(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0),
            }, 0)),
        };

        var index = root.Children.IndexOf(panel);
        root.Children.Insert(index, _outerRim);
        root.Children.Insert(index + 1, glow);
        root.Children.Add(highlight);
        root.Children.Add(_metal);

        window.SizeChanged += (_, _) => UpdateMetal();
        GlassSettings.Changed += OnSettingsChanged;
        window.Closed += (_, _) => GlassSettings.Changed -= OnSettingsChanged;
        ApplyRim();
    }

    public static WidgetChrome Apply(Window window, Grid root, Border panel) => new(window, root, panel);

    private void OnSettingsChanged()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.BeginInvoke(new Action(OnSettingsChanged));
            return;
        }

        ApplyRim();
    }

    private void ApplyRim()
    {
        _metalEffect = GlassSettings.Rim switch
        {
            RimStyle.MetalSilver => MetalRimEffect.ApplyPreset("silver", true),
            RimStyle.MetalGold => MetalRimEffect.ApplyPreset("gold", true),
            RimStyle.MetalChromatic => MetalRimEffect.ApplyPreset("chromatic", true),
            _ => null,
        };
        if (_metalEffect is { IsAvailable: false } || !RenderCapability.IsPixelShaderVersionSupported(3, 0))
        {
            _metalEffect = null;
        }

        _metal.Effect = _metalEffect;
        _metal.Visibility = _metalEffect is null ? Visibility.Collapsed : Visibility.Visible;
        var plain = _metalEffect is null ? Visibility.Visible : Visibility.Hidden;
        _outerRim.Visibility = plain;
        _panel.BorderBrush = _metalEffect is null ? _rimBrush : Brushes.Transparent;
        UpdateMetal();
    }

    private void UpdateMetal()
    {
        if (_metalEffect is null || _window.ActualWidth <= 0 || _window.ActualHeight <= 0)
        {
            return;
        }

        _metalEffect.Thickness = 1.8;
        _metalEffect.GlowGain = 0.45;
        _metalEffect.CornerRadius = Radius;
        _metalEffect.Size = new Size(_window.ActualWidth, _window.ActualHeight);
    }

    private static LinearGradientBrush Vertical(params (Color Color, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
