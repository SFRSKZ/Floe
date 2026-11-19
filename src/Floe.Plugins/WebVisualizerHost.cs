using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Floe.Core.Interop;
using Microsoft.Web.WebView2.Core;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using D2Color = System.Drawing.Color;

namespace Floe.Plugins;

public sealed record PluginPerf(int Fps, double FrameMs, string Tier);

/// <summary>A rectangle in physical screen pixels, kept out of WPF so the host can run anywhere.</summary>
public readonly record struct PluginRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Right => X + Width;
    public int Top => Y;
    public int Bottom => Y + Height;
}

/// <summary>
/// Hosts a WebGL2 visualizer plugin in a click-through strip along the bottom of the screen.
/// <para>
/// The window owns no pixels: WebView2 draws into a DirectComposition visual, so the transparent
/// parts of the page really show the desktop. One background STA thread owns the window, the
/// composition tree, the WebView2 controller and the audio reader, and pumps messages so the
/// browser stays responsive while the dock itself never waits on it.
/// </para>
/// </summary>
public sealed class WebVisualizerHost : IDisposable
{
    private const int FadeOutMs = 450;

    private readonly VisualizerPlugin _plugin;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly WndProc _wndProc;

    // requests from the Floe thread, guarded by _gate
    private bool _placePending;
    private bool _dockPending;
    private bool _sinkPending;
    private bool _appearancePending;
    private PluginRect _requestedRect;
    private PluginRect? _requestedDock;
    private double _requestedPpd = 1;
    private IntPtr _requestedDockHwnd;
    private string _core = "#70E2F5";
    private string _second = "#AF8CFF";
    private string _accent = "#FF7AB8";
    private string _deep = "#081826";
    private string _optionsJson = "{}";
    private int _fpsCap = 60;

    private volatile bool _runningWanted;
    private volatile bool _disposed;
    private volatile bool _exit;
    private volatile int _lastFps;
    private volatile string _lastTier = string.Empty;
    private volatile string? _failure;
    private IntPtr _hwnd;
    private bool _created;

    // thread state
    private string _className = string.Empty;
    private ID3D11Device? _device;
    private IDCompositionDevice? _composition;
    private IDCompositionTarget? _target;
    private IDCompositionVisual? _rootVisual;
    private IDCompositionVisual? _webVisual;
    private object? _webVisualRcw;
    private CoreWebView2Environment? _environment;
    private CoreWebView2CompositionController? _controller;
    private CoreWebView2? _web;
    private bool _navigated;
    private bool _active;
    private bool _fadingOut;
    private double _fadeOutAt;
    private double _lastPost;
    private double _startedAt;
    private AudioSpectrum? _audio;
    private AudioPeakMeter? _meter;
    private readonly SpectrumFrame _audioFrame;
    private readonly float[] _channelPeaks = new float[2];
    private int _width = 1;
    private int _height = 1;
    private int _left;
    private int _top;
    private double _ppd = 1;
    private PluginRect? _dockRect;
    private IntPtr _dockHwnd;
    private bool _stateSent;
    private readonly IntPtr[] _waitOne = new IntPtr[1];

    public WebVisualizerHost(VisualizerPlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _audioFrame = new SpectrumFrame(Math.Clamp(plugin.Manifest.Audio.Bands, 4, 64));
        _wndProc = WindowProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "Floe.PluginHost" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(8));

        if (Failure is not null)
        {
            throw new InvalidOperationException($"visualizer plugin did not start: {Failure}");
        }
    }

    public IntPtr Handle => Volatile.Read(ref _hwnd);

    public bool IsRunning => _runningWanted && !_disposed;

    public string? Failure => _failure;

    public string PluginId => _plugin.Id;

    public int LastFps => _lastFps;

    /// <summary>Diagnostics and previews can supply frames instead of the WASAPI reader.</summary>
    public Func<double, SpectrumFrame>? FrameSource { get; set; }

    public string LastTier => _lastTier;

    /// <summary>Physical-pixel strip; <paramref name="dock"/> is the dock capsule on screen, also in pixels.</summary>
    public void Place(PluginRect pixels, double pixelsPerDip, PluginRect? dock)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || pixelsPerDip <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _requestedRect = pixels;
            _requestedPpd = pixelsPerDip;
            _requestedDock = dock;
            _placePending = true;
        }

        _wake.Set();
    }

    public void PlaceBelowDock(IntPtr dock)
    {
        lock (_gate)
        {
            _requestedDockHwnd = dock;
            _dockPending = true;
            _sinkPending = true;
        }

        _wake.Set();
    }

    public void SetAppearance(string core, string second, string accent, string deep, string optionsJson, int fpsCap)
    {
        lock (_gate)
        {
            _core = core;
            _second = second;
            _accent = accent;
            _deep = deep;
            _optionsJson = optionsJson;
            _fpsCap = Math.Clamp(fpsCap <= 0 ? 60 : fpsCap, 15, 120);
            _appearancePending = true;
        }

        _wake.Set();
    }

    public void SetRunning(bool running)
    {
        if (_disposed)
        {
            return;
        }

        _runningWanted = running;
        _wake.Set();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runningWanted = false;
        _wake.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(3));
        }

        if (!_thread.IsAlive)
        {
            _wake.Dispose();
            _ready.Dispose();
        }
    }

    // ---- thread ---------------------------------------------------------------------------

    private void Run()
    {
        try
        {
            CreateWindow();
            CreateDevice();
            CreateWebView();
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            Log(ex);
        }
        finally
        {
            _ready.Set();
        }

        if (_failure is null && _hwnd != IntPtr.Zero)
        {
            try
            {
                Loop();
            }
            catch (Exception ex)
            {
                _failure = ex.Message;
                Log(ex);
            }
        }

        Teardown();
    }

    private void Loop()
    {
        while (!_exit)
        {
            Pump();
            if (_exit)
            {
                break;
            }

            ApplyPlacement();

            double now = _clock.Elapsed.TotalSeconds;
            bool wanted = _runningWanted;

            if (wanted && !_active)
            {
                StartRendering(now);
            }

            if (_active)
            {
                if (!wanted && !_fadingOut)
                {
                    _fadingOut = true;
                    _fadeOutAt = now + FadeOutMs / 1000.0;
                    PostState(running: false);
                }

                if (_fadingOut && now >= _fadeOutAt)
                {
                    StopRendering();
                    continue;
                }

                WaitForSlot();
                PostFrame(now);
            }
            else
            {
                WaitIdle();
            }
        }
    }

    private void StartRendering(double now)
    {
        _active = true;
        _fadingOut = false;
        _startedAt = now;
        _lastPost = 0;
        _audio ??= new AudioSpectrum();
        _meter ??= new AudioPeakMeter();
        if (_controller is not null)
        {
            _controller.IsVisible = true;
        }

        ShowWindow(_hwnd, SwShowNoActivate);
        if (_dockHwnd != IntPtr.Zero)
        {
            WindowStyling.PlaceBelow(_hwnd, _dockHwnd);
        }

        _stateSent = false;
        PostState(running: true);
    }

    private void StopRendering()
    {
        _active = false;
        _fadingOut = false;
        if (_controller is not null)
        {
            _controller.IsVisible = false;
        }

        ShowWindow(_hwnd, SwHide);
        _audio?.Dispose();
        _audio = null;
        _meter?.Dispose();
        _meter = null;
    }

    private void PostState(bool running)
    {
        if (_web is null)
        {
            return;
        }

        var dock = _dockRect;
        PluginState state;
        lock (_gate)
        {
            double left = 0, right = 1, bottom = 0, top = 0;
            if (dock is { } d && _width > 0 && _height > 0)
            {
                left = (double)(d.Left - _left) / _width;
                right = (double)(d.Right - _left) / _width;
                bottom = (double)(_height - (d.Bottom - _top)) / _height;
                top = (double)(_height - (d.Top - _top)) / _height;
            }

            state = new PluginState
            {
                Width = (int)Math.Round(_width / Math.Max(1.0, _ppd)),
                Height = (int)Math.Round(_height / Math.Max(1.0, _ppd)),
                Dpr = _ppd,
                DockLeft = Math.Clamp(left, -0.2, 1.2),
                DockRight = Math.Clamp(right, -0.2, 1.2),
                DockBottom = Math.Clamp(bottom, -0.2, 1.2),
                DockTop = Math.Clamp(top, -0.2, 1.2),
                Core = _core,
                Second = _second,
                Accent = _accent,
                Deep = _deep,
                OptionsJson = _optionsJson,
                FpsCap = _fpsCap,
                Running = running,
            };
        }

        _stateSent = true;
        TryPost(PluginProtocol.BuildState(state));
    }

    private void PostFrame(double now)
    {
        if (_web is null)
        {
            return;
        }

        if (!_stateSent)
        {
            PostState(running: _runningWanted);
        }

        double dt = _lastPost <= 0 ? 1.0 / 60 : Math.Clamp(now - _lastPost, 0.001, 0.1);
        _audio?.Read(_audioFrame, dt);

        float balance = 0;
        if (_meter is not null && _meter.GetChannelPeaks(_channelPeaks) >= 1)
        {
            float left = _channelPeaks[0];
            float right = _channelPeaks.Length > 1 ? _channelPeaks[1] : left;
            float sum = left + right;
            if (sum > 0.0001f)
            {
                balance = Math.Clamp((right - left) / sum, -1, 1);
            }
        }

        _lastPost = now;
        TryPost(PluginProtocol.BuildFrame(_audioFrame, now - _startedAt, balance));
    }

    private static void CopyFrame(SpectrumFrame from, SpectrumFrame to)
    {
        int bands = Math.Min(from.Bands.Length, to.Bands.Length);
        for (int i = 0; i < bands; i++) to.Bands[i] = from.Bands[i];
        for (int i = bands; i < to.Bands.Length; i++) to.Bands[i] = 0;
        to.Bass = from.Bass;
        to.Mid = from.Mid;
        to.Treble = from.Treble;
        to.Level = from.Level;
        to.Kick = from.Kick;
        to.Snare = from.Snare;
        to.Hat = from.Hat;
        int wave = Math.Min(from.Wave.Length, to.Wave.Length);
        for (int i = 0; i < wave; i++) to.Wave[i] = from.Wave[i];
        for (int i = wave; i < to.Wave.Length; i++) to.Wave[i] = 0;
    }

    private void TryPost(string json)
    {
        try
        {
            _web?.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void WaitForSlot()
    {
        double fps = _fpsCap <= 0 ? 60 : _fpsCap;
        long interval = (long)(Stopwatch.Frequency / fps);
        while (!_exit)
        {
            long remaining = interval - (long)((_clock.Elapsed.TotalSeconds - _lastPost) * Stopwatch.Frequency);
            if (remaining <= 0)
            {
                return;
            }

            int wait = (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency);
            _waitOne[0] = _wake.SafeWaitHandle.DangerousGetHandle();
            uint result = MsgWaitForMultipleObjects(1, _waitOne, false, (uint)Math.Clamp(wait, 0, 250), QsAllInput);
            if (result == WaitObject0 + 1)
            {
                Pump();
            }

            if (result == WaitObject0)
            {
                return;
            }
        }
    }

    private void WaitIdle()
    {
        while (!_exit)
        {
            _waitOne[0] = _wake.SafeWaitHandle.DangerousGetHandle();
            uint result = MsgWaitForMultipleObjects(1, _waitOne, false, Infinite, QsAllInput);
            if (result == WaitObject0)
            {
                return;
            }

            if (result == WaitObject0 + 1)
            {
                Pump();
                if (_placePending || _dockPending || _appearancePending)
                {
                    return;
                }

                continue;
            }

            return;
        }
    }

    private void ApplyPlacement()
    {
        PluginRect rect;
        PluginRect? dock;
        double ppd;
        IntPtr dockHwnd;
        bool place, dockChanged, sink, appearance;
        lock (_gate)
        {
            place = _placePending;
            _placePending = false;
            dock = _requestedDock;
            rect = _requestedRect;
            ppd = _requestedPpd;
            dockChanged = _dockPending;
            _dockPending = false;
            sink = _sinkPending;
            _sinkPending = false;
            dockHwnd = _requestedDockHwnd;
            appearance = _appearancePending;
            _appearancePending = false;
        }

        if (!place && !dockChanged && !appearance)
        {
            return;
        }

        if (place)
        {
            int width = Math.Max(1, rect.Width);
            int height = Math.Max(1, rect.Height);
            if (rect.X != _left || rect.Y != _top || width != _width || height != _height)
            {
                SetWindowPos(_hwnd, IntPtr.Zero, rect.X, rect.Y, width, height, SwpNoActivate | SwpNoZOrder);
            }

            _left = rect.X;
            _top = rect.Y;
            _width = width;
            _height = height;
            _ppd = ppd;
            _dockRect = dock;
            ApplyBounds();
            _stateSent = false;
        }

        if (dockChanged)
        {
            _dockHwnd = dockHwnd;
            if (sink || dockHwnd != IntPtr.Zero)
            {
                WindowStyling.PlaceBelow(_hwnd, dockHwnd);
            }
        }

        if (_active)
        {
            PostState(running: _runningWanted && !_fadingOut);
        }
    }

    private void ApplyBounds()
    {
        if (_controller is null)
        {
            return;
        }

        try
        {
            _controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
            _controller.RasterizationScale = _ppd;
            _controller.Bounds = new Rectangle(0, 0, _width, _height);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    // ---- creation -------------------------------------------------------------------------

    private void CreateWindow()
    {
        var instance = GetModuleHandleW(null);
        _className = "FloePluginHost_" + Guid.NewGuid().ToString("N");
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
        var hwnd = CreateWindowExW(exStyle, _className, "Floe Visualizer Plugin", WsPopup,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        _hwnd = hwnd;
        _created = true;
        SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }

    private void CreateDevice()
    {
        try
        {
            _device = D3D11.D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 });
        }
        catch (Exception)
        {
            _device = D3D11.D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport,
                new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 });
        }

        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        _composition = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
        IDCompositionTarget target;
        IDCompositionVisual root;
        IDCompositionVisual webVisual;
        _composition.CreateTargetForHwnd(_hwnd, true, out target).CheckError();
        _composition.CreateVisual(out root).CheckError();
        _composition.CreateVisual(out webVisual).CheckError();
        _target = target;
        _rootVisual = root;
        _webVisual = webVisual;
        _target.SetRoot(_rootVisual).CheckError();
        _rootVisual.AddVisual(_webVisual, true, null).CheckError();
        _composition.Commit().CheckError();
    }

    private void CreateWebView()
    {
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floe", "WebView2");
        Directory.CreateDirectory(userData);

        Trace("creating environment");
        var environment = Wait(CoreWebView2Environment.CreateAsync(null, userData, null));
        Trace("environment ready");
        _environment = environment;
        var controller = Wait(environment.CreateCoreWebView2CompositionControllerAsync(_hwnd));
        _controller = controller;

        if (_webVisual is null)
        {
            throw new InvalidOperationException("composition visual missing");
        }

        // The wrapper rejects a bare Vortice object; an RCW for the same visual is accepted.
        Trace("controller ready");
        _webVisualRcw = Marshal.GetObjectForIUnknown(_webVisual.NativePointer);
        controller.RootVisualTarget = _webVisualRcw;
        Trace("root visual set");
        _composition?.Commit().CheckError();

        controller.DefaultBackgroundColor = D2Color.Transparent;
        controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        controller.RasterizationScale = _ppd;
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.Bounds = new Rectangle(0, 0, Math.Max(1, _width), Math.Max(1, _height));
        controller.IsVisible = false;

        var web = controller.CoreWebView2;
        _web = web;
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.AreDevToolsEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.IsZoomControlEnabled = false;
        web.Settings.AreHostObjectsAllowed = false;
        web.Settings.IsWebMessageEnabled = true;
        web.Settings.IsGeneralAutofillEnabled = false;
        web.Settings.IsPasswordAutosaveEnabled = false;

        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.NavigationStarting += (_, e) =>
        {
            Trace($"navigation starting {e.Uri}");
        };
        web.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("https://floe.plugin/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        };
        web.WebMessageReceived += OnWebMessage;
        web.NavigationCompleted += (_, e) =>
        {
            Trace($"navigation completed success={e.IsSuccess} status={e.WebErrorStatus}");
            _navigated = true;
            if (_active && _runningWanted)
            {
                controller.IsVisible = true;
            }
        };
        web.ProcessFailed += (_, e) =>
        {
            _failure = $"WebView2 process failed: {e.ProcessFailedKind}";
            _exit = true;
            _wake.Set();
        };

        web.SetVirtualHostNameToFolderMapping("floe.plugin", _plugin.Folder, CoreWebView2HostResourceAccessKind.Allow);
        var entry = Path.GetRelativePath(_plugin.Folder, _plugin.EntryPath).Replace('\\', '/');
        web.Navigate($"https://floe.plugin/{entry}");

        _navigated = false;
        var until = Environment.TickCount64 + 6000;
        while (!_navigated && Environment.TickCount64 < until && !_exit)
        {
            Pump();
            Thread.Sleep(5);
        }

        if (!_navigated)
        {
            Log(new InvalidOperationException("plugin page did not finish navigating"));
        }

        if (Tracing)
        {
            try
            {
                var probe = Wait(web.ExecuteScriptAsync(
                    "JSON.stringify({bridge:!!(window.chrome&&window.chrome.webview),title:document.title,gl:(function(){try{return !!document.createElement('canvas').getContext('webgl2')}catch(e){return 'threw'}})()})"));
                Trace("probe " + probe);
            }
            catch (Exception ex)
            {
                Trace("probe failed " + ex.Message);
            }
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "perf":
                    if (root.TryGetProperty("fps", out var fps) && fps.TryGetInt32(out var f))
                    {
                        _lastFps = f;
                    }

                    if (root.TryGetProperty("tier", out var tier) && tier.GetString() is { } name)
                    {
                        _lastTier = name;
                    }

                    break;
                case "error":
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() : "plugin error";
                    _failure = message ?? "plugin error";
                    Log(new InvalidOperationException(_failure));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    private void Teardown()
    {
        try
        {
            _web?.Stop();
        }
        catch
        {
            // the browser is already gone
        }

        try
        {
            _controller?.Close();
        }
        catch
        {
            // the controller is already gone
        }

        _controller = null;
        _web = null;
        _environment = null;
        _webVisualRcw = null;

        _webVisual?.Dispose();
        _webVisual = null;
        _rootVisual?.Dispose();
        _rootVisual = null;
        _target?.Dispose();
        _target = null;
        _composition?.Dispose();
        _composition = null;
        _device?.Dispose();
        _device = null;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_created)
        {
            UnregisterClass(_className, GetModuleHandleW(null));
            _created = false;
        }
    }

    // ---- helpers --------------------------------------------------------------------------

    private T Wait<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            Pump();
            Thread.Sleep(2);
        }

        return task.GetAwaiter().GetResult();
    }

    internal static readonly string TracePath = Path.Combine(Path.GetTempPath(), "floe-plugin-host.log");

    private static readonly bool Tracing = Environment.GetEnvironmentVariable("FLOE_PLUGIN_TRACE") == "1";

    internal static void Trace(string message)
    {
        if (!Tracing)
        {
            return;
        }

        try { File.AppendAllText(TracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch { }
    }

    private void Log(Exception exception)
    {
        Debug.WriteLine($"[Floe.Plugin] {exception.Message} ({exception.GetType().Name})");
    }

    private static void Pump()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.Message == WmQuit)
            {
                return;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
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

    // ---- interop -------------------------------------------------------------------------

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







