using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;

namespace MacDock.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly List<(Border Row, int Index)> _rows = new();

    public SettingsWindow()
    {
        InitializeComponent();

        // the caller positions the window right after Show()/UpdateLayout(); keep it transparent
        // until the first dispatcher pass so it never flashes at the default position
        Opacity = 0;
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Opacity = 1);

        AuraSettings.Load();
        BuildPaletteRows();
        ClickEffectToggle.IsChecked = AuraSettings.ClickEffect;
        ClickDark.IsChecked = AuraSettings.ClickTone == ClickTone.Dark;
        ClickLight.IsChecked = AuraSettings.ClickTone == ClickTone.Light;
        ClickEffectToggle.Checked += (_, _) => AuraSettings.SetClickEffect(true);
        ClickEffectToggle.Unchecked += (_, _) => AuraSettings.SetClickEffect(false);
        ClickDark.Checked += (_, _) => AuraSettings.SetClickTone(ClickTone.Dark);
        ClickLight.Checked += (_, _) => AuraSettings.SetClickTone(ClickTone.Light);

        EffectGlow.IsChecked = AuraSettings.Effect == AuraEffect.Glow;
        EffectBurst.IsChecked = AuraSettings.Effect == AuraEffect.Burst;
        EffectGlow.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Glow);
        EffectBurst.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Burst);
        EffectNeon.IsChecked = AuraSettings.Effect == AuraEffect.Neon;
        EffectGlass.IsChecked = AuraSettings.Effect == AuraEffect.Glass;
        EffectNeon.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Neon);
        EffectGlass.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Glass);
        EffectAurora.IsChecked = AuraSettings.Effect == AuraEffect.Aurora;
        EffectAurora.Checked += (_, _) => AuraSettings.SetEffect(AuraEffect.Aurora);

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
        BuildGlassControls();
        BuildTabs();
        RefreshRows();

        // the window sits above the dock: keep its bottom edge put when a page changes its height
        SizeChanged += (_, e) =>
        {
            if (IsLoaded && e.HeightChanged && e.PreviousSize.Height > 0)
            {
                Top -= e.NewSize.Height - e.PreviousSize.Height;
            }
        };
    }

    private static int _lastTab;

    private void BuildTabs()
    {
        var tabs = new[] { TabEffect, TabButton, TabGlass };
        var pages = new FrameworkElement[] { EffectPage, ButtonPage, GlassPage };
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            tabs[i].Checked += (_, _) =>
            {
                _lastTab = index;
                for (var k = 0; k < pages.Length; k++)
                {
                    pages[k].Visibility = k == index ? Visibility.Visible : Visibility.Collapsed;
                }
            };
        }

        tabs[_lastTab].IsChecked = true;
    }

    private readonly List<Border> _colorSwatches = new();

    private void BuildDesktopButtonControls()
    {
        DesktopButtonSettings.Load();

        StyleRing.IsChecked = DesktopButtonSettings.Style == DesktopButtonStyle.Ring;
        StyleWindows.IsChecked = DesktopButtonSettings.Style == DesktopButtonStyle.Windows;
        StyleMonitor.IsChecked = DesktopButtonSettings.Style == DesktopButtonStyle.Monitor;
        StyleOrb.IsChecked = DesktopButtonSettings.Style == DesktopButtonStyle.Orb;

        StyleRing.Checked += (_, _) => DesktopButtonSettings.SetStyle(DesktopButtonStyle.Ring);
        StyleWindows.Checked += (_, _) => DesktopButtonSettings.SetStyle(DesktopButtonStyle.Windows);
        StyleMonitor.Checked += (_, _) => DesktopButtonSettings.SetStyle(DesktopButtonStyle.Monitor);
        StyleOrb.Checked += (_, _) => DesktopButtonSettings.SetStyle(DesktopButtonStyle.Orb);

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

    private void BuildGlassControls()
    {
        GlassSettings.Load();

        RimPlain.IsChecked = GlassSettings.Rim == RimStyle.Plain;
        RimSilver.IsChecked = GlassSettings.Rim == RimStyle.MetalSilver;
        RimGold.IsChecked = GlassSettings.Rim == RimStyle.MetalGold;
        RimChromatic.IsChecked = GlassSettings.Rim == RimStyle.MetalChromatic;

        RimPlain.Checked += (_, _) => GlassSettings.SetRim(RimStyle.Plain);
        RimSilver.Checked += (_, _) => GlassSettings.SetRim(RimStyle.MetalSilver);
        RimGold.Checked += (_, _) => GlassSettings.SetRim(RimStyle.MetalGold);
        RimChromatic.Checked += (_, _) => GlassSettings.SetRim(RimStyle.MetalChromatic);

        BulgeSoft.IsChecked = GlassSettings.Bulge == BulgeStyle.Soft;
        BulgePedestal.IsChecked = GlassSettings.Bulge == BulgeStyle.Pedestal;
        BulgeFlat.IsChecked = GlassSettings.Bulge == BulgeStyle.Flat;

        BulgeSoft.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Soft);
        BulgePedestal.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Pedestal);
        BulgeFlat.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Flat);

        VisualizerToggle.IsChecked = GlassSettings.Visualizer;
        VisualizerToggle.Checked += (_, _) => GlassSettings.SetVisualizer(true);
        VisualizerToggle.Unchecked += (_, _) => GlassSettings.SetVisualizer(false);

        VisualizerLookGlow.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Glow;
        VisualizerLookHologram.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Hologram;
        VisualizerLookGlow.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Glow);
        VisualizerLookHologram.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Hologram);
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
                Margin = new Thickness(0, 0, 6, 6),
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
        IntensityPanel.Visibility = spectral ? Visibility.Visible : Visibility.Collapsed;

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
        }

        PaletteList.Visibility = paletteActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
