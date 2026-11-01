using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;

namespace Floe.App.Windows;

// The CP77 studio: a small dock on top, and clicking one of its parts opens that part's colour below it.
public partial class SettingsWindow
{
    private enum StudioPart
    {
        Body,
        Accent,
        Strip,
        Ink,
    }

    private static Color Rgb(uint hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private static readonly (string Name, Color? Body, Color? Accent, Color? Strip, Color? Ink)[] StudioPacks =
    {
        ("Varsayılan", null, null, null, null),
        ("Arasaka", Rgb(0xFF003C), Rgb(0xFCEE0A), Rgb(0xFF003C), null),
        ("Netrunner", Rgb(0x00E5FF), Rgb(0xE00038), Rgb(0x00E5FF), null),
        ("Pacifica", Rgb(0xFF2D95), Rgb(0xCFFFFF), Rgb(0xFF2D95), null),
        ("Militech", Rgb(0x1F7A1F), Rgb(0xFCEE0A), Rgb(0xFCEE0A), Rgb(0xFCEE0A)),
        ("Kiroshi", Rgb(0xECECE6), Rgb(0xFF003C), Rgb(0xFCEE0A), null),
        ("Blackwall", Rgb(0x14141A), Rgb(0xFF003C), Rgb(0xFF003C), null),
    };

    private static readonly uint[] StudioSwatches = { 0xFCEE0A, 0xFF003C, 0x00E5FF, 0xFF2D95, 0x39FF88, 0xECECE6, 0xFF8A00, 0x14141A };

    private static readonly Color Stage = Color.FromRgb(0x0B, 0x0B, 0x10);
    private static readonly Color PanelEdge = Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF);
    private readonly Canvas _studioDock = new() { Width = 360, Height = 84, Background = new SolidColorBrush(Stage), ClipToBounds = true };
    private readonly WrapPanel _studioPacks = new() { Margin = new Thickness(0, 0, 0, 2) };
    private readonly Dictionary<StudioPart, RadioButton> _studioTabs = new();
    private readonly Slider _studioHue = new() { Minimum = 0, Maximum = 360 };
    private readonly Slider _studioSaturation = new() { Minimum = 0, Maximum = 1 };
    private readonly Slider _studioValue = new() { Minimum = 0.05, Maximum = 1 };
    private DispatcherTimer? _studioTimer;
    private StudioPart _part;
    private Color? _studioBody, _studioAccent, _studioStrip, _studioInk;
    private bool _syncingStudio;
    private Button? _studioReset;

    /// <summary>Opens on the studio, for the dock's menu.</summary>
    public void ShowStudio() => TabGlass.IsChecked = true;

    private CyberLook StudioLook => CyberLook.For(_studioBody, _studioAccent, _studioStrip, _studioInk);

    private void InitializeStudio()
    {
        _studioBody = GlassSettings.DockColor;
        _studioAccent = GlassSettings.CyberAccent;
        _studioStrip = GlassSettings.CyberStrip;
        _studioInk = GlassSettings.CyberInk;

        var parts = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var (part, name, tip) in new[]
        {
            (StudioPart.Body, "Gövde", "Dock'un rengi"),
            (StudioPart.Accent, "Vurgu", "Üstüne gelinen ikonun çerçevesi, çalışan noktaları, medya çubuğu"),
            (StudioPart.Strip, "Şerit", "Menü kenarları, HUD yazıları, seçili düğmeler"),
            (StudioPart.Ink, "Yazı", "Saat ve yazılar"),
        })
        {
            var tab = new RadioButton { GroupName = "StudioPart", Style = (Style)FindResource("ModeToggle"), Content = name, ToolTip = tip };
            tab.Checked += (_, _) => SelectPart(part);
            _studioTabs[part] = tab;
            parts.Children.Add(tab);
        }

        var swatches = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var hex in StudioSwatches)
        {
            var color = Rgb(hex);
            var swatch = new Border { Width = 28, Height = 20, Margin = new Thickness(0, 0, 6, 6), Background = new SolidColorBrush(color), BorderBrush = new SolidColorBrush(PanelEdge), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            swatch.MouseLeftButtonDown += (_, _) => SetPart(color);
            swatches.Children.Add(swatch);
        }

        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int k = 0; k <= 6; k++) hues.GradientStops.Add(new GradientStop(FromHsv(k * 60, 1, 1), k / 6.0));
        hues.Freeze();
        _studioHue.Background = hues;
        foreach (var slider in new[] { _studioHue, _studioSaturation, _studioValue })
        {
            slider.Style = (Style)FindResource("ColorSlider");
            slider.ValueChanged += (_, _) => StudioSliderChanged();
        }

        var cut = new Slider { Style = (Style)FindResource("GlassSlider"), Minimum = 0, Maximum = 2, Value = GlassSettings.CyberCut };
        cut.ValueChanged += (_, _) =>
        {
            GlassSettings.SetCyberCut(cut.Value);
            DrawStudioDock();
        };
        var grain = new Slider { Style = (Style)FindResource("GlassSlider"), Minimum = 0.3, Maximum = 2, Value = GlassSettings.GrainAmount };
        grain.ValueChanged += (_, _) => GlassSettings.SetGrainAmount(grain.Value);

        var contour = new CheckBox
        {
            Style = (Style)FindResource("GlassToggle"),
            Content = "Kontur ikonlar",
            ToolTip = "Uygulamaların kendi ikonları yerine tek renk çizgi ikonlar",
            IsChecked = GlassSettings.ContourIcons,
        };
        contour.Checked += (_, _) => GlassSettings.SetContourIcons(true);
        contour.Unchecked += (_, _) => GlassSettings.SetContourIcons(false);

        var reset = new Button { Style = (Style)FindResource("GlassButton"), Content = "Otomatik", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 4, 0, 0), Foreground = CloseButton.Foreground };
        _studioReset = reset;
        reset.Click += (_, _) => SetPart(null);

        StudioHost.Children.Add(_studioDock);
        StudioHost.Children.Add(parts);
        StudioHost.Children.Add(swatches);
        StudioHost.Children.Add(StudioRow("Renk", _studioHue));
        StudioHost.Children.Add(StudioRow("Doygunluk", _studioSaturation));
        StudioHost.Children.Add(StudioRow("Parlaklık", _studioValue));
        StudioHost.Children.Add(reset);
        StudioHost.Children.Add(StudioTitle("Hazır paketler"));
        StudioHost.Children.Add(_studioPacks);
        StudioHost.Children.Add(StudioTitle("Şekil"));
        StudioHost.Children.Add(StudioRow("Köşe kesimi", cut));
        StudioHost.Children.Add(StudioRow("Gren miktarı", grain));
        StudioHost.Children.Add(StudioTitle("İkonlar"));
        StudioHost.Children.Add(contour);

        _studioTabs[StudioPart.Body].IsChecked = true;
        DrawStudioPacks();
    }

    private static TextBlock StudioTitle(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0xFF, 0xFF, 0xFF)),
        FontSize = 11.5,
        Margin = new Thickness(2, 12, 0, 6),
    };

    private static Grid StudioRow(string label, Slider slider)
    {
        var row = new Grid { Height = 28 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        return row;
    }

    private void SelectPart(StudioPart part)
    {
        _part = part;
        ToHsv(PartColor(StudioLook), out double h, out double s, out double v);
        _syncingStudio = true;
        _studioHue.Value = h;
        _studioSaturation.Value = s;
        _studioValue.Value = Math.Max(_studioValue.Minimum, v);
        _syncingStudio = false;
        RefreshStudioTracks();
        DrawStudioDock();
    }

    private Color PartColor(CyberLook look) => _part switch
    {
        StudioPart.Body => look.Body,
        StudioPart.Accent => look.Accent,
        StudioPart.Strip => look.Strip,
        _ => look.Ink,
    };

    private void SetPart(Color? color)
    {
        StorePart(color);
        SelectPart(_part);
        DrawStudioPacks();
        PushStudio();
    }

    private void StorePart(Color? color)
    {
        switch (_part)
        {
            case StudioPart.Body: _studioBody = color; break;
            case StudioPart.Accent: _studioAccent = color; break;
            case StudioPart.Strip: _studioStrip = color; break;
            default: _studioInk = color; break;
        }
    }

    private void StudioSliderChanged()
    {
        RefreshStudioTracks();
        if (_syncingStudio) return;
        StorePart(FromHsv(_studioHue.Value, _studioSaturation.Value, _studioValue.Value));
        DrawStudioDock();
        DrawStudioPacks();

        // a colour change repaints the whole dock, so a dragged slider only gets to do it a few times a second
        if (_studioTimer is null)
        {
            _studioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _studioTimer.Tick += (_, _) =>
            {
                _studioTimer.Stop();
                PushStudio();
            };
        }
        _studioTimer.Stop();
        _studioTimer.Start();
    }

    private void PushStudio() => GlassSettings.SetCyberColours(_studioBody, _studioAccent, _studioStrip, _studioInk);

    private void RefreshStudioTracks()
    {
        double h = _studioHue.Value, s = _studioSaturation.Value, v = _studioValue.Value;
        _studioSaturation.Background = new LinearGradientBrush(FromHsv(h, 0, v), FromHsv(h, 1, v), 0);
        _studioValue.Background = new LinearGradientBrush(FromHsv(h, s, _studioValue.Minimum), FromHsv(h, s, 1), 0);
    }

    private const int MostSavedPacks = 12;

    private void DrawStudioPacks()
    {
        _studioPacks.Children.Clear();
        foreach (var (name, body, accent, strip, ink) in StudioPacks)
        {
            _studioPacks.Children.Add(PackChip(new CyberPreset(Loc.T(name), body, accent, strip, ink)));
        }

        foreach (var preset in GlassSettings.CyberPresets)
        {
            var chip = PackChip(preset);
            chip.ToolTip = Loc.T("Sağ tık: sil");
            chip.MouseRightButtonDown += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                GlassSettings.SetCyberPresets(GlassSettings.CyberPresets.Where(saved => saved != preset));
                DrawStudioPacks();
            });
            _studioPacks.Children.Add(chip);
        }

        if (GlassSettings.CyberPresets.Count >= MostSavedPacks) return;
        var save = new Border
        {
            BorderBrush = new SolidColorBrush(PanelEdge),
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(9, 3, 9, 3),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = Loc.T("+ Kaydet"), Foreground = Brushes.White, FontSize = 12 },
        };
        save.MouseLeftButtonDown += (_, _) => Dispatcher.BeginInvoke(SaveStudioPack);
        _studioPacks.Children.Add(save);
    }

    private Border PackChip(CyberPreset preset)
    {
        var look = CyberLook.For(preset.Body, preset.Accent, preset.Strip, preset.Ink);
        var chosen = IsChosen(preset);
        var chip = new Border
        {
            Background = new SolidColorBrush(look.Body),
            BorderBrush = new SolidColorBrush(chosen ? CyberLook.InkOn(look.Body) : look.Accent),
            BorderThickness = chosen ? new Thickness(2) : new Thickness(0, 0, 0, 3),
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(9, 3, 9, 3),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = preset.Name, Foreground = new SolidColorBrush(look.Ink), FontSize = 12 },
        };
        chip.MouseLeftButtonDown += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            (_studioBody, _studioAccent, _studioStrip, _studioInk) = (preset.Body, preset.Accent, preset.Strip, preset.Ink);
            SelectPart(_part);
            DrawStudioPacks();
            PushStudio();
        });
        return chip;
    }

    private bool IsChosen(CyberPreset preset) =>
        _studioBody == preset.Body && _studioAccent == preset.Accent && _studioStrip == preset.Strip && _studioInk == preset.Ink;

    // the current colours under the next free number; colours a pack already has are not saved twice
    private void SaveStudioPack()
    {
        var saved = GlassSettings.CyberPresets;
        if (saved.Any(IsChosen) || StudioPacks.Any(pack => IsChosen(new CyberPreset(pack.Name, pack.Body, pack.Accent, pack.Strip, pack.Ink)))) return;
        var number = 1;
        string Name() => string.Format(Loc.T("Kayıt {0}"), number);
        while (saved.Any(preset => preset.Name == Name())) number++;
        GlassSettings.SetCyberPresets(saved.Append(new CyberPreset(Name(), _studioBody, _studioAccent, _studioStrip, _studioInk)));
        DrawStudioPacks();
    }

    private void DrawStudioDock()
    {
        const double Left = 18, Top = 20, Width = 324, Height = 42;
        var look = StudioLook;
        var canvas = _studioDock;
        canvas.Children.Clear();
        double cut = 9 * GlassSettings.CyberCut;

        var plate = Plate(Left, Top, Width, Height, cut);
        Add(new Path { Data = plate, Fill = new SolidColorBrush(look.Body) }, StudioPart.Body);

        // the icons: the second one stands hovered, in its frame, in the accent
        const double Icon = 16;
        for (int i = 0; i < 5; i++)
        {
            double x = Left + 22 + (i * 36);
            bool hovered = i == 1;
            if (hovered) Add(new Path { Data = Plate(x - 8, Top + 3, Icon + 16, Height - 3, 5 * GlassSettings.CyberCut), Stroke = new SolidColorBrush(look.Accent), StrokeThickness = 1.5 }, StudioPart.Accent);
            Add(new Path { Data = Plate(x, Top + 13, Icon, Icon, 4), Fill = new SolidColorBrush(hovered ? look.Accent : look.Ink) }, hovered ? StudioPart.Accent : StudioPart.Ink);
        }

        var clock = new TextBlock { Text = "23:41", FontFamily = CyberLook.Font, FontWeight = FontWeights.Bold, FontSize = 20, Foreground = new SolidColorBrush(look.Ink) };
        Canvas.SetLeft(clock, Left + Width - 66);
        Canvas.SetTop(clock, Top + 8);
        Add(clock, StudioPart.Ink);

        // the accent: the plate under the bar, as the dock draws it
        Add(new Path { Data = Plate(Left + 20, Top + Height + 5, 92, 5, 0), Fill = new SolidColorBrush(look.Accent) }, StudioPart.Accent);

        // the strip: the edge the dock draws around its panels, outside the plate so it reads on its own stage
        var edge = Plate(Left - 2, Top - 2, Width + 4, Height + 4, cut + 2);
        canvas.Children.Add(new Path { Data = edge, Stroke = new SolidColorBrush(look.Strip), StrokeThickness = 1, IsHitTestVisible = false, SnapsToDevicePixels = true });
        var edgeHit = new Path { Data = Plate(Left - 1, Top - 1, Width + 2, Height + 2, cut + 1), Stroke = Brushes.Transparent, StrokeThickness = 6 };
        Add(edgeHit, StudioPart.Strip);

        // what the click selected: the dashed mark takes the ink of the colour it lies on
        var mark = _part switch
        {
            StudioPart.Body => plate,
            StudioPart.Accent => Plate(Left + 54, Top + 9, Icon + 8, Icon + 8, 0),
            StudioPart.Strip => Plate(Left + 4, Top + 4, Width - 8, Height - 8, Math.Max(0, cut - 4)),
            _ => Plate(Left + Width - 72, Top + 7, 60, 28, 0),
        };
        var markInk = _part switch
        {
            StudioPart.Accent => CyberLook.InkOn(look.Ink),
            _ => CyberLook.InkOn(look.Body),
        };
        canvas.Children.Add(new Path { Data = mark, Stroke = new SolidColorBrush(markInk), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false, SnapsToDevicePixels = true });

        void Add(FrameworkElement shape, StudioPart part)
        {
            shape.Cursor = Cursors.Hand;
            shape.MouseLeftButtonDown += (_, _) => _studioTabs[part].IsChecked = true;
            canvas.Children.Add(shape);
        }
    }

    private static PathGeometry Plate(double x, double y, double width, double height, double cut)
    {
        var figure = new PathFigure { StartPoint = new Point(x + cut, y), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(new[]
        {
            new Point(x + width, y),
            new Point(x + width, y + height - cut),
            new Point(x + width - cut, y + height),
            new Point(x, y + height),
            new Point(x, y + cut),
        }, true));
        return new PathGeometry(new[] { figure });
    }
}
