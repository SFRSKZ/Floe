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
using Floe.Core.Models;
using Floe.Core.Services;

namespace Floe.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly List<(Border Row, Border Rim, Grid Shell, int Index)> _rows = new();

    private bool _closing;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = Product.Name + " " + Loc.T("Ayarları");
        // each product hides the themes and looks the other one owns; the Cyber code stays in both builds
#if CP77
        foreach (var replaced in new UIElement[] { ThemeTitle, ThemeRow, DockColorToggle, DockColorPicker })
        {
            replaced.Visibility = Visibility.Collapsed;
        }

        TabEffect.Content = "Genel";
        TabGlass.Content = "Stüdyo";
        ClickEffectToggle.Content = "Hex dökümü";
        ClickSoundToggle.Content = "Tıklama sesi";
        VisualizerLookHologram.Visibility = Visibility.Collapsed;
        VisualizerLookStardust.Visibility = Visibility.Collapsed;
        VisualizerLookDustWave.Visibility = Visibility.Collapsed;
        VisualizerLookDustMilk.Visibility = Visibility.Collapsed;
        VisualizerLookChladni.Visibility = Visibility.Collapsed;
        VisualizerLookFireflies.Visibility = Visibility.Collapsed;
        VisualizerLookFerrofluid.Visibility = Visibility.Collapsed;
        VisualizerLookOled.Visibility = Visibility.Collapsed;
        VisualizerLookLed.Visibility = Visibility.Collapsed;
        GpuVisualizerToggle.Visibility = Visibility.Collapsed;
        // the Cyber themes bring their own colours, surface and click, so these would do nothing here
        foreach (var unused in new UIElement[]
        {
            EffectTitle, EffectRow, ColorModeTitle, ColorModeRow, PaletteList, ClickScreenToggle, ClickPaintToggle, ClickToneRow, ClickPaletteRow,
            SurfaceTitle, SurfaceRow, RimTitle, RimRow, BulgeTitle, BulgeRow, IntroTitle, IntroRow, DerezzToggle, DerezzCellTitle, DerezzCellRow,
        })
        {
            unused.Visibility = Visibility.Collapsed;
        }
#else
        FilmGrainGap.Visibility = Visibility.Collapsed;
        FilmGrainToggle.Visibility = Visibility.Collapsed;
        VisualizerLookCyberSignal.Visibility = Visibility.Collapsed;
        VisualizerLookCyberBlackwall.Visibility = Visibility.Collapsed;
        VisualizerLookCyberCircuit.Visibility = Visibility.Collapsed;
        VisualizerLookCyberGlyph.Visibility = Visibility.Collapsed;
        VisualizerLookCyberRain.Visibility = Visibility.Collapsed;
        VisualizerLookCyberSamurai.Visibility = Visibility.Collapsed;
        VisualizerLookCyberBraindance.Visibility = Visibility.Collapsed;
        VisualizerLookCyberSandevistan.Visibility = Visibility.Collapsed;
        VisualizerLookCyberBreach.Visibility = Visibility.Collapsed;
        VisualizerLookCyberCollar.Visibility = Visibility.Collapsed;
#endif
        ApplyChrome();
        GlassSettings.Changed += ApplyChromeOnUi;
        Closed += (_, _) => GlassSettings.Changed -= ApplyChromeOnUi;

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
        InitializeWeatherCity();
        LanguageAuto.IsChecked = GlassSettings.Language == AppLanguage.Auto;
        LanguageTurkish.IsChecked = GlassSettings.Language == AppLanguage.Turkish;
        LanguageEnglish.IsChecked = GlassSettings.Language == AppLanguage.English;
        foreach (var (toggle, language) in new[] { (LanguageAuto, AppLanguage.Auto), (LanguageTurkish, AppLanguage.Turkish), (LanguageEnglish, AppLanguage.English) })
        {
            toggle.Checked += (_, _) =>
            {
                GlassSettings.SetLanguage(language);
                Loc.Apply(this);
            };
        }

        Loaded += (_, _) => Loc.Apply(this);
        ClickEffectToggle.IsChecked = AuraSettings.ClickEffect;
        ClickDark.IsChecked = AuraSettings.ClickTone == ClickTone.Dark;
        ClickLight.IsChecked = AuraSettings.ClickTone == ClickTone.Light;
        ClickEffectToggle.Checked += (_, _) => AuraSettings.SetClickEffect(true);
        ClickEffectToggle.Unchecked += (_, _) => AuraSettings.SetClickEffect(false);
        ClickScreenToggle.IsChecked = AuraSettings.ClickScreen;
        ClickScreenToggle.Checked += (_, _) => AuraSettings.SetClickScreen(true);
        ClickScreenToggle.Unchecked += (_, _) => AuraSettings.SetClickScreen(false);
        ClickPaintToggle.IsChecked = AuraSettings.ClickPaint;
        ClickPaintToggle.Checked += (_, _) => AuraSettings.SetClickPaint(true);
        ClickPaintToggle.Unchecked += (_, _) => AuraSettings.SetClickPaint(false);
        ClickSoundToggle.IsChecked = AuraSettings.ClickSound;
        ClickSoundToggle.Checked += (_, _) =>
        {
            AuraSettings.SetClickSound(true);
            ClickSound.Play(AuraSettings.ClickTone);
        };
        ClickSoundToggle.Unchecked += (_, _) => AuraSettings.SetClickSound(false);
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
        BuildDockControls();
        BuildTabs();
        RefreshRows();
        AttachChromatic();
        _chromaticCyber = GlassSettings.Cyber; // the faces above are built for the theme this window opens in
        Loaded += (_, _) => _chromatic?.Start();
        Closed += (_, _) => _chromatic?.Dispose();

        // never taller than the screen: the pages scroll inside what is left under the tabs
        Loaded += (_, _) =>
        {
            var work = GetWorkArea(this);
            PageScroller.MaxHeight = Math.Max(200, work.Height - 24 - (ActualHeight - PageScroller.ActualHeight));
        };

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
    private readonly List<Border> _chromaticFaces = new();
    // the theme the faces were last built for; the constructor builds them for the theme it opens in
    private bool _chromaticCyber;

    private static readonly bool NoMetal =
        (Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "").Contains("metal");

    private void ApplyChromeOnUi() => Dispatcher.BeginInvoke(ApplyChrome);

    // the toggles' colour and corners follow the theme, the cyber themes' hard yellow included
    private void ApplyChrome()
    {
        bool cyber = GlassSettings.Cyber;
        var accent = cyber ? CyberLook.Current.Strip : Color.FromRgb(0x5B, 0xC8, 0xFF);
        Resources["FloeAccent"] = new SolidColorBrush(accent);
        Resources["FloeAccentDim"] = new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B));
        Resources["FloeAccentEdge"] = new SolidColorBrush(Color.FromArgb(0x8A, accent.R, accent.G, accent.B));
        Resources["FloeCorner"] = new CornerRadius(cyber ? 0 : 9);
        Resources["FloeOnAccent"] = new SolidColorBrush(cyber ? CyberLook.InkOn(accent) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        CloseButton.Foreground = (Brush)Resources["FloeOnAccent"];
        WeatherCityFind.Foreground = CloseButton.Foreground;
        if (_studioReset is not null) _studioReset.Foreground = CloseButton.Foreground;
        RefreshChromatic();
    }

    /// <summary>
    /// The shader faces belong to the glass themes; the cyber themes draw flat plates instead. A theme change while
    /// the window is open takes the faces off and stops their clock, or builds them again in the glass themes.
    /// </summary>
    private void RefreshChromatic()
    {
        bool cyber = GlassSettings.Cyber;
        if (cyber == _chromaticCyber) return;
        _chromaticCyber = cyber;
        if (cyber)
        {
            DetachChromatic();
            return;
        }

        AttachChromatic();
    }

    private void DetachChromatic()
    {
        foreach (var face in _chromaticFaces) face.Effect = null;
        _chromaticFaces.Clear();
        foreach (var (rim, _) in _metalRims) rim.Effect = null;
        _metalRims.Clear();
        _chromatic?.Dispose();
        _chromatic = null;
    }

    /// <summary>
    /// Puts the chromatic material on the selected toggles and the close button only:
    /// unchecked toggles keep the plain glass face. The same clock also runs the metal
    /// rims around every option (quiet until selected), which stop with the window.
    /// </summary>
    private void AttachChromatic()
    {
        if (GlassSettings.Cyber) return; // flat plates, no shader faces

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
            toggle.Checked -= OnToggleChecked;
            toggle.Checked += OnToggleChecked;
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
            return;
        }

        // a theme change while the window is open builds a fresh clock; the constructor's Loaded hook is past
        if (IsLoaded) _chromatic.Start();
    }

    private void OnToggleChecked(object sender, RoutedEventArgs e) => _chromatic?.Wake();

    private bool AttachChromaticFace(Control control, Func<bool> active)
    {
        control.ApplyTemplate();
        if (control.Template?.FindName("Chroma", control) is Border face)
        {
            _chromatic?.Add(face, control, active);
            _chromaticFaces.Add(face);
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
        var tabs = new[] { TabEffect, TabGlass, TabVisualizer, TabDock };
        var pages = new FrameworkElement[] { EffectPage, GlassPage, VisualizerPage, DockPage };
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

    private void BuildDockControls()
    {
        DockScaleSlider.Value = DockSettings.Scale;
        DockScaleValue.Text = $"{DockSettings.Scale * 100:0}%";
        DockScaleSlider.ValueChanged += (_, e) =>
        {
            DockSettings.SetScale(e.NewValue);
            DockScaleValue.Text = $"{DockSettings.Scale * 100:0}%";
        };

        HideOnFullscreenToggle.IsChecked = DockSettings.HideOnFullscreen;
        HideOnFullscreenToggle.Checked += (_, _) => DockSettings.SetHideOnFullscreen(true);
        HideOnFullscreenToggle.Unchecked += (_, _) => DockSettings.SetHideOnFullscreen(false);
        HideWindowsTaskbarToggle.IsChecked = DockSettings.HideWindowsTaskbar;
        HideWindowsTaskbarToggle.Checked += (_, _) => DockSettings.SetHideWindowsTaskbar(true);
        HideWindowsTaskbarToggle.Unchecked += (_, _) => DockSettings.SetHideWindowsTaskbar(false);
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

        VisualizerThemeToggle.IsChecked = GlassSettings.VisualizerThemeColours;
        VisualizerThemeToggle.Checked += (_, _) => GlassSettings.SetVisualizerThemeColours(true);
        VisualizerThemeToggle.Unchecked += (_, _) => GlassSettings.SetVisualizerThemeColours(false);
        VisualizerThemeToggle.Visibility = Floe.Core.Services.Product.Cp77 ? Visibility.Visible : Visibility.Collapsed;
        SignalEqualizerToggle.IsChecked = GlassSettings.SignalEqualizer;
        SignalEqualizerToggle.Checked += (_, _) => GlassSettings.SetSignalEqualizer(true);
        SignalEqualizerToggle.Unchecked += (_, _) => GlassSettings.SetSignalEqualizer(false);
        GlyphNegativeToggle.IsChecked = GlassSettings.GlyphNegative;
        GlyphNegativeToggle.Checked += (_, _) => GlassSettings.SetGlyphNegative(true);
        GlyphNegativeToggle.Unchecked += (_, _) => GlassSettings.SetGlyphNegative(false);
        FilmGrainToggle.IsChecked = GlassSettings.FilmGrain;
        FilmGrainToggle.Checked += (_, _) => GlassSettings.SetFilmGrain(true);
        FilmGrainToggle.Unchecked += (_, _) => GlassSettings.SetFilmGrain(false);
        ShrinkToggle.IsChecked = GlassSettings.Shrink;
        ShrinkToggle.Checked += (_, _) => GlassSettings.SetShrink(true);
        ShrinkToggle.Unchecked += (_, _) => GlassSettings.SetShrink(false);

        SurfaceClassic.IsChecked = GlassSettings.Surface == DockSurface.Classic;
        SurfaceMica.IsChecked = GlassSettings.Surface == DockSurface.Mica;
        SurfaceFrost.IsChecked = GlassSettings.Surface == DockSurface.Frost;
        SurfaceGraphite.IsChecked = GlassSettings.Surface == DockSurface.Graphite;
        SurfaceFloating.IsChecked = GlassSettings.Surface == DockSurface.Floating;

        SurfaceClassic.Checked += (_, _) => GlassSettings.SetSurface(DockSurface.Classic);
        SurfaceMica.Checked += (_, _) => GlassSettings.SetSurface(DockSurface.Mica);
        SurfaceFrost.Checked += (_, _) => GlassSettings.SetSurface(DockSurface.Frost);
        SurfaceGraphite.Checked += (_, _) => GlassSettings.SetSurface(DockSurface.Graphite);
        SurfaceFloating.Checked += (_, _) => GlassSettings.SetSurface(DockSurface.Floating);

        BulgeSoft.IsChecked = GlassSettings.Bulge == BulgeStyle.Soft;
        BulgePedestal.IsChecked = GlassSettings.Bulge == BulgeStyle.Pedestal;
        BulgeFlat.IsChecked = GlassSettings.Bulge == BulgeStyle.Flat;

        BulgeSoft.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Soft);
        BulgePedestal.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Pedestal);
        BulgeFlat.Checked += (_, _) => GlassSettings.SetBulge(BulgeStyle.Flat);

        VisualizerToggle.IsChecked = GlassSettings.Visualizer;
        VisualizerToggle.Checked += (_, _) => GlassSettings.SetVisualizer(true);
        VisualizerToggle.Unchecked += (_, _) => GlassSettings.SetVisualizer(false);

        // a theme turns its visualizer on with it: Tron the OLED panel
        foreach (var (button, theme) in new[]
        {
            (ThemeNone, DockTheme.None), (ThemeTronLegacy, DockTheme.TronLegacy), (ThemeTronAres, DockTheme.TronAres),
        })
        {
            button.IsChecked = GlassSettings.Theme == theme;
            button.Checked += (_, _) =>
            {
                GlassSettings.SetTheme(theme);
                if (!GlassSettings.Tron) return;
                VisualizerToggle.IsChecked = true;
                VisualizerLookOled.IsChecked = true;
            };
        }

#if CP77
        InitializeStudio();
#else
        InitializeDockColor();
#endif

        OledColourToggle.IsChecked = GlassSettings.OledColour;
        OledColourToggle.Checked += (_, _) => GlassSettings.SetOledColour(true);
        OledColourToggle.Unchecked += (_, _) => GlassSettings.SetOledColour(false);
        LedOverlayToggle.IsChecked = GlassSettings.LedOverlay;
        LedOverlayToggle.Checked += (_, _) => GlassSettings.SetLedOverlay(true);
        LedOverlayToggle.Unchecked += (_, _) => GlassSettings.SetLedOverlay(false);
        GpuVisualizerToggle.IsChecked = GlassSettings.GpuVisualizer;
        GpuVisualizerToggle.Checked += (_, _) => GlassSettings.SetGpuVisualizer(true);
        GpuVisualizerToggle.Unchecked += (_, _) => GlassSettings.SetGpuVisualizer(false);

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
        VisualizerLookStardust.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Stardust;
        VisualizerLookStardust.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Stardust);
        VisualizerLookDustWave.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.DustWave;
        VisualizerLookDustWave.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.DustWave);
        VisualizerLookDustMilk.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.DustMilk;
        VisualizerLookDustMilk.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.DustMilk);
        VisualizerLookChladni.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Chladni;
        VisualizerLookChladni.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Chladni);
        VisualizerLookFireflies.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Fireflies;
        VisualizerLookFireflies.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Fireflies);
        VisualizerLookFerrofluid.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Ferrofluid;
        VisualizerLookFerrofluid.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Ferrofluid);
        VisualizerLookSilkContour.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.SilkContour;
        VisualizerLookSilkContour.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.SilkContour);
        VisualizerLookOled.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Oled;
        VisualizerLookOled.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Oled);
        VisualizerLookPrism.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Prism;
        VisualizerLookPrism.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Prism);
        VisualizerLookWheat.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Wheat;
        VisualizerLookWheat.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Wheat);
        VisualizerLookRibbon.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Ribbon;
        VisualizerLookRibbon.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Ribbon);
        foreach (var (toggle, look) in new[] { (VisualizerLookCyberSignal, VisualizerStyle.CyberSignal), (VisualizerLookCyberBlackwall, VisualizerStyle.CyberBlackwall), (VisualizerLookCyberCircuit, VisualizerStyle.CyberCircuit), (VisualizerLookCyberGlyph, VisualizerStyle.CyberGlyph), (VisualizerLookCyberRain, VisualizerStyle.CyberRain), (VisualizerLookCyberSamurai, VisualizerStyle.CyberSamurai),
            (VisualizerLookCyberBraindance, VisualizerStyle.CyberBraindance), (VisualizerLookCyberSandevistan, VisualizerStyle.CyberSandevistan), (VisualizerLookCyberBreach, VisualizerStyle.CyberBreach), (VisualizerLookCyberCollar, VisualizerStyle.CyberCollar) })
        {
            toggle.IsChecked = GlassSettings.VisualizerLook == look;
            toggle.Checked += (_, _) => GlassSettings.SetVisualizerLook(look);
        }

        VisualizerLookLed.IsChecked = GlassSettings.VisualizerLook == VisualizerStyle.Led;
        VisualizerLookLed.Checked += (_, _) => GlassSettings.SetVisualizerLook(VisualizerStyle.Led);

        // the OLED panel has its own effects and none of the colour settings: each look shows only what it uses
        void ShowLookOptions()
        {
            var led = VisualizerLookLed.IsChecked == true;
            var oled = led || VisualizerLookOled.IsChecked == true;
            OledOptions.Visibility = oled && !Floe.Core.Services.Product.Cp77 ? Visibility.Visible : Visibility.Collapsed;
            // the LED look is always in colour; the toggle is the OLED panel's
            OledColourToggle.Visibility = led ? Visibility.Collapsed : Visibility.Visible;
            SignalEqualizerToggle.Visibility = VisualizerLookCyberSignal.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            GlyphNegativeToggle.Visibility = VisualizerLookCyberGlyph.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LookOptions.Visibility = oled || Floe.Core.Services.Product.Cp77 ? Visibility.Collapsed : Visibility.Visible;
        }

        foreach (var look in new[] { VisualizerLookGlow, VisualizerLookHologram, VisualizerLookCubes, VisualizerLookStardust, VisualizerLookDustWave, VisualizerLookDustMilk, VisualizerLookSilkContour, VisualizerLookOled, VisualizerLookLed, VisualizerLookPrism, VisualizerLookWheat, VisualizerLookRibbon, VisualizerLookChladni, VisualizerLookFireflies, VisualizerLookFerrofluid })
        {
            look.Checked += (_, _) => ShowLookOptions();
        }

        ShowLookOptions();
        // the buttons in the order they show, each with the panel preset it plays (-1 takes turns)
        var oledEffects = new[]
        {
            (OledEffectCycle, -1), (OledEffectSpectrum, 6), (OledEffectSonar, 7), (OledEffectStars, 8), (OledEffectRain, 9),
            (OledEffectPlasma, 10), (OledEffectTunnel, 0), (OledEffectRiver, 1), (OledEffectWhirl, 2), (OledEffectSmoke, 3),
            (OledEffectEcho, 4), (OledEffectGrid, 5),
        };
        foreach (var (button, effect) in oledEffects)
        {
            button.IsChecked = GlassSettings.OledEffect == effect;
            button.Checked += (_, _) => GlassSettings.SetOledEffect(effect);
        }

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
        HologramTintCustom.IsChecked = GlassSettings.HologramTint == HologramColor.Custom;
        HologramTintCustom.Checked += (_, _) => GlassSettings.SetHologramTint(HologramColor.Custom);
        DustColorsCustom.IsChecked = GlassSettings.DustColors == DustPalette.Custom;
        DustColorsCustom.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Custom);
        InitializeCustomColors();
        DustColorsTint.IsChecked = GlassSettings.DustColors == DustPalette.Tint;
        DustColorsTint.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Tint);
        DustColorsSunset.IsChecked = GlassSettings.DustColors == DustPalette.Sunset;
        DustColorsSunset.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Sunset);
        DustColorsAurora.IsChecked = GlassSettings.DustColors == DustPalette.Aurora;
        DustColorsAurora.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Aurora);
        DustColorsOcean.IsChecked = GlassSettings.DustColors == DustPalette.Ocean;
        DustColorsOcean.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Ocean);
        DustColorsNeon.IsChecked = GlassSettings.DustColors == DustPalette.Neon;
        DustColorsNeon.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Neon);
        DustColorsRainbow.IsChecked = GlassSettings.DustColors == DustPalette.Rainbow;
        DustColorsRainbow.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Rainbow);
        DustColorsSilk.IsChecked = GlassSettings.DustColors == DustPalette.Silk;
        DustColorsSilk.Checked += (_, _) => GlassSettings.SetDustColors(DustPalette.Silk);

        IntroDrop.IsChecked = GlassSettings.Intro == NowPlayingIntro.Drop;
        IntroDrop.Checked += (_, _) => GlassSettings.SetIntro(NowPlayingIntro.Drop);
        IntroCubes.IsChecked = GlassSettings.Intro == NowPlayingIntro.Cubes;
        IntroCubes.Checked += (_, _) => GlassSettings.SetIntro(NowPlayingIntro.Cubes);
        IntroDust.IsChecked = GlassSettings.Intro == NowPlayingIntro.Dust;
        IntroDust.Checked += (_, _) => GlassSettings.SetIntro(NowPlayingIntro.Dust);
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
            PerformanceLevel.Full => Loc.T("Şu an: Tam"),
            PerformanceLevel.Balanced => Loc.T("Şu an: Dengeli"),
            _ => Loc.T("Şu an: Hafif"),
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
                CornerRadius = new CornerRadius(GlassSettings.Cyber ? 0 : 9),
                Padding = new Thickness(10, 7, 10, 7),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            // the ring overlays the card so its halo can bleed past the card's bounds
            var rim = new Border
            {
                CornerRadius = new CornerRadius(GlassSettings.Cyber ? 0 : 9),
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
        IntensityPanel.Visibility = spectral && !Floe.Core.Services.Product.Cp77 ? Visibility.Visible : Visibility.Collapsed;

        var paletteActive = AuraSettings.Mode == AuraColorMode.Palette;

        foreach (var (row, rim, shell, index) in _rows)
        {
            var selected = paletteActive && index == AuraSettings.PaletteIndex;
            row.Background = new SolidColorBrush(selected
                ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            row.BorderBrush = new SolidColorBrush(selected
                ? (GlassSettings.Cyber ? CyberLook.Current.Strip : Color.FromArgb(0x80, 0x5B, 0xC8, 0xFF))
                : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            // every card wears the ring; the selected one is brighter and sits above its neighbours
            Panel.SetZIndex(shell, selected ? 1 : 0);
        }

        PaletteList.Visibility = paletteActive && !Floe.Core.Services.Product.Cp77 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
