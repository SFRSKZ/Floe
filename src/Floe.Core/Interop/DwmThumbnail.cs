using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class DwmThumbnail
{
    private const int TnpRectDestination = 0x00000001;
    private const int TnpOpacity = 0x00000004;
    private const int TnpVisible = 0x00000008;
    private const int TnpSourceClientAreaOnly = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public int Flags;
        public Rect Destination;
        public Rect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail, out Size size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);

    public sealed class Thumbnail : IDisposable
    {
        private IntPtr _handle;

        private Thumbnail(IntPtr handle)
        {
            _handle = handle;
        }

        public static Thumbnail? Register(IntPtr destination, IntPtr source)
        {
            if (destination == IntPtr.Zero || source == IntPtr.Zero)
            {
                return null;
            }

            return DwmRegisterThumbnail(destination, source, out var handle) == 0 && handle != IntPtr.Zero
                ? new Thumbnail(handle)
                : null;
        }

        public (int Width, int Height) GetSourceSize()
            => DwmQueryThumbnailSourceSize(_handle, out var size) == 0 ? (size.Width, size.Height) : (0, 0);

        public void Show(int left, int top, int right, int bottom)
        {
            var properties = new ThumbnailProperties
            {
                Flags = TnpRectDestination | TnpOpacity | TnpVisible | TnpSourceClientAreaOnly,
                Destination = new Rect { Left = left, Top = top, Right = right, Bottom = bottom },
                Opacity = 255,
                Visible = true,
                SourceClientAreaOnly = true,
            };

            DwmUpdateThumbnailProperties(_handle, ref properties);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
