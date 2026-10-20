using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MacDock.Core.Interop;

public static class ScreenInput
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public static (int X, int Y) GetCursorPosition()
        => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    public static int PrimaryScreenWidth => GetSystemMetrics(SmCxScreen);

    public static int PrimaryScreenHeight => GetSystemMetrics(SmCyScreen);

    public static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
        => GetWindowRect(hwnd, out rect);

    /// <summary>Monitor that currently contains the window (primary when it has no window yet).</summary>
    public static IntPtr GetMonitorForWindow(IntPtr hwnd)
        => hwnd == IntPtr.Zero
            ? MonitorFromWindow(hwnd, MonitorDefaultToPrimary)
            : MonitorFromWindow(hwnd, MonitorDefaultToNearest);

    public static IntPtr GetForegroundMonitor()
    {
        var foreground = GetForegroundWindow();
        return foreground == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(foreground, MonitorDefaultToNearest);
    }

    public static bool TryGetMonitorInfo(IntPtr monitor, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info);
    }

    /// <summary>Effective DPI scale (1.0 at 96 DPI) of the monitor the window is on.</summary>
    public static double GetDpiScale(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return 1.0;
        }

        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public static int GetIdleMilliseconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        var elapsed = unchecked((uint)Environment.TickCount - info.Time);
        return (int)Math.Min(elapsed, int.MaxValue);
    }

    public static IntPtr GetForegroundHandle() => GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    public static bool IsWindowMinimized(IntPtr hwnd) => hwnd != IntPtr.Zero && IsIconic(hwnd);

    public static string GetForegroundProcessName()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return string.Empty;
        }

        GetWindowThreadProcessId(foreground, out var processId);

        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public readonly record struct ForegroundInfo(string ProcessName, string Title, string Path);

    public static ForegroundInfo GetForegroundInfo()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return new ForegroundInfo(string.Empty, string.Empty, string.Empty);
        }

        GetWindowThreadProcessId(foreground, out var processId);

        var name = string.Empty;
        try
        {
            name = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            // process may have exited
        }

        return new ForegroundInfo(name, GetWindowTitle(foreground), AppWindows.GetProcessPath(processId));
    }

    public static ForegroundInfo GetWindowInfo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return new ForegroundInfo(string.Empty, string.Empty, string.Empty);
        }

        GetWindowThreadProcessId(hwnd, out var processId);

        var name = string.Empty;
        try
        {
            name = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            // process may have exited
        }

        return new ForegroundInfo(name, GetWindowTitle(hwnd), AppWindows.GetProcessPath(processId));
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    public static bool IsForegroundFullscreen() => IsForegroundFullscreen(IntPtr.Zero);

    /// <summary>
    /// True when the foreground window covers the whole monitor rect (caption or not).
    /// When <paramref name="monitor"/> is given, the window must also live on that monitor.
    /// </summary>
    public static bool IsForegroundFullscreen(IntPtr monitor)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsIconic(foreground))
        {
            return false;
        }

        var className = GetWindowClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero && foregroundMonitor != monitor)
        {
            return false;
        }

        var target = monitor != IntPtr.Zero ? monitor : foregroundMonitor;
        if (!TryGetMonitorInfo(target, out var info) || !GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        const int tolerance = 2;
        var area = info.Monitor;
        return rect.Left <= area.Left + tolerance
            && rect.Top <= area.Top + tolerance
            && rect.Right >= area.Right - tolerance
            && rect.Bottom >= area.Bottom - tolerance;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
