using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The melt doze effect: the dock's snapshot turns into liquid and pours down (derezz), or a pool under the
/// strip flows back up into the dock (rezz). The liquid is a real 2D FLIP fluid on the GPU.
/// <para>
/// One particle per 3x3 target pixel of the snapshot carries its position, velocity, home and colour. Per
/// substep they are scattered bilinearly into a 6-pixel collocated grid with fixed-point atomics (P2G), the
/// grid is projected (divergence, 40 Jacobi iterations, gradient subtraction) and the particles read the
/// velocity back with the FLIP weight of .95 (G2P), then advect through the grid with RK2. The particle idea
/// and the look of a FLIP particle fluid follow dli/fluid "Fluid Particles" (MIT),
/// https://github.com/dli/fluid.
/// </para>
/// <para>
/// Drawing is a liquid, not a shower of dots: every live particle is an additively blended gaussian quad into
/// a float field, and one full-screen pass reads that density as a height to light it: normal from the density
/// gradient, top-left diffuse, a sharp white specular and a thin dark rim where the surface begins.
/// </para>
/// </summary>
public sealed class MeltGpuScene : IGpuScene
{
    private const int MaxParticles = 40000;
    private const int ThreadsPerGroup = 64;
    private const int PressureIterations = 40;
    private const float CellPixels = 6;
    private const float QuadRadius = 2.2f;
    private const float DerezzSeconds = 2.2f;
    private const float FloorClosedSeconds = .45f;

    // A melt is thick, not a waterfall: the liquid runs off the dock at a walking pace, which is also what
    // keeps the pour on screen for the second and a half the effect is meant to last.
    private const float MeltSpeed = 85;

    // ---- cross-thread hand-off ------------------------------------------------------------------

    /// <summary>Everything one Start() produced, built on the UI thread and consumed once by the render thread.</summary>
    private sealed class Request
    {
        public byte[] Pixels = Array.Empty<byte>();
        public int Width;
        public int Height;
        public Int32Rect Home;
        public bool Rezz;
        public float Speed = 1;
        public ParticleGpu[] Particles = Array.Empty<ParticleGpu>();
        public int Count;
        public float Spacing = 3;
    }

    private readonly object _lock = new();
    private Request? _request;
    private bool _reverseRequest;
    private Request? _current;
    private volatile bool _running;
    private volatile bool _finished;

    // ---- device objects -------------------------------------------------------------------------

    private ID3D11Device? _device;
    private ID3D11ComputeShader? _clearGridCs, _clearFieldCs, _clearStatsCs, _forcesCs, _p2gCs;
    private ID3D11ComputeShader? _normalizeCs, _divergenceCs, _pressureCs, _gradientCs, _integrateCs;
    private ID3D11VertexShader? _fullscreenVs, _particleVs;
    private ID3D11PixelShader? _particlePs, _compositePs, _snapshotPs;
    private ID3D11Buffer? _simConstants, _renderConstants, _particles, _accum, _velA, _velB, _div;
    private ID3D11Buffer? _pressureA, _pressureB, _stats, _statsStaging;
    private ID3D11ShaderResourceView? _particlesSrv, _accumSrv, _velASrv, _velBSrv, _divSrv, _pressureASrv, _pressureBSrv;
    private ID3D11UnorderedAccessView? _particlesUav, _accumUav, _velAUav, _velBUav, _divUav, _pressureAUav, _pressureBUav, _statsUav;
    private ID3D11Texture2D? _accTexture, _snapshotTexture;
    private ID3D11RenderTargetView? _accRtv;
    private ID3D11ShaderResourceView? _accSrv, _snapshotSrv;
    private ID3D11BlendState? _additive, _opaque, _over;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;

    // bound state, kept in arrays so a frame does not allocate
    private readonly ID3D11Buffer[] _simCb = new ID3D11Buffer[1];
    private readonly ID3D11Buffer[] _renderCb = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _srvs = new ID3D11ShaderResourceView[3];
    private readonly ID3D11ShaderResourceView[] _oneSrv = new ID3D11ShaderResourceView[1];
    private readonly ID3D11UnorderedAccessView[] _uavs = new ID3D11UnorderedAccessView[2];
    private readonly ID3D11SamplerState[] _linearSampler = new ID3D11SamplerState[1];
    private readonly float[] _sim = new float[20];
    private readonly float[] _rcb = new float[16];
    private Viewport[] _viewports = Array.Empty<Viewport>();

    // ---- state of the run -----------------------------------------------------------------------

    private int _pixelW, _pixelH;
    private int _gridW, _gridH, _cells, _cellGroups;
    private int _count, _particleGroups;
    private int _mode;                  // 0 derezz, 1 rezz
    private float _speed = 1;
    private float _elapsed;
    private float _spacing = 3;
    private int _pinned;
    private bool _pinnedReady;
    private bool _pinAll;
    private bool _active;
    private bool _visible;
    private Int32Rect _home;

    // ---- lifecycle ------------------------------------------------------------------------------

    /// <summary>
    /// UI thread. Copies the snapshot's pixels and builds the particles now; the render thread picks the
    /// request up on its next Render. <paramref name="home"/> is the snapshot's rect in target pixels.
    /// </summary>
    public void Start(BitmapSource snapshot, Int32Rect home, bool rezz, double speed = 1)
    {
        var request = Build(snapshot, home, rezz, (float)speed);
        lock (_lock)
        {
            _request = request;
            _reverseRequest = false;
        }

        _finished = false;
        _running = true;
    }

    /// <summary>UI thread. A running derezz turns around and flows back home from wherever the liquid is.</summary>
    public void Reverse()
    {
        lock (_lock) _reverseRequest = true;
        _finished = false;
        _running = true;
    }

    /// <summary>True once a derezz has drained away, or a rezz has settled: the dock may be shown again.</summary>
    public bool Finished => _finished;

    /// <summary>True from Start (or Reverse) until Finished.</summary>
    public bool Running => _running;

    public void Initialize(ID3D11Device device)
    {
        _device = device;

        var fullscreen = Compile("vs_5_0", FullscreenVs);
        _fullscreenVs = device.CreateVertexShader(fullscreen.Span, null);
        _particleVs = device.CreateVertexShader(Compile("vs_5_0", ParticleVs).Span, null);
        _particlePs = device.CreatePixelShader(Compile("ps_5_0", ParticlePs).Span, null);
        _compositePs = device.CreatePixelShader(Compile("ps_5_0", CompositePs).Span, null);
        _snapshotPs = device.CreatePixelShader(Compile("ps_5_0", SnapshotPs).Span, null);

        _clearGridCs = device.CreateComputeShader(Compute(ClearGridCs).Span, null);
        _clearFieldCs = device.CreateComputeShader(Compute(ClearFieldCs).Span, null);
        _clearStatsCs = device.CreateComputeShader(Compute(ClearStatsCs).Span, null);
        _forcesCs = device.CreateComputeShader(Compute(ForcesCs).Span, null);
        _p2gCs = device.CreateComputeShader(Compute(P2GCs).Span, null);
        _normalizeCs = device.CreateComputeShader(Compute(NormalizeCs).Span, null);
        _divergenceCs = device.CreateComputeShader(Compute(DivergenceCs).Span, null);
        _pressureCs = device.CreateComputeShader(Compute(PressureCs).Span, null);
        _gradientCs = device.CreateComputeShader(Compute(GradientCs).Span, null);
        _integrateCs = device.CreateComputeShader(Compute(IntegrateCs).Span, null);

        _simConstants = device.CreateBuffer(new BufferDescription(80, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _renderConstants = device.CreateBuffer(new BufferDescription(64, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _simCb[0] = _simConstants;
        _renderCb[0] = _renderConstants;

        // Structured buffers need the D3D10-style misc flag as well as the stride, or D3D creates a plain
        // buffer and every buffer view is rejected with E_INVALIDARG.
        _particles = device.CreateBuffer(new BufferDescription((uint)(MaxParticles * 64), BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 64));
        _particlesSrv = CreateSrv(device, _particles, MaxParticles);
        _particlesUav = CreateUav(device, _particles, MaxParticles);

        // the pinned count, read back without waiting; a monotonic total for the whole run
        _stats = device.CreateBuffer(new BufferDescription(4, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        _statsUav = CreateUav(device, _stats, 1);
        _statsStaging = device.CreateBuffer(new BufferDescription(4, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.BufferStructured, 4));

        _additive = device.CreateBlendState(AdditiveBlend());
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _over = device.CreateBlendState(OverBlend());
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _linearSampler[0] = _linear;
    }

    /// <summary>Target size changed: the grid and the field covers are size-dependent, the particles are not.</summary>
    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        BuildSized(width, height);

        // the run's particles are target pixels: restart it on the new size rather than shear it
        lock (_lock)
        {
            if (_current is not null) _request = _current;
        }
    }

    /// <summary>Drops every GPU object; the pending or running request stays, so a new device re-runs it.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _particlesSrv?.Dispose(); _particlesUav?.Dispose();
        _particlesSrv = null; _particlesUav = null;
        _particles?.Dispose(); _particles = null;
        _statsUav?.Dispose(); _stats?.Dispose(); _statsStaging?.Dispose();
        _statsUav = null; _stats = null; _statsStaging = null;

        _fullscreenVs?.Dispose(); _particleVs?.Dispose();
        _fullscreenVs = _particleVs = null;
        _particlePs?.Dispose(); _compositePs?.Dispose(); _snapshotPs?.Dispose();
        _particlePs = _compositePs = _snapshotPs = null;

        _clearGridCs?.Dispose(); _clearFieldCs?.Dispose(); _clearStatsCs?.Dispose();
        _forcesCs?.Dispose(); _p2gCs?.Dispose(); _normalizeCs?.Dispose();
        _divergenceCs?.Dispose(); _pressureCs?.Dispose(); _gradientCs?.Dispose(); _integrateCs?.Dispose();
        _clearGridCs = _clearFieldCs = _clearStatsCs = null;
        _forcesCs = _p2gCs = _normalizeCs = _divergenceCs = _pressureCs = _gradientCs = _integrateCs = null;

        _simConstants?.Dispose(); _renderConstants?.Dispose();
        _simConstants = _renderConstants = null;
        _simCb[0] = null!;
        _renderCb[0] = null!;

        _additive?.Dispose(); _opaque?.Dispose(); _over?.Dispose();
        _additive = _opaque = _over = null;
        _raster?.Dispose(); _noDepth?.Dispose();
        _raster = null; _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _linearSampler[0] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void BuildSized(int width, int height)
    {
        if (_device is null) return;
        ReleaseSized();
        _pixelW = width;
        _pixelH = height;
        _gridW = Math.Max(4, (width + (int)CellPixels - 1) / (int)CellPixels);
        _gridH = Math.Max(4, (height + (int)CellPixels - 1) / (int)CellPixels);
        _cells = _gridW * _gridH;
        _cellGroups = (_cells + ThreadsPerGroup - 1) / ThreadsPerGroup;

        uint fieldBytes = (uint)_cells * 16;
        // four ints per cell (vx, vy, weight, unused), so one int is one structure element
        _accum = _device.CreateBuffer(new BufferDescription(fieldBytes, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        _accumSrv = CreateSrv(_device, _accum, _cells * 4);
        _accumUav = CreateUav(_device, _accum, _cells * 4);
        _velA = CreateField(fieldBytes, out _velASrv, out _velAUav);
        _velB = CreateField(fieldBytes, out _velBSrv, out _velBUav);
        _div = CreateField(fieldBytes, out _divSrv, out _divUav);
        _pressureA = CreateField(fieldBytes, out _pressureASrv, out _pressureAUav);
        _pressureB = CreateField(fieldBytes, out _pressureBSrv, out _pressureBUav);

        _accTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _accRtv = _device.CreateRenderTargetView(_accTexture, null);
        _accSrv = _device.CreateShaderResourceView(_accTexture, null);

        _viewports = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
    }

    private ID3D11Buffer CreateField(uint bytes, out ID3D11ShaderResourceView srv, out ID3D11UnorderedAccessView uav)
    {
        var buffer = _device!.CreateBuffer(new BufferDescription(bytes, BindFlags.ShaderResource | BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        srv = CreateSrv(_device, buffer, _cells);
        uav = CreateUav(_device, buffer, _cells);
        return buffer;
    }

    // A structured buffer needs its view spelled out: the default description is not a buffer view.
    private static ID3D11ShaderResourceView CreateSrv(ID3D11Device device, ID3D11Buffer buffer, int elements) =>
        device.CreateShaderResourceView(buffer, new ShaderResourceViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = ShaderResourceViewDimension.Buffer,
            Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = (uint)elements },
        });

    private static ID3D11UnorderedAccessView CreateUav(ID3D11Device device, ID3D11Buffer buffer, int elements) =>
        device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
        {
            Format = Format.Unknown,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView { FirstElement = 0, NumElements = (uint)elements },
        });

    private void ReleaseSized()
    {
        _accumSrv?.Dispose(); _accumUav?.Dispose(); _accum?.Dispose();
        _velASrv?.Dispose(); _velAUav?.Dispose(); _velA?.Dispose();
        _velBSrv?.Dispose(); _velBUav?.Dispose(); _velB?.Dispose();
        _divSrv?.Dispose(); _divUav?.Dispose(); _div?.Dispose();
        _pressureASrv?.Dispose(); _pressureAUav?.Dispose(); _pressureA?.Dispose();
        _pressureBSrv?.Dispose(); _pressureBUav?.Dispose(); _pressureB?.Dispose();
        _accRtv?.Dispose(); _accSrv?.Dispose(); _accTexture?.Dispose();
        _snapshotSrv?.Dispose(); _snapshotTexture?.Dispose();
        _accumSrv = null; _accumUav = null;
        _velASrv = null; _velAUav = null;
        _velBSrv = null; _velBUav = null;
        _divSrv = null; _divUav = null;
        _pressureASrv = null; _pressureAUav = null;
        _pressureBSrv = null; _pressureBUav = null;
        _accRtv = null; _accSrv = null; _accTexture = null;
        _snapshotSrv = null; _snapshotTexture = null;
        _accum = _velA = _velB = _div = _pressureA = _pressureB = null;
        _viewports = Array.Empty<Viewport>();
        _gridW = _gridH = _cells = _cellGroups = 0;
        _pixelW = _pixelH = 0;
    }

    // ---- one frame ------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _particles is null || _compositePs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _pixelW || frame.Height != _pixelH) BuildSized(frame.Width, frame.Height);
        if (_accum is null || _accRtv is null) return;

        Request? request;
        bool reverse;
        lock (_lock)
        {
            request = _request;
            _request = null;
            reverse = _reverseRequest;
            _reverseRequest = false;
        }

        if (request is not null) Begin(context, request);
        else if (reverse && _current is not null) TurnAround();

        if (!_active)
        {
            // a finished rezz keeps the settled liquid on screen (it is the dock's own picture), a finished
            // derezz has nothing left to draw
            if (_visible && _current is not null) Draw(context, target, Math.Clamp(frame.Fade, 0, 1), 0);
            return;
        }

        // the snapshot stands in for the dock for the first frames: the dock melts out of its own picture
        float snapshot = _mode == 0 && _elapsed < .05f ? 1 - (_elapsed / .05f) : 0;
        float dt = Math.Clamp(frame.Dt, 0, .033f) * _speed;
        if (dt > 0)
        {
            _elapsed += dt;
            Simulate(context, dt);
        }

        if (_mode == 0 && _elapsed >= DerezzSeconds) Finish();

        if (_mode == 1)
        {
            ReadPinned(context);
            if ((_count > 0 && _pinned >= _count - (_count / 100)) || _elapsed > 4)
            {
                _pinAll = true;
                Finish();
            }
        }

        // the frame that ends a run still draws: a blank frame between liquid and dock would read as a flicker
        Draw(context, target, Math.Clamp(frame.Fade, 0, 1), _active ? snapshot : 0);
    }

    private void Begin(ID3D11DeviceContext context, Request request)
    {
        _current = request;
        _mode = request.Rezz ? 1 : 0;
        _speed = Math.Clamp(request.Speed, .05f, 4);
        _count = request.Count;
        _spacing = request.Spacing;
        _home = request.Home;
        _elapsed = 0;
        _pinned = 0;
        _pinnedReady = false;
        _pinAll = false;

        if (_count <= 0 || _particles is null)
        {
            Finish();
            return;
        }

        _particleGroups = (_count + ThreadsPerGroup - 1) / ThreadsPerGroup;
        context.UpdateSubresource(request.Particles, _particles);
        UploadSnapshot(context, request);

        SetSim(context, 0, 0, 1);
        context.CSSetConstantBuffers(0, _simCb);
        Run(context, _clearGridCs!, _cellGroups, null, null, _accumUav, null);
        Run(context, _clearFieldCs!, _cellGroups, null, null, _velAUav, null);
        Run(context, _clearFieldCs!, _cellGroups, null, null, _velBUav, null);
        Run(context, _clearFieldCs!, _cellGroups, null, null, _divUav, null);
        Run(context, _clearFieldCs!, _cellGroups, null, null, _pressureAUav, null);
        Run(context, _clearStatsCs!, 1, null, null, _statsUav, null);

        _active = true;
        _visible = true;
        _running = true;
        _finished = false;
    }

    private void TurnAround()
    {
        // same particles, same homes: gravity off, springs on, the dead come back as a pool below
        _mode = 1;
        _elapsed = 0;
        _pinned = 0;
        _pinAll = false;
        _running = true;
        _finished = false;
    }

    private void Finish()
    {
        _active = false;
        _running = false;
        _finished = true;
    }

    private void UploadSnapshot(ID3D11DeviceContext context, Request request)
    {
        if (_device is null) return;
        if (_snapshotTexture is null || _snapshotTexture.Description.Width != (uint)request.Width || _snapshotTexture.Description.Height != (uint)request.Height)
        {
            _snapshotSrv?.Dispose();
            _snapshotSrv = null;
            _snapshotTexture?.Dispose();
            _snapshotTexture = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)request.Width, (uint)request.Height, 1, 1,
                BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            _snapshotSrv = _device.CreateShaderResourceView(_snapshotTexture, null);
        }

        uint row = (uint)request.Width * 4;
        context.UpdateSubresource(request.Pixels, _snapshotTexture, 0, row, row * (uint)request.Height);
    }

    // ---- the solver -----------------------------------------------------------------------------

    private void Simulate(ID3D11DeviceContext context, float dt)
    {
        float floorSolid = _mode == 1 || _elapsed < FloorClosedSeconds ? 1 : 0;
        float h = dt * .5f;
        float time = _elapsed - dt;

        context.CSSetConstantBuffers(0, _simCb);
        for (int sub = 0; sub < 2; sub++)
        {
            SetSim(context, h, time + (sub * h), floorSolid);

            Run(context, _clearGridCs!, _cellGroups, null, null, _accumUav, null);
            Run(context, _forcesCs!, _particleGroups, null, null, _particlesUav, null);
            Run(context, _p2gCs!, _particleGroups, _particlesSrv, null, _accumUav, null);
            Run(context, _normalizeCs!, _cellGroups, _accumSrv, null, _velAUav, null);
            Run(context, _divergenceCs!, _cellGroups, _velASrv, null, _divUav, null);
            Run(context, _clearFieldCs!, _cellGroups, null, null, _pressureAUav, null);

            for (int i = 0; i < PressureIterations; i++)
            {
                Run(context, _pressureCs!, _cellGroups, _pressureASrv, _divSrv, _pressureBUav, null, _velASrv);
                Swap(ref _pressureASrv, ref _pressureBSrv);
                Swap(ref _pressureAUav, ref _pressureBUav);
            }

            Run(context, _gradientCs!, _cellGroups, _velASrv, _pressureASrv, _velBUav, null);
            Run(context, _integrateCs!, _particleGroups, _velASrv, _velBSrv, _particlesUav, _statsUav);
        }

        context.CSUnsetUnorderedAccessViews(0, 2, 0);
        context.CSSetShader(null);
    }

    private static void Swap(ref ID3D11ShaderResourceView? a, ref ID3D11ShaderResourceView? b) => (a, b) = (b, a);

    private static void Swap(ref ID3D11UnorderedAccessView? a, ref ID3D11UnorderedAccessView? b) => (a, b) = (b, a);

    /// <summary>The uniforms every compute pass shares.</summary>
    private void SetSim(ID3D11DeviceContext context, float dt, float time, float floorSolid)
    {
        _sim[0] = _gridW;
        _sim[1] = _gridH;
        _sim[2] = _cells;
        _sim[3] = dt;
        _sim[4] = CellPixels;
        _sim[5] = _pixelW;
        _sim[6] = _pixelH;
        _sim[7] = _count;
        _sim[8] = _mode;
        _sim[9] = time;
        _sim[10] = floorSolid;
        _sim[11] = 3;
        _sim[12] = 2600;
        _sim[13] = _pinAll ? 1 : 0;
        _sim[14] = 90;
        _sim[15] = 14;
        _sim[16] = _home.X;
        _sim[17] = _home.Y;
        _sim[18] = _home.Width;
        _sim[19] = _home.Height;
        context.UpdateSubresource(_sim, _simConstants!);
    }

    private void Run(ID3D11DeviceContext context, ID3D11ComputeShader shader, int groups,
        ID3D11ShaderResourceView? s0, ID3D11ShaderResourceView? s1,
        ID3D11UnorderedAccessView? u0, ID3D11UnorderedAccessView? u1, ID3D11ShaderResourceView? s2 = null)
    {
        context.CSSetShader(shader);
        // drop the previous pass's views first: a resource that is still a UAV would silently lose its
        // shader-resource binding for this dispatch
        context.CSUnsetUnorderedAccessViews(0, 2, 0);
        _srvs[0] = s0!;
        _srvs[1] = s1!;
        _srvs[2] = s2!;
        context.CSSetShaderResources(0, _srvs);
        _uavs[0] = u0!;
        _uavs[1] = u1!;
        context.CSSetUnorderedAccessViews(0, _uavs);
        context.Dispatch((uint)Math.Max(1, groups), 1, 1);
    }

    // ---- readback -------------------------------------------------------------------------------

    /// <summary>
    /// How many particles have pinned, without ever waiting on the GPU: last frame's copy is read now and
    /// this frame's is queued for the next one, since a read straight after the copy is always still drawing.
    /// </summary>
    private void ReadPinned(ID3D11DeviceContext context)
    {
        if (_stats is null || _statsStaging is null) return;
        if (_pinnedReady)
        {
            try
            {
                var mapped = context.Map((ID3D11Resource)_statsStaging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait);
                try { _pinned = Marshal.ReadInt32(mapped.DataPointer); }
                finally { context.Unmap(_statsStaging, 0); }
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                // still drawing: keep the last count that came back
            }
        }

        context.CopyResource(_statsStaging, _stats);
        _pinnedReady = true;
    }

    // ---- drawing --------------------------------------------------------------------------------

    private void Draw(ID3D11DeviceContext context, ID3D11RenderTargetView target, float fade, float snapshot)
    {
        if (_accRtv is null || _accSrv is null) return;

        float radius = QuadRadius * _spacing;
        _rcb[0] = _pixelW;
        _rcb[1] = _pixelH;
        _rcb[2] = radius;
        _rcb[3] = fade;
        _rcb[4] = _home.X;
        _rcb[5] = _home.Y;
        _rcb[6] = _home.Width;
        _rcb[7] = _home.Height;
        _rcb[8] = _snapshotTexture?.Description.Width ?? 1;
        _rcb[9] = _snapshotTexture?.Description.Height ?? 1;
        _rcb[10] = _rcb[11] = 0;
        _rcb[12] = _rcb[13] = _rcb[14] = _rcb[15] = 0;
        context.UpdateSubresource(_rcb, _renderConstants!);

        context.RSSetViewports(_viewports);
        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.VSSetConstantBuffers(0, _renderCb);
        context.PSSetConstantBuffers(0, _renderCb);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        // a) every particle as an additive gaussian, into the density field
        context.OMSetRenderTargets(_accRtv, null);
        context.ClearRenderTargetView(_accRtv, new Color4(0, 0, 0, 0));
        context.OMSetBlendState(_additive);
        context.IASetInputLayout(null);
        context.VSSetShader(_particleVs);
        context.PSSetShader(_particlePs);
        _oneSrv[0] = _particlesSrv!;
        context.VSSetShaderResources(0, _oneSrv);
        context.DrawInstanced(6, (uint)_count, 0, 0);

        // b) light the density field like a liquid surface
        context.OMSetRenderTargets(target, null);
        context.OMSetBlendState(_opaque);
        context.IASetInputLayout(null);
        context.VSSetShader(_fullscreenVs);
        context.PSSetShader(_compositePs);
        _oneSrv[0] = _accSrv;
        context.PSSetShaderResources(0, _oneSrv);
        context.Draw(3, 0);

        // c) frame zero is the dock itself, fading while the first of it starts to run
        if (snapshot > 0 && _mode == 0 && _snapshotSrv is not null)
        {
            context.OMSetBlendState(_over);
            context.VSSetShader(_fullscreenVs);
            context.PSSetShader(_snapshotPs);
            _oneSrv[0] = _snapshotSrv;
            context.PSSetShaderResources(0, _oneSrv);
            context.PSSetSamplers(0, _linearSampler);
            _rcb[3] = fade * snapshot;
            context.UpdateSubresource(_rcb, _renderConstants!);
            context.Draw(3, 0);
            _rcb[3] = fade;
        }
    }

    // ---- building the particles -----------------------------------------------------------------

    private Request Build(BitmapSource snapshot, Int32Rect home, bool rezz, float speed)
    {
        var source = snapshot.Format == PixelFormats.Pbgra32
            ? snapshot
            : new FormatConvertedBitmap(snapshot, PixelFormats.Pbgra32, null, 0);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        // one particle per 3x3 target pixel, wider only when the dock would not fit the cap
        var particles = new ParticleGpu[MaxParticles];
        int spacing = 3;
        int count;
        while (true)
        {
            count = 0;
            for (int y = spacing / 2; y < height; y += spacing)
            {
                for (int x = spacing / 2; x < width; x += spacing)
                {
                    int o = (y * width + x) * 4;
                    if (pixels[o + 3] <= 76) continue;
                    if (count < MaxParticles) particles[count] = MakeParticle(pixels, o, home, x, y, rezz);
                    count++;
                }
            }

            if (count <= MaxParticles) break;
            spacing++;
        }

        return new Request
        {
            Pixels = pixels,
            Width = width,
            Height = height,
            Home = home,
            Rezz = rezz,
            Speed = speed,
            Particles = particles,
            Count = Math.Min(count, MaxParticles),
            Spacing = spacing,
        };
    }

    private ParticleGpu MakeParticle(byte[] pixels, int o, Int32Rect home, int x, int y, bool rezz)
    {
        float homeX = home.X + x;
        float homeY = home.Y + y;
        // Pbgra32: blue, green, red, alpha; the colour is already premultiplied, as the shader wants it
        float b = pixels[o] / 255f;
        float g = pixels[o + 1] / 255f;
        float r = pixels[o + 2] / 255f;

        float centre = home.X + ((home.Width - 1) * .5f);
        float fromMiddle = MathF.Abs(homeX - centre) / MathF.Max(1, home.Width);
        // The randomness is a smooth field, not a per-particle draw: a particle that lets go even a few
        // hundredths before its neighbour tears a hole in the sheet, and the liquid then reads as gravel.
        float patch = SmoothNoise(x, y, 40);
        float release = rezz ? 0 : .02f + (.25f * fromMiddle) + (.08f * patch);

        float px, py;
        if (rezz)
        {
            // a pool just under the strip, spread under the dock, which the springs pull back up
            px = homeX + ((Hash(((x / 8) * 2.3f) + ((y / 8) * 5.9f)) - .5f) * 24);
            py = home.Y + home.Height + 18 + (Hash((x * .37f) + (y * 1.13f)) * 50);
        }
        else
        {
            px = homeX;
            py = homeY;
        }

        return new ParticleGpu
        {
            PosVel = new Vector4(px, py, 0, 0),
            HomeCol = new Vector4(homeX, homeY, r, g),
            Extra = new Vector4(b, release, 0, 0),
            Spare = new Vector4(Hash((x * 3.1f) + (y * 0.7f)) * 100, 0, 0, 0),
        };
    }

    private static float Hash(float n)
    {
        float v = MathF.Sin(n * 12.9898f) * 43758.5453f;
        return v - MathF.Floor(v);
    }

    /// <summary>A value in 0..1 that changes from patch to patch but never jumps between neighbours.</summary>
    private static float SmoothNoise(float x, float y, float cell)
    {
        float gx = x / cell, gy = y / cell;
        int ix = (int)MathF.Floor(gx), iy = (int)MathF.Floor(gy);
        float fx = gx - ix, fy = gy - iy;
        // smoothstep the weights, or the bilinear seams still show as creases in the melt front
        fx = fx * fx * (3 - (2 * fx));
        fy = fy * fy * (3 - (2 * fy));
        float a = Hash((ix * 3.7f) + (iy * 17.3f));
        float b = Hash(((ix + 1) * 3.7f) + (iy * 17.3f));
        float c = Hash((ix * 3.7f) + ((iy + 1) * 17.3f));
        float d = Hash(((ix + 1) * 3.7f) + ((iy + 1) * 17.3f));
        return (a * (1 - fx) * (1 - fy)) + (b * fx * (1 - fy)) + (c * (1 - fx) * fy) + (d * fx * fy);
    }

    // ---- shaders and states ---------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Melt", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private static ReadOnlyMemory<byte> Compute(string body) =>
        Compile("cs_5_0", SimPrelude + "\n" + body);

    private static BlendDescription AdditiveBlend()
    {
        var blend = new BlendDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        blend.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.One,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        return blend;
    }

    private static BlendDescription OverBlend()
    {
        var blend = new BlendDescription { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
        blend.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.One,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        return blend;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleGpu
    {
        public Vector4 PosVel;   // x, y, vx, vy
        public Vector4 HomeCol;  // home x, home y, r, g
        public Vector4 Extra;    // b, release seconds, pinned, dead
        public Vector4 Spare;    // jitter seed
    }

    // ---- HLSL -----------------------------------------------------------------------------------

    /// <summary>The uniforms and the grid helpers every compute pass shares.</summary>
    private const string SimPrelude = """
        cbuffer Sim : register(b0)
        {
            float GridW;
            float GridH;
            float Cells;
            float Dt;
            float Cell;
            float TargetW;
            float TargetH;
            float Count;
            float Mode;          // 0 derezz, 1 rezz
            float Time;
            float FloorSolid;
            float Jitter;
            float Gravity;
            float PinAll;
            float Accel;
            float Damping;
            float4 Spare;
        };

        struct Particle
        {
            float4 posVel;
            float4 homeCol;
            float4 extra;
            float4 spare;
        };

        int2 CellOf(uint id)
        {
            int w = (int)GridW;
            return int2((int)id % w, (int)id / w);
        }

        uint IndexOf(int2 c)
        {
            return (uint)((c.y * (int)GridW) + c.x);
        }

        // Left, right and the top are the strip's walls; the bottom is a wall while the floor is closed
        // and open air once the derezz lets go, so the liquid can leave the strip.
        bool SolidCell(int2 c)
        {
            if (c.x < 0 || c.x >= (int)GridW || c.y < 0) return true;
            if (c.y >= (int)GridH) return FloorSolid > .5;
            return false;
        }
        """;

    private const string ClearGridCs = """
        RWStructuredBuffer<int> Accum : register(u0);

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            int i = (int)id * 4;
            Accum[i] = 0;
            Accum[i + 1] = 0;
            Accum[i + 2] = 0;
            Accum[i + 3] = 0;
        }
        """;

    private const string ClearFieldCs = """
        RWStructuredBuffer<float4> Field : register(u0);

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            Field[id] = 0;
        }
        """;

    private const string ClearStatsCs = """
        RWStructuredBuffer<uint> Stats : register(u0);

        [numthreads(1, 1, 1)]
        void main()
        {
            Stats[0] = 0;
        }
        """;

    // Gravity and the rezz springs go on the particles before the pressure solve: a force put on the grid
    // alone would reach the particles only through the PIC 5%, and the projection would eat it.
    private const string ForcesCs = """
        RWStructuredBuffer<Particle> Particles : register(u0);

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Count) return;
            Particle p = Particles[id];
            if (p.extra.z > .5 || p.extra.w > .5) return;
            if (Mode < .5 && Time < p.extra.y) return;

            float2 v = p.posVel.zw;
            if (Mode > .5) v += ((p.homeCol.xy - p.posVel.xy) * Accel - (v * Damping)) * Dt;
            else v.y += Gravity * Dt;
            p.posVel.zw = v;
            Particles[id] = p;
        }
        """;

    private const string P2GCs = """
        StructuredBuffer<Particle> Particles : register(t0);
        RWStructuredBuffer<int> Accum : register(u0);

        // fixed point 1024: velocities in px/s stay exact enough and the sums fit an int
        void Splat(int2 c, float w, float2 v)
        {
            if (c.x < 0 || c.y < 0 || c.x >= (int)GridW || c.y >= (int)GridH) return;
            int i = (c.y * (int)GridW + c.x) * 4;
            InterlockedAdd(Accum[i + 0], (int)round(v.x * w * 1024.0));
            InterlockedAdd(Accum[i + 1], (int)round(v.y * w * 1024.0));
            InterlockedAdd(Accum[i + 2], (int)round(w * 1024.0));
        }

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Count) return;
            Particle p = Particles[id];
            // held particles are still the dock: they must not seed the fluid
            if (p.extra.z > .5 || p.extra.w > .5) return;
            if (Mode < .5 && Time < p.extra.y) return;

            float2 g = (p.posVel.xy / Cell) - .5;
            int2 i0 = (int2)floor(g);
            float2 f = g - i0;
            float2 v = p.posVel.zw;
            Splat(i0, (1 - f.x) * (1 - f.y), v);
            Splat(i0 + int2(1, 0), f.x * (1 - f.y), v);
            Splat(i0 + int2(0, 1), (1 - f.x) * f.y, v);
            Splat(i0 + int2(1, 1), f.x * f.y, v);
        }
        """;

    private const string NormalizeCs = """
        StructuredBuffer<int> Accum : register(t0);
        RWStructuredBuffer<float4> Velocity : register(u0);

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            int i = (int)id * 4;
            float wSum = (float)Accum[i + 2];
            float w = wSum * (1.0 / 1024.0);
            float2 v = 0;
            float mask = 0;
            if (w > .01)
            {
                v = float2(Accum[i], Accum[i + 1]) / max(wSum, 1);
                mask = 1;
            }
            Velocity[id] = float4(v, mask, 1);
        }
        """;

    private const string DivergenceCs = """
        StructuredBuffer<float4> Velocity : register(t0);
        RWStructuredBuffer<float4> Divergence : register(u0);

        float2 VelAt(int2 c) { return Velocity[IndexOf(c)].xy; }

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            if (Velocity[id].z < .5)
            {
                Divergence[id] = 0;
                return;
            }

            int2 c = CellOf(id);
            float2 vc = Velocity[id].xy;
            float2 l = SolidCell(c + int2(-1, 0)) ? -vc : VelAt(c + int2(-1, 0));
            float2 r = SolidCell(c + int2(1, 0)) ? -vc : VelAt(c + int2(1, 0));
            float2 t = SolidCell(c + int2(0, -1)) ? -vc : VelAt(c + int2(0, -1));
            float2 b = SolidCell(c + int2(0, 1)) ? -vc : VelAt(c + int2(0, 1));
            Divergence[id] = float4(.5 * ((r.x - l.x) + (b.y - t.y)), 0, 0, 0);
        }
        """;

    private const string PressureCs = """
        StructuredBuffer<float4> Pressure : register(t0);
        StructuredBuffer<float4> Divergence : register(t1);
        StructuredBuffer<float4> Velocity : register(t2);
        RWStructuredBuffer<float4> Result : register(u0);

        // air is at zero pressure, a solid neighbour mirrors this cell's pressure
        float Neighbour(int2 c, float centre)
        {
            if (c.x < 0 || c.x >= (int)GridW || c.y < 0) return centre;
            if (c.y >= (int)GridH) return FloorSolid > .5 ? centre : 0;
            if (Velocity[IndexOf(c)].z < .5) return 0;
            return Pressure[IndexOf(c)].x;
        }

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            if (Velocity[id].z < .5)
            {
                Result[id] = 0;
                return;
            }

            int2 c = CellOf(id);
            float p = Pressure[id].x;
            float sum = Neighbour(c + int2(-1, 0), p) + Neighbour(c + int2(1, 0), p)
                      + Neighbour(c + int2(0, -1), p) + Neighbour(c + int2(0, 1), p);
            // no negative pressure: the liquid can be pushed but never pulled, so a moving mass is free to
            // leave a void behind it instead of dragging itself back
            Result[id] = float4(max(0, (sum - Divergence[id].x) * .25), 0, 0, 0);
        }
        """;

    private const string GradientCs = """
        StructuredBuffer<float4> Velocity : register(t0);
        StructuredBuffer<float4> Pressure : register(t1);
        RWStructuredBuffer<float4> Result : register(u0);

        float Neighbour(int2 c, float centre)
        {
            if (c.x < 0 || c.x >= (int)GridW || c.y < 0) return centre;
            if (c.y >= (int)GridH) return FloorSolid > .5 ? centre : 0;
            if (Velocity[IndexOf(c)].z < .5) return 0;
            return Pressure[IndexOf(c)].x;
        }

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Cells) return;
            float4 here = Velocity[id];
            if (here.z < .5)
            {
                Result[id] = here;
                return;
            }

            int2 c = CellOf(id);
            float p = Pressure[id].x;
            float l = Neighbour(c + int2(-1, 0), p);
            float r = Neighbour(c + int2(1, 0), p);
            float t = Neighbour(c + int2(0, -1), p);
            float b = Neighbour(c + int2(0, 1), p);
            float2 v = here.xy - (.5 * float2(r - l, b - t));
            Result[id] = float4(v, here.z, 1);
        }
        """;

    private const string IntegrateCs = """
        StructuredBuffer<float4> VelocityOld : register(t0);
        StructuredBuffer<float4> VelocityNew : register(t1);
        RWStructuredBuffer<Particle> Particles : register(u0);
        RWStructuredBuffer<uint> Stats : register(u1);

        float Hash(float n)
        {
            return frac(sin(n * 12.9898) * 43758.5453);
        }

        float2 SampleOld(float2 p)
        {
            float2 g = (p / Cell) - .5;
            int2 i0 = (int2)floor(g);
            float2 f = saturate(g - i0);
            int2 a = clamp(i0, int2(0, 0), int2((int)GridW - 1, (int)GridH - 1));
            int2 b = clamp(i0 + 1, int2(0, 0), int2((int)GridW - 1, (int)GridH - 1));
            float2 v00 = VelocityOld[IndexOf(int2(a.x, a.y))].xy;
            float2 v10 = VelocityOld[IndexOf(int2(b.x, a.y))].xy;
            float2 v01 = VelocityOld[IndexOf(int2(a.x, b.y))].xy;
            float2 v11 = VelocityOld[IndexOf(int2(b.x, b.y))].xy;
            return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
        }

        float2 SampleNew(float2 p)
        {
            float2 g = (p / Cell) - .5;
            int2 i0 = (int2)floor(g);
            float2 f = saturate(g - i0);
            int2 a = clamp(i0, int2(0, 0), int2((int)GridW - 1, (int)GridH - 1));
            int2 b = clamp(i0 + 1, int2(0, 0), int2((int)GridW - 1, (int)GridH - 1));
            float2 v00 = VelocityNew[IndexOf(int2(a.x, a.y))].xy;
            float2 v10 = VelocityNew[IndexOf(int2(b.x, a.y))].xy;
            float2 v01 = VelocityNew[IndexOf(int2(a.x, b.y))].xy;
            float2 v11 = VelocityNew[IndexOf(int2(b.x, b.y))].xy;
            return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
        }

        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID)
        {
            if (id >= (uint)Count) return;
            Particle p = Particles[id];

            if (p.extra.w > .5)
            {
                // a derezz leaves the strip for good; a rezz brings the lost liquid back as a pool below
                if (Mode < .5) return;
                float2 pool = float2(p.homeCol.x + ((Hash(id * 1.7) - .5) * 24), TargetH + 12 + (Hash(id * 3.1) * 50));
                p.posVel = float4(pool, 0, 0);
                p.extra.w = 0;
                Particles[id] = p;
                return;
            }

            if (p.extra.z > .5) return;

            if (PinAll > .5)
            {
                p.posVel = float4(p.homeCol.xy, 0, 0);
                p.extra.z = 1;
                Particles[id] = p;
                InterlockedAdd(Stats[0], 1);
                return;
            }

            if (Mode < .5 && Time < p.extra.y)
            {
                p.posVel = float4(p.homeCol.xy, 0, 0);
                Particles[id] = p;
                return;
            }

            float2 pos = p.posVel.xy;
            float2 vel = p.posVel.zw;

            // G2P: 5% of the grid's own velocity, 95% of what the particle had plus the pressure's change
            float2 vNew = SampleNew(pos);
            float2 vOld = SampleOld(pos);
            vel = lerp(vNew, vel + (vNew - vOld), .95);

            // RK2 through the projected grid; a particle outside the grid (a rezz pool under the strip,
            // or one flung into the air) has no fluid to follow, so it keeps its own velocity
            float2 mid = pos + (vNew * Dt * .5);
            float2 adv = SampleNew(mid);
            if (dot(adv, adv) < 1 || Mode > .5) adv = vel;
            pos += adv * Dt;

            // A particle that keeps its velocity after a clamp tells the grid the wall is not there, so the
            // pressure never builds and the liquid slides through: stop it on the wall instead.
            if (pos.x < 1) { pos.x = 1; vel.x = max(vel.x, 0); }
            if (pos.x > TargetW - 1) { pos.x = TargetW - 1; vel.x = min(vel.x, 0); }
            if (pos.y < 1) { pos.y = 1; vel.y = max(vel.y, 0); }
            // only a derezz is held by the floor; a rezz starts outside the strip and has to come back in
            if (Mode < .5 && FloorSolid > .5 && pos.y > TargetH - 3) { pos.y = TargetH - 3; vel.y = min(vel.y, 0); }
            if (Mode < .5 && pos.y >= TargetH)
            {
                p.extra.w = 1;
                Particles[id] = p;
                return;
            }

            if (Mode < .5)
            {
                // a melt is thick: it runs off the dock instead of dropping like a stone
                vel.y = min(vel.y, 85);
                float2 j = float2(Hash(p.spare.x + (Time * 7.3)), Hash((p.spare.x * 1.37) + (Time * 3.1))) - .5;
                vel += j * Jitter;
            }

            if (Mode > .5 && distance(pos, p.homeCol.xy) < .6 && length(vel) < 12)
            {
                pos = p.homeCol.xy;
                vel = 0;
                p.extra.z = 1;
                InterlockedAdd(Stats[0], 1);
            }

            p.posVel = float4(pos, vel);
            Particles[id] = p;
        }
        """;

    private const string RenderConstants = """
        cbuffer Render : register(b0)
        {
            float2 Size;
            float Radius;
            float Fade;
            float4 Home;
            float2 Unused;
            float2 Pad;
        };
        """;

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

    private const string ParticleVs = RenderConstants + """

        struct Particle
        {
            float4 posVel;
            float4 homeCol;
            float4 extra;
            float4 spare;
        };

        StructuredBuffer<Particle> Particles : register(t0);

        struct VSOut
        {
            float4 position : SV_Position;
            float2 local : TEXCOORD0;
            float3 color : COLOR0;
        };

        VSOut main(uint vid : SV_VertexID, uint iid : SV_InstanceID)
        {
            Particle p = Particles[iid];
            VSOut o;
            if (p.extra.w > .5)
            {
                o.position = float4(2, 2, 2, 1);
                o.local = 0;
                o.color = 0;
                return o;
            }

            float2 corner = float2((vid == 1 || vid == 4 || vid == 5) ? 1 : -1, (vid < 2) ? -1 : 1);
            float2 q = p.posVel.xy + (corner * Radius);
            o.position = float4((q.x / Size.x * 2) - 1, 1 - (q.y / Size.y * 2), 0, 1);
            o.local = corner * Radius;
            o.color = float3(p.homeCol.z, p.homeCol.w, p.extra.x);
            return o;
        }
        """;

    // The gaussian is normalised so that a full sheet of particles sums to a density of about 1.2 at
    // spacing 3 and radius 6.6: a smooth field, not a heap of dots.
    private const string ParticlePs = RenderConstants + """

        struct VSOut
        {
            float4 position : SV_Position;
            float2 local : TEXCOORD0;
            float3 color : COLOR0;
        };

        float4 main(VSOut i) : SV_Target
        {
            // The gaussian is narrow next to its quad on purpose: a quad cut at 1.8 sigma leaves a step on
            // the particle lattice, and the shading turns that step into a hatched surface. At 3 sigma the
            // step is a percent of the peak and the sum of the particles is perfectly smooth.
            float sigma = Radius * .33;
            float g = exp(-dot(i.local, i.local) / (2 * sigma * sigma)) * .35;
            return float4(i.color * g, g);
        }
        """;

    private const string CompositePs = RenderConstants + """

        Texture2D Fluid : register(t0);

        float AlphaAt(int2 c)
        {
            c = clamp(c, int2(0, 0), int2((int)Size.x - 1, (int)Size.y - 1));
            return Fluid.Load(int3(c, 0)).a;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            int2 p = (int2)position.xy;
            float4 here = Fluid.Load(int3(p, 0));
            float w = here.a;
            if (w < .35) discard;

            // the density is a height field: its slope tilts the surface, so the liquid has a body. The taps
            // are one particle spacing apart, so the ripple of the particle lattice cancels instead of
            // aliasing into a hatched surface.
            int tap = max(2, (int)round(Radius / 2.2));
            float2 slope = float2(AlphaAt(p + int2(tap, 0)) - AlphaAt(p + int2(-tap, 0)),
                                  AlphaAt(p + int2(0, tap)) - AlphaAt(p + int2(0, -tap))) / (2.0 * tap) * 2.2;
            float3 n = normalize(float3(-slope * 1.6, 1));
            float3 l = normalize(float3(-.45, -.6, .65));
            float3 h = normalize(l + float3(0, 0, 1));

            float3 color = here.rgb / max(w, 1e-4);
            color *= .75 + (.25 * saturate(dot(n, l)));
            color += (.5 * pow(saturate(dot(n, h)), 40)) * float3(.9, .96, 1);

            // a thin darker rim where the surface begins, and the soft edge itself
            float rim = smoothstep(.35, .5, w) * (1 - smoothstep(.5, .78, w));
            color *= 1 - (.45 * rim);

            float a = smoothstep(.35, .5, w) * Fade;
            return float4(color * a, a);
        }
        """;

    private const string SnapshotPs = RenderConstants + """

        Texture2D Snapshot : register(t0);
        SamplerState LinearClamp : register(s0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            // the picture sits in Home; everything outside it is left alone
            float2 uv = (position.xy - Home.xy) / Home.zw;
            if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) discard;
            float4 c = Snapshot.SampleLevel(LinearClamp, uv, 0);
            return float4(c.rgb, c.a) * Fade;
        }
        """;
}
