using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class WindowActivator
{
    private const int SwRestore = 9;
    private const byte VkMenu = 0x12;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    public static bool Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        var attached = false;
        if (foregroundThread != targetThread && foregroundThread != 0 && targetThread != 0)
        {
            attached = AttachThreadInput(foregroundThread, targetThread, true);
        }

        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(foregroundThread, targetThread, false);
            }
        }

        if (GetForegroundWindow() == hwnd)
        {
            return true;
        }

        // Foreground lock workaround: a synthetic Alt press unlocks SetForegroundWindow.
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyEventKeyUp, UIntPtr.Zero);
        SetForegroundWindow(hwnd);

        return GetForegroundWindow() == hwnd;
    }
}
