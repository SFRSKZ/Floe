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
using Floe.App.ViewModels;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // The cyber themes swap the glass capsule for flat plates with cut corners: one bar, or separate modules for the
    // start button, the icons, the sections and the clock. The plates share the glass's geometry, so the shadow and
    // the click-through follow them. Nothing bulges; the hovered icon stands on a plate of its own instead.

    private readonly Path _slotFill = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Fill = CyberFxLayer.Solid(FxInk) };
    private static double CyberChamfer => 14 * GlassSettings.CyberCut;
    private const double DecalRoom = 160;
    private const int SecondBars = 6;
    private const double ClockLight = 11;

    private readonly Path _cyberAccent = new() { IsHitTestVisible = false };
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
    private readonly Path[] _secondBars = new Path[SecondBars];
    private readonly Path _decalBox = new() { StrokeThickness = 1.5 };
    private readonly Path _decalBoxEdge = new() { StrokeThickness = 3.5 };
    private readonly Path _decalCross = new() { StrokeThickness = 1.5, Data = Geometry.Parse("M0,0 L16,16 M16,0 L0,16") };
    private readonly Path _decalCrossEdge = new() { StrokeThickness = 3.5 };
    private readonly RotateTransform _crossTurn = new() { CenterX = 8, CenterY = 8 };
    private double _crossAngle;
    private int _shownSecond = -1;
    private int _shownTens = -1;
    private readonly Path _decalText = new(), _decalTextEdge = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
    private readonly DispatcherTimer _secondsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitializeCyber()
    {
        ShapeLayer.Children.Add(_cyberAccent);
        InitializeCyberGrain();
        ShapeLayer.Children.Add(_cyberDecal);

        // the markings hang straight on the wallpaper; each carries a thin dark edge, so they read on any desktop
        var edge = CyberFxLayer.Solid(DecalBacking);
        _decalTextEdge.Stroke = edge;
        _cyberDecal.Children.Add(_decalTextEdge);
        _cyberDecal.Children.Add(_decalText);
        for (var i = 0; i < SecondBars; i++)
        {
            double x = i * 17;
            _secondBars[i] = new Path { Data = Geometry.Parse($"M{x + 14},0 h12 L{x + 12},30 h-12 Z"), Stroke = edge, StrokeThickness = 1 };
            Canvas.SetTop(_secondBars[i], 28);
            _cyberDecal.Children.Add(_secondBars[i]);
        }

        _decalBox.Data = Geometry.Parse("M0,0 H16 V16 H0 Z");
        _decalBoxEdge.Data = _decalBox.Data;
        _decalBoxEdge.Stroke = edge;
        _decalCrossEdge.Data = _decalCross.Data;
        _decalCrossEdge.Stroke = edge;
        _decalCross.RenderTransform = _decalCrossEdge.RenderTransform = _crossTurn;
        foreach (var mark in new[] { _decalBoxEdge, _decalCrossEdge, _decalBox, _decalCross })
        {
            Canvas.SetLeft(mark, 124);
            Canvas.SetTop(mark, 35);
            _cyberDecal.Children.Add(mark);
        }

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

    // every frame with the layout: the most magnified icon takes the item frame
    private void UpdateCyberHover(IReadOnlyList<double> scales)
    {
        if (!GlassSettings.Cyber || scales.Count == 0 || _isDragging)
        {
            SetFxHover(null);
            PlaceSlotFill(null);
            foreach (var slot in _slots) slot.LineBrush.Color = CyberLook.Current.Ink;
            return;
        }

        var index = 0;
        for (var i = 1; i < scales.Count; i++)
        {
            if (scales[i] > scales[index]) index = i;
        }

        var lift = (scales[index] - 1) / (HoverMaxScale - 1);
        lift = Math.Round(lift * 6) / 6; // it climbs in steps, it does not swell
        var hovered = lift > .3 ? _slots[index] : null;
        SetFxHover(hovered);
        PlaceSlotFill(hovered);

        // the frame's inside is dark on every theme, where the ink would vanish
        var look = CyberLook.Current;
        foreach (var slot in _slots)
        {
            var color = ReferenceEquals(slot, hovered) ? CyberLook.Readable(look.Accent, FxInk) : look.Ink;
            if (slot.LineBrush.Color != color) slot.LineBrush.Color = color;
        }
    }

    // the frame's inside sits under the icons, its lines over them
    private void PlaceSlotFill(IconSlot? slot)
    {
        _slotFill.Visibility = slot is null ? Visibility.Collapsed : Visibility.Visible;
        if (slot is not null) _slotFill.Data = CyberFxLayer.SlotOutline(SlotFrameBounds(slot));
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
            _decalText.Fill = CyberFxLayer.Solid(Color.FromRgb(0xF3, 0xEF, 0xDC));
            var words = new FormattedText(
                "BATCH A7-53X" + Environment.NewLine + "[ACCESS RESTRICTED]",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(CyberLook.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                11,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { LineHeight = 13 };
            _decalText.Data = _decalTextEdge.Data = words.BuildGeometry(default);
            ShowSeconds();
        }

        bool barcode = cyber;
        _settingsDivider.Visibility = barcode ? Visibility.Collapsed : Visibility.Visible;
        _barcodeTag.Visibility = barcode ? Visibility.Visible : Visibility.Collapsed;
        _barcodeTag.Paint(look.Ink);

        // the clock's divider is a warning light on the HUD
        _clockDivider.Width = barcode ? ClockLight : 1;
        _clockDivider.Height = barcode ? ClockLight : 34;
        _clockDivider.RadiusX = _clockDivider.RadiusY = barcode ? 0 : .5;
        _clockDivider.Fill = barcode ? CyberFxLayer.Solid(look.Accent) : _lineBrush;
        _clockDivider.Opacity = 1;
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

    // the cyber themes draw the apps of the outline set in the ink, as one family; the rest keep their own artwork
    private void ApplyCyberIcon(IconSlot slot)
    {
        if (slot.Button.Content is not Grid { Children: [Image artwork, Rectangle line, ..] }) return;
        var mask = ContourMask(slot.Item);
        artwork.Visibility = mask is null ? Visibility.Visible : Visibility.Hidden;
        line.Visibility = mask is null ? Visibility.Collapsed : Visibility.Visible;
        slot.LineBrush.Color = CyberLook.Current.Ink;
        line.Fill = slot.LineBrush;
        line.OpacityMask = mask;
    }

    private static ImageBrush? ContourMask(DockItemViewModel item)
    {
        if (!GlassSettings.Cyber || !GlassSettings.ContourIcons) return null;
        if (IconChoices.SetIcon(item.App) is { } file) return ContourIcons.Load(file);
        return IconChoices.HasChoice(item.App) ? null : ContourIcons.Mask(item.Name, item.ProcessName);
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
