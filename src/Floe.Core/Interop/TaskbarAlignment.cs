using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Floe.Core.Interop;

/// <summary>
/// Where Windows opens its Start menu: in the middle or at the left, as the taskbar's alignment says. A dock laid out
/// as a taskbar keeps its start button at the left, so while it runs the alignment is turned left; the value Windows
/// had before is kept under Floe's own key and put back when the dock gives the taskbar back, even after a crash.
/// </summary>
public static class TaskbarAlignment
{
    private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string AlignValue = "TaskbarAl";
    private const string FloeKey = @"Software\Floe";

    private const int Left = 0;
    // Windows 11 aligns to the middle when the value is missing
    private const int Centre = 1;

    private const int HwndBroadcast = 0xFFFF;
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    /// <summary>left: the dock is a taskbar now; otherwise the alignment Windows had comes back.</summary>
    public static void Apply(bool left, string product)
    {
        try
        {
            var savedName = $"{product}.TaskbarAlBefore";
            using var floe = Registry.CurrentUser.CreateSubKey(FloeKey);
            using var explorer = Registry.CurrentUser.CreateSubKey(ExplorerKey);
            var current = explorer.GetValue(AlignValue) is int value ? value : Centre;
            var saved = floe.GetValue(savedName) as int?;

            if (left)
            {
                // the first time only: a crash leaves the old value behind, and it must not be overwritten with left
                if (saved is null) floe.SetValue(savedName, current, RegistryValueKind.DWord);
                if (current == Left) return;
                explorer.SetValue(AlignValue, Left, RegistryValueKind.DWord);
            }
            else
            {
                if (saved is null) return;
                floe.DeleteValue(savedName, false);
                if (current == saved.Value) return;
                explorer.SetValue(AlignValue, saved.Value, RegistryValueKind.DWord);
            }

            SendMessageTimeout((IntPtr)HwndBroadcast, WmSettingChange, IntPtr.Zero, "TraySettings", SmtoAbortIfHung, 500, out _);
        }
        catch
        {
            // the Start menu opening in the middle is no reason to stop the dock
        }
    }
}
