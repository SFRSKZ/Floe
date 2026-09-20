using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class Keyboard
{
    private const byte VkLWin = 0x5B;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    public static void SendKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    public static void SendWinKey(byte key)
    {
        keybd_event(VkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkLWin, 0, KeyEventKeyUp, UIntPtr.Zero);
    }
}
