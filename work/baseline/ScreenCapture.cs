using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class ScreenCapture
{
    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dst, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dst, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, int srcWidth, int srcHeight, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    private const int Halftone = 4;

    public static byte[]? CaptureScaled(int x, int y, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, out int stride)
    {
        stride = targetWidth * 4;
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = targetWidth,
            Height = -targetHeight,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };

        var dib = CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = SelectObject(memory, dib);
        var pixels = new byte[targetWidth * targetHeight * 4];

        try
        {
            SetStretchBltMode(memory, Halftone);
            StretchBlt(memory, 0, 0, targetWidth, targetHeight, screen, x, y, sourceWidth, sourceHeight, SrcCopy);
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }

        return pixels;
    }

    public static byte[]? Capture(int x, int y, int width, int height, out int stride)
    {
        stride = width * 4;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };

        var dib = CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
        var previous = SelectObject(memory, dib);
        var pixels = new byte[width * height * 4];

        try
        {
            BitBlt(memory, 0, 0, width, height, screen, x, y, SrcCopy);
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(dib);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }

        return pixels;
    }
}
