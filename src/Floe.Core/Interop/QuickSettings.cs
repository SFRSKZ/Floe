using System.Runtime.InteropServices;
using System.Text;

namespace Floe.Core.Interop;

/// <summary>Opens the Windows Quick Settings flyout and slides it over a point of our choosing.</summary>
public static class QuickSettings
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Win+A, then moves the flyout so it is centred on <paramref name="centerX"/> (physical px).</summary>
    public static void OpenAt(int centerX)
    {
        Keyboard.SendWinKey(0x41);
        Task.Run(() =>
        {
            // the shell creates the flyout a few frames later and always docks it bottom-right
            for (var attempt = 0; attempt < 80; attempt++)
            {
                Thread.Sleep(15);
                var hwnd = GetForegroundWindow();
                if (!IsQuickSettings(hwnd) || !GetWindowRect(hwnd, out var rect))
                {
                    continue;
                }

                var width = rect.Right - rect.Left;
                var monitor = MonitorFromWindow(hwnd, 2);
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                var left = centerX - (width / 2);
                if (GetMonitorInfo(monitor, ref info))
                {
                    left = Math.Clamp(left, info.Monitor.Left, info.Monitor.Right - width);
                }

                SetWindowPos(hwnd, IntPtr.Zero, left, rect.Top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                return;
            }
        });
    }

    private static bool IsQuickSettings(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() == "ControlCenterWindow";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
