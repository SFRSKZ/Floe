using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class WindowStyling
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public static void MakeNonActivatingToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    public static void MakeClickThrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    /// <summary>Turns mouse input on the window off (clicks fall to what lies below) or back on.</summary>
    public static void SetClickThrough(IntPtr hwnd, bool through)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style = through ? style | NativeMethods.WsExTransparent : style & ~(long)NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    public static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    public static void SetNoActivate(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        if (enabled)
        {
            style |= NativeMethods.WsExNoActivate;
        }
        else
        {
            style &= ~NativeMethods.WsExNoActivate;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    /// <summary>Puts <paramref name="hwnd"/> directly under <paramref name="above"/> in the z-order.</summary>
    public static void PlaceBelow(IntPtr hwnd, IntPtr above)
    {
        if (hwnd != IntPtr.Zero && above != IntPtr.Zero)
        {
            SetWindowPos(hwnd, above, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    public static void BringToTop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }
}
