using System;
using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class ShellImage
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(Size size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IntPtr ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int SiigbfIconOnly = 0x4;

    public static IntPtr GetHBitmap(string parsingName, int size)
    {
        var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var ptr) != 0 || ptr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            var factory = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(ptr);
            var sz = new Size { Cx = size, Cy = size };
            factory.GetImage(sz, SiigbfIconOnly, out var hBitmap);
            return hBitmap;
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    public static void DeleteHBitmap(IntPtr hBitmap)
    {
        if (hBitmap != IntPtr.Zero)
        {
            DeleteObject(hBitmap);
        }
    }
}
