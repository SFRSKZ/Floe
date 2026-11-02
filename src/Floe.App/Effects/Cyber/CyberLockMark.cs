using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Floe.App.Services;

namespace Floe.App.Effects.Cyber;

/// <summary>
/// The cyber themes' mark on the app in front, in the language of a glitched logo intro: the icon arrives out of its
/// own rows stretched into long lines, then holds with a few thin signal lines running out of its edges; now and then
/// a short tear throws bands of it sideways into streaks. The lines are one colour, black or white, whichever reads
/// on the dock.
/// <para>
/// It lives in the slot, so it follows the icon as the dock magnifies, and it runs on a stepped clock like the
/// Signal Lost visualizer: a state holds for a few steps and then jumps, and the element only redraws on a jump.
/// Everything is whole blocks and hard lines, without blur.
/// </para>
/// </summary>
public sealed class CyberLockMark : FrameworkElement
{
    private const double Reach = 72;  // how far the lines may run out on either side of the icon
    private const double Bleed = 10;  // the card artwork reaches this far past the slot
    private const double Row = 2;     // one row of the icon, in dock units
    private const double Inset = 4;   // the card frame's own lines sit within this much of the slot's edge
    private const int LockSteps = 6;

    private readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(110) };
    private BitmapSource? _icon;
    private Rect _iconArea;
    // per row of the icon: where its drawing starts and ends; NaN for an empty row
    private (double Left, double Right)[] _rows = Array.Empty<(double, double)>();
    private int[] _solid = Array.Empty<int>();  // the rows that have any drawing
    private Rect _slot;                          // the slot itself, inside the card's bleed
    private bool _stale = true;
    private long _step, _holdUntil, _burstAt = 30, _seed;
    private int _lock, _burst;

    public CyberLockMark()
    {
        IsHitTestVisible = false;
        // the mark starts Reach left of the slot and Bleed above it
        Margin = new Thickness(-Reach, -Bleed, -Reach, -Bleed);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        _clock.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _clock.Start();
            else _clock.Stop();
        };
    }

    /// <summary>The icon under the mark changed (a new slot, a new card, a new theme): read it again.</summary>
    public void Refresh()
    {
        _stale = true;
        InvalidateVisual();
    }

    /// <summary>Locks on to a new app: it comes in out of its stretched rows.</summary>
    public void Lock()
    {
        Refresh();
        _lock = LockSteps;
        _burst = 0;
        _burstAt = _step + 30 + (Next(_step, 7) % 30);
    }

    /// <summary>Skips to the resting state, for a still render.</summary>
    public void Settle()
    {
        _lock = 0;
        _burst = 0;
        InvalidateVisual();
    }

    /// <summary>One still, for --render: "lock" at a share of the way in (1 = just arrived), "rest" or "tear" at a seed.</summary>
    internal void Preview(string mode, double value)
    {
        _stale = true;
        _lock = mode == "lock" ? (int)Math.Round(value * LockSteps) : 0;
        _burst = mode == "tear" ? 2 : 0;
        _seed = mode == "lock" ? 1 : (long)value;
        _step = (long)(value * 1000);
        InvalidateVisual();
    }

    private void Advance()
    {
        _step++;
        bool changed = _lock > 0 || _burst > 0;
        if (_lock > 0) _lock--;
        if (_burst > 0) _burst--;

        if (_step >= _holdUntil)
        {
            // the lines hold for a few steps, then jump
            _seed++;
            _holdUntil = _step + 3 + (Next(_seed, 1) % 6);
            changed = true;
        }

        if (_step >= _burstAt && _lock == 0)
        {
            // every few seconds, a short tear
            _burst = 2 + (int)(Next(_seed, 2) % 3);
            _burstAt = _step + 28 + (Next(_seed, 3) % 40);
            changed = true;
        }

        if (changed) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_stale && Parent is Grid grid) Read(grid);
        if (_icon is null || _solid.Length == 0) return;

        var look = CyberLook.Current;
        var ink = new SolidColorBrush(CyberLook.InkOn(look.Body));
        double intensity = _lock > 0 ? _lock / (double)LockSteps : _burst > 0 ? .55 : 0;
        if (intensity > 0) DrawTear(dc, look, ink, intensity, (_seed * 131) + _step);
        DrawLines(dc, ink, intensity, _seed);
    }

    // the resting lines: a few rows run out of the icon's edges
    private void DrawLines(DrawingContext dc, Brush ink, double intensity, long seed)
    {
        int count = 3 + (int)(Unit(seed, 11) * 3) + (int)(intensity * 10);
        for (var k = 0; k < count; k++)
        {
            int r = _solid[(int)(Unit(seed, 20 + k) * _solid.Length)];
            var row = _rows[r];
            double y = _iconArea.Y + (r * Row);
            double side = Unit(seed, 40 + k);
            double length = 4 + (Unit(seed, 60 + k) * (20 + (intensity * 46)));
            if (side < .55) Line(dc, ink, row.Left - length - 1, y, length);
            if (side > .45) Line(dc, ink, row.Right + 1, y, length);
        }
    }

    // the tear: bands of the icon thrown sideways, the gap they leave filled with the dock, the far edge smeared out
    private void DrawTear(DrawingContext dc, CyberLook look, Brush ink, double intensity, long seed)
    {
        var body = new SolidColorBrush(look.Body);
        int bands = 1 + (int)(intensity * 4);
        for (var b = 0; b < bands; b++)
        {
            int first = _solid[(int)(Unit(seed, 100 + b) * _solid.Length)];
            int height = 1 + (int)(Unit(seed, 120 + b) * 3);
            double shift = (Unit(seed, 140 + b) < .5 ? -1 : 1) * Math.Round(3 + (Unit(seed, 160 + b) * 12 * intensity));
            double top = _iconArea.Y + (first * Row);
            var band = new Rect(_slot.X, top, _slot.Width, Math.Min(height * Row, _slot.Bottom - top));
            if (band.Height <= 0) continue;

            dc.DrawRectangle(body, null, band);
            var moved = band;
            moved.Offset(shift, 0);
            dc.PushClip(new RectangleGeometry(moved));
            dc.DrawImage(_icon, new Rect(_iconArea.X + shift, _iconArea.Y, _iconArea.Width, _iconArea.Height));
            dc.Pop();

            // the smear: each row of the band runs on past its edge as a hard line
            for (var r = first; r < Math.Min(first + height, _rows.Length); r++)
            {
                var row = _rows[r];
                if (double.IsNaN(row.Left)) continue;
                double y = _iconArea.Y + (r * Row);
                double length = 12 + (Unit(seed, 200 + r) * 56 * intensity);
                if (shift > 0) Line(dc, ink, row.Right + shift + 1, y, length);
                else Line(dc, ink, row.Left + shift - length - 1, y, length);
            }
        }
    }

    private static void Line(DrawingContext dc, Brush ink, double x, double y, double length)
    {
        length = Math.Round(length);
        if (length >= 2) dc.DrawRectangle(ink, null, new Rect(Math.Round(x), y, length, Row));
    }

    // reads what the slot shows (its artwork or outline icon, not the running dot) into rows
    private void Read(Grid grid)
    {
        _stale = false;
        _icon = null;
        _rows = Array.Empty<(double, double)>();
        _solid = Array.Empty<int>();
        double box = grid.ActualWidth;
        if (box <= 0) return;

        const double scale = 2;
        double side = box + (2 * Bleed);
        var picture = new DrawingVisual();
        using (var dc = picture.RenderOpen())
        {
            for (var i = 0; i < Math.Min(2, grid.Children.Count); i++)
            {
                if (grid.Children[i] is not FrameworkElement child || child.Visibility != Visibility.Visible || child.ActualWidth <= 0) continue;
                var at = child.TranslatePoint(new Point(), grid);
                dc.DrawRectangle(new VisualBrush(child), null, new Rect(at.X + Bleed, at.Y + Bleed, child.ActualWidth, child.ActualHeight));
            }
        }

        int pixels = (int)Math.Ceiling(side * scale);
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(picture);
        bitmap.Freeze();
        _icon = bitmap;
        _iconArea = new Rect(Reach - Bleed, 0, side, side);
        _slot = new Rect(Reach, Bleed, box, box);

        var data = new byte[pixels * pixels * 4];
        bitmap.CopyPixels(data, pixels * 4, 0);
        // only the logo inside the slot counts: the card's frame around it runs along the slot's edge
        var body = CyberLook.Current.Body;
        int from = (int)((Bleed + Inset) * scale), to = (int)((Bleed + box - Inset) * scale);
        int rows = (int)(side / Row);
        _rows = new (double, double)[rows];
        for (var r = 0; r < rows; r++)
        {
            int py = Math.Min(pixels - 1, (int)(((r * Row) + (Row / 2)) * scale));
            int left = -1, right = -1;
            for (var px = from; px < to && py >= from && py < to; px++)
            {
                if (!Solid(data, ((py * pixels) + px) * 4, body)) continue;
                if (left < 0) left = px;
                right = px;
            }

            _rows[r] = left < 0
                ? (double.NaN, double.NaN)
                : (_iconArea.X + (left / scale), _iconArea.X + ((right + 1) / scale));
        }

        _solid = Enumerable.Range(0, rows).Where(r => !double.IsNaN(_rows[r].Left)).ToArray();
    }

    // a pixel that is really drawn: opaque, and not the dock's own colour (the card frames fade into it)
    private static bool Solid(byte[] data, int i, Color body)
    {
        if (data[i + 3] < 160) return false;
        var c = At(data, i);
        return Math.Abs(Luma(c) - Luma(body)) > .3 || Math.Abs(c.R - body.R) + Math.Abs(c.G - body.G) + Math.Abs(c.B - body.B) > 200;
    }

    private static Color At(byte[] data, int i)
    {
        double a = Math.Max(1, (int)data[i + 3]) / 255.0;
        return Color.FromRgb((byte)Math.Min(255, data[i + 2] / a), (byte)Math.Min(255, data[i + 1] / a), (byte)Math.Min(255, data[i] / a));
    }

    private static double Luma(Color c) => ((.2126 * c.R) + (.7152 * c.G) + (.0722 * c.B)) / 255;

    private static double Unit(long seed, int salt) => (Next(seed, salt) % 10000) / 10000.0;

    private static long Next(long seed, int salt)
    {
        unchecked
        {
            ulong x = ((ulong)seed * 0x9E3779B97F4A7C15UL) ^ ((ulong)salt * 0xBF58476D1CE4E5B9UL);
            x ^= x >> 31;
            x *= 0x94D049BB133111EBUL;
            x ^= x >> 29;
            return (long)(x & 0x7FFFFFFF);
        }
    }
}
