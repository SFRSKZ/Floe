using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Floe.Core.Interop;

namespace Floe.App.Services;

internal static class CaptureReveal
{
    private static readonly Dictionary<IntPtr, int> Windows = new();
    private static readonly bool NoExclude = (Environment.GetEnvironmentVariable("FLOE_NOFX") ?? "").Contains("exclude");
    private static Dispatcher? _dispatcher;
    private static DispatcherTimer? _timer;
    private static HwndSource? _source;
    private static EventWaitHandle? _signal;
    private static RegisteredWaitHandle? _wait;
    private static long _deadline;
    private static long _screenshotDeadline;
    private static bool _recording;
    private static bool _snipRecording;
    private static DispatcherTimer? _snipTimer;
    private static bool _stopped;

    // The Snipping Tool's recording toolbar, by its title in Turkish and English: while it is up a recording runs.
    private static readonly string[] SnipToolbarTitles = { "Kayıt araç çubuğu", "Recording toolbar" };

    public static bool Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            Apply();
            // a deadline may still be armed from before recording started
            Arm();
        }
    }

    public static void Initialize(HwndSource source)
    {
        _stopped = false;
        _source = source;
        _dispatcher = source.Dispatcher;
        source.AddHook(WndProc);
        AddClipboardFormatListener(source.Handle);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer = timer;
        timer.Tick += (_, _) =>
        {
            Apply();
            // nothing changes until the next reveal, and recording applies its state directly
            if (_recording || Environment.TickCount64 >= Math.Max(_deadline, _screenshotDeadline))
            {
                timer.Stop();
            }
        };
        // a screen recording outlasts the screenshot window, so the dock stays in it while the toolbar is up
        _snipTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _snipTimer.Tick += (_, _) =>
        {
            var recording = IsSnipRecording();
            if (recording == _snipRecording) return;
            _snipRecording = recording;
            Apply();
        };
        _snipTimer.Start();
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Floe.Reveal");
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
            _dispatcher.BeginInvoke(new Action(() => { if (!_stopped) Reveal(TimeSpan.FromSeconds(4)); })), null, Timeout.Infinite, false);
        ScreenshotKeyboard.Start(replay => _dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_stopped) return;
            _screenshotDeadline = Environment.TickCount64 + 12000;
            Apply();
            Arm();
            await Task.Delay(120);
            if (!_stopped) replay();
        })));
    }

    /// <summary>Polls while a reveal or screenshot deadline is armed; recording needs no timer.</summary>
    private static void Arm()
    {
        if (_timer is null || _timer.IsEnabled) return;
        if (Environment.TickCount64 < Math.Max(_deadline, _screenshotDeadline)) _timer.Start();
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
        Arm();
    }

    private static void Apply()
    {
        var reveal = _recording || _snipRecording || Environment.TickCount64 < Math.Max(_deadline, _screenshotDeadline);
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
        _snipTimer?.Stop();
        _snipRecording = false;
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

    /// <summary>Whether a visible Snipping Tool recording toolbar is on screen.</summary>
    private static bool IsSnipRecording()
    {
        var found = false;
        var title = new System.Text.StringBuilder(64);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            title.Clear();
            if (GetWindowText(hwnd, title, title.Capacity) == 0) return true;
            var text = title.ToString();
            if (!SnipToolbarTitles.Any(t => text.Equals(t, StringComparison.OrdinalIgnoreCase))) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                found = process.ProcessName.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // the window's process went away between the two calls
            }
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
}
