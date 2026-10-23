using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The custom colours: the single tint (hologram, cubes, dust) and the four stops of the custom dust palette,
/// shown as swatches and edited with hue, saturation and brightness sliders. Editing a swatch switches its
/// group to the custom choice, so the change shows at once.
/// </summary>
public partial class SettingsWindow
{
    private const int SwatchCount = 5;
    private readonly Border[] _swatches = new Border[SwatchCount];
    private int _swatch;
    private bool _syncingPicker;

    private void InitializeCustomColors()
    {
        for (int i = 0; i < SwatchCount; i++)
        {
            int index = i;
            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                // the single tint stands apart from the palette's four
                Margin = new Thickness(0, 0, i == 0 ? 18 : 8, 0),
                Cursor = Cursors.Hand,
                ToolTip = i == 0 ? "Tek renk" : $"Palet rengi {i}",
            };
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                SelectSwatch(index);
                e.Handled = true;
            };
            _swatches[i] = swatch;
            CustomSwatches.Children.Add(swatch);
        }

        var hues = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int k = 0; k <= 6; k++) hues.GradientStops.Add(new GradientStop(FromHsv(k * 60, 1, 1), k / 6.0));
        hues.Freeze();
        CustomHue.Background = hues;
        CustomHue.ValueChanged += (_, _) => PickerChanged();
        CustomSaturation.ValueChanged += (_, _) => PickerChanged();
        CustomValue.ValueChanged += (_, _) => PickerChanged();
        SelectSwatch(0);
    }

    private static Color SwatchColor(int i) => i == 0 ? GlassSettings.CustomTint : GlassSettings.CustomPalette[i - 1];

    private void SelectSwatch(int i)
    {
        _swatch = i;
        ToHsv(SwatchColor(i), out double h, out double s, out double v);
        _syncingPicker = true;
        CustomHue.Value = h;
        CustomSaturation.Value = s;
        CustomValue.Value = Math.Max(CustomValue.Minimum, v);
        _syncingPicker = false;
        CustomTarget.Text = i == 0 ? "Seçili: tek renk (hologram, küpler ve tozlar)" : $"Seçili: özel toz paletinin {i}. rengi (ortadan kenara doğru)";
        RefreshCustomColors();
    }

    private void PickerChanged()
    {
        if (_syncingPicker) return;
        var color = FromHsv(CustomHue.Value, CustomSaturation.Value, CustomValue.Value);
        if (_swatch == 0)
        {
            GlassSettings.SetCustomTint(color);
            if (GlassSettings.HologramTint != HologramColor.Custom) HologramTintCustom.IsChecked = true;
        }
        else
        {
            GlassSettings.SetCustomPaletteStop(_swatch - 1, color);
            if (GlassSettings.DustColors != DustPalette.Custom) DustColorsCustom.IsChecked = true;
        }

        RefreshCustomColors();
    }

    private void RefreshCustomColors()
    {
        for (int i = 0; i < SwatchCount; i++)
        {
            _swatches[i].Background = new SolidColorBrush(SwatchColor(i));
            _swatches[i].BorderBrush = new SolidColorBrush(i == _swatch ? Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        }

        // the tracks show what each slider would pick, given the other two
        double h = CustomHue.Value, s = CustomSaturation.Value, v = CustomValue.Value;
        CustomSaturation.Background = new LinearGradientBrush(FromHsv(h, 0, v), FromHsv(h, 1, v), 0);
        CustomValue.Background = new LinearGradientBrush(FromHsv(h, s, CustomValue.Minimum), FromHsv(h, s, 1), 0);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60 % 2) - 1)), m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void ToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
    }
}
