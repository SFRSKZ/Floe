using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class WindowRegion
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePolygonRgn(Point32[] points, int count, int fillMode);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr source1, IntPtr source2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool redraw);

    public static void SetRounded(IntPtr hwnd, int width, int height, int radius)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var hRgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (hRgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, hRgn, true);
        }
    }

    public static void SetPolygon(IntPtr hwnd, IReadOnlyList<(int X, int Y)> points)
    {
        if (hwnd == IntPtr.Zero || points.Count < 3)
        {
            return;
        }

        var array = new Point32[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            array[i] = new Point32 { X = points[i].X, Y = points[i].Y };
        }

        var hRgn = CreatePolygonRgn(array, array.Length, 2);
        if (hRgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, hRgn, true);
        }
    }

    public static void SetRect(IntPtr hwnd, int left, int top, int right, int bottom)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var hRgn = CreateRectRgn(left, top, right, bottom);
        if (hRgn != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, hRgn, true);
        }
    }

    /// <summary>
    /// The window's region is every rect put together: a bar can keep a decoration drawn beside it without taking
    /// the clicks that land on the empty band between the two.
    /// </summary>
    public static void SetRects(IntPtr hwnd, IReadOnlyList<(int Left, int Top, int Right, int Bottom)> rects)
    {
        if (hwnd == IntPtr.Zero || rects.Count == 0)
        {
            return;
        }

        var hRgn = CreateRectRgn(0, 0, 0, 0);
        if (hRgn == IntPtr.Zero)
        {
            return;
        }

        const int RgnOr = 2;
        foreach (var (left, top, right, bottom) in rects)
        {
            var part = CreateRectRgn(left, top, right, bottom);
            if (part == IntPtr.Zero)
            {
                continue;
            }

            CombineRgn(hRgn, hRgn, part, RgnOr);
            DeleteObject(part);
        }

        SetWindowRgn(hwnd, hRgn, true);
    }

    public static void Clear(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
    }
}
