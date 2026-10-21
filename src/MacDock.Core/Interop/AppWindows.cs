using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MacDock.Core.Interop;

public readonly record struct AppWindow(IntPtr Hwnd, string Title, string ProcessPath, string ProcessName);

public static class AppWindows
{
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const long WsExToolWindow = 0x00000080;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int DwmwaCloaked = 14;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr handle, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    // exe paths never change for a live process; the creation time keeps reused PIDs distinct
    private static readonly Dictionary<uint, (long Created, string Path)> ProcessPathCache = new();

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "ApplicationFrameWindow",
    };

    public static IReadOnlyList<AppWindow> Enumerate(int selfProcessId)
    {
        var result = new List<AppWindow>();
        var paths = new Dictionary<uint, string>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GwOwner) != IntPtr.Zero || GetWindowTextLength(hWnd) == 0)
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0)
            {
                return true;
            }

            if (IgnoredClasses.Contains(GetClassName(hWnd)))
            {
                return true;
            }

            DwmGetWindowAttribute(hWnd, DwmwaCloaked, out var cloaked, sizeof(int));
            if (cloaked != 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processId);
            if (processId == selfProcessId)
            {
                return true;
            }

            var path = GetCachedPath(processId, paths);
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            result.Add(new AppWindow(hWnd, GetTitle(hWnd), path, Path.GetFileNameWithoutExtension(path)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        var buffer = new StringBuilder(length + 1);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    private static string GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetCachedPath(uint processId, Dictionary<uint, string> pass)
    {
        if (pass.TryGetValue(processId, out var path))
        {
            return path;
        }

        path = GetProcessPath(processId);
        pass[processId] = path;
        return path;
    }

    public static string GetProcessPath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            GetProcessTimes(handle, out var creation, out _, out _, out _);
            var created = ((long)creation.High << 32) | creation.Low;
            if (created != 0 && ProcessPathCache.TryGetValue(processId, out var cached) && cached.Created == created)
            {
                return cached.Path;
            }

            var size = 1024;
            var buffer = new StringBuilder(size);
            var path = QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : string.Empty;

            // only successes are kept across passes: a process that just started may not be queryable yet
            if (!string.IsNullOrEmpty(path))
            {
                if (ProcessPathCache.Count > 256)
                {
                    ProcessPathCache.Clear();
                }

                ProcessPathCache[processId] = (created, path);
            }

            return path;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
