using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Floe.Core.Interop;

/// <param name="AppId">The window's AppUserModelID when Windows knows one (packaged apps, apps that set their own).</param>
public readonly record struct AppWindow(IntPtr Hwnd, string Title, string ProcessPath, string ProcessName, string AppId = "");

public static class AppWindows
{
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const uint GwOwner = 4;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const long WsCaption = 0x00C00000;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int DwmwaCloaked = 14;
    private const uint WmClose = 0x0010;
    // an untitled window this small is a stub or a message sink, not something to switch to
    private const int MinSize = 32;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder id);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    /// <summary>Asks a window to close, as its own close button would; the app may still ask to save or refuse.</summary>
    public static void RequestClose(IntPtr hwnd) => PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);

    public static bool IsShown(IntPtr hwnd) => IsWindowVisible(hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    // exe paths never change for a live process; the creation time keeps reused PIDs distinct
    private static readonly Dictionary<uint, (long Created, string Path)> ProcessPathCache = new();
    private static readonly Dictionary<uint, (long Created, string AppId)> ProcessAppIdCache = new();
    private static readonly Dictionary<(IntPtr Hwnd, uint ProcessId), (string AppId, byte Checks)> WindowAppIdCache = new();

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        // a top-level CoreWindow is shell chrome (Start, search, the clock flyout); apps show theirs inside a frame
        "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
    };

    public static IReadOnlyList<AppWindow> Enumerate(int selfProcessId)
    {
        var result = new List<AppWindow>();
        var paths = new Dictionary<uint, string>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0
                || (GetWindow(hWnd, GwOwner) != IntPtr.Zero && (exStyle & WsExAppWindow) == 0))
            {
                return true;
            }

            // an untitled window still counts when it asks for a taskbar button or has a caption, and has a real
            // size; untitled stubs and message sinks have neither
            if (GetWindowTextLength(hWnd) == 0 && !IsUntitledAppWindow(hWnd, exStyle))
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

            if (Describe(hWnd, processId, paths) is not { } app)
            {
                return true;
            }

            result.Add(new AppWindow(hWnd, GetTitle(hWnd), app.Path, app.Name, app.AppId));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsUntitledAppWindow(IntPtr hWnd, long exStyle)
    {
        var asksForButton = (exStyle & WsExAppWindow) != 0;
        var captioned = (GetWindowLongPtr(hWnd, GwlStyle).ToInt64() & WsCaption) == WsCaption;
        return (asksForButton || captioned)
            && GetWindowRect(hWnd, out var rect)
            && rect.Right - rect.Left >= MinSize
            && rect.Bottom - rect.Top >= MinSize;
    }

    /// <summary>
    /// The app behind a top-level window: its process name, exe and app id. A UWP app's frame is lent by
    /// ApplicationFrameHost, so the app is the process of the CoreWindow inside it; an empty frame is no app.
    /// </summary>
    public static (string Name, string Path, string AppId)? Describe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hWnd, out var processId);
        return Describe(hWnd, processId, new Dictionary<uint, string>());
    }

    private static (string Name, string Path, string AppId)? Describe(IntPtr hWnd, uint processId, Dictionary<uint, string> paths)
    {
        var path = GetCachedPath(processId, paths);
        // the name survives an exe path that cannot be read (an elevated or protected process)
        var name = string.IsNullOrEmpty(path) ? GetProcessName(processId) : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return null;

        if (string.Equals(name, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            var inner = FrameContent(hWnd, processId);
            if (inner == 0) return null; // a suspended or closing app leaves its empty frame behind
            processId = inner;
            path = GetCachedPath(processId, paths);
            name = string.IsNullOrEmpty(path) ? GetProcessName(processId) : Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)) return null;
        }

        var appId = GetCachedWindowAppId(hWnd, processId);
        if (appId.Length == 0) appId = GetCachedProcessAppId(processId);
        return (name, path, appId);
    }

    private static string GetCachedWindowAppId(IntPtr hWnd, uint processId)
    {
        var key = (hWnd, processId);
        if (WindowAppIdCache.TryGetValue(key, out var cached) && (cached.AppId.Length > 0 || cached.Checks >= 2))
        {
            return cached.AppId;
        }

        var appId = WindowAppId(hWnd);
        if (WindowAppIdCache.Count > 256)
        {
            WindowAppIdCache.Clear();
        }

        var nextChecks = (byte)Math.Min(2, (cached.Checks) + 1);
        WindowAppIdCache[key] = (appId, nextChecks);
        return appId;
    }

    private static string GetCachedProcessAppId(uint processId)
    {
        if (ProcessPathCache.TryGetValue(processId, out var pathInfo)
            && pathInfo.Created != 0
            && ProcessAppIdCache.TryGetValue(processId, out var cached)
            && cached.Created == pathInfo.Created)
        {
            return cached.AppId;
        }

        var appId = ProcessAppId(processId);
        if (pathInfo.Created != 0)
        {
            if (ProcessAppIdCache.Count > 256)
            {
                ProcessAppIdCache.Clear();
            }

            ProcessAppIdCache[processId] = (pathInfo.Created, appId);
        }

        return appId;
    }

    private static uint FrameContent(IntPtr frame, uint frameProcess)
    {
        uint found = 0;
        EnumChildWindows(frame, (child, _) =>
        {
            if (GetClassName(child) != "Windows.UI.Core.CoreWindow") return true;
            GetWindowThreadProcessId(child, out var pid);
            if (pid == frameProcess) return true;
            found = pid;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The AppUserModelID a window carries in its property store (set by the app or the shell), or empty.</summary>
    public static string WindowAppId(IntPtr hWnd)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hWnd, ref iid, out var store) != 0 || store is null) return string.Empty;
            try
            {
                var key = AppUserModelIdKey;
                if (store.GetValue(ref key, out var value) != 0) return string.Empty;
                try
                {
                    return value.Type == VtLpwstr && value.Pointer != IntPtr.Zero
                        ? Marshal.PtrToStringUni(value.Pointer) ?? string.Empty
                        : string.Empty;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>A packaged process's AppUserModelID, or empty for an ordinary desktop process.</summary>
    public static string ProcessAppId(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return string.Empty;
        try
        {
            var length = 256;
            var buffer = new StringBuilder(length);
            return GetApplicationUserModelId(handle, ref length, buffer) == 0 ? buffer.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const ushort VtLpwstr = 31;
    private static readonly PropertyKey AppUserModelIdKey = new() { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer;
        public IntPtr Extra;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0) return string.Empty;
        var buffer = new StringBuilder(length + 1);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

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

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
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
