using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;
using Floe.Core.Interop;

namespace Floe.App.Windows;

/// <summary>
/// The dock's trailing sections: now playing, weather, clock and the Quick Settings button.
/// Now playing exists only while music plays; it arrives as a drop of glass that falls onto the
/// dock and melts into it.
/// </summary>
public partial class DockWindow
{
    private const double MediaWidth = 250;
    private const double WeatherWidth = 84;
    private const double ArtSize = 40;
    private const double MediaButton = 30;
    private static readonly TimeSpan MediaLinger = TimeSpan.FromSeconds(4);
    private static readonly Color GlyphColor = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);

    private readonly Grid _media = new() { Width = MediaWidth, Height = ArtSize, Opacity = 0 };
    private readonly Image _mediaArt = new() { Stretch = Stretch.UniformToFill };
    // the cover before a track change, under the new one while that fades in
    private readonly Image _mediaArtBehind = new() { Stretch = Stretch.UniformToFill };
    // the cover fills the whole section, a little past the text, up to the dividers and nearly the bar's height
    private static readonly Thickness CoverBleed = new(-(ClockGap / 2) + 2, -8, -(ClockGap / 2) + 2, -8);
    private const double CoverRadius = 12;
    private readonly TextBlock _mediaTitle = new() { Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _mediaArtist = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ScaleTransform _mediaProgress = new(0, 1);
    private readonly Image _playGlyph = new() { Width = 22, Height = 22, IsHitTestVisible = false };
    private readonly Rectangle _mediaDivider = new();

    private readonly StackPanel _weather = new() { Orientation = Orientation.Horizontal, Width = WeatherWidth, Height = 40 };
    private readonly Image _weatherIcon = new() { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _weatherTemp = new() { Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold, Text = "–" };
    private readonly TextBlock _weatherPlace = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Width = 46 };
    private readonly Rectangle _sectionDivider = new();

    private readonly Grid _quickSettings = new() { Width = StartWidth, Height = StartWidth, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly Rectangle _settingsDivider = new();

    private LiquidDropWindow? _dropWindow;
    private CubesIntroWindow? _cubesIntroWindow;
    private bool _introIsCubes;
    private bool _cubesOutro;
    private double _dropImpact = LiquidDropWindow.ImpactTime;
    private double _dropX;

    private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(15) };

    private double _mediaReveal;
    private double _mediaRevealVel;
    private double _mediaTarget;
    private double _dropTime = -1;
    private DateTime _lastPlaying = DateTime.MinValue;
    private bool _mediaRefreshing;
    private bool _mediaPlaying;
    // the app that plays, so a click on the cover can bring it forward
    private string? _mediaSource;
    private double _restContent;

    /// <summary>Width of everything after the icons, with now playing grown to <paramref name="media"/>.</summary>
    private static double TrailWidth(double media) =>
        ClockGap + ((MediaWidth + ClockGap) * media) + WeatherWidth + TrayWidth + ClockGap + ClockWidth + StartGap + StartWidth + ClockPad;

    private bool SectionsSettled => _dropTime < 0 && !_cubesOutro && Math.Abs(_mediaReveal - _mediaTarget) < 0.002 && Math.Abs(_mediaRevealVel) < 0.01;

    private void InitializeSections()
    {
        foreach (var divider in new[] { _mediaDivider, _sectionDivider, _settingsDivider })
        {
            divider.Width = 1;
            divider.Height = 34;
            divider.RadiusX = divider.RadiusY = 0.5;
            divider.Fill = _clockDivider.Fill;
            divider.IsHitTestVisible = false;
            _clockLayer.Children.Add(divider);
        }

        BuildMedia();
        BuildWeather();
        BuildSettings();
        _clockLayer.Children.Add(_media);
        _clockLayer.Children.Add(_weather);
        _clockLayer.Children.Add(_quickSettings);
        InitializeTray();

        _mediaTimer.Tick += async (_, _) => await PollMediaAsync();
        _mediaTimer.Start();
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        _weatherTimer.Start();
        Closed += (_, _) =>
        {
            _mediaTimer.Stop();
            _weatherTimer.Stop();
            _dropWindow?.Close();
            _cubesIntroWindow?.Close();
        };

        _ = RefreshMediaAsync();
        _ = RefreshWeatherAsync();
    }

    private void BuildMedia()
    {
        _media.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _media.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MediaButton * 3) });
        _media.Children.Add(BuildCover());

        var progress = new Grid { Height = 2, Margin = new Thickness(0, 4, 0, 0) };
        progress.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(1) });
        progress.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(1),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = _mediaProgress,
        });

        var text = new StackPanel { Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_mediaTitle);
        text.Children.Add(_mediaArtist);
        text.Children.Add(progress);
        Grid.SetColumn(text, 0);
        _media.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(GlyphButton(Glyph("previous", 20), MediaButton, () => _ = MediaService.PreviousAsync()));
        _playGlyph.Source = DotMatrixIcons.Render("play", 22, GlyphColor, DpiScale);
        buttons.Children.Add(GlyphButton(_playGlyph, MediaButton, () => _ = TogglePlayAsync()));
        buttons.Children.Add(GlyphButton(Glyph("next", 20), MediaButton, () => _ = MediaService.NextAsync()));
        Grid.SetColumn(buttons, 1);
        _media.Children.Add(buttons);

        // the cover and the titles open the player; the buttons handle their own clicks first
        _media.Background = Brushes.Transparent;
        _media.Cursor = Cursors.Hand;
        _media.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            BringPlayerForward(_mediaSource);
        };
    }

    // The album cover as the section's backdrop: it melts into the glass at both ends, darkens under the text and
    // buttons so they stay readable, and carries the same soft sheen as the glass on top.
    private Grid BuildCover()
    {
        var cover = new Grid { Margin = CoverBleed, IsHitTestVisible = false };
        Grid.SetColumnSpan(cover, 2);
        cover.Children.Add(_mediaArtBehind);
        cover.Children.Add(_mediaArt);

        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, 0, 0, 0), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x5C, 0, 0, 0), 0.45));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, 0, 0, 0), 1));
        shade.Freeze();
        cover.Children.Add(new Border { Background = shade });

        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 0, 0, 0), 1));
        sheen.Freeze();
        cover.Children.Add(new Border { Background = sheen });

        var melt = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        melt.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        melt.GradientStops.Add(new GradientStop(Colors.White, 0.1));
        melt.GradientStops.Add(new GradientStop(Colors.White, 0.9));
        melt.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        melt.Freeze();
        cover.OpacityMask = melt;

        var clip = new RectangleGeometry { RadiusX = CoverRadius, RadiusY = CoverRadius };
        cover.Clip = clip;
        cover.SizeChanged += (_, e) => clip.Rect = new Rect(e.NewSize);
        return cover;
    }

    // a new track's cover fades in over the last one instead of popping in
    private void ShowCover(ImageSource? artwork)
    {
        _mediaArtBehind.Source = _mediaArt.Source;
        _mediaArt.Source = artwork;
        _mediaArt.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void BuildWeather()
    {
        _weatherIcon.Margin = new Thickness(0, 0, 6, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_weatherTemp);
        text.Children.Add(_weatherPlace);
        _weather.Children.Add(_weatherIcon);
        _weather.Children.Add(text);
    }

    private void BuildSettings()
    {
        var glyph = Glyph("settings", StartGlyph);
        glyph.Opacity = 0.82;
        _quickSettings.ToolTip = "Hızlı ayarlar";
        _quickSettings.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        _quickSettings.RenderTransform = scale;
        _quickSettings.Children.Add(glyph);
        _quickSettings.MouseEnter += (_, _) => AnimateGlyph(scale, glyph, 1.12, 1);
        _quickSettings.MouseLeave += (_, _) => AnimateGlyph(scale, glyph, 1, 0.82);
        _quickSettings.MouseLeftButtonUp += (_, _) =>
        {
            var center = _quickSettings.PointToScreen(new Point(StartWidth / 2, StartWidth / 2));
            QuickSettings.OpenAt((int)Math.Round(center.X));
        };
    }

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private Image Glyph(string key, double size) => new()
    {
        Width = size,
        Height = size,
        IsHitTestVisible = false,
        Source = DotMatrixIcons.Render(key, size, GlyphColor, DpiScale),
    };

    private static Grid GlyphButton(Image glyph, double size, Action click)
    {
        var button = new Grid { Width = size, Height = size, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = scale;
        glyph.Opacity = 0.95;
        button.Children.Add(glyph);
        button.MouseEnter += (_, _) => AnimateGlyph(scale, glyph, 1.15, 1);
        button.MouseLeave += (_, _) => AnimateGlyph(scale, glyph, 1, 0.95);
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    private static void AnimateGlyph(ScaleTransform scale, UIElement glyph, double to, double opacity)
    {
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, duration) { EasingFunction = ease });
        glyph.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
    }

    private async Task TogglePlayAsync()
    {
        await MediaService.TogglePlayPauseAsync();
        await RefreshMediaAsync();
    }

    private async Task PollMediaAsync()
    {
        // nothing of the media section can be on screen while the dock is tucked away;
        // the first tick after it comes back refreshes it again
        if (!IsVisible || !_shown || _fullscreenHidden)
        {
            return;
        }

        await RefreshMediaAsync();
    }

    private async Task RefreshMediaAsync()
    {
        if (_mediaRefreshing)
        {
            return;
        }

        _mediaRefreshing = true;
        try
        {
            var media = await MediaService.GetAsync();
            var playing = media is not null && !string.IsNullOrWhiteSpace(media.Title) && await MediaService.IsPlayingAsync();
            if (media is not null && !string.IsNullOrWhiteSpace(media.Title))
            {
                // the poll runs every second - only touch what actually changed
                if (_mediaTitle.Text != media.Title)
                {
                    _mediaTitle.Text = media.Title;
                }

                if (_mediaArtist.Text != media.Artist)
                {
                    _mediaArtist.Text = media.Artist;
                }

                _mediaSource = media.Source;

                // MediaService hands back the same frozen bitmap for the same track
                if (!ReferenceEquals(_mediaArt.Source, media.Artwork))
                {
                    ShowCover(media.Artwork);
                }

                // sub-pixel progress moves are not worth invalidating the transform
                if (Math.Abs(_mediaProgress.ScaleX - media.Progress) > 0.001)
                {
                    _mediaProgress.ScaleX = media.Progress;
                }
            }

            if (playing != _mediaPlaying)
            {
                _mediaPlaying = playing;
                _playGlyph.Source = DotMatrixIcons.Render(playing ? "pause" : "play", 22, GlyphColor, DpiScale);
            }

            if (playing)
            {
                _lastPlaying = DateTime.Now;
                if (_mediaTarget == 0 && _dropTime < 0)
                {
                    StartDrop();
                }
            }
            else if (_mediaTarget > 0 && (media is null || DateTime.Now - _lastPlaying > MediaLinger))
            {
                // a closed player leaves at once; a short pause from the dock itself should not
                _mediaTarget = 0;
                StartCubesOutro();
                EnsureAnimation();
            }
        }
        catch (Exception ex)
        {
            Log($"media section: {ex.Message}");
        }
        finally
        {
            _mediaRefreshing = false;
        }
    }

    private async Task RefreshWeatherAsync()
    {
        try
        {
            var weather = await WeatherService.GetAsync();
            if (weather is null)
            {
                return;
            }

            _weatherTemp.Text = $"{Math.Round(weather.Temperature)}°";
            _weatherPlace.Text = weather.Location;
            _weatherIcon.Source = DotMatrixIcons.Render(weather.Condition, 30, Colors.White, DpiScale);
        }
        catch (Exception ex)
        {
            Log($"weather section: {ex.Message}");
        }
    }

    private void StartDrop()
    {
        if (!IsVisible || !_shown)
        {
            _mediaTarget = 1;
            EnsureAnimation();
            return;
        }

        _introIsCubes = GlassSettings.Intro is NowPlayingIntro.Cubes or NowPlayingIntro.Dust;
        _dropImpact = _introIsCubes ? CubesIntroWindow.ImpactTime : LiquidDropWindow.ImpactTime;
        if (_introIsCubes)
        {
            _cubesOutro = false;
            _cubesIntroWindow ??= new CubesIntroWindow(!NoFx.Contains("exclude"));
            _cubesIntroWindow.Start(SlotOnScreen(), _mediaArt.Source, _mediaPlaying, SlotScale(), GlassSettings.Intro == NowPlayingIntro.Dust);
        }
        else
        {
            _dropWindow ??= new LiquidDropWindow(!NoFx.Contains("exclude"));
            _dropWindow.Start(RimOnScreen());
        }

        _dropTime = 0;
        EnsureAnimation();
    }

    /// <summary>Where the drop lands: the dock's top edge above the now-playing slot, in screen DIP.</summary>
    private Point RimOnScreen()
    {
        // the glass edge the eye reads sits a few DIP inside the capsule bounds
        var p = _clockLayer.PointToScreen(new Point(_dropX, RestTop + 7));
        return new Point(p.X / DpiScale, p.Y / DpiScale);
    }

    /// <summary>Where the cubes build the section: the middle of the now-playing slot, in screen DIP.</summary>
    private Point SlotOnScreen()
    {
        // _dropX centres the gap with its divider; the section itself sits half a divider gap to the left
        var p = _clockLayer.PointToScreen(new Point(_dropX - (ClockGap * Math.Max(0, _mediaReveal) / 2), RestTop + (RestBarHeight / 2)));
        return new Point(p.X / DpiScale, p.Y / DpiScale);
    }

    /// <summary>How large the sections are drawn on screen in DIP per layout DIP: below 1 while the dock is shrunk.</summary>
    private double SlotScale()
    {
        var a = _clockLayer.PointToScreen(new Point(0, 0));
        var b = _clockLayer.PointToScreen(new Point(100, 0));
        var scale = (b.X - a.X) / (100 * DpiScale);
        return scale > 0.1 && scale < 4 ? scale : 1;
    }

    /// <summary>With the cubes or dust intro the section breaks back into cubes or dust that scatter as it closes.</summary>
    private void StartCubesOutro()
    {
        if (GlassSettings.Intro is not (NowPlayingIntro.Cubes or NowPlayingIntro.Dust) || !IsVisible || !_shown || _mediaReveal < 0.3)
        {
            return;
        }

        _dropTime = -1;
        _introIsCubes = true;
        _cubesIntroWindow ??= new CubesIntroWindow(!NoFx.Contains("exclude"));
        _cubesIntroWindow.StartOutro(SlotOnScreen(), _mediaArt.Source, _mediaPlaying, SlotScale(), GlassSettings.Intro == NowPlayingIntro.Dust);
        _cubesOutro = true;
    }

    /// <summary>Advances the drop and the now-playing width; called from the dock's frame loop.</summary>
    private void AnimateSections(double dt)
    {
        if (_dropTime >= 0)
        {
            _dropTime += dt;
            if (_dropTime >= _dropImpact && _mediaTarget == 0)
            {
                // impact: the dock opens up to take the drop in
                _mediaTarget = 1;
            }

            var advanced = _introIsCubes
                ? _cubesIntroWindow?.Advance(dt, SlotOnScreen(), SlotScale()) ?? false
                : _dropWindow?.Advance(dt, RimOnScreen().X) ?? false;
            if (!advanced)
            {
                _dropTime = -1;
            }
        }

        if (_cubesOutro)
        {
            _cubesOutro = _cubesIntroWindow?.Advance(dt, SlotOnScreen(), SlotScale()) ?? false;
        }

        if (_metalEffect is not null)
        {
            // the metal rim lights up where the drop lands and the glints run off along it
            var age = _dropTime - _dropImpact;
            _metalEffect.FlashAge = _dropTime >= 0 && age >= 0 && !_introIsCubes ? age : -1;
            _metalEffect.FlashX = _dropX - _capsuleLeft;
        }

        // a soft spring: the opening overshoots a touch, like liquid settling
        _mediaReveal = Math.Max(0, Spring(_mediaReveal, ref _mediaRevealVel, _mediaTarget, dt, 150, 17));
        if (SectionsSettled)
        {
            _mediaReveal = _mediaTarget;
            _mediaRevealVel = 0;
        }

        _restCapsuleWidth = LeadX + _restContent + TrailWidth(_mediaReveal);
        _restCapsuleLeft = (_windowWidth - _restCapsuleWidth) / 2;
    }

    /// <summary>Lays the sections out from the end of the icon row.</summary>
    private void PositionSections(double x)
    {
        var reveal = Math.Max(0, _mediaReveal);
        var lineTop = RestTop + ((RestBarHeight - _sectionDivider.Height) / 2);

        Canvas.SetLeft(_sectionDivider, x + (ClockGap / 2));
        Canvas.SetTop(_sectionDivider, lineTop);
        x += ClockGap;

        // now playing grows from its centre; its content fades in once there is room for it
        var mediaSpan = (MediaWidth + ClockGap) * reveal;
        Canvas.SetLeft(_media, x + ((mediaSpan - ClockGap * reveal - MediaWidth) / 2));
        Canvas.SetTop(_media, RestTop + ((RestBarHeight - ArtSize) / 2));
        // while the cubes stand in for the section the real one stays hidden under them
        var veil = _introIsCubes && _cubesIntroWindow is not null ? _cubesIntroWindow.ContentVeil : 1;
        _media.Opacity = Math.Clamp((reveal - 0.55) / 0.4, 0, 1) * veil;
        _media.IsHitTestVisible = reveal > 0.9;
        _media.Visibility = reveal > 0.01 ? Visibility.Visible : Visibility.Collapsed;
        x += mediaSpan;
        Canvas.SetLeft(_mediaDivider, x - (ClockGap / 2));
        Canvas.SetTop(_mediaDivider, lineTop);
        _mediaDivider.Opacity = Math.Clamp(reveal, 0, 1);

        _dropX = x - (mediaSpan / 2);

        Canvas.SetLeft(_weather, x);
        Canvas.SetTop(_weather, RestTop + ((RestBarHeight - _weather.Height) / 2));
        x += WeatherWidth;

        // the ^ for the background apps sits before the clock, as Windows has it
        Canvas.SetLeft(_tray, x);
        Canvas.SetTop(_tray, RestTop + ((RestBarHeight - StartWidth) / 2));
        x += TrayWidth;

        Canvas.SetLeft(_clockDivider, x + (ClockGap / 2));
        Canvas.SetTop(_clockDivider, lineTop);
        x += ClockGap;
        Canvas.SetLeft(_clock, x);
        Canvas.SetTop(_clock, RestTop + 6);
        x += ClockWidth;

        Canvas.SetLeft(_settingsDivider, x + (StartGap / 2));
        Canvas.SetTop(_settingsDivider, lineTop);
        x += StartGap;
        Canvas.SetLeft(_quickSettings, x);
        Canvas.SetTop(_quickSettings, RestTop + ((RestBarHeight - StartWidth) / 2));
    }
}
