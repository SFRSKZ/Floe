using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Floe.App.Effects.Derezz;

/// <summary>
/// Shows a running particle dissolve: one bitmap at the snapshot's resolution over the whole layer, redrawn only
/// where points were or are.
/// </summary>
internal sealed class ParticleLayer : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private uint[] _buffer = Array.Empty<uint>();
    private int _width, _height;
    private double _scale = 1;
    private Int32Rect _dirty = Int32Rect.Empty;

    public ParticleLayer()
    {
        IsHitTestVisible = false;
    }

    public void Draw(ParticleDissolve field, double time)
    {
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * field.Scale));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * field.Scale));
        if (_bitmap is null || width != _width || height != _height || Math.Abs(_scale - field.Scale) > 1e-6)
        {
            _width = width;
            _height = height;
            _scale = field.Scale;
            _buffer = new uint[width * height];
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            _dirty = Int32Rect.Empty;
            InvalidateVisual();
        }

        // wipe last frame's points, draw this frame's, upload what either touched
        if (!_dirty.IsEmpty)
        {
            for (int y = _dirty.Y; y < _dirty.Y + _dirty.Height; y++) Array.Clear(_buffer, (y * _width) + _dirty.X, _dirty.Width);
        }

        var now = field.Render(_buffer, _width, _height, time);
        var upload = Union(_dirty, now);
        _dirty = now;
        if (!upload.IsEmpty) _bitmap.WritePixels(upload, _buffer, _width * 4, (upload.Y * _width) + upload.X);
    }

    public void Clear()
    {
        if (_bitmap is null) return;
        _bitmap = null;
        _buffer = Array.Empty<uint>();
        _dirty = Int32Rect.Empty;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is null) return;
        dc.DrawImage(_bitmap, new Rect(0, 0, _width / _scale, _height / _scale));
    }

    private static Int32Rect Union(Int32Rect a, Int32Rect b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
        int x1 = Math.Max(a.X + a.Width, b.X + b.Width), y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Int32Rect(x0, y0, x1 - x0, y1 - y0);
    }
}
