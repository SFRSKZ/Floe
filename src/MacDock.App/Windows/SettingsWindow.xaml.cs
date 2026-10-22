using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.Core.Interop;

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

        BuildPaletteRows();
        ClickEffectToggle.IsChecked = AuraSettings.ClickEffect;
        ClickDark.IsChecked = AuraSettings.ClickTone == ClickTone.Dark;
        ClickLight.IsChecked = AuraSettings.ClickTone == ClickTone.Light;
        ClickEffectToggle.Checked += (_, _) => AuraSettings.SetClickEffect(true);
        ClickEffectToggle.Unchecked += (_, _) => AuraSettings.SetClickEffect(false);
        ClickDark.Checked += (_, _) => AuraSettings.SetClickTone(ClickTone.Dark);
        ClickLight.Checked += (_, _) => AuraSettings.SetClickTone(ClickTone.Light);
        ClickRainbow.IsChecked = AuraSettings.ClickPalette == ClickPalette.Rainbow;
        ClickClear.IsChecked = AuraSettings.ClickPalette == ClickPalette.Clear;
        ClickIcon.IsChecked = AuraSettings.ClickPalette == ClickPalette.Icon;
        ClickRainbow.Checked += (_, _) => AuraSettings.SetClickPalette(ClickPalette.Rainbow);
        ClickClear.Checked += (_, _) => AuraSettings.SetClickPalette(ClickPalette.Clear);
        ClickIcon.Checked += (_, _) => AuraSettings.SetClickPalette(ClickPalette.Icon);
        ClickSpeedSlider.Value = AuraSettings.ClickSpeed;
        ClickSpeedValue.Text = AuraSettings.ClickSpeed.ToString("0.0") + "x";
        ClickSpeedSlider.ValueChanged += (_, e) =>
        {
            AuraSettings.SetClickSpeed(e.NewValue);
            ClickSpeedValue.Text = AuraSettings.ClickSpeed.ToString("0.0") + "x";
        };

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

        BuildGlassControls();
        BuildTabs();
        RefreshRows();

        // the window sits above the dock: keep its bottom edge put when a page changes its height
        SizeChanged += (_, e) =>
        {
            if (IsLoaded && e.HeightChanged && e.PreviousSize.Height > 0)
            {
                var work = GetWorkArea(this);
                Top = Math.Clamp(Top - (e.NewSize.Height - e.PreviousSize.Height),
                    work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
                Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
            }
        };
    }

    private static int _lastTab;

    internal static Rect GetWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!ScreenInput.TryGetMonitorInfo(ScreenInput.GetMonitorForWindow(hwnd), out var monitor))
        {
            return SystemParameters.WorkArea;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var work = monitor.Work;
        return new Rect(work.Left / dpi.DpiScaleX, work.Top / dpi.DpiScaleY,
            (work.Right - work.Left) / dpi.DpiScaleX, (work.Bottom - work.Top) / dpi.DpiScaleY);
    }

    private void BuildTabs()
    {
        var tabs = new[] { TabEffect, TabGlass };
        var pages = new FrameworkElement[] { EffectPage, GlassPage };
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

    private void BuildGlassControls()
    {

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

        DerezzToggle.IsChecked = AuraSettings.Derezz;
        DerezzToggle.Checked += (_, _) =>
        {
            AuraSettings.SetDerezz(true);
            DerezzSpeedSlider.IsEnabled = true;
        };
        DerezzToggle.Unchecked += (_, _) =>
        {
            AuraSettings.SetDerezz(false);
            DerezzSpeedSlider.IsEnabled = false;
        };
        DerezzSpeedSlider.IsEnabled = AuraSettings.Derezz;
        DerezzSpeedSlider.Value = AuraSettings.DerezzSpeed;
        DerezzSpeedValue.Text = AuraSettings.DerezzSpeed.ToString("0.0") + "x";
        // Once the slider settles, the dock plays the effect at the new speed.
        var derezzPreview = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        derezzPreview.Tick += (_, _) =>
        {
            derezzPreview.Stop();
            AuraSettings.PreviewDerezz();
        };
        Closed += (_, _) => derezzPreview.Stop();
        DerezzSpeedSlider.ValueChanged += (_, e) =>
        {
            AuraSettings.SetDerezzSpeed(e.NewValue);
            DerezzSpeedValue.Text = AuraSettings.DerezzSpeed.ToString("0.0") + "x";
            derezzPreview.Stop();
            derezzPreview.Start();
        };

        VisualizerLookGlow.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Glow;
        VisualizerLookHologram.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Hologram;
        VisualizerLookGlow.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Glow);
        VisualizerLookHologram.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Hologram);

        HologramTintBlue.IsChecked = GlassSettings.HologramTint == HologramColor.Blue;
        HologramTintCyan.IsChecked = GlassSettings.HologramTint == HologramColor.Cyan;
        HologramTintViolet.IsChecked = GlassSettings.HologramTint == HologramColor.Violet;
        HologramTintPink.IsChecked = GlassSettings.HologramTint == HologramColor.Pink;
        HologramTintGold.IsChecked = GlassSettings.HologramTint == HologramColor.Gold;
        HologramTintGreen.IsChecked = GlassSettings.HologramTint == HologramColor.Green;
        HologramTintWhite.IsChecked = GlassSettings.HologramTint == HologramColor.White;
        HologramTintBlue.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Blue);
        HologramTintCyan.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Cyan);
        HologramTintViolet.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Violet);
        HologramTintPink.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Pink);
        HologramTintGold.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Gold);
        HologramTintGreen.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Green);
        HologramTintWhite.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.White);

        IntroDrop.IsChecked = GlassSettings.Intro == NowPlayingIntro.Drop;
        IntroHologram.IsChecked = GlassSettings.Intro == NowPlayingIntro.Hologram;
        IntroDrop.Checked += (_, _) => GlassSettings.SetIntro(NowPlayingIntro.Drop);
        IntroHologram.Checked += (_, _) => GlassSettings.SetIntro(NowPlayingIntro.Hologram);
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
