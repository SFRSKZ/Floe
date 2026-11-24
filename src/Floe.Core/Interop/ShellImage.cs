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

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    private static readonly Dictionary<string, string?> DisplayNameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves the localized display name of a shell item (such as <c>shell:AppsFolder\...</c>).</summary>
    public static string? GetDisplayName(string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        lock (DisplayNameCache)
        {
            if (DisplayNameCache.TryGetValue(parsingName, out var cached)) return cached;
        }

        var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var ptr) != 0 || ptr == IntPtr.Zero)
        {
            lock (DisplayNameCache) DisplayNameCache[parsingName] = null;
            return null;
        }

        string? name = null;
        try
        {
            var item = (IShellItem)Marshal.GetObjectForIUnknown(ptr);
            item.GetDisplayName(0, out var raw);
            if (!string.IsNullOrWhiteSpace(raw)) name = raw.Trim();
        }
        catch
        {
            name = null;
        }
        finally
        {
            Marshal.Release(ptr);
        }

        lock (DisplayNameCache) DisplayNameCache[parsingName] = name;
        return name;
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
