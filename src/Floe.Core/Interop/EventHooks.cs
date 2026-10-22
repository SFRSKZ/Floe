using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class EventHooks
{
    public const uint EventSystemAlert = 0x0002;
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectCreate = 0x8000;
    public const uint EventObjectDestroy = 0x8001;

    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    public delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    public sealed class Hook : IDisposable
    {
        private readonly WinEventDelegate _callback;
        private IntPtr _hook;

        public Hook(uint eventMin, uint eventMax, Action<IntPtr> onWindowEvent)
        {
            // keep the delegate alive and only report top-level window events
            _callback = (_, _, hwnd, idObject, idChild, _, _) =>
            {
                if (idObject == 0 && idChild == 0)
                {
                    onWindowEvent(hwnd);
                }
            };

            _hook = SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);
        }

        public bool IsValid => _hook != IntPtr.Zero;

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
