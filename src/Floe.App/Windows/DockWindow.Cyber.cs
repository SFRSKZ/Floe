using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Effects.Cyber;
using Floe.App.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // The cyber themes swap the glass capsule for flat plates with cut corners: one bar, or separate modules for the
    // start button, the icons, the sections and the clock. The plates share the glass's geometry, so the shadow and
    // the click-through follow them. Nothing bulges; the hovered icon stands on a plate of its own instead.

    private static double CyberChamfer => 14 * GlassSettings.CyberCut;
    private static double PlateChamfer => 9 * GlassSettings.CyberCut;
    private const double DecalRoom = 160;
    private const int SecondBars = 6;
    private const double ClockLight = 11;

    private readonly Path _cyberAccent = new() { IsHitTestVisible = false };
    private readonly Path _cyberPlate = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly List<(Image Image, string Key, double Size)> _inkGlyphs = new();
    private string? _weatherCondition;
    private double _sectionsX;
    private double _clockX;
    private Color? _appliedDockColor;
    private CyberLook? _appliedCyberLook;
    private double _appliedCut = 1;

    // the markings beside the bar; the six slanted bars count the minute, one more every ten seconds
    private readonly Canvas _cyberDecal = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private static readonly Color DecalBacking = Color.FromRgb(0x0A, 0x0A, 0x10);
    private readonly Path _decalPlate = new() { IsHitTestVisible = false };
    private readonly Path[] _secondBars = new Path[SecondBars];
    private readonly Path _decalBox = new() { StrokeThickness = 1.5 };
    private readonly Path _decalCross = new() { StrokeThickness = 1.5, Data = Geometry.Parse("M0,0 L16,16 M16,0 L0,16") };
    private readonly RotateTransform _crossTurn = new() { CenterX = 8, CenterY = 8 };
    private double _crossAngle;
    private int _shownSecond = -1;
    private int _shownTens = -1;
    private readonly TextBlock _decalText = new() { Text = "BATCH A7-53X" + Environment.NewLine + "[ACCESS RESTRICTED]", FontSize = 11, LineHeight = 13, FontWeight = FontWeights.SemiBold };
    private readonly DispatcherTimer _secondsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitializeCyber()
    {
        ShapeLayer.Children.Add(_cyberAccent);
        ShapeLayer.Children.Add(_cyberPlate);
        InitializeCyberGrain();
        ShapeLayer.Children.Add(_cyberDecal);

        // the markings hang on the wallpaper, so they stand on a small dark plate of their own and read on any desktop
        _decalPlate.Data = CyberFxLayer.Plate(new Rect(-6, -6, 152, 70), 6);
        _decalPlate.Fill = CyberFxLayer.Solid(DecalBacking);
        _cyberDecal.Children.Add(_decalPlate);
        _cyberDecal.Children.Add(_decalText);
        for (var i = 0; i < SecondBars; i++)
        {
            double x = i * 17;
            _secondBars[i] = new Path { Data = Geometry.Parse($"M{x + 14},0 h12 L{x + 12},30 h-12 Z") };
            Canvas.SetTop(_secondBars[i], 28);
            _cyberDecal.Children.Add(_secondBars[i]);
        }

        _decalBox.Data = Geometry.Parse("M0,0 H16 V16 H0 Z");
        Canvas.SetLeft(_decalBox, 124);
        Canvas.SetTop(_decalBox, 35);
        _cyberDecal.Children.Add(_decalBox);
        _decalCross.RenderTransform = _crossTurn;
        Canvas.SetLeft(_decalCross, 124);
        Canvas.SetTop(_decalCross, 35);
        _cyberDecal.Children.Add(_decalCross);

        _secondsTimer.Tick += (_, _) => ShowSeconds();
        _secondsTimer.Start();
    }

    private void ShowSeconds()
    {
        if (!GlassSettings.Cyber) return;
        var second = DateTime.Now.Second;
        _clockDivider.Opacity = second % 2;
        var lit = (second + 1) / 10;
        var look = CyberLook.Current;
        var unlit = CyberLook.Readable(look.Body, DecalBacking);
        var off = CyberFxLayer.Solid(unlit);
        // lit bars take the strip's colour when it tells them from the unlit ones
        var on = CyberFxLayer.Solid(CyberLook.Contrast(look.Strip, unlit) >= 2 ? look.Strip : FxCyan);
        for (var i = 0; i < SecondBars; i++) _secondBars[i].Fill = i < lit ? on : off;

        // the cross ticks like a watch, and every ten seconds it spins round in the glitch's blue; a repaint within
        // the same second must not tick it again, and it aims at a set angle so an interrupted turn can't leave it crooked
        bool ten = _shownTens >= 0 && lit != _shownTens;
        if (second == _shownSecond) return;
        _shownSecond = second;
        _shownTens = lit;
        _decalCross.Stroke = ten ? on : off;
        if (!ten && second % 2 != 0) return;
        _crossAngle += ten ? 270 : 90;
        var turn = new DoubleAnimation { To = _crossAngle, Duration = TimeSpan.FromSeconds(ten ? .5 : .22), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _crossTurn.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    // a barcode stands in for the last divider only; a whole row of them is noise
    private static Brush Barcode(Color ink)
    {
        var rows = new GeometryGroup();
        double y = 0;
        var i = 0;
        int[] pattern = { 3, 1, 2, 1, 4, 2, 1, 3, 1, 1, 2, 3, 1, 2, 3, 1 };
        while (y < 34)
        {
            double row = Math.Min(pattern[i % pattern.Length], 34 - y);
            rows.Children.Add(new RectangleGeometry(new Rect(0, y, 10, row)));
            y += row + pattern[(i + 5) % pattern.Length];
            i++;
        }

        var brush = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(ink), null, rows)) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        brush.Freeze();
        return brush;
    }

    private static void AddPlate(PathGeometry target, double left, double top, double right, double bottom, double chamfer)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = true, StartPoint = new Point(left + chamfer, top) };
        figure.Segments.Add(new PolyLineSegment(new[]
        {
            new Point(right, top),
            new Point(right, bottom - chamfer),
            new Point(right - chamfer, bottom),
            new Point(left, bottom),
            new Point(left, top + chamfer),
        }, false));
        target.Figures.Add(figure);
    }

    private void BuildCyberShape()
    {
        double left = _capsuleLeft, right = _capsuleLeft + _capsuleWidth, top = RestTop, bottom = Bottom;
        var accent = new PathGeometry();
        _shapeGeometry.Figures.Clear();
        AddPlate(_shapeGeometry, left, top, right, bottom, CyberChamfer);
        AddPlate(accent, left + 20, bottom + 4, left + 110, bottom + 8, 0);
        _cyberAccent.Data = accent;
        Canvas.SetLeft(_cyberDecal, right + 12);
        Canvas.SetTop(_cyberDecal, top + 6);
    }

    // every frame with the layout: the plate behind the most magnified icon, as tall as the icon stands
    private void UpdateCyberPlate(IReadOnlyList<double> scales)
    {
        if (!GlassSettings.Cyber || scales.Count == 0)
        {
            SetFxHover(null);
            _cyberPlate.Visibility = Visibility.Collapsed;
            return;
        }

        // a held icon leaves its plate behind, and in the plate's colour it would vanish into the bar
        if (_isDragging)
        {
            SetFxHover(null);
            _cyberPlate.Visibility = Visibility.Collapsed;
            foreach (var held in _slots) held.LineBrush.Color = CyberLook.Current.Ink;
            return;
        }

        var index = 0;
        for (var i = 1; i < scales.Count; i++)
        {
            if (scales[i] > scales[index]) index = i;
        }

        var lift = (scales[index] - 1) / (HoverMaxScale - 1);
        lift = Math.Round(lift * 6) / 6; // it climbs in steps, it does not swell
        SetFxHover(lift > .3 ? _slots[index] : null);
        var look = CyberLook.Current;
        for (var i = 0; i < _slots.Count; i++)
        {
            var color = i == index && lift > 0.35 ? CyberLook.Readable(look.Accent, look.Ink) : look.Ink;
            if (_slots[i].LineBrush.Color != color) _slots[i].LineBrush.Color = color;
        }

        if (lift < 0.04)
        {
            _cyberPlate.Visibility = Visibility.Collapsed;
            return;
        }

        var slot = _slots[index];
        var half = ((IconBox * scales[index]) / 2) + 5;
        var plate = new PathGeometry();
        AddPlate(plate, slot.Center - half, IconBottom - (IconBox * scales[index]) - 6, slot.Center + half, Bottom, PlateChamfer);
        _cyberPlate.Data = plate;
        _cyberPlate.Opacity = Math.Min(1, lift * 2);
        _cyberPlate.Visibility = Visibility.Visible;
    }

    // the text and glyphs that sit on the dock itself
    private void ApplyCyberInk()
    {
        bool cyber = GlassSettings.Cyber;
        UpdateCyberGrain();
        var look = CyberLook.Current;
        var ink = cyber ? look.Ink : Colors.White;
        var brush = new SolidColorBrush(ink);
        brush.Freeze();
        var soft = new SolidColorBrush(Color.FromArgb(0xB0, ink.R, ink.G, ink.B));
        soft.Freeze();
        _clockTime.Foreground = _clockDate.Foreground = _weatherTemp.Foreground = brush;
        _weatherPlace.Foreground = soft;

        var body = cyber ? CyberLook.Font : FontFamily;
        _clockTime.FontFamily = _clockDate.FontFamily = cyber ? CyberLook.Font : _clockFont;
        _clockTime.FontSize = cyber ? 31 : 26;
        _clockDate.FontSize = cyber ? 15 : 14;
        _clockDate.Opacity = cyber ? 1 : 0.9;
        _weatherTemp.FontSize = cyber ? 20 : 17;
        _weatherPlace.FontSize = cyber ? 13 : 10;
        _weatherPlace.Foreground = cyber ? brush : soft;
        _weatherPlace.Width = cyber ? 56 : 46;
        _weatherIcon.Width = _weatherIcon.Height = cyber ? 26 : 30;
        _weatherIcon.Margin = new Thickness(0, 0, cyber ? 4 : 6, 0);
        _weatherTemp.FontFamily = _weatherPlace.FontFamily = _mediaTitle.FontFamily = _mediaArtist.FontFamily = _tooltipText.FontFamily = cyber ? CyberLook.Font : new FontFamily("Segoe UI");

        foreach (var text in new[] { _clockTime, _clockDate })
        {
            if (text.Effect is DropShadowEffect halo && cyber) halo.Opacity = 0;
        }

        byte line = !cyber ? (byte)0x2E : (byte)0x66;
        _lineBrush.Color = Color.FromArgb(line, ink.R, ink.G, ink.B);
        ((SolidColorBrush)_separator.Fill).Color = cyber ? Color.FromArgb(0x66, ink.R, ink.G, ink.B) : Color.FromArgb(0x6E, 0xFF, 0xFF, 0xFF);
        _runningDotBrush.Color = cyber ? CyberLook.Readable(look.Accent, look.Body) : Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);

        var accent = new SolidColorBrush(look.Accent);
        accent.Freeze();
        _cyberAccent.Fill = accent;
        _cyberAccent.Visibility = cyber ? Visibility.Visible : Visibility.Collapsed;
        _cyberDecal.Visibility = _cyberAccent.Visibility;
        if (cyber)
        {
            _decalBox.Stroke = CyberFxLayer.Solid(CyberLook.Readable(look.Body, DecalBacking));
            _decalText.Foreground = CyberFxLayer.Solid(Color.FromRgb(0xF3, 0xEF, 0xDC));
            _decalText.FontFamily = CyberLook.Font;
            ShowSeconds();
        }

        bool barcode = cyber;
        _settingsDivider.Width = barcode ? 10 : 1;
        _settingsDivider.Fill = barcode ? Barcode(ink) : _lineBrush;

        // the clock's divider is a warning light on the HUD
        _clockDivider.Width = barcode ? ClockLight : 1;
        _clockDivider.Height = barcode ? ClockLight : 34;
        _clockDivider.RadiusX = _clockDivider.RadiusY = barcode ? 0 : .5;
        _clockDivider.Fill = barcode ? CyberFxLayer.Solid(look.Accent) : _lineBrush;
        _clockDivider.Opacity = 1;
        var plate = new SolidColorBrush(look.Ink);
        plate.Freeze();
        _cyberPlate.Fill = plate;
        _grain.Visibility = cyber ? Visibility.Collapsed : Visibility.Visible;

        foreach (var (image, key, size) in _inkGlyphs)
        {
            image.Source = cyber ? CyberIcons.Draw(CyberGlyph(key), brush) : DotMatrixIcons.Render(key, size, InkGlyphColor, DpiScale);
            image.Opacity = cyber ? 1 : 0.82;
        }

        if (_weatherCondition is not null) _weatherIcon.Source = WeatherGlyph(_weatherCondition);
        ApplyCyberMedia();
        UpdateStartGlyph();
        foreach (var slot in _slots) ApplyCyberIcon(slot);
    }

    // the cyber themes draw every app as a line icon in the ink, as one family, and hide its own artwork
    private void ApplyCyberIcon(IconSlot slot)
    {
        if (slot.Button.Content is not Grid { Children: [Image artwork, Image line, ..] }) return;
        bool cyber = GlassSettings.Cyber;
        artwork.Visibility = cyber ? Visibility.Hidden : Visibility.Visible;
        line.Visibility = cyber ? Visibility.Visible : Visibility.Collapsed;
        if (!cyber) return;
        slot.LineBrush.Color = CyberLook.Current.Ink;
        line.Source ??= CyberIcons.ForApp(slot.Item.Name, slot.LineBrush);
    }

    private static string CyberGlyph(string key) => key == "settings" ? "adjustments-horizontal" : "chevron-up";

    private ImageSource? WeatherGlyph(string condition) => GlassSettings.Cyber
        ? CyberIcons.Weather(condition, CyberFxLayer.Solid(CyberLook.Current.Ink))
        : DotMatrixIcons.Render(condition, 30, InkGlyphColor, DpiScale);

    private ImageSource? MediaGlyphSource(string key, double size) => GlassSettings.Cyber
        ? CyberIcons.Draw($"floe-{key}", CyberFxLayer.Solid(CyberLook.Current.Ink))
        : DotMatrixIcons.Render(key, size, GlyphColor, DpiScale);

    private Image MediaGlyph(string key, double size)
    {
        var glyph = Glyph(key, size);
        _mediaGlyphs.Add((glyph, key, size));
        return glyph;
    }

    // glass: the cover spreads under the text, darkened and melting away. Cyber: a square plate on the dock's own
    // colour, with the text and the buttons in its ink
    private void ApplyCyberMedia()
    {
        bool cyber = GlassSettings.Cyber;
        var ink = cyber ? CyberLook.Current.Ink : Colors.White;
        Brush Tone(byte alpha) => CyberFxLayer.Solid(Color.FromArgb(alpha, ink.R, ink.G, ink.B));

        _media.Width = MediaWidth;
        _cover.Margin = cyber ? new Thickness(0, -8, 0, -8) : CoverBleed;
        _cover.HorizontalAlignment = cyber ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _cover.Width = cyber ? CoverSquare : double.NaN;
        _cover.OpacityMask = cyber ? null : _coverMelt;
        _cover.Clip = cyber ? CyberFxLayer.Plate(new Rect(0, 0, CoverSquare, CoverSquare), CoverChamfer) : _coverClip;
        foreach (var glass in _coverGlass) glass.Visibility = cyber ? Visibility.Collapsed : Visibility.Visible;

        _mediaText.Margin = cyber ? new Thickness(CoverSquare + 8, 0, 2, 0) : new Thickness(6, 0, 6, 0);
        _mediaTitle.Foreground = Tone(0xFF);
        _mediaArtist.Foreground = Tone(cyber ? (byte)0xFF : (byte)0xB0);
        _mediaTitle.FontSize = cyber ? 16 : 12.5;
        _mediaArtist.FontSize = cyber ? 13 : 10.5;
        _mediaTrack.Background = Tone(0x33);
        _mediaFill.Background = cyber ? CyberFxLayer.Solid(CyberLook.Current.Accent) : Tone(0xDD);
        _mediaTrack.CornerRadius = _mediaFill.CornerRadius = new CornerRadius(cyber ? 0 : 1);

        foreach (var (image, key, size) in _mediaGlyphs) image.Source = MediaGlyphSource(key, size);
        _playGlyph.Source = MediaGlyphSource(_mediaPlaying ? "pause" : "play", 22);
    }

    private Color InkGlyphColor => GlassSettings.Cyber ? CyberLook.Current.Ink : GlyphColor;

    private Color StartGlyphColor => GlassSettings.Cyber ? CyberLook.Current.Ink : DesktopButtonSettings.Color;
}
