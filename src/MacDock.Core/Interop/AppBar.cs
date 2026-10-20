using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

/// <summary>
/// Thin wrapper around SHAppBarMessage: registers the dock as a shell appbar so the
/// work area stops above the visible bar instead of behind it.
/// </summary>
public sealed class AppBar : IDisposable
{
    /// <summary>Callback message posted to the appbar window (WM_APP + 2, distinct from the tray callback).</summary>
    public const int CallbackMessage = 0x8000 + 2;

    public const int AbnPosChanged = 1;
    public const int AbnFullscreenApp = 2;

    private const int AbmNew = 0;
    private const int AbmRemove = 1;
    private const int AbmQueryPos = 2;
    private const int AbmSetPos = 3;

    private const uint AbeBottom = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public ScreenInput.Rect Rect;
        public IntPtr Param;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    private IntPtr _hwnd;
    private ScreenInput.Rect _applied;

    public bool IsRegistered { get; private set; }

    public bool Register(IntPtr hwnd, uint callbackMessage = CallbackMessage)
    {
        if (IsRegistered)
        {
            return true;
        }

        var data = CreateData(hwnd);
        data.CallbackMessage = callbackMessage;
        IsRegistered = SHAppBarMessage(AbmNew, ref data) != IntPtr.Zero;
        if (IsRegistered)
        {
            _hwnd = hwnd;
        }

        return IsRegistered;
    }

    public void Remove()
    {
        if (!IsRegistered)
        {
            return;
        }

        var data = CreateData(_hwnd);
        SHAppBarMessage(AbmRemove, ref data);
        IsRegistered = false;
        _applied = default;
    }

    /// <summary>
    /// Reserves a bar of <paramref name="heightPx"/> along the bottom edge of <paramref name="monitor"/>.
    /// ABM_QUERYPOS lets the shell shift the strip away from other appbars; the bottom edge is then
    /// re-anchored to the monitor so the dock keeps the physical bottom strip (e.g. over Explorer's
    /// hidden-but-still-reserved taskbar) instead of leaving a dead gap below it.
    /// </summary>
    public bool SetPosition(ScreenInput.Rect monitor, int heightPx)
    {
        if (!IsRegistered || heightPx <= 0)
        {
            return false;
        }

        var height = Math.Min(heightPx, monitor.Bottom - monitor.Top);

        // SETPOS changes the work area, which broadcasts WM_SETTINGCHANGE, which makes the dock
        // reposition again: skip identical requests so that never turns into a loop.
        if (_applied.Left == monitor.Left && _applied.Right == monitor.Right
            && _applied.Bottom == monitor.Bottom && _applied.Top == monitor.Bottom - height)
        {
            return true;
        }
        var data = CreateData(_hwnd);
        data.Edge = AbeBottom;
        data.Rect = new ScreenInput.Rect
        {
            Left = monitor.Left,
            Right = monitor.Right,
            Bottom = monitor.Bottom,
            Top = monitor.Bottom - height,
        };

        SHAppBarMessage(AbmQueryPos, ref data);

        // keep our own edge anchor: QUERYPOS reports the strip left by the taskbar, which is hidden
        data.Rect.Left = monitor.Left;
        data.Rect.Right = monitor.Right;
        data.Rect.Bottom = monitor.Bottom;
        data.Rect.Top = monitor.Bottom - height;

        SHAppBarMessage(AbmSetPos, ref data);
        _applied = new ScreenInput.Rect { Left = monitor.Left, Right = monitor.Right, Bottom = monitor.Bottom, Top = monitor.Bottom - height };
        return true;
    }

    public void Dispose() => Remove();

    private static AppBarData CreateData(IntPtr hwnd)
        => new()
        {
            Size = Marshal.SizeOf<AppBarData>(),
            Window = hwnd,
            CallbackMessage = CallbackMessage,
        };
}
