using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class WindowAffinity
{
    private const uint WdaExcludeFromCapture = 0x00000011;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        }
    }
}
