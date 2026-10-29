using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Floe.App.Effects.Gpu;
using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Floe.App.Services;

/// <summary>
/// The backdrop behind the dock glass, captured live with Windows.Graphics.Capture instead of BitBlt.
/// The monitor the window sits on is grabbed as a free-threaded BGRA frame pool; the window rectangle is
/// cut out of each frame, box-filtered down to half resolution and Gaussian-blurred on the GPU, then the
/// readable rows are handed to the UI as BGRA bytes. Frames only reach the UI when the picture changed and
/// at most one present is queued, exactly like <see cref="BackdropCapture"/>.
/// </summary>
public sealed class GpuBackdropCapture : IDisposable
{
    private const int PoolBuffers = 2;
    private const int IdleIntervalMs = 33;
    private const int PollIntervalMs = 100;
    private const float MaxRadius = 40;
    private const int NrOfFloats = 16;

    private static readonly Guid CaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly Dispatcher _dispatcher;
    private readonly Action<byte[], int, int, int> _present;
    private readonly Func<bool>? _isActive;
    private readonly Func<double> _blurRadiusPx;
    private readonly Action _presentLatest;

    // publish state, shared between the capture worker and the UI thread
    private readonly object _sync = new();
    private bool _pending;
    private byte[]? _pixels;
    private int _presentWidth;
    private int _presentHeight;
    private int _presentStride;

    // readback buffers, owned by the worker thread
    private byte[] _readback = Array.Empty<byte>();
    private byte[]? _previous;
    private long _lastPresent;
    private volatile bool _disposed;

    // capture objects, owned by the worker thread once it runs
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _worker;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private IntPtr _monitor;
    private int _captureWidth;
    private int _captureHeight;
    private volatile bool _itemClosed;

    // GPU objects
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _captureDevice;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _downsampleShader;
    private ID3D11PixelShader? _blurShader;
    private ID3D11SamplerState? _sampler;
    private ID3D11Buffer? _constants;
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _linearSampler = new ID3D11SamplerState[1];
    private readonly float[] _constantData = new float[NrOfFloats];

    // sized resources, rebuilt whenever the half-res rect changes
    private ID3D11Texture2D? _downTexture, _tempTexture, _blurTexture, _staging;
    private ID3D11RenderTargetView? _downTarget, _tempTarget, _blurTarget;
    private ID3D11ShaderResourceView? _downSource, _tempSource, _blurSource;
    private ID3D11ShaderResourceView? _frameSource;
    private int _targetWidth;
    private int _targetHeight;
    private Viewport[] _viewport = Array.Empty<Viewport>();

    private GpuBackdropCapture(Window window, IntPtr hwnd, Action<byte[], int, int, int> present, Func<bool>? isActive, Func<double> blurRadiusPx)
    {
        _window = window;
        _hwnd = hwnd;
        _dispatcher = window.Dispatcher;
        _present = present;
        _isActive = isActive;
        _blurRadiusPx = blurRadiusPx;
        _presentLatest = PresentLatest;
        window.Closed += OnClosed;
    }

    /// <summary>
    /// Starts the live capture, or returns null when the OS cannot do it or any part of the setup fails.
    /// The window must have a handle already, so call this once it is shown.
    /// </summary>
    public static GpuBackdropCapture? TryCreate(Window window, Action<byte[], int, int, int> present, Func<bool>? isActive, Func<double> blurRadiusPx)
    {
        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                return null;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var capture = new GpuBackdropCapture(window, hwnd, present, isActive, blurRadiusPx);
            try
            {
                capture.InitializeGpu();
                capture.RebuildCapture(ScreenInput.GetMonitorForWindow(hwnd));
            }
            catch (Exception)
            {
                capture.Dispose();
                return null;
            }

            capture._worker = new Thread(capture.CaptureLoop) { IsBackground = true, Name = "FloeGpuBackdrop" };
            capture._worker.Start();
            return capture;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- setup ------------------------------------------------------------------------------------

    private void InitializeGpu()
    {
        _device = GpuDevice.Create();
        _context = _device.ImmediateContext;

        var vs = Compile("vs_4_0", FullscreenVs);
        _vertexShader = _device.CreateVertexShader(vs.Span, null);
        _downsampleShader = _device.CreatePixelShader(Compile("ps_4_0", DownsamplePs).Span, null);
        _blurShader = _device.CreatePixelShader(Compile("ps_4_0", BlurPs).Span, null);
        _sampler = _device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _constants = _device.CreateBuffer(new BufferDescription(NrOfFloats * sizeof(float), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _linearSampler[0] = _sampler;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        if (CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable) < 0 || inspectable == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDirect3D11DeviceFromDXGIDevice failed");
        }

        try
        {
            _captureDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
        var iid = CaptureItemIid;
        var pointer = interop.CreateForMonitor(monitor, ref iid);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateForMonitor failed");
        }

        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    private void RebuildCapture(IntPtr monitor)
    {
        CloseCapture();
        _monitor = monitor;

        var item = CreateItemForMonitor(monitor);
        _item = item;
        item.Closed += OnItemClosed;

        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _captureDevice!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            PoolBuffers,
            item.Size);
        _pool = pool;

        var session = pool.CreateCaptureSession(item);
        _session = session;
        try
        {
            session.IsCursorCaptureEnabled = false;
        }
        catch (Exception)
        {
            // older Windows builds do not know the property
        }

        try
        {
            // without this Windows outlines the whole monitor in yellow while the dock watches it
            session.IsBorderRequired = false;
        }
        catch (Exception)
        {
            // Windows 10 always draws the border
        }

        pool.FrameArrived += OnFrameArrived;
        session.StartCapture();

        _captureWidth = item.Size.Width;
        _captureHeight = item.Size.Height;
        _itemClosed = false;
    }

    private void CloseCapture()
    {
        if (_pool is not null)
        {
            try
            {
                _pool.FrameArrived -= OnFrameArrived;
            }
            catch (Exception)
            {
                // the pool is already gone
            }
        }

        try
        {
            _session?.Dispose();
        }
        catch (Exception)
        {
            // ignore a session that the OS already closed
        }

        try
        {
            _pool?.Dispose();
        }
        catch (Exception)
        {
            // ignore a pool that the OS already closed
        }

        if (_item is not null)
        {
            try
            {
                _item.Closed -= OnItemClosed;
            }
            catch (Exception)
            {
                // ignore a lost item
            }
        }

        _session = null;
        _pool = null;
        _item = null;
    }

    // ---- the capture worker -----------------------------------------------------------------------

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            _wake.Set();
        }
        catch (Exception)
        {
            // disposing: nothing to wake
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        _itemClosed = true;
        try
        {
            _wake.Set();
        }
        catch (Exception)
        {
            // disposing
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void CaptureLoop()
    {
        while (!_disposed)
        {
            try
            {
                _wake.WaitOne(PollIntervalMs);
            }
            catch (Exception)
            {
                break;
            }

            if (_disposed)
            {
                break;
            }

            try
            {
                Pump();
            }
            catch (Exception)
            {
                // a lost device, a closed item or a display change must never take the dock down
            }
        }
    }

    private void Pump()
    {
        var monitor = ScreenInput.GetMonitorForWindow(_hwnd);
        var pool = _pool;
        if (pool is null || _itemClosed || monitor != _monitor)
        {
            // a new monitor, a closed item or a failed first attempt: build the capture again
            RebuildCapture(monitor);
            return;
        }

        // the pool keeps two buffers; only the newest frame matters, the rest has to be drained
        Direct3D11CaptureFrame? frame = null;
        Direct3D11CaptureFrame? next;
        while ((next = pool.TryGetNextFrame()) is not null)
        {
            frame?.Dispose();
            frame = next;
        }

        if (frame is null)
        {
            return;
        }

        using (frame)
        {
            if (BackdropCapture.Frozen || !IsWindowVisible(_hwnd))
            {
                return;
            }

            var content = frame.ContentSize;
            if (content.Width < 2 || content.Height < 2)
            {
                return;
            }

            if (content.Width != _captureWidth || content.Height != _captureHeight)
            {
                // the monitor changed resolution under us: the pool has to follow it
                pool.Recreate(_captureDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, PoolBuffers, content);
                _captureWidth = content.Width;
                _captureHeight = content.Height;
                return;
            }

            var active = _isActive?.Invoke() ?? false;
            long now = Stopwatch.GetTimestamp();
            if (!active && (now - _lastPresent) * 1000.0 / Stopwatch.Frequency < IdleIntervalMs)
            {
                return;
            }

            _lastPresent = now;
            Render(frame);
        }
    }

    private void Render(Direct3D11CaptureFrame frame)
    {
        var context = _context!;
        using var source = GetTexture(frame.Surface);

        if (!ScreenInput.TryGetWindowRect(_hwnd, out var rect) || !ScreenInput.TryGetMonitorInfo(_monitor, out var info))
        {
            return;
        }

        var content = frame.ContentSize;
        int left = Math.Clamp(rect.Left - info.Monitor.Left, 0, content.Width - 1);
        int top = Math.Clamp(rect.Top - info.Monitor.Top, 0, content.Height - 1);
        int right = Math.Clamp(rect.Right - info.Monitor.Left, left + 1, content.Width);
        int bottom = Math.Clamp(rect.Bottom - info.Monitor.Top, top + 1, content.Height);
        int sourceWidth = right - left;
        int sourceHeight = bottom - top;
        if (sourceWidth < 2 || sourceHeight < 2)
        {
            return;
        }

        int width = (sourceWidth + 1) / 2;
        int height = (sourceHeight + 1) / 2;
        EnsureSize(width, height);

        float sigma = (float)Math.Max(0, _blurRadiusPx() / 2);
        float radius = sigma < .5f ? 0 : Math.Min(MaxRadius, MathF.Ceiling(sigma * 3));

        context.RSSetViewports(_viewport);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetConstantBuffers(0, _constantBuffer);
        context.PSSetSamplers(0, _linearSampler);

        // the area behind the window, box filtered to half resolution
        _frameSource?.Dispose();
        _frameSource = _device!.CreateShaderResourceView(source, null);
        Upload(context, width, height, left, top, sourceWidth, sourceHeight, content.Width, content.Height, 0, 0, 0, 0);
        context.OMSetRenderTargets(_downTarget!, null);
        context.PSSetShader(_downsampleShader);
        context.PSSetShaderResource(0, _frameSource);
        context.Draw(3, 0);

        // separable gaussian: rows, then columns
        context.PSSetShaderResource(0, null!);
        context.PSSetShader(_blurShader);
        Upload(context, width, height, 0, 0, 0, 0, 0, 0, 1, 0, sigma, radius);
        context.OMSetRenderTargets(_tempTarget!, null);
        context.PSSetShaderResource(0, _downSource!);
        context.Draw(3, 0);

        context.PSSetShaderResource(0, null!);
        Upload(context, width, height, 0, 0, 0, 0, 0, 0, 0, 1, sigma, radius);
        context.OMSetRenderTargets(_blurTarget!, null);
        context.PSSetShaderResource(0, _tempSource!);
        context.Draw(3, 0);

        // readable copy of the blurred half-res strip
        context.PSSetShaderResource(0, null!);
        context.CopyResource(_staging!, _blurTexture!);

        var mapped = context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            Publish(mapped.DataPointer, (int)mapped.RowPitch, width, height);
        }
        finally
        {
            context.Unmap(_staging!, 0);
        }
    }

    private void Upload(ID3D11DeviceContext context, int width, int height, float left, float top, float sourceWidth, float sourceHeight, float frameWidth, float frameHeight, float dirX, float dirY, float sigma, float radius)
    {
        var data = _constantData;
        data[0] = width;
        data[1] = height;
        data[2] = 1f / width;
        data[3] = 1f / height;
        data[4] = left;
        data[5] = top;
        data[6] = sourceWidth;
        data[7] = sourceHeight;
        data[8] = frameWidth;
        data[9] = frameHeight;
        data[10] = dirX;
        data[11] = dirY;
        data[12] = sigma;
        data[13] = radius;
        data[14] = 0;
        data[15] = 0;
        context.UpdateSubresource(data, _constants!);
    }

    private void EnsureSize(int width, int height)
    {
        if (width == _targetWidth && height == _targetHeight && _downTexture is not null)
        {
            return;
        }

        ReleaseSized();
        _targetWidth = width;
        _targetHeight = height;

        var device = _device!;
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        _downTexture = device.CreateTexture2D(desc);
        _tempTexture = device.CreateTexture2D(desc);
        _blurTexture = device.CreateTexture2D(desc);
        _downTarget = device.CreateRenderTargetView(_downTexture, null);
        _tempTarget = device.CreateRenderTargetView(_tempTexture, null);
        _blurTarget = device.CreateRenderTargetView(_blurTexture, null);
        _downSource = device.CreateShaderResourceView(_downTexture, null);
        _tempSource = device.CreateShaderResourceView(_tempTexture, null);
        _blurSource = device.CreateShaderResourceView(_blurTexture, null);
        _staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, 1, 0, ResourceOptionFlags.None));
        _viewport = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
    }

    // ---- publishing -------------------------------------------------------------------------------

    private void Publish(IntPtr data, int rowPitch, int width, int height)
    {
        int stride = width * 4;
        int length = stride * height;
        if (_readback.Length < length)
        {
            _readback = new byte[length];
        }

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(data, y * rowPitch), _readback, y * stride, stride);
        }

        // WGC already only delivers changed frames; a byte for byte repeat is still skipped
        if (_previous is { } previous && previous.Length >= length && previous.AsSpan(0, length).SequenceEqual(_readback.AsSpan(0, length)))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_pixels is null || _pixels.Length < length)
            {
                _pixels = new byte[length];
            }

            // the UI reads its own copy, so the next frame cannot overwrite it mid read
            Buffer.BlockCopy(_readback, 0, _pixels, 0, length);
            _presentWidth = width;
            _presentHeight = height;
            _presentStride = stride;
            if (!_pending && !_dispatcher.HasShutdownStarted)
            {
                _pending = true;
                _dispatcher.BeginInvoke(DispatcherPriority.Render, _presentLatest);
            }
        }

        var swap = _previous;
        _previous = _readback;
        _readback = swap is not null && swap.Length >= length ? swap : new byte[length];
    }

    private void PresentLatest()
    {
        lock (_sync)
        {
            _pending = false;
            if (!_disposed && _pixels is not null)
            {
                _present(_pixels, _presentWidth, _presentHeight, _presentStride);
            }
        }
    }

    private static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        IntPtr abi;
        bool release;
        if (surface is WinRT.IWinRTObject winrtObject)
        {
            abi = winrtObject.NativeObject.ThisPtr;
            release = false;
        }
        else
        {
            abi = Marshal.GetIUnknownForObject(surface);
            release = true;
        }

        try
        {
            return new ID3D11Texture2D(GetDxgiInterface(abi, Texture2DIid));
        }
        finally
        {
            if (release)
            {
                Marshal.Release(abi);
            }
        }
    }

    private static IntPtr GetDxgiInterface(IntPtr iunknown, Guid iid)
    {
        var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
        int hr = Marshal.QueryInterface(iunknown, ref accessIid, out var access);
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr)!;
        }

        try
        {
            var dxgi = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(access);
            hr = dxgi.GetInterface(ref iid, out var resource);
            if (hr < 0)
            {
                throw Marshal.GetExceptionForHR(hr)!;
            }

            return resource;
        }
        finally
        {
            Marshal.Release(access);
        }
    }

    // ---- teardown ---------------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _wake.Set();
        }
        catch (Exception)
        {
            // already gone
        }

        _window.Closed -= OnClosed;

        var worker = _worker;
        if (worker is not null && worker != Thread.CurrentThread)
        {
            try
            {
                worker.Join(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // keep going: stopping the app matters more than a clean join
            }
        }

        _worker = null;
        CloseCapture();
        ReleaseGpu();
        _wake.Dispose();
    }

    private void ReleaseGpu()
    {
        ReleaseSized();
        _frameSource?.Dispose();
        _frameSource = null;
        _vertexShader?.Dispose();
        _downsampleShader?.Dispose();
        _blurShader?.Dispose();
        _vertexShader = null;
        _downsampleShader = null;
        _blurShader = null;
        _sampler?.Dispose();
        _sampler = null;
        _linearSampler[0] = null!;
        _constants?.Dispose();
        _constants = null;
        _constantBuffer[0] = null!;
        _context?.Dispose();
        _context = null;
        _captureDevice?.Dispose();
        _captureDevice = null;
        _device?.Dispose();
        _device = null;
    }

    private void ReleaseSized()
    {
        _frameSource?.Dispose();
        _frameSource = null;
        _downTarget?.Dispose();
        _tempTarget?.Dispose();
        _blurTarget?.Dispose();
        _downSource?.Dispose();
        _tempSource?.Dispose();
        _blurSource?.Dispose();
        _downTarget = _tempTarget = _blurTarget = null;
        _downSource = _tempSource = _blurSource = null;
        _downTexture?.Dispose();
        _tempTexture?.Dispose();
        _blurTexture?.Dispose();
        _staging?.Dispose();
        _downTexture = _tempTexture = _blurTexture = _staging = null;
        _viewport = Array.Empty<Viewport>();
        _targetWidth = _targetHeight = 0;
    }

    // ---- interop and shaders ----------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(ref Guid iid, out IntPtr resource);
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "GpuBackdrop", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private const string FullscreenVs = """
        struct VSOut
        {
            float4 position : SV_Position;
        };

        VSOut main(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.position = float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
            return o;
        }
        """;

    private const string DownsamplePs = """
        cbuffer Params : register(b0)
        {
            float2 DestSize;
            float2 DestTexel;
            float4 SourceRect;
            float2 SourceSize;
            float2 Dir;
            float Sigma;
            float Radius;
            float2 Pad;
        };

        Texture2D Source : register(t0);
        SamplerState LinearClamp : register(s0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // the window's part of the monitor, one destination pixel per two source pixels
            float2 scale = SourceRect.zw / DestSize;
            float2 uv = (SourceRect.xy + (position.xy * scale)) / SourceSize;
            float2 q = (scale * 0.25) / SourceSize;
            float4 sum = Source.SampleLevel(LinearClamp, uv + float2(-q.x, -q.y), 0)
                + Source.SampleLevel(LinearClamp, uv + float2(q.x, -q.y), 0)
                + Source.SampleLevel(LinearClamp, uv + float2(-q.x, q.y), 0)
                + Source.SampleLevel(LinearClamp, uv + float2(q.x, q.y), 0);
            return sum * 0.25;
        }
        """;

    private const string BlurPs = """
        cbuffer Params : register(b0)
        {
            float2 DestSize;
            float2 DestTexel;
            float4 SourceRect;
            float2 SourceSize;
            float2 Dir;
            float Sigma;
            float Radius;
            float2 Pad;
        };

        Texture2D Source : register(t0);
        SamplerState LinearClamp : register(s0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = position.xy / DestSize;
            int radius = (int)(Radius + 0.5);
            if (radius < 1 || Sigma < 0.05)
            {
                return Source.SampleLevel(LinearClamp, uv, 0);
            }

            // separable gaussian, sigma in half-res pixels
            float inv = 1.0 / (2.0 * Sigma * Sigma);
            float4 sum = 0;
            float total = 0;
            [loop] for (int i = -radius; i <= radius; i++)
            {
                float weight = exp(-(float)(i * i) * inv);
                sum += Source.SampleLevel(LinearClamp, uv + (Dir * (i * DestTexel)), 0) * weight;
                total += weight;
            }

            return sum / total;
        }
        """;
}
