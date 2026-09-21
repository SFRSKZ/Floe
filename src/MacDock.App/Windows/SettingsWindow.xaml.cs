using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MacDock.App.Services;

namespace MacDock.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly List<(Border Row, int Index)> _rows = new();

    public SettingsWindow()
    {
        InitializeComponent();

        AuraSettings.Load();
        BuildPaletteRows();

        EffectGlow.IsChecked = AuraSettings.Effect == AuraEffect.Glow;
        EffectBurst.IsChecked = AuraSettings.Effect == AuraEffect.Burst;
        EffectGlow.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Glow);
        EffectBurst.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Burst);

        IntensitySlider.Value = AuraSettings.SpectralIntensity;
        IntensityValue.Text = AuraSettings.SpectralIntensity.ToString("0.0");
        IntensitySlider.ValueChanged += (_, e) =>
        {
            AuraSettings.SetSpectralIntensity(e.NewValue);
            IntensityValue.Text = AuraSettings.SpectralIntensity.ToString("0.0");
        };

        ModePalette.IsChecked = AuraSettings.Mode == AuraColorMode.Palette;
        ModeIcon.IsChecked = AuraSettings.Mode == AuraColorMode.Icon;
        ModePalette.Checked += (_, _) => AuraSettings.SetMode(AuraColorMode.Palette);
        ModeIcon.Checked += (_, _) => AuraSettings.SetMode(AuraColorMode.Icon);

        AuraSettings.Changed += RefreshRows;
        Closed += (_, _) => AuraSettings.Changed -= RefreshRows;

        BuildDesktopButtonControls();
        RefreshRows();
    }

    private readonly List<Border> _colorSwatches = new();

    private void BuildDesktopButtonControls()
    {
        DesktopButtonSettings.Load();

        SizeSlider.Value = DesktopButtonSettings.Size;
        SizeValue.Text = DesktopButtonSettings.Size.ToString("0.00");
        SizeSlider.ValueChanged += (_, e) =>
        {
            DesktopButtonSettings.SetSize(e.NewValue);
            SizeValue.Text = DesktopButtonSettings.Size.ToString("0.00");
        };

        BrightSlider.Value = DesktopButtonSettings.Brightness;
        BrightValue.Text = DesktopButtonSettings.Brightness.ToString("0.0");
        BrightSlider.ValueChanged += (_, e) =>
        {
            DesktopButtonSettings.SetBrightness(e.NewValue);
            BrightValue.Text = DesktopButtonSettings.Brightness.ToString("0.0");
        };

        for (var i = 0; i < DesktopButtonSettings.Colors.Count; i++)
        {
            var color = DesktopButtonSettings.Colors[i].Color;
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(color),
                Cursor = Cursors.Hand,
            };

            var index = i;
            swatch.MouseLeftButtonUp += (_, _) => DesktopButtonSettings.SetColorIndex(index);
            _colorSwatches.Add(swatch);
            ButtonColors.Children.Add(swatch);
        }

        DesktopButtonSettings.Changed += RefreshSwatches;
        Closed += (_, _) => DesktopButtonSettings.Changed -= RefreshSwatches;
        RefreshSwatches();
    }

    private void RefreshSwatches()
    {
        for (var i = 0; i < _colorSwatches.Count; i++)
        {
            var selected = i == DesktopButtonSettings.ColorIndex;
            _colorSwatches[i].BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        }
    }

    private void BuildPaletteRows()
    {
        for (var i = 0; i < AuraSettings.Palettes.Count; i++)
        {
            var palette = AuraSettings.Palettes[i];
            var row = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var color in palette.Colors)
            {
                content.Children.Add(new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = new SolidColorBrush(color),
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = palette.Name,
                Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                FontSize = 12.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            row.Child = content;

            var index = i;
            row.MouseLeftButtonUp += (_, _) => AuraSettings.SetPalette(index);

            _rows.Add((row, index));
            PaletteList.Children.Add(row);
        }
    }

    private void RefreshRows()
    {
        var spectral = AuraSettings.Effect == AuraEffect.Burst;
        IntensityPanel.Opacity = spectral ? 1 : 0.4;
        IntensitySlider.IsEnabled = spectral;

        var paletteActive = AuraSettings.Mode == AuraColorMode.Palette;

        foreach (var (row, index) in _rows)
        {
            var selected = paletteActive && index == AuraSettings.PaletteIndex;
            row.Background = new SolidColorBrush(selected
                ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            row.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0x80, 0x5B, 0xC8, 0xFF)
                : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            row.Opacity = paletteActive ? 1 : 0.45;
            row.IsHitTestVisible = paletteActive;
        }

        PaletteHeader.Opacity = paletteActive ? 1 : 0.45;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
