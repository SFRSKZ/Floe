using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>Film grain on the Cyber dock's surface: one tile of hard noise that jumps a dozen times a second.</summary>
public partial class DockWindow
{
    private const int GrainTile = 256;
    private const byte GrainStrength = 30;

    private readonly Path _cyberGrain = new() { IsHitTestVisible = false };
    private readonly Path _plateGrain = new() { IsHitTestVisible = false };
    private readonly TranslateTransform _grainShift = new();
    private readonly Random _grainRandom = new();
    private readonly DispatcherTimer _grainTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(83) };
    private ImageBrush? _grainBrush;
    private double _grainMade;

    private void InitializeCyberGrain()
    {
        _cyberGrain.Data = _shapeGeometry;
        _plateGrain.SetBinding(Path.DataProperty, new Binding(nameof(Path.Data)) { Source = _cyberPlate });
        _plateGrain.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = _cyberPlate });
        ShapeLayer.Children.Add(_cyberGrain);
        ShapeLayer.Children.Add(_plateGrain);
        _grainTimer.Tick += (_, _) => ShiftGrain();
        GlassSettings.Changed += () => Dispatcher.BeginInvoke(UpdateCyberGrain);
    }

    private void UpdateCyberGrain()
    {
        bool on = GlassSettings.Cyber && GlassSettings.FilmGrain;
        if (_grainBrush is null || _grainMade != GlassSettings.GrainAmount)
        {
            _grainMade = GlassSettings.GrainAmount;
            _grainBrush = MakeGrainBrush();
        }

        _cyberGrain.Fill = _plateGrain.Fill = on ? _grainBrush : null;
        if (on) _grainTimer.Start();
        else _grainTimer.Stop();
    }

    private void ShiftGrain()
    {
        if (FxAsleep || !IsVisible) return;
        _grainShift.X = _grainRandom.Next(GrainTile);
        _grainShift.Y = _grainRandom.Next(GrainTile);
    }

    // half the specks dark, half light, so it reads on the yellow bar and on the black plate alike
    private ImageBrush MakeGrainBrush()
    {
        var random = new Random(77);
        var pixels = new uint[GrainTile * GrainTile];
        for (var i = 0; i < pixels.Length; i++)
        {
            uint alpha = (uint)random.Next((int)(GrainStrength * _grainMade));
            pixels[i] = (alpha << 24) | (random.Next(2) == 0 ? 0u : (alpha << 16) | (alpha << 8) | alpha);
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var tile = BitmapSource.Create(GrainTile, GrainTile, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32, null, pixels, GrainTile * 4);
        tile.Freeze();
        var brush = new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, GrainTile / dpi, GrainTile / dpi),
            Transform = _grainShift,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        return brush;
    }
}
