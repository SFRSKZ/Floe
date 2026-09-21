using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MacDock.Core.Interop;

public static class ScreenInput
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    public static (int X, int Y) GetCursorPosition()
        => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    public static int PrimaryScreenWidth => GetSystemMetrics(SmCxScreen);

    public static int PrimaryScreenHeight => GetSystemMetrics(SmCyScreen);

    public static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
        => GetWindowRect(hwnd, out rect);

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

    public static bool IsForegroundFullscreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var className = GetWindowClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            return false;
        }

        // a maximized normal window still has a caption; only borderless windows count
        var style = GetWindowLongPtr(foreground, GwlStyle).ToInt64();
        if ((style & WsCaption) == WsCaption)
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        return rect.Left <= 0
            && rect.Top <= 0
            && rect.Right >= PrimaryScreenWidth
            && rect.Bottom >= PrimaryScreenHeight;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(64);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
