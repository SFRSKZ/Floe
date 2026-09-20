using System.Runtime.InteropServices;
using System.Text;

namespace MacDock.Core.Interop;

public static class TaskbarController
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static IEnumerable<IntPtr> TaskbarWindows()
    {
        var result = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            var buffer = new StringBuilder(64);
            GetClassName(hWnd, buffer, buffer.Capacity);
            var className = buffer.ToString();

            if (className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd")
            {
                result.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        return result;
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
