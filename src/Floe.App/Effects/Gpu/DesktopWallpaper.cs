using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Floe.Core.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Plays a GPU scene as the desktop wallpaper of the primary monitor: a child window behind the desktop icons
/// (in the shell's WorkerW), drawn by its own thread with its own device and audio capture. It stops drawing
/// while a fullscreen window covers the monitor, and gives the desktop its picture back when disposed.
/// </summary>
internal sealed class DesktopWallpaper : IDisposable
{
    private const double FrameRate = 60;

    private readonly Func<IGpuScene> _makeScene;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly WndProc _wndProc;
    private readonly string _className = "FloeDesktopWallpaper" + Environment.ProcessId;
    private volatile bool _stop;
    private uint _threadId;
    private IntPtr _hwnd, _host;

    public DesktopWallpaper(Func<IGpuScene> makeScene)
    {
        _makeScene = makeScene;
        _wndProc = WindowProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "FloeWallpaper" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait(5000);
    }

    /// <summary>False when the shell had no wallpaper layer to join, or the device could not be made.</summary>
    public bool Running { get; private set; }

    public void Dispose()
    {
        _stop = true;
        if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(3000);
        _ready.Dispose();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        IGpuScene? scene = null;
        ID3D11Device? device = null;
        GpuSwapChain? chain = null;
        ID3D11RenderTargetView? target = null;
        AudioSpectrum? audio = null;
        try
        {
            _host = FindWallpaperLayer();
            if (_host == IntPtr.Zero)
            {
                Log("wallpaper: no WorkerW found");
                return;
            }

            // the primary monitor, in the host's client coordinates (the host spans the whole virtual screen)
            int w = GetSystemMetrics(SmCxScreen), h = GetSystemMetrics(SmCyScreen);
            var origin = new POINT();
            ScreenToClient(_host, ref origin);
            CreateWindow(origin.X, origin.Y, w, h);

            device = GpuDevice.Create();
            chain = GpuSwapChain.Create(device, _hwnd, w, h);
            scene = _makeScene();
            scene.Initialize(device);
            scene.Resize(w, h);
            using (var back = chain.Chain.GetBuffer<ID3D11Texture2D>(0)) target = device.CreateRenderTargetView(back, null);
            audio = new AudioSpectrum();
            Running = true;
            Log($"wallpaper: on {w}x{h} in 0x{_host.ToInt64():X}");
            _ready.Set();
            Loop(device, chain, target, scene, audio, w, h);
        }
        catch (Exception ex)
        {
            Log("wallpaper: " + ex.Message);
        }
        finally
        {
            _ready.Set();
            Running = false;
            try { audio?.Dispose(); } catch { }
            try { scene?.ReleaseDevice(); scene?.Dispose(); } catch { }
            target?.Dispose();
            chain?.Dispose();
            device?.ImmediateContext.ClearState();
            device?.Dispose();
            if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
            UnregisterClassW(_className, GetModuleHandleW(null));
            RestoreDesktop();
        }
    }

    private void Loop(ID3D11Device device, GpuSwapChain chain, ID3D11RenderTargetView target, IGpuScene scene, AudioSpectrum audio, int w, int h)
    {
        var context = device.ImmediateContext;
        var frame = new GpuFrame { Width = w, Height = h, PixelsPerDip = 1, Fade = 1, DockLeft = 0, DockRight = 0 };
        var calm = new AudioCalm();
        long interval = (long)(Stopwatch.Frequency / FrameRate);
        long last = Stopwatch.GetTimestamp(), lastCheck = 0;
        bool covered = false;
        double time = 0;
        while (!_stop)
        {
            if (!Pump()) return;
            long now = Stopwatch.GetTimestamp();
            if (now - lastCheck > Stopwatch.Frequency / 4)
            {
                lastCheck = now;
                bool was = covered;
                string? why = Hidden();
                covered = why is not null;
                if (covered != was) Log(covered ? "wallpaper: paused, " + why : "wallpaper: resumed");
            }

            if (covered)
            {
                // nothing to draw for: sleep, but keep the clock from jumping when it comes back
                MsgWaitForMultipleObjects(0, null, false, 250, QsAllInput);
                last = Stopwatch.GetTimestamp();
                continue;
            }

            long wait = interval - (now - last);
            if (wait > 0)
            {
                MsgWaitForMultipleObjects(0, null, false, (uint)Math.Max(1, wait * 1000 / Stopwatch.Frequency), QsAllInput);
                continue;
            }

            WaitForSingleObject(chain.FrameLatency, 100);
            now = Stopwatch.GetTimestamp();
            float dt = (float)Math.Min(.1, (now - last) / (double)Stopwatch.Frequency);
            last = now;
            time += dt;

            frame.Dt = dt;
            frame.Time = (float)time;
            audio.Read(frame.Audio, dt);
            calm.Apply(frame.Audio, dt);
            context.ClearRenderTargetView(target, new Color4(0, 0, 0, 1));
            try
            {
                scene.Render(context, target, frame);
                chain.Chain.Present(1, PresentFlags.None).CheckError();
            }
            catch (Exception ex) when (GpuDevice.IsDeviceLost(ex))
            {
                // a lost device ends the wallpaper; the desktop gets its picture back and the dock stays up
                Log("wallpaper: device lost");
                return;
            }
        }
    }

    private static bool Pump()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.Message == WmQuit) return false;
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        return true;
    }

    // ---- the shell's wallpaper layer -------------------------------------------------------------

    /// <summary>
    /// Asks Progman for the WorkerW that sits between the wallpaper and the icons. Before 24H2 it is a top-level
    /// window right after the one holding SHELLDLL_DefView; from 24H2 it is a child of Progman.
    /// </summary>
    private static IntPtr FindWallpaperLayer()
    {
        var progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;
        SendMessageTimeoutW(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SmtoNormal, 1000, out _);
        SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SmtoNormal, 1000, out _);

        var child = FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
        if (child != IntPtr.Zero) return child;

        IntPtr found = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowExW(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            found = FindWindowExW(IntPtr.Zero, top, "WorkerW", null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Why nobody can see the desktop of the primary monitor, or null when they can: a fullscreen window in
    /// front, a maximized window, or ordinary windows that between them cover nearly all of it.
    /// </summary>
    private static string? Hidden()
    {
        int w = GetSystemMetrics(SmCxScreen), h = GetSystemMetrics(SmCyScreen);
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && !IsShell(fg) && GetWindowRect(fg, out var f) && f.Left <= 0 && f.Top <= 0 && f.Right >= w && f.Bottom >= h)
        {
            return "fullscreen window";
        }

        // a coarse grid over the screen: the cells the windows cover, so overlapping windows count once
        const int Cols = 48, Rows = 27;
        var cells = new bool[Cols * Rows];
        int covered = 0;
        string? why = null;
        EnumWindows((hwnd, _) =>
        {
            if (!Counts(hwnd) || !GetWindowRect(hwnd, out var r)) return true;
            int x0 = Math.Max(0, r.Left), y0 = Math.Max(0, r.Top), x1 = Math.Min(w, r.Right), y1 = Math.Min(h, r.Bottom);
            if (x1 <= x0 || y1 <= y0) return true;
            if (IsZoomed(hwnd))
            {
                why = "maximized window";
                return false;
            }

            for (int cy = (y0 * Rows) / h; cy < Rows && (cy * h) / Rows < y1; cy++)
            {
                for (int cx = (x0 * Cols) / w; cx < Cols && (cx * w) / Cols < x1; cx++)
                {
                    // a cell counts when the window holds its centre
                    int px = ((cx * w) / Cols) + (w / Cols / 2), py = ((cy * h) / Rows) + (h / Rows / 2);
                    if (px < x0 || px >= x1 || py < y0 || py >= y1 || cells[(cy * Cols) + cx]) continue;
                    cells[(cy * Cols) + cx] = true;
                    covered++;
                }
            }

            return true;
        }, IntPtr.Zero);
        if (why is not null) return why;
        return covered >= Cols * Rows * 92 / 100 ? "windows cover the desktop" : null;
    }

    /// <summary>A window someone sees on the desktop: visible, not minimized, not cloaked, not a tool or overlay.</summary>
    private static bool Counts(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsShell(hwnd)) return false;
        long ex = GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64();
        // the dock, its visualizer and other overlays are tool or click-through windows
        if ((ex & (WsExToolWindow | WsExNoActivate | WsExTransparentStyle)) != 0) return false;
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        return true;
    }

    private static bool IsShell(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>Repaints the static wallpaper over what the scene left behind, without changing any setting.</summary>
    private void RestoreDesktop()
    {
        try
        {
            var path = new StringBuilder(260);
            if (SystemParametersInfoW(SpiGetDeskWallpaper, (uint)path.Capacity, path, 0) && path.Length > 0)
            {
                // flags 0: applies the same picture again in memory only, nothing is written or broadcast
                SystemParametersInfoW(SpiSetDeskWallpaper, 0, path, 0);
            }

            if (_host != IntPtr.Zero) RedrawWindow(_host, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
        }
        catch
        {
            // best effort; the next explorer repaint fixes it anyway
        }
    }

    private void CreateWindow(int x, int y, int w, int h)
    {
        var instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

        _hwnd = CreateWindowExW(WsExNoRedirectionBitmap | WsExToolWindow | WsExNoActivate, _className, "Floe Wallpaper",
            WsChild | WsVisible | WsDisabled, x, y, w, h, _host, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam) => message switch
    {
        WmNcHitTest => new IntPtr(HtTransparent),
        WmEraseBackground => new IntPtr(1),
        _ => DefWindowProcW(hwnd, message, wParam, lParam),
    };

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "floe.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // diagnostics only
        }
    }

    // ---- interop ---------------------------------------------------------------------------------

    private const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsDisabled = 0x08000000;
    private const uint WsExNoRedirectionBitmap = 0x00200000, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    private const uint WmQuit = 0x0012, WmNcHitTest = 0x0084, WmEraseBackground = 0x0014;
    private const int HtTransparent = -1;
    private const uint PmRemove = 1, QsAllInput = 0x04FF, SmtoNormal = 0;
    private const int SmCxScreen = 0, SmCyScreen = 1;
    private const int GwlExStyle = -20, DwmwaCloaked = 14;
    private const long WsExTransparentStyle = 0x00000020;
    private const uint SpiGetDeskWallpaper = 0x0073, SpiSetDeskWallpaper = 0x0014;
    private const uint RdwInvalidate = 0x1, RdwErase = 0x4, RdwAllChildren = 0x80, RdwUpdateNow = 0x100;

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public POINT Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG message, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[]? handles, bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfoW(uint action, uint param, StringBuilder value, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr update, IntPtr region, uint flags);
}
