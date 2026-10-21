using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using MacDock.Core.Interop;

namespace MacDock.App.Services;

internal static class CaptureReveal
{
    private static readonly Dictionary<IntPtr, int> Windows = new();
    private static readonly bool NoExclude = (Environment.GetEnvironmentVariable("MACDOCK_NOFX") ?? "").Contains("exclude");
    private static Dispatcher? _dispatcher;
    private static DispatcherTimer? _timer;
    private static HwndSource? _source;
    private static EventWaitHandle? _signal;
    private static RegisteredWaitHandle? _wait;
    private static long _deadline;
    private static long _screenshotDeadline;
    private static bool _recording;
    private static bool _stopped;

    public static bool Recording
    {
        get => _recording;
        set { _recording = value; Apply(); }
    }

    public static void Initialize(HwndSource source)
    {
        _stopped = false;
        _source = source;
        _dispatcher = source.Dispatcher;
        source.AddHook(WndProc);
        AddClipboardFormatListener(source.Handle);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => Apply(), _dispatcher);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MacDock.Reveal");
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
            _dispatcher.BeginInvoke(new Action(() => { if (!_stopped) Reveal(TimeSpan.FromSeconds(4)); })), null, Timeout.Infinite, false);
        ScreenshotKeyboard.Start(replay => _dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_stopped) return;
            _screenshotDeadline = Environment.TickCount64 + 12000;
            Apply();
            await Task.Delay(120);
            if (!_stopped) replay();
        })));
    }

    public static void Exclude(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || NoExclude) return;
        Windows[hwnd] = Windows.GetValueOrDefault(hwnd) + 1;
        if (BackdropCapture.Frozen) WindowAffinity.IncludeInCapture(hwnd);
        else WindowAffinity.ExcludeFromCapture(hwnd);
    }

    public static void Forget(IntPtr hwnd)
    {
        if (!Windows.TryGetValue(hwnd, out var count)) return;
        if (count > 1) Windows[hwnd] = count - 1;
        else
        {
            Windows.Remove(hwnd);
            WindowAffinity.IncludeInCapture(hwnd);
        }
    }

    public static void Reveal(TimeSpan duration)
    {
        _deadline = Math.Max(_deadline, Environment.TickCount64 + (long)duration.TotalMilliseconds);
        Apply();
    }

    private static void Apply()
    {
        var reveal = _recording || Environment.TickCount64 < Math.Max(_deadline, _screenshotDeadline);
        if (reveal == BackdropCapture.Frozen) return;
        if (reveal) BackdropCapture.Frozen = true;
        foreach (var hwnd in Windows.Keys)
        {
            if (reveal) WindowAffinity.IncludeInCapture(hwnd);
            else WindowAffinity.ExcludeFromCapture(hwnd);
        }
        if (!reveal) BackdropCapture.Frozen = false;
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x031D && _screenshotDeadline > Environment.TickCount64
            && (IsClipboardFormatAvailable(2) || IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17)))
        {
            _screenshotDeadline = Math.Min(_screenshotDeadline, Environment.TickCount64 + 300);
        }
        return IntPtr.Zero;
    }

    public static void Shutdown()
    {
        _stopped = true;
        ScreenshotKeyboard.Stop();
        _wait?.Unregister(null);
        _signal?.Dispose();
        _timer?.Stop();
        if (_source is not null)
        {
            RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
        }
        _recording = false;
        _deadline = _screenshotDeadline = 0;
        Apply();
        Windows.Clear();
    }

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
}
