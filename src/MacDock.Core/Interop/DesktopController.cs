using System;
using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class DesktopController
{
    private const byte VkLWin = 0x5B;
    private const byte VkD = 0x44;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkVolumeDown = 0xAE;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    /// <summary>Shows the desktop (toggles all windows away) so files can be dropped on it.</summary>
    public static void ShowDesktop()
    {
        keybd_event(VkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(VkD, 0, 0, UIntPtr.Zero);
        keybd_event(VkD, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkLWin, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    public const int HotkeyIdVeil = 1;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint VkV = 0x56;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static void VolumeUp() => Tap(VkVolumeUp);

    public static void VolumeDown() => Tap(VkVolumeDown);

    private static void Tap(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }
}
