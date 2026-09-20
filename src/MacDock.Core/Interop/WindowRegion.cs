using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

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
    private static extern IntPtr CreatePolygonRgn(Point32[] points, int count, int fillMode);

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
}
