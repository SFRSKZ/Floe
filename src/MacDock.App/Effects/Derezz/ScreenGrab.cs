using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.Derezz;

/// <summary>
/// Copies what is on screen. RenderTargetBitmap cannot run the dock's ps_3_0 glass, so a derezz takes its
/// picture of the dock from the composed desktop instead.
/// </summary>
internal static class ScreenGrab
{
    private const int SrcCopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;

    /// <summary>Grabs a physical-pixel rectangle and keeps only the rounded rect inside it.</summary>
    public static BitmapSource? Capture(Int32Rect area, double cornerRadius)
    {
        if (area.Width <= 0 || area.Height <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
        IntPtr previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy | CaptureBlt)) return null;
            SelectObject(memory, previous);
            var grabbed = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, area.Width, area.Height);
                dc.PushClip(new RectangleGeometry(bounds, cornerRadius, cornerRadius));
                dc.DrawImage(grabbed, bounds);
                dc.Pop();
            }

            var result = new RenderTargetBitmap(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual);
            result.Freeze();
            return result;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
}
