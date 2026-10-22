using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class TaskbarController
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";

    // re-scan occasionally so a monitor hot-plugged after caching still gets hidden
    private const long RescanMs = 5000;

    private static IntPtr[] _cached = Array.Empty<IntPtr>();
    private static long _cachedAt;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static IntPtr[] TaskbarWindows()
    {
        var stale = _cached.Length == 0 || Environment.TickCount64 - _cachedAt > RescanMs;
        if (!stale)
        {
            foreach (var handle in _cached)
            {
                if (!IsWindow(handle))
                {
                    stale = true;
                    break;
                }
            }
        }

        if (!stale)
        {
            return _cached;
        }

        var found = new List<IntPtr>();
        var primary = FindWindow(null, PrimaryClass);
        if (primary != IntPtr.Zero)
        {
            found.Add(primary);
        }

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, null, SecondaryClass)) != IntPtr.Zero)
        {
            found.Add(secondary);
        }

        _cached = found.ToArray();
        _cachedAt = Environment.TickCount64;
        return _cached;
    }

    public static void Hide()
    {
        foreach (var taskbar in TaskbarWindows())
        {
            ShowWindow(taskbar, SwHide);
        }
    }

    public static void Show()
    {
        foreach (var taskbar in TaskbarWindows())
        {
            ShowWindow(taskbar, SwShow);
        }
    }

    public static bool IsHidden()
    {
        var any = false;
        var hidden = true;

        foreach (var taskbar in TaskbarWindows())
        {
            any = true;
            if (IsWindowVisible(taskbar))
            {
                hidden = false;
            }
        }

        return any && hidden;
    }
}
