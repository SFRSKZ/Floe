using System.Runtime.InteropServices;
using MacDock.Core.Models;

namespace MacDock.Core.Interop;

public static class DwmEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static int ExtendGlass(IntPtr hwnd)
    {
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        return DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    public static int SetCornerPreference(IntPtr hwnd, bool round)
    {
        var value = round ? DwmwcpRound : 1;
        return DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref value, sizeof(int));
    }

    public static int SetDarkMode(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        return DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    public static int SetBackdrop(IntPtr hwnd, BackdropType type)
    {
        var value = (int)type;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref value, sizeof(int));
    }

    public static DwmResult Apply(IntPtr hwnd, BackdropType type, bool dark, bool round = true)
    {
        if (hwnd == IntPtr.Zero)
        {
            return new DwmResult(-1, -1, -1, -1);
        }

        var extend = ExtendGlass(hwnd);
        var corner = SetCornerPreference(hwnd, round);
        var darkMode = SetDarkMode(hwnd, dark);
        var backdrop = SetBackdrop(hwnd, type);
        return new DwmResult(extend, corner, darkMode, backdrop);
    }

    public static void DisableBackdrop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetBackdrop(hwnd, BackdropType.None);
        }
    }
}

public readonly record struct DwmResult(int Extend, int Corner, int DarkMode, int Backdrop)
{
    public bool Ok => Extend == 0 && Backdrop == 0;

    public override string ToString()
        => $"extend={Extend} corner={Corner} dark={DarkMode} backdrop={Backdrop}";
}
