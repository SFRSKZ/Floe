using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// A composition-hosted visualizer strip. One background thread owns the window, the D3D11 device and the
/// render loop, so a frame never waits on the WPF render thread; the WPF side only posts small requests.
/// The window is click-through, never activates and is excluded from the dock's backdrop capture.
/// </summary>
internal sealed class GpuLayerWindow : IDisposable
{
    // The old layered visualizer's fades, so the switch is invisible.
    private const double FadeInSeconds = .22;
    private const double FadeOutSeconds = .40;
    private static readonly MediaColor BasePrimary = MediaColor.FromRgb(0x5C, 0xA8, 0xFF);

    private readonly IGpuScene _scene;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEvent _ready = new(false);
    private readonly object _gate = new();
    private readonly GpuFrame _frame = new();
    private readonly float[] _paletteR = new float[GpuFrame.PaletteSize];
    private readonly float[] _paletteG = new float[GpuFrame.PaletteSize];
    private readonly float[] _paletteB = new float[GpuFrame.PaletteSize];
    private readonly IntPtr[] _waitOne = new IntPtr[1];
    private readonly ID3D11ShaderResourceView[] _noInputs = new ID3D11ShaderResourceView[1];
    private readonly WndProc _wndProc;

    // Requests from the WPF thread, guarded by _gate.
    private bool _placePending;
    private bool _dockPending;
    private bool _sinkPending;
    private Int32Rect _requestedRect;
    private double _requestedPpd = 1;
    private IntPtr _requestedDock;
    private volatile bool _runningWanted;
    private volatile bool _waveRequested;
    // the album cover and the playing icon's place, handed over from the UI thread
    private volatile byte[]? _cover;
    private volatile int _coverSize;
    private int _coverVersion;
    private float _sourceX = 0.5f;
    private float _dockLeft = 0.37f, _dockRight = 0.63f;
    private volatile bool _led;
    private volatile bool _disposed;

    // Render-thread state; nothing below is touched from the WPF thread.
    private string _className = string.Empty;
    private IntPtr _hwnd;
    private volatile bool _exit;
    private bool _halted;
    private bool _logged;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private GpuSwapChain? _swapChain;
    private ID3D11RenderTargetView? _renderTarget;
    private LedScreenPass? _ledPass;
    private bool _ledUnavailable;
    private AudioSpectrum? _audio;
    private readonly AudioCalm _calm = new();
    private bool _active;
    private bool _shown;
    private double _fade;
    private double _time;
    private double _lastFrameSeconds;
    private long _lastPresent;
    private int _width = 1;
    private int _height = 1;
    private int _left;
    private int _top;
    private int _windowWidth;
    private int _windowHeight;
    private double _ppd = 1;
    private IntPtr _dock;

    /// <param name="capturable">
    /// Leaves the layer in screen captures, so the dock's glass shows it through its blur; by default the
    /// light stays out of the dock's backdrop.
    /// </param>
    public GpuLayerWindow(IGpuScene scene, bool capturable = false)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _wndProc = WindowProc;
        _className = "FloeGpuLayer_" + Guid.NewGuid().ToString("N");
        _thread = new Thread(RenderThread) { IsBackground = true, Name = "FloeGpuLayer" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        // The handle is public API, so wait for the window to exist; a hung creation simply leaves it zero.
        _ready.WaitOne(TimeSpan.FromSeconds(5));

        var hwnd = Volatile.Read(ref _hwnd);
        if (hwnd != IntPtr.Zero && !capturable && Application.Current is { } app)
        {
            app.Dispatcher.Invoke(() => CaptureReveal.Exclude(hwnd));
        }
    }

    public IntPtr Handle => Volatile.Read(ref _hwnd);

    /// <summary>
    /// Frames a second this layer draws at, overriding the visualizer pacing (0 keeps it); a layer that moves with
    /// the dock asks for the monitor's rate, so it never steps slower than the dock beside it.
    /// </summary>
    public double Fps { get; set; }

    /// <summary>
    /// Draws only when <see cref="RequestFrame"/> asks (while running; the fade-out still runs at <see cref="Fps"/>):
    /// for a layer whose content arrives from the dock's frames, so it never presents the same picture twice.
    /// </summary>
    public bool OnDemand { get; set; }

    public void RequestFrame()
    {
        _frameRequested = true;
        _wake.Set();
    }

    private volatile bool _frameRequested;

    public bool IsRunning => _runningWanted && !_disposed;

    /// <summary>The dust wave look of the stardust scene; copied into <see cref="GpuFrame.Wave"/>.</summary>
    public bool Wave
    {
        get => _waveRequested;
        set => _waveRequested = value;
    }

    /// <summary>Shows the strip as a colour LED screen instead of the plain picture.</summary>
    /// <summary>The album cover as square BGRA rows (null: none); a new array counts as a new song.</summary>
    public void SetCover(byte[]? bgra, int size)
    {
        if (ReferenceEquals(bgra, _cover)) return;
        _coverSize = size;
        _cover = bgra;
        Interlocked.Increment(ref _coverVersion);
    }

    /// <summary>Where the dock's capsule stands along the strip, 0..1.</summary>
    public void SetDock(float left, float right)
    {
        Volatile.Write(ref _dockLeft, left);
        Volatile.Write(ref _dockRight, right);
    }

    /// <summary>Where the playing app's icon sits along the strip, 0..1.</summary>
    public float SourceX
    {
        get => Volatile.Read(ref _sourceX);
        set => Volatile.Write(ref _sourceX, value);
    }

    public bool LedScreen
    {
        get => _led;
        set => _led = value;
    }

    /// <summary>Physical-pixel strip, in screen coordinates, kept just below <paramref name="dock"/>.</summary>
    public void Place(Int32Rect pixels, double pixelsPerDip, IntPtr dock)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixelsPerDip <= 0) return;
        lock (_gate)
        {
            if (_requestedRect == pixels
                && Math.Abs(_requestedPpd - pixelsPerDip) < .0001
                && _requestedDock == dock
                && !_placePending
                && !_dockPending)
            {
                return;
            }

            _requestedRect = pixels;
            _requestedPpd = pixelsPerDip;
            _requestedDock = dock;
            _placePending = true;
        }

        _wake.Set();
    }

    public void PlaceBelowDock(IntPtr dock)
    {
        // always re-applied: the dock may have come back above or below us with the same handle
        lock (_gate)
        {
            _requestedDock = dock;
            _dockPending = true;
            _sinkPending = true;
        }

        _wake.Set();
    }

    public void SetRunning(bool running)
    {
        if (_disposed) return;
        _runningWanted = running;
        _wake.Set();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runningWanted = false;

        // Take the handle before the render thread destroys it: CaptureReveal keys its registry by hwnd.
        var hwnd = Volatile.Read(ref _hwnd);

        _exit = true;
        _wake.Set();
        if (_thread.IsAlive) _thread.Join(TimeSpan.FromSeconds(3));
        bool stopped = !_thread.IsAlive;

        if (stopped) _scene.Dispose();

        if (hwnd != IntPtr.Zero)
        {
            if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
            {
                try { app.Dispatcher.Invoke(() => CaptureReveal.Forget(hwnd)); }
                catch (Exception ex) { Debug.WriteLine($"[Floe.Gpu] capture cleanup: {ex.Message}"); }
            }
            else
            {
                CaptureReveal.Forget(hwnd);
            }
        }

        if (stopped)
        {
            _wake.Dispose();
            _ready.Dispose();
        }
    }

    // ---- render thread ---------------------------------------------------------------------------

    private void RenderThread()
    {
        try
        {
            CreateWindow();
            CreateDevice();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
        finally
        {
            _ready.Set();
        }

        if (_hwnd != IntPtr.Zero && _device is not null)
        {
            try { RenderLoop(); }
            catch (Exception ex) { Log(ex); }
        }

        try { _scene.ReleaseDevice(); } catch (Exception ex) { Log(ex); }
        ReleaseDevice();
        if (_hwnd != IntPtr.Zero)
        {
            var hwnd = _hwnd;
            _hwnd = IntPtr.Zero;
            // Win32 only destroys a window on the thread that created it, so this is Dispose's teardown.
            DestroyWindow(hwnd);
            UnregisterClass(_className, GetModuleHandleW(null));
        }
    }

    private void RenderLoop()
    {
        var clock = Stopwatch.StartNew();
        _lastFrameSeconds = 0;

        while (!_exit)
        {
            PumpMessages();
            if (_exit) return;

            try
            {
                ApplyPendingPlace();

                bool wanted = _runningWanted;
                if (wanted && !_active && !_halted) StartRendering(clock);

                if (!_active)
                {
                    WaitIdle();
                    continue;
                }

                // The chain first, the frame rate second: this is what keeps latency at one frame.
                WaitForSlot();

                double now = clock.Elapsed.TotalSeconds;
                double dt = Math.Clamp(now - _lastFrameSeconds, .001, .1);
                _lastFrameSeconds = now;

                if (wanted)
                {
                    _fade = Math.Min(1, _fade + (dt / FadeInSeconds));
                }
                else
                {
                    _fade -= dt / FadeOutSeconds;
                    if (_fade <= 0)
                    {
                        _fade = 0;
                        if (!_runningWanted)
                        {
                            // the fade has finished: hide, release the audio and wait on the event
                            StopRendering();
                            continue;
                        }
                    }
                }

                _time += dt;
                DrawFrame((float)dt);
            }
            catch (Exception ex) when (GpuDevice.IsDeviceLost(ex))
            {
                try { RecoverDevice(); }
                catch (Exception again) { Log(again); Halt(); }
            }
            catch (Exception ex)
            {
                Log(ex);
                Halt();
            }
        }
    }

    private void StartRendering(Stopwatch clock)
    {
        // The window is shown on the first presented frame (DrawFrame), so it never flashes empty.
        _active = true;
        _fade = 0;
        _lastFrameSeconds = clock.Elapsed.TotalSeconds;
        _audio ??= new AudioSpectrum();
    }

    private void StopRendering()
    {
        if (_shown)
        {
            ShowWindow(_hwnd, SwHide);
            _shown = false;
        }

        _active = false;
        _audio?.Dispose();
        _audio = null;
    }

    private void Halt()
    {
        _halted = true;
        StopRendering();
    }

    private void WaitIdle()
    {
        while (!_exit)
        {
            _waitOne[0] = _wake.SafeWaitHandle.DangerousGetHandle();
            uint result = MsgWaitForMultipleObjects(1, _waitOne, false, Infinite, QsAllInput);
            if (result == WaitObject0) return;
            if (result == WaitObject0 + 1)
            {
                PumpMessages();
                continue;
            }

            return;
        }
    }

    private void WaitForSlot()
    {
        if (_exit) return;

        if (_swapChain is { FrameLatency: not 0 } chain)
        {
            _waitOne[0] = chain.FrameLatency;
            uint result = MsgWaitForMultipleObjects(1, _waitOne, false, 1000, QsAllInput);
            if (result == WaitObject0 + 1) PumpMessages();
        }

        if (OnDemand && _runningWanted)
        {
            while (!_exit && !_frameRequested && _runningWanted)
            {
                _waitOne[0] = _wake.SafeWaitHandle.DangerousGetHandle();
                uint woke = MsgWaitForMultipleObjects(1, _waitOne, false, 250, QsAllInput);
                if (woke == WaitObject0 + 1) PumpMessages();
            }

            _frameRequested = false;
            return;
        }

        // Performance.VisualizerFps is the user's cap; without one the strip runs at 60, or 30 while
        // the dock animates (see VisualizerPacing).
        double fps = Fps > 0 ? Fps : Performance.VisualizerFps;
        if (fps <= 0) fps = VisualizerPacing.DockAnimating ? 30 : 60;
        long interval = (long)(Stopwatch.Frequency / fps);
        while (!_exit)
        {
            long remaining = interval - (Stopwatch.GetTimestamp() - _lastPresent);
            if (remaining <= 0) return;
            int wait = (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
            _waitOne[0] = _wake.SafeWaitHandle.DangerousGetHandle();
            uint result = MsgWaitForMultipleObjects(1, _waitOne, false, (uint)Math.Clamp(wait, 0, 250), QsAllInput);
            if (result == WaitObject0) return; // something changed; take it up on the next frame
            if (result == WaitObject0 + 1) PumpMessages();
        }
    }

    private void ApplyPendingPlace()
    {
        Int32Rect rect;
        double ppd;
        IntPtr dock;
        bool place;
        bool dockChanged, sink;
        lock (_gate)
        {
            sink = _sinkPending;
            _sinkPending = false;
            place = _placePending;
            dockChanged = _placePending || _dockPending;
            _placePending = false;
            _dockPending = false;
            rect = _requestedRect;
            ppd = _requestedPpd;
            dock = _requestedDock;
        }

        if (!place && !dockChanged) return;

        if (place)
        {
            int width = Math.Max(1, rect.Width);
            int height = Math.Max(1, rect.Height);
            if (rect.X != _left || rect.Y != _top || width != _windowWidth || height != _windowHeight)
            {
                SetWindowPos(_hwnd, IntPtr.Zero, rect.X, rect.Y, width, height, SwpNoActivate | SwpNoZOrder);
                _left = rect.X;
                _top = rect.Y;
                _windowWidth = width;
                _windowHeight = height;
            }

            if (width != _width || height != _height)
            {
                _width = width;
                _height = height;
                ResizeTarget();
            }

            _ppd = ppd;
        }

        if (dockChanged && (dock != _dock || sink))
        {
            _dock = dock;
            WindowStyling.PlaceBelow(_hwnd, dock);
        }
    }

    private void ResizeTarget()
    {
        if (_swapChain is null || _device is null) return;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _swapChain.Resize(_width, _height);
        CreateTarget();
        _scene.Resize(_width, _height);
        _ledPass?.Resize(_width, _height);
        _lastPresent = 0;
    }

    private void DrawFrame(float dt)
    {
        if (_swapChain is null || _renderTarget is null || _context is null) return;

        _frame.Dt = dt;
        _frame.Time = (float)_time;
        _frame.Fade = (float)Math.Clamp(_fade, 0, 1);
        _frame.Width = _width;
        _frame.Height = _height;
        _frame.PixelsPerDip = (float)_ppd;
        _frame.Performance = Performance.Level;
        _frame.Wave = _waveRequested;
        _audio?.Read(_frame.Audio, dt);
        _calm.Apply(_frame.Audio, (float)dt);
        _frame.Balance = _frame.Audio.Balance;
        _frame.Cover = _cover;
        _frame.CoverSize = _coverSize;
        _frame.CoverVersion = Volatile.Read(ref _coverVersion);
        _frame.SourceX = SourceX;
        _frame.DockLeft = Volatile.Read(ref _dockLeft);
        _frame.DockRight = Volatile.Read(ref _dockRight);

        VisualizerPalette.Fill(_paletteR, _paletteG, _paletteB, _time, BasePrimary);
        for (int i = 0; i < GpuFrame.PaletteSize; i++)
        {
            _frame.Palette[i] = new Vector3(_paletteR[i], _paletteG[i], _paletteB[i]);
        }

        _context.ClearRenderTargetView(_renderTarget, new Color4(0, 0, 0, 0));
        if (_led && !_ledUnavailable)
        {
            // The scene draws into the pass's own picture, then the pass turns that into the LED grid; a failed
            // pass falls back to the plain picture rather than taking the strip down.
            _context.PSSetShaderResources(0, _noInputs);
            _ledPass ??= CreateLedPass();
            if (_ledPass?.SceneTarget is { } sceneTarget)
            {
                _context.ClearRenderTargetView(sceneTarget, new Color4(0, 0, 0, 0));
                _scene.Render(_context, sceneTarget, _frame);
                _ledPass.Apply(_context, _renderTarget, _width, _height, (float)_ppd, (float)_time);
            }
            else
            {
                _scene.Render(_context, _renderTarget, _frame);
            }
        }
        else
        {
            _scene.Render(_context, _renderTarget, _frame);
        }

        // Pace from the start of the present: Present(1) itself waits for the monitor's vblank, so the
        // interval has to cover that wait or 30 fps would come out as 20.
        _lastPresent = Stopwatch.GetTimestamp();
        _swapChain.Chain.Present(1, PresentFlags.None).CheckError();

        if (!_shown)
        {
            ShowWindow(_hwnd, SwShowNoActivate);
            _shown = true;
            if (_dock != IntPtr.Zero)
            {
                // showing a topmost window may lift it above the dock again
                WindowStyling.PlaceBelow(_hwnd, _dock);
            }
        }
    }

    // ---- device ----------------------------------------------------------------------------------

    private void CreateDevice()
    {
        _device = GpuDevice.Create();
        _context = _device.ImmediateContext;
        _swapChain = GpuSwapChain.Create(_device, _hwnd, _width, _height);
        CreateTarget();
        _scene.Initialize(_device);
        _scene.Resize(_width, _height);
    }

    private void CreateTarget()
    {
        if (_device is null || _swapChain is null) return;
        using var backBuffer = _swapChain.Chain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer, null);
    }

    // Made on first use, so a layer that never turns the LED look on never pays for its picture and shaders.
    private LedScreenPass? CreateLedPass()
    {
        if (_device is null) return null;
        LedScreenPass? pass = null;
        try
        {
            pass = new LedScreenPass();
            pass.Initialize(_device);
            pass.Resize(_width, _height);
            return pass;
        }
        catch (Exception ex)
        {
            pass?.Dispose();
            _ledUnavailable = true;
            Debug.WriteLine($"[Floe.Gpu] led pass: {ex.Message}");
            return null;
        }
    }

    private void RecoverDevice()
    {
        _scene.ReleaseDevice();
        ReleaseDevice();
        _device = GpuDevice.Create();
        _context = _device.ImmediateContext;
        _swapChain = GpuSwapChain.Create(_device, _hwnd, _width, _height);
        CreateTarget();
        _scene.Initialize(_device);
        _scene.Resize(_width, _height);
        _lastPresent = 0;
        Debug.WriteLine("[Floe.Gpu] device lost; recreated");
    }

    private void ReleaseDevice()
    {
        _ledPass?.Dispose();
        _ledPass = null;
        // a new device may compile the pass fine even if the last one could not
        _ledUnavailable = false;
        _renderTarget?.Dispose();
        _renderTarget = null;
        _swapChain?.Dispose();
        _swapChain = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;
    }

    // ---- window ----------------------------------------------------------------------------------

    private void CreateWindow()
    {
        var instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = _className,
        };
        if (RegisterClassEx(ref wc) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }

        const uint exStyle = WsExNoRedirectionBitmap | WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost;
        var hwnd = CreateWindowExW(
            exStyle,
            _className,
            "Floe GPU Visualizer",
            WsPopup,
            0, 0, 1, 1,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        _hwnd = hwnd;
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmNcHitTest:
                return new IntPtr(HtTransparent);
            case WmMouseActivate:
                return new IntPtr(MaNoActivate);
            case WmEraseBackground:
                return new IntPtr(1);
            case WmClose:
                return IntPtr.Zero;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private static void PumpMessages()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.Message == WmQuit) return;
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private void Log(Exception exception)
    {
        if (_logged) return;
        _logged = true;
        Debug.WriteLine($"[Floe.Gpu] {exception}");
    }

    // ---- interop ---------------------------------------------------------------------------------

    private const uint WsPopup = 0x80000000;
    private const uint WsExNoRedirectionBitmap = 0x00200000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint LwaAlpha = 0x00000002;
    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int HtTransparent = -1;
    private const int MaNoActivate = 3;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmClose = 0x0010;
    private const uint WmQuit = 0x0012;
    private const uint PmRemove = 0x0001;
    private const uint QsAllInput = 0x04FF;
    private const uint WaitObject0 = 0;
    private const uint Infinite = 0xFFFFFFFF;

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

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
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
