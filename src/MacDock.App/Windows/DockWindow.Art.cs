using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MacDock.App.Effects.Derezz;

namespace MacDock.App.Windows;

/// <summary>
/// The album art swaps with a derezz: the old cover breaks apart while the new one is built up from rising cubes.
/// </summary>
public partial class DockWindow
{
    private const double ArtCell = 3.7;
    private const double ArtSpread = .30;
    private const double ArtSpeed = 1.5;
    private const double ArtDelay = .30;
    private const double ArtCorner = 9;

    private Border? _artFrame;
    private DerezzField? _artLeave;
    private DerezzField? _artArrive;
    private Rect _artRect;
    private double _artTime;

    private void SwapArtwork(ImageSource? next)
    {
        var old = _mediaArt.Source;
        if (old is null || next is null || _dozing || _dozeHidden || !IsVisible || !_shown || _mediaReveal < .95 || _artFrame is null)
        {
            FinishArt();
            _mediaArt.Source = next;
            return;
        }

        var rect = _artFrame.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, _artFrame.ActualWidth, _artFrame.ActualHeight));
        var oldSnap = Snapshot(old, rect);
        var newSnap = Snapshot(next, rect);
        if (oldSnap is null || newSnap is null)
        {
            FinishArt();
            _mediaArt.Source = next;
            return;
        }

        FinishArt();
        _artRect = rect;
        _artLeave = new DerezzField(oldSnap, rect, ArtCell, new Point(rect.Right, rect.Bottom), WindowHeight,
            rezz: false, spread: ArtSpread, seed: Random.Shared.Next(256));
        _artArrive = new DerezzField(newSnap, rect, ArtCell, new Point(rect.Left, rect.Bottom), WindowHeight,
            rezz: true, spread: ArtSpread, speed: ArtSpeed, seed: Random.Shared.Next(256));
        _mediaArt.Source = next;
        _artFrame.OpacityMask = ArtMask(_artArrive, 0);
        _artTime = 0;
        StartDerezzLoop();
    }

    /// <summary>Advances the album art derezz; false once there is nothing left to draw.</summary>
    private bool AdvanceArt(double dt, List<(DerezzField, double)> runs)
    {
        if (_artLeave is not { } leave || _artArrive is not { } arrive) return false;
        _artTime += dt;
        bool running = false;
        if (_artTime < leave.Duration)
        {
            runs.Add((leave, _artTime));
            running = true;
        }

        double a = _artTime - ArtDelay;
        if (a < arrive.Duration)
        {
            if (_artFrame is not null) _artFrame.OpacityMask = ArtMask(arrive, Math.Max(0, a));
            if (a >= 0) runs.Add((arrive, a));
            running = true;
        }

        if (running) return true;
        FinishArt();
        return false;
    }

    private void FinishArt()
    {
        if (_artFrame is not null) _artFrame.OpacityMask = null;
        _artLeave = null;
        _artArrive = null;
        _artTime = 0;
    }

    // Shows the live cover only in the cells the rezz has already put back.
    private DrawingBrush ArtMask(DerezzField field, double time)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(_artRect)));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, field.ShownRegion(time)));
        var brush = new DrawingBrush(group)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = _artRect,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, Math.Max(1, _artFrame?.ActualWidth ?? _artRect.Width),
                Math.Max(1, _artFrame?.ActualHeight ?? _artRect.Height)),
            Stretch = Stretch.Fill,
        };
        brush.Freeze();
        return brush;
    }

    // What the border shows: uniform fill, centre cropped, corners rounded, at device pixels.
    private BitmapSource? Snapshot(ImageSource source, Rect rect)
    {
        if (rect.Width < 1 || rect.Height < 1 || source.Width < 1 || source.Height < 1) return null;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height), ArtCorner, ArtCorner));
            double scale = Math.Max(rect.Width / source.Width, rect.Height / source.Height);
            double width = source.Width * scale, height = source.Height * scale;
            dc.DrawImage(source, new Rect((rect.Width - width) / 2, (rect.Height - height) / 2, width, height));
            dc.Pop();
        }

        var dpi = VisualTreeHelper.GetDpi(Root);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Round(rect.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Round(rect.Height * dpi.DpiScaleY)), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
