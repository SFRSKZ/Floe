using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// Per-pixel transparency without a layered window: DWM's frame is pulled over the whole client area and WPF
/// clears to transparent, so DWM blends the hardware-rendered surface by its alpha. A layered window instead
/// reads every frame back from the GPU and hands it to DWM as a bitmap.
/// </summary>
internal static class DwmTransparency
{
    public static bool Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd) is not { CompositionTarget: { } target }) return false;
        target.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        // Windows 11 would round the window's corners and draw a border round it
        int square = 1, none = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 33, ref square, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34, ref none, sizeof(int));
        return DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
