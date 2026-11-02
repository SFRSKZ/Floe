using System.Runtime.InteropServices;
using Floe.Core.Models;

namespace Floe.Core.Interop;

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

    private const uint AbeLeft = 0;
    private const uint AbeRight = 1;
    private const uint AbeTop = 2;
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
    private ScreenInput.Rect _requested;

    public bool IsRegistered { get; private set; }

    public ScreenInput.Rect AppliedRect => _applied;

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
        _requested = default;
    }

    /// <summary>
    /// Reserves a strip of <paramref name="thicknessPx"/> along the selected monitor edge. With
    /// <paramref name="holdEdge"/> the strip stays on the physical edge, because a hidden taskbar still reserves
    /// its own and ABM_QUERYPOS would leave a dead gap; otherwise the shell moves it beside the visible taskbar and
    /// any other appbars.
    /// </summary>
    public bool SetPosition(ScreenInput.Rect monitor, int thicknessPx, DockEdge edge, bool holdEdge)
    {
        if (!IsRegistered || thicknessPx <= 0)
        {
            return false;
        }

        var width = monitor.Right - monitor.Left;
        var height = monitor.Bottom - monitor.Top;
        var thickness = Math.Min(thicknessPx, edge is DockEdge.Left or DockEdge.Right ? width : height);
        var data = CreateData(_hwnd);
        data.Edge = edge switch
        {
            DockEdge.Left => AbeLeft,
            DockEdge.Right => AbeRight,
            DockEdge.Top => AbeTop,
            _ => AbeBottom,
        };
        data.Rect = Strip(monitor, thickness, edge);

        if (!holdEdge)
        {
            SHAppBarMessage(AbmQueryPos, ref data);
            data.Rect = Strip(data.Rect, thickness, edge);
        }

        // SETPOS changes the work area and notifies every appbar, this one too, which makes the dock
        // reposition again: skip identical requests so that never turns into a loop.
        var requested = data.Rect;
        if (Same(requested, _requested))
        {
            return true;
        }

        SHAppBarMessage(AbmSetPos, ref data);
        _requested = requested;
        _applied = holdEdge ? requested : data.Rect;
        return true;
    }

    private static ScreenInput.Rect Strip(ScreenInput.Rect area, int thickness, DockEdge edge) => edge switch
    {
        DockEdge.Left => new ScreenInput.Rect { Left = area.Left, Top = area.Top, Right = area.Left + thickness, Bottom = area.Bottom },
        DockEdge.Right => new ScreenInput.Rect { Left = area.Right - thickness, Top = area.Top, Right = area.Right, Bottom = area.Bottom },
        DockEdge.Top => new ScreenInput.Rect { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Top + thickness },
        _ => new ScreenInput.Rect { Left = area.Left, Top = area.Bottom - thickness, Right = area.Right, Bottom = area.Bottom },
    };

    private static bool Same(ScreenInput.Rect a, ScreenInput.Rect b)
        => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    public void Dispose() => Remove();

    private static AppBarData CreateData(IntPtr hwnd)
        => new()
        {
            Size = Marshal.SizeOf<AppBarData>(),
            Window = hwnd,
            CallbackMessage = CallbackMessage,
        };
}
