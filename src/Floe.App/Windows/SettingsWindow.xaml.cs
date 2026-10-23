using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Effects.Chromatic;
using Floe.App.Effects.MetalRim;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly List<(Border Row, Border Rim, Grid Shell, int Index)> _rows = new();

    private bool _closing;

    public SettingsWindow()
    {
        InitializeComponent();

        // the caller positions the window right after Show()/UpdateLayout(); keep it transparent
        // until the first dispatcher pass so it never flashes at the default position
        Opacity = 0;
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Opacity = 1);
        // a click anywhere else closes it, like a popover
        Deactivated += (_, _) =>
        {
            if (!_closing) Close();
        };
        Closing += (_, _) => _closing = true;

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
        BuildPerformanceControls();
        BuildTabs();
        RefreshRows();
        AttachChromatic();
        Loaded += (_, _) => _chromatic?.Start();
        Closed += (_, _) => _chromatic?.Dispose();

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

    private ChromaticAnimator? _chromatic;
    private readonly List<(Border Rim, Func<bool> Strong)> _metalRims = new();

    private static readonly bool NoMetal =
        (Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "").Contains("metal");

    /// <summary>
    /// Puts the chromatic material on the selected toggles and the close button only:
    /// unchecked toggles keep the plain glass face. The same clock also runs the metal
    /// rims around every option (quiet until selected), which stop with the window.
    /// </summary>
    private void AttachChromatic()
    {
        if (Content is not DependencyObject root)
        {
            return;
        }

        var faces = ChromaticEffect.IsAvailable && RenderCapability.IsPixelShaderVersionSupported(2, 0);
        var rims = !NoMetal && RenderCapability.IsPixelShaderVersionSupported(3, 0);
        if (!faces && !rims)
        {
            return;
        }

        _chromatic = new ChromaticAnimator();
        var used = false;
        foreach (var toggle in FindVisualChildren<RadioButton>(root))
        {
            used |= faces && AttachChromaticFace(toggle, () => toggle.IsChecked == true);
            used |= rims && AttachMetalRim(toggle, () => toggle.IsChecked == true);
            // a new selection still settles while idle motion is off
            toggle.Checked += (_, _) => _chromatic?.Wake();
        }
        used |= faces && AttachChromaticFace(CloseButton, () => true);
        used |= rims && AttachMetalRim(CloseButton, () => true);
        if (rims)
        {
            foreach (var (_, rim, _, index) in _rows)
            {
                var strong = new Func<bool>(() =>
                    AuraSettings.Mode == AuraColorMode.Palette && AuraSettings.PaletteIndex == index);
                if (_chromatic.AddRim(rim, strong))
                {
                    _metalRims.Add((rim, strong));
                    used = true;
                }
            }
        }

        if (!used)
        {
            _chromatic.Dispose();
            _chromatic = null;
        }
    }

    private bool AttachChromaticFace(Control control, Func<bool> active)
    {
        control.ApplyTemplate();
        if (control.Template?.FindName("Chroma", control) is Border face)
        {
            _chromatic?.Add(face, control, active);
            return true;
        }

        return false;
    }

    private bool AttachMetalRim(Control control, Func<bool> strong)
    {
        control.ApplyTemplate();
        if (control.Template?.FindName("Rim", control) is Border rim && _chromatic?.AddRim(rim, strong) == true)
        {
            _metalRims.Add((rim, strong));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Diagnostic only: RenderTargetBitmap is software-only and skips ps_3_0, so the
    /// --rendersettings PNG gets its rims composited from the WARP renderer.
    /// </summary>
    internal void DrawMetalRims(DrawingContext context, Visual root)
    {
        foreach (var (rim, _) in _metalRims)
        {
            if (!rim.IsVisible || rim.ActualWidth < 1 || rim.ActualHeight < 1
                || rim.Effect is not MetalRimEffect live)
            {
                continue;
            }

            var rect = rim.TransformToAncestor(root).TransformBounds(new Rect(rim.RenderSize));
            var effect = MetalRimEffect.ApplyPreset("chromatic", true);
            effect.Thickness = live.Thickness;
            effect.Strength = live.Strength;
            effect.GlowGain = live.GlowGain;
            effect.CornerRadius = live.CornerRadius;
            effect.Size = rect.Size;
            effect.Time = 4;
            using var renderer = new MetalRimPreview.OffscreenRenderer(
                (int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height));
            var bitmap = renderer.Render(effect);
            context.DrawImage(bitmap, new Rect(
                rect.X - MetalRimEffect.GlowPadding, rect.Y - MetalRimEffect.GlowPadding,
                bitmap.PixelWidth, bitmap.PixelHeight));
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

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
            DerezzCellSlider.IsEnabled = true;
        };
        DerezzToggle.Unchecked += (_, _) =>
        {
            AuraSettings.SetDerezz(false);
            DerezzSpeedSlider.IsEnabled = false;
            DerezzCellSlider.IsEnabled = false;
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
        DerezzCellSlider.IsEnabled = AuraSettings.Derezz;
        DerezzCellSlider.Value = AuraSettings.DerezzCell;
        DerezzCellValue.Text = AuraSettings.DerezzCell.ToString("0") + " px";
        DerezzCellSlider.ValueChanged += (_, e) =>
        {
            AuraSettings.SetDerezzCell(e.NewValue);
            DerezzCellValue.Text = AuraSettings.DerezzCell.ToString("0") + " px";
            derezzPreview.Stop();
            derezzPreview.Start();
        };

        VisualizerLookGlow.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Glow;
        VisualizerLookHologram.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Hologram;
        VisualizerLookGlow.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Glow);
        VisualizerLookHologram.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Hologram);
        VisualizerLookCubes.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Cubes;
        VisualizerLookCubes.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Cubes);
        CubeSizeSlider.Value = GlassSettings.CubeSize;
        CubeSizeValue.Text = GlassSettings.CubeSize.ToString("0") + " px";
        CubeSizeSlider.ValueChanged += (_, e) =>
        {
            GlassSettings.SetCubeSize(e.NewValue);
            CubeSizeValue.Text = GlassSettings.CubeSize.ToString("0") + " px";
        };

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

    /// <summary>
    /// The dock picks the level itself in Auto; the status line shows what it settled on,
    /// including when it steps down or back up while the window is open.
    /// </summary>
    private void BuildPerformanceControls()
    {
        PerformanceAuto.IsChecked = Performance.Mode == PerformanceMode.Auto;
        PerformanceQuality.IsChecked = Performance.Mode == PerformanceMode.Quality;
        PerformanceSmooth.IsChecked = Performance.Mode == PerformanceMode.Smooth;
        PerformanceAuto.Checked += (_, _) => Performance.SetMode(PerformanceMode.Auto);
        PerformanceQuality.Checked += (_, _) => Performance.SetMode(PerformanceMode.Quality);
        PerformanceSmooth.Checked += (_, _) => Performance.SetMode(PerformanceMode.Smooth);

        UpdatePerformanceStatus();
        Performance.Changed += UpdatePerformanceStatus;
        Closed += (_, _) => Performance.Changed -= UpdatePerformanceStatus;
    }

    private void UpdatePerformanceStatus()
    {
        PerformanceStatus.Text = Performance.Level switch
        {
            PerformanceLevel.Full => "Şu an: Tam",
            PerformanceLevel.Balanced => "Şu an: Dengeli",
            _ => "Şu an: Hafif",
        };
    }

    private void BuildPaletteRows()
    {
        for (var i = 0; i < AuraSettings.Palettes.Count; i++)
        {
            var palette = AuraSettings.Palettes[i];
            var shell = new Grid { Margin = new Thickness(0, 0, 6, 6) };
            var row = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 7, 10, 7),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            // the ring overlays the card so its halo can bleed past the card's bounds
            var rim = new Border
            {
                CornerRadius = new CornerRadius(9),
                IsHitTestVisible = false,
            };
            shell.Children.Add(row);
            shell.Children.Add(rim);

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
            row.MouseLeftButtonUp += (_, _) =>
            {
                AuraSettings.SetPalette(index);
                _chromatic?.Wake();
            };

            _rows.Add((row, rim, shell, index));
            PaletteList.Children.Add(shell);
        }
    }

    private void RefreshRows()
    {
        var spectral = AuraSettings.Effect == AuraEffect.Burst;
        IntensityPanel.Visibility = spectral ? Visibility.Visible : Visibility.Collapsed;

        var paletteActive = AuraSettings.Mode == AuraColorMode.Palette;

        foreach (var (row, rim, shell, index) in _rows)
        {
            var selected = paletteActive && index == AuraSettings.PaletteIndex;
            row.Background = new SolidColorBrush(selected
                ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            row.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0x80, 0x5B, 0xC8, 0xFF)
                : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            // every card wears the ring; the selected one is brighter and sits above its neighbours
            Panel.SetZIndex(shell, selected ? 1 : 0);
        }

        PaletteList.Visibility = paletteActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
