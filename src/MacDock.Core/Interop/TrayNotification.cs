using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class TrayNotification
{
    public const int CallbackMessage = 0x8000 + 1; // WM_APP + 1

    public const int WmLeftButtonUp = 0x0202;
    public const int WmRightButtonUp = 0x0205;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    /// <summary>Broadcast message Explorer sends when it (re)starts; the tray icon and appbar must be re-added.</summary>
    public static int TaskbarCreatedMessage { get; } = RegisterWindowMessage("TaskbarCreated");

    public static bool Add(IntPtr window, string tip, IntPtr icon)
    {
        var data = Create(window, tip, icon);
        return Shell_NotifyIcon(NimAdd, ref data);
    }

    public static void Remove(IntPtr window, string tip, IntPtr icon)
    {
        var data = Create(window, tip, icon);
        Shell_NotifyIcon(NimDelete, ref data);
    }

    public static IntPtr LoadAppIcon(string exePath)
    {
        try
        {
            var icons = new IntPtr[1];
            if (ExtractIconEx(exePath, 0, icons, null, 1) > 0 && icons[0] != IntPtr.Zero)
            {
                return icons[0];
            }
        }
        catch
        {
            // fall through to the stock icon
        }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
    }

    public static void DestroyAppIcon(IntPtr icon)
        => DestroyIcon(icon);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    private static NotifyIconData Create(IntPtr window, string tip, IntPtr icon)
        => new()
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = icon,
            Tip = tip,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr owner, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;

    /// <summary>Shows a small native menu; returns the selected index or -1. "-" adds a separator.</summary>
    public static int ShowMenu(IntPtr owner, int x, int y, IReadOnlyList<string> labels)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            for (var i = 0; i < labels.Count; i++)
            {
                if (labels[i] == "-")
                {
                    AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                }
                else
                {
                    AppendMenu(menu, MfString, (UIntPtr)(uint)(i + 1), labels[i]);
                }
            }

            SetForegroundWindow(owner);
            var command = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton, x, y, 0, owner, IntPtr.Zero);
            return command - 1;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }
}
