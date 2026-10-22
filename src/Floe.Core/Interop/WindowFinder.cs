using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Floe.Core.Interop;

public static class WindowFinder
{
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Button",
    };

    public static IntPtr FindMainWindow(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return IntPtr.Zero;
        }

        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                processIds.Add((uint)process.Id);
            }
            catch
            {
                // the process may exit while enumerating
            }
        }

        if (processIds.Count == 0)
        {
            return IntPtr.Zero;
        }

        var result = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (!processIds.Contains(pid) || !IsWindowVisible(hWnd))
            {
                return true;
            }

            if (GetWindow(hWnd, GwOwner) != IntPtr.Zero || GetWindowTextLength(hWnd) == 0)
            {
                return true;
            }

            if (IgnoredClasses.Contains(GetWindowClass(hWnd)))
            {
                return true;
            }

            result = hWnd;
            return false;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
