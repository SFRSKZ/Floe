using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class WindowActivator
{
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
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

    /// <summary>
    /// The foreground window when it belongs to the same app as one of <paramref name="windows"/> (same window, or
    /// same process), else zero.
    /// </summary>
    public static IntPtr ForegroundAmong(IReadOnlyList<IntPtr> windows)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || windows.Count == 0)
        {
            return IntPtr.Zero;
        }

        if (windows.Contains(foreground))
        {
            return foreground;
        }

        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        foreach (var window in windows)
        {
            GetWindowThreadProcessId(window, out var process);
            if (process != 0 && process == foregroundProcess)
            {
                return foreground;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Minimises <paramref name="hwnd"/>, handing the foreground to the window under it.</summary>
    public static void Minimize(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SwMinimize);
        }
    }

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
