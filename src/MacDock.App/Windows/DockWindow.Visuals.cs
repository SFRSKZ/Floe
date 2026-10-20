using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MacDock.App.Services;
using MacDock.App.ViewModels;
using MacDock.Core.Interop;
using MacDock.Core.Models;
using MacDock.Core.Services;

namespace MacDock.App.Windows;

public partial class DockWindow
{

    private static BitmapSource MakeNoiseBitmap()
    {
        const int size = 64;
        var random = new Random(20260921);
        var pixels = new byte[size * size * 4];

        for (var i = 0; i < size * size; i++)
        {
            var value = (byte)random.Next(0, 256);
            pixels[(i * 4) + 0] = value;
            pixels[(i * 4) + 1] = value;
            pixels[(i * 4) + 2] = value;
            pixels[(i * 4) + 3] = 255;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush BuildGlassBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.08));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x1C, 0x1A, 0x1C, 0x24), 0.42));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x42, 0x0A, 0x0C, 0x14), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush BuildSheenBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.4));
        return brush;
    }

    private static Brush BuildSpecularBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1.0));
        brush.Freeze();
        return brush;
    }

    private void PresentBackdrop(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_backdropBitmap is null || _backdropBitmap.PixelWidth != width || _backdropBitmap.PixelHeight != height)
        {
            _backdropBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _backdrop.Source = _backdropBitmap;
        }

        _backdropBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
    }

}
