using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The fluid fire visualizer: a real 2D fluid on the GPU, drawn as black, red, orange and yellow flames
/// running out from under the dock to both sides.
/// <para>
/// It is a stable-fluids solver, not a feedback trick: velocity and dye are ping-ponged through
/// semi-Lagrangian advection, vorticity confinement, buoyancy from the dye itself, and a 20-iteration
/// Jacobi pressure projection. The spectrum feeds the solver where the fire leaves the dock: the bass
/// under the middle, every band along the bottom edge, and each kick an extra gust and a burst of dye.
/// The solver follows Pavel Dobryakov's WebGL-Fluid-Simulation (MIT).
/// </para>
/// <para>
/// The shaders keep the reference's y-up unit square; D3D stores the textures y down, so every sample
/// flips v and every pixel shader rebuilds uv from SV_Position the same way. Velocity is in sim texels
/// per second, as in the reference.
/// </para>
/// </summary>
public sealed class FluidFireGpuScene : IGpuScene
{
    private const int Bands = 24;

    // the demo's approved settings
    private const float Speed = .40f;
    private const float Curl = 30;
    private const float Buoyancy = 240;
    private const float Side = 280;
    private const float VelocityDissipation = .4f;
    private const float DyeDissipation = 1.6f;
    private const int PressureIterations = 20;

    // one sim cell is 150/SH of the 680x150 demo, kept in DIP, so the fire keeps its size on any strip
    private const float CellDip = 2.66f;

    // the spectrum and the beat, smoothed on the CPU; the shaders only see the splats
    private readonly float[] _bands = new float[Bands];
    private float _beat;
    private bool _kickArmed = true;
    private float _kickCooldown;

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _splatPs, _advectPs, _divergencePs, _curlPs, _vorticityPs;
    private ID3D11PixelShader? _buoyancyPs, _clearPs, _pressurePs, _gradientPs, _displayPs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;

    private Target? _velocity0, _velocity1, _pressure0, _pressure1;
    private Target? _divergence, _curl, _dye0, _dye1;

    // bound state, kept in arrays so a frame does not allocate
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _inputs = new ID3D11ShaderResourceView[2];
    private readonly ID3D11SamplerState[] _linearSampler = new ID3D11SamplerState[1];
    private readonly float[] _cb = new float[20];

    private Viewport[] _simViewport = Array.Empty<Viewport>();
    private Viewport[] _dyeViewport = Array.Empty<Viewport>();
    private Viewport[] _outputViewport = Array.Empty<Viewport>();

    private int _velocityPing, _dyePing, _pressurePing;
    private bool _needClear;
    private int _pixelW, _pixelH;
    private float _pixelsPerDip = 1;
    private int _simW, _simH, _dyeW, _dyeH, _columns;

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        var vs = Compile("vs_4_0", FullscreenVs);
        _vertexShader = device.CreateVertexShader(vs.Span, null);
        _splatPs = device.CreatePixelShader(Pixel(SplatPs).Span, null);
        _advectPs = device.CreatePixelShader(Pixel(AdvectPs).Span, null);
        _divergencePs = device.CreatePixelShader(Pixel(DivergencePs).Span, null);
        _curlPs = device.CreatePixelShader(Pixel(CurlPs).Span, null);
        _vorticityPs = device.CreatePixelShader(Pixel(VorticityPs).Span, null);
        _buoyancyPs = device.CreatePixelShader(Pixel(BuoyancyPs).Span, null);
        _clearPs = device.CreatePixelShader(Pixel(ClearPs).Span, null);
        _pressurePs = device.CreatePixelShader(Pixel(PressurePs).Span, null);
        _gradientPs = device.CreatePixelShader(Pixel(GradientPs).Span, null);
        _displayPs = device.CreatePixelShader(Pixel(DisplayPs).Span, null);

        _constants = device.CreateBuffer(new BufferDescription(80, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _linearSampler[0] = _linear;
    }

    /// <summary>The grid is size-dependent: every field starts empty again, as in the reference.</summary>
    public void Resize(int width, int height) => Build(width, height, _pixelsPerDip);

    /// <summary>Drops every GPU object, keeping the smoothed spectrum so a new device carries on where it was.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertexShader?.Dispose(); _vertexShader = null;
        _splatPs?.Dispose(); _advectPs?.Dispose(); _divergencePs?.Dispose(); _curlPs?.Dispose();
        _vorticityPs?.Dispose(); _buoyancyPs?.Dispose(); _clearPs?.Dispose(); _pressurePs?.Dispose();
        _gradientPs?.Dispose(); _displayPs?.Dispose();
        _splatPs = _advectPs = _divergencePs = _curlPs = _vorticityPs = null;
        _buoyancyPs = _clearPs = _pressurePs = _gradientPs = _displayPs = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose();
        _opaque = null; _raster = null; _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void Build(int width, int height, float pixelsPerDip)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _pixelW = width;
        _pixelH = height;
        _pixelsPerDip = pixelsPerDip > 0 ? pixelsPerDip : 1;

        // the demo's sim cell, kept in DIP: 256x56 on its 680x150 strip
        _simW = Math.Max(8, (int)MathF.Round(width / _pixelsPerDip / CellDip));
        _simH = Math.Max(8, (int)MathF.Round(height / _pixelsPerDip / CellDip));

        // its fire bed has one source every 85 DIP (8 per half of the 680 DIP strip), so a longer strip
        // gets more of them instead of a few dots spread thin
        _columns = Math.Max(8, (int)MathF.Round(width / _pixelsPerDip / 85f));

        // the dye is as fine as the strip, halved when it is wider than the reference's 2048 limit
        bool half = width > 2048;
        _dyeW = half ? Math.Max(8, width / 2) : width;
        _dyeH = half ? Math.Max(8, height / 2) : height;

        _velocity0 = new Target(_device, _simW, _simH);
        _velocity1 = new Target(_device, _simW, _simH);
        _pressure0 = new Target(_device, _simW, _simH);
        _pressure1 = new Target(_device, _simW, _simH);
        _divergence = new Target(_device, _simW, _simH);
        _curl = new Target(_device, _simW, _simH);
        _dye0 = new Target(_device, _dyeW, _dyeH);
        _dye1 = new Target(_device, _dyeW, _dyeH);

        _simViewport = new[] { new Viewport { Width = _simW, Height = _simH, MinDepth = 0, MaxDepth = 1 } };
        _dyeViewport = new[] { new Viewport { Width = _dyeW, Height = _dyeH, MinDepth = 0, MaxDepth = 1 } };
        _outputViewport = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };

        _velocityPing = _dyePing = _pressurePing = 0;
        _needClear = true;
    }

    private void ReleaseSized()
    {
        _velocity0?.Dispose(); _velocity1?.Dispose();
        _pressure0?.Dispose(); _pressure1?.Dispose();
        _divergence?.Dispose(); _curl?.Dispose();
        _dye0?.Dispose(); _dye1?.Dispose();
        _velocity0 = _velocity1 = null;
        _pressure0 = _pressure1 = null;
        _divergence = _curl = null;
        _dye0 = _dye1 = null;
        _simViewport = _dyeViewport = _outputViewport = Array.Empty<Viewport>();
        _pixelW = _pixelH = _simW = _simH = _dyeW = _dyeH = _columns = 0;
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _splatPs is null || _displayPs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;

        // the host knows the DPI only with the frame, so the grid is built here the first time
        var ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        if (frame.Width != _pixelW || frame.Height != _pixelH || MathF.Abs(ppd - _pixelsPerDip) > .001f)
        {
            Build(frame.Width, frame.Height, ppd);
        }

        if (_velocity0 is null || _velocity1 is null || _pressure0 is null || _pressure1 is null
            || _divergence is null || _curl is null || _dye0 is null || _dye1 is null) return;

        // real seconds, clamped like the reference, scaled by the approved speed
        float realDt = Math.Clamp(frame.Dt, 0, .033f);
        float dt = realDt * Speed;
        float k60 = dt * 60;
        bool kick = UpdateAudio(realDt, frame.Audio);
        float aspect = frame.Width / (float)frame.Height;

        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.VSSetConstantBuffers(0, _constantBuffer);
        context.PSSetConstantBuffers(0, _constantBuffer);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetSamplers(0, _linearSampler);
        context.OMSetBlendState(_opaque);

        if (_needClear)
        {
            var empty = new Color4(0, 0, 0, 0);
            context.ClearRenderTargetView(_velocity0.View, empty);
            context.ClearRenderTargetView(_velocity1.View, empty);
            context.ClearRenderTargetView(_pressure0.View, empty);
            context.ClearRenderTargetView(_pressure1.View, empty);
            context.ClearRenderTargetView(_divergence.View, empty);
            context.ClearRenderTargetView(_curl.View, empty);
            context.ClearRenderTargetView(_dye0.View, empty);
            context.ClearRenderTargetView(_dye1.View, empty);
            _needClear = false;
        }

        Splat(context, aspect, k60, kick);
        Simulate(context, dt);

        // the heat ramp onto the strip: premultiplied, no background colour
        _cb[0] = 1f / _pixelW; _cb[1] = 1f / _pixelH;
        _cb[2] = _pixelW; _cb[3] = _pixelH;
        _cb[4] = 1f / _simW; _cb[5] = 1f / _simH;
        _cb[6] = aspect;
        _cb[7] = dt;
        _cb[8] = DyeDissipation;
        _cb[9] = Curl;
        _cb[10] = Buoyancy;
        _cb[11] = 0;
        _cb[12] = .5f; _cb[13] = .5f;
        _cb[14] = 0;
        _cb[15] = Math.Clamp(frame.Fade, 0, 1);
        _cb[16] = _cb[17] = _cb[18] = _cb[19] = 0;

        context.RSSetViewports(_outputViewport);
        context.OMSetRenderTargets(target, null);
        context.PSSetShader(_displayPs);
        _inputs[0] = DyeSrv;
        _inputs[1] = null!;
        context.PSSetShaderResources(0, _inputs);
        context.UpdateSubresource(_cb, _constants!);
        context.Draw(3, 0);
    }

    // ---- the reference's splats -------------------------------------------------------------------

    /// <summary>
    /// The dock inside the strip, as uv (0..1, y up from the strip's bottom); the fire is born on its two side edges.
    /// Any thread.
    /// </summary>
    public void SetDock(float left, float right, float bottom, float top)
    {
        left = Math.Clamp(left, .02f, .5f);
        right = Math.Clamp(right, .5f, .98f);
        bottom = Math.Clamp(bottom, 0, .9f);
        top = Math.Clamp(top, bottom + .05f, 1);
        lock (_dockLock) _dock = (left, right, bottom, top);
    }

    private readonly object _dockLock = new();
    private (float Left, float Right, float Bottom, float Top) _dock = (.5f, .5f, .1f, .5f);

    private void Splat(ID3D11DeviceContext context, float aspect, float k60, bool kick)
    {
        float bass = _bands[0];
        float widthDip = _pixelW / _pixelsPerDip;
        (float Left, float Right, float Bottom, float Top) dock;
        lock (_dockLock) dock = _dock;
        float dip = 1 / Math.Max(1, widthDip);
        float mid = (dock.Bottom + dock.Top) * .5f;

        // The demo's sideways push crosses its 680 DIP strip in a fifth of a second; on a wider strip the
        // same texel speed would barely show, so the sideways components keep that pace across the width.
        float stretch = _columns / 8f;

        // everything is born on the dock's two side edges: the bass at the middle of each edge, with a jet that
        // throws the fire out to the screen's sides
        float jet = Side * (1 + (2 * _beat)) * k60 * .1f * stretch;
        float lift = Side * .15f * k60 * .1f;
        for (int s = -1; s <= 1; s += 2)
        {
            float x0 = s < 0 ? dock.Left : dock.Right;
            SplatDye(context, aspect, x0, mid, (0.35f + bass) * k60, .003f);
            SplatVelocity(context, aspect, x0 + (s * 12 * dip), mid, s * jet, lift, .003f);
        }

        // the bands stacked up each side edge, low bands at the bottom; each throws its own tongue outward
        const int Rows = 8;
        for (int k = 0; k < Rows; k++)
        {
            float f = (k + .5f) / Rows;
            int index = Math.Clamp((int)(f * Bands * .95f), 0, Bands - 1);
            float b = _bands[index];
            float y = dock.Bottom + (f * (dock.Top - dock.Bottom));
            for (int s = -1; s <= 1; s += 2)
            {
                float x = (s < 0 ? dock.Left : dock.Right) + (s * 6 * dip);
                SplatDye(context, aspect, x, y, b * b * 1.6f * k60, .0012f);
                SplatVelocity(context, aspect, x, y, s * Side * .7f * b * k60 * .1f * stretch, Side * .1f * b * k60 * .1f, .002f);
            }
        }

        // the kick throws a gust and a burst of fire out of both side edges
        if (kick)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float x0 = s < 0 ? dock.Left : dock.Right;
                SplatVelocity(context, aspect, x0 + (s * 30 * dip), mid, s * Side * 4 * stretch, Side * .5f, .006f);
                SplatDye(context, aspect, x0, mid, 2.5f, .006f);
            }
        }
    }

    private void SplatDye(ID3D11DeviceContext context, float aspect, float x, float y, float value, float radius)
    {
        _cb[0] = 1f / _dyeW; _cb[1] = 1f / _dyeH;
        _cb[2] = _dyeW; _cb[3] = _dyeH;
        _cb[4] = 1f / _simW; _cb[5] = 1f / _simH;
        _cb[6] = aspect;
        _cb[16] = value; _cb[17] = 0; _cb[18] = 0;
        _cb[12] = x; _cb[13] = y;
        _cb[14] = radius;

        var source = _dyePing == 0 ? _dye0! : _dye1!;
        var destination = _dyePing == 0 ? _dye1! : _dye0!;
        Run(context, _splatPs!, destination, _dyeViewport, source.Srv, null);
        _dyePing ^= 1;
    }

    private void SplatVelocity(ID3D11DeviceContext context, float aspect, float x, float y, float vx, float vy, float radius)
    {
        _cb[0] = 1f / _simW; _cb[1] = 1f / _simH;
        _cb[2] = _simW; _cb[3] = _simH;
        _cb[4] = 1f / _simW; _cb[5] = 1f / _simH;
        _cb[6] = aspect;
        _cb[16] = vx; _cb[17] = vy; _cb[18] = 0;
        _cb[12] = x; _cb[13] = y;
        _cb[14] = radius;

        var source = _velocityPing == 0 ? _velocity0! : _velocity1!;
        var destination = _velocityPing == 0 ? _velocity1! : _velocity0!;
        Run(context, _splatPs!, destination, _simViewport, source.Srv, null);
        _velocityPing ^= 1;
    }

    // ---- the solver -------------------------------------------------------------------------------

    // The uniforms every sim pass shares; the few that differ are overwritten by the caller.
    private void SetSimParams(float texelX, float texelY, float dt)
    {
        _cb[0] = texelX; _cb[1] = texelY;
        _cb[2] = _simW; _cb[3] = _simH;
        _cb[4] = 1f / _simW; _cb[5] = 1f / _simH;
        _cb[6] = _simW / (float)_simH;
        _cb[7] = dt;
        _cb[8] = VelocityDissipation;
        _cb[9] = Curl;
        _cb[10] = Buoyancy;
        _cb[11] = 0;
        _cb[12] = .5f; _cb[13] = .5f;
        _cb[14] = 0;
        _cb[15] = 1;
        _cb[16] = _cb[17] = _cb[18] = _cb[19] = 0;
    }

    private void Simulate(ID3D11DeviceContext context, float dt)
    {
        float simTexelX = 1f / _simW, simTexelY = 1f / _simH;
        float dyeTexelX = 1f / _dyeW, dyeTexelY = 1f / _dyeH;

        // curl of the velocity
        SetSimParams(simTexelX, simTexelY, dt);
        Run(context, _curlPs!, _curl!, _simViewport, VelocitySrv, null);

        // vorticity confinement, then buoyancy from the dye: the two passes that make it curl and rise
        SetSimParams(simTexelX, simTexelY, dt);
        var (velocitySource, velocityDest) = Ping(_velocityPing, _velocity0!, _velocity1!);
        Run(context, _vorticityPs!, velocityDest, _simViewport, velocitySource.Srv, _curl!.Srv);
        _velocityPing ^= 1;

        SetSimParams(simTexelX, simTexelY, dt);
        (velocitySource, velocityDest) = Ping(_velocityPing, _velocity0!, _velocity1!);
        Run(context, _buoyancyPs!, velocityDest, _simViewport, velocitySource.Srv, DyeSrv);
        _velocityPing ^= 1;

        // divergence, then the pressure that cancels it: one clear, 20 Jacobi iterations
        SetSimParams(simTexelX, simTexelY, dt);
        Run(context, _divergencePs!, _divergence!, _simViewport, VelocitySrv, null);

        SetSimParams(simTexelX, simTexelY, dt);
        _cb[11] = .8f;
        var (pressureSource, pressureDest) = Ping(_pressurePing, _pressure0!, _pressure1!);
        Run(context, _clearPs!, pressureDest, _simViewport, pressureSource.Srv, null);
        _pressurePing ^= 1;

        SetSimParams(simTexelX, simTexelY, dt);
        for (int i = 0; i < PressureIterations; i++)
        {
            (pressureSource, pressureDest) = Ping(_pressurePing, _pressure0!, _pressure1!);
            Run(context, _pressurePs!, pressureDest, _simViewport, pressureSource.Srv, _divergence!.Srv);
            _pressurePing ^= 1;
        }

        // subtract the pressure gradient: the velocity is incompressible again
        SetSimParams(simTexelX, simTexelY, dt);
        (velocitySource, velocityDest) = Ping(_velocityPing, _velocity0!, _velocity1!);
        Run(context, _gradientPs!, velocityDest, _simViewport, PressureSrv, velocitySource.Srv);
        _velocityPing ^= 1;

        // advect the velocity through itself, then the dye through the velocity
        SetSimParams(simTexelX, simTexelY, dt);
        (velocitySource, velocityDest) = Ping(_velocityPing, _velocity0!, _velocity1!);
        Run(context, _advectPs!, velocityDest, _simViewport, velocitySource.Srv, velocitySource.Srv);
        _velocityPing ^= 1;

        _cb[0] = dyeTexelX; _cb[1] = dyeTexelY;
        _cb[2] = _dyeW; _cb[3] = _dyeH;
        _cb[4] = simTexelX; _cb[5] = simTexelY;
        _cb[6] = _dyeW / (float)_dyeH;
        _cb[7] = dt;
        _cb[8] = DyeDissipation;
        var (dyeSource, dyeDest) = Ping(_dyePing, _dye0!, _dye1!);
        Run(context, _advectPs!, dyeDest, _dyeViewport, VelocitySrv, dyeSource.Srv);
        _dyePing ^= 1;
    }

    private static (Target Source, Target Destination) Ping(int ping, Target first, Target second) =>
        ping == 0 ? (first, second) : (second, first);

    private ID3D11ShaderResourceView VelocitySrv =>
        (_velocityPing == 0 ? _velocity0! : _velocity1!).Srv;

    private ID3D11ShaderResourceView DyeSrv =>
        (_dyePing == 0 ? _dye0! : _dye1!).Srv;

    private ID3D11ShaderResourceView PressureSrv =>
        (_pressurePing == 0 ? _pressure0! : _pressure1!).Srv;

    // One full-screen pass: the caller has just filled _cb, this binds and draws it.
    private void Run(ID3D11DeviceContext context, ID3D11PixelShader shader, Target destination, Viewport[] viewport, ID3D11ShaderResourceView source, ID3D11ShaderResourceView? second)
    {
        context.RSSetViewports(viewport);
        context.OMSetRenderTargets(destination.View, null);
        context.PSSetShader(shader);
        _inputs[0] = source;
        _inputs[1] = second!;
        context.PSSetShaderResources(0, _inputs);
        context.UpdateSubresource(_cb, _constants!);
        context.Draw(3, 0);
    }

    // ---- audio ------------------------------------------------------------------------------------

    /// <summary>Smooths the bands like the reference and returns whether this frame is a kick onset.</summary>
    private bool UpdateAudio(float dt, SpectrumFrame audio)
    {
        var source = audio.Bands;
        float smooth = (.2f * Speed) + .02f;
        for (int i = 0; i < Bands; i++)
        {
            float target = i < source.Length ? Math.Clamp(source[i], 0, 1) : 0;
            _bands[i] += (target - _bands[i]) * smooth;
        }

        // the kick rings out for a moment, so the fire keeps its push between the frames of a beat
        _beat = MathF.Max(_beat * MathF.Exp(-dt * 5), Math.Clamp(audio.Kick, 0, 1));

        _kickCooldown = MathF.Max(0, _kickCooldown - dt);
        bool kick = false;
        if (audio.Kick > .3f)
        {
            if (_kickArmed && _kickCooldown <= 0)
            {
                kick = true;
                _kickCooldown = .15f;
            }

            _kickArmed = false;
        }
        else
        {
            _kickArmed = true;
        }

        return kick;
    }

    // ---- shaders ----------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "FluidFire", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    // A pass: the shared uniforms and helpers, then the pass's own body.
    private static ReadOnlyMemory<byte> Pixel(string body) =>
        Compile("ps_4_0", FrameConstants + "\n" + body);

    /// <summary>One float texture: the field, its render target and its shader view, always together.</summary>
    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public Target(ID3D11Device device, int width, int height)
        {
            Width = width;
            Height = height;
            _texture = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            View = device.CreateRenderTargetView(_texture, null);
            Srv = device.CreateShaderResourceView(_texture, null);
        }

        public int Width { get; }
        public int Height { get; }
        public ID3D11RenderTargetView View { get; }
        public ID3D11ShaderResourceView Srv { get; }

        public void Dispose()
        {
            Srv.Dispose();
            View.Dispose();
            _texture.Dispose();
        }
    }

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

    // Shared by every pass: the reference's uniforms, and its y-up unit square rebuilt from SV_Position.
    private const string FrameConstants = """
        cbuffer Params : register(b0)
        {
            float2 Texel;
            float2 Size;
            float2 VelTexel;
            float Aspect;
            float Dt;
            float Dissipation;
            float Curl;
            float Buoyancy;
            float Value;
            float2 Point;
            float Radius;
            float Fade;
            float4 SplatColor;
        };

        SamplerState LinearClamp : register(s0);

        // The reference works in a y-up unit square; the texture is stored y down, so each sample flips v
        // and each pixel rebuilds uv the same way. The maths below then matches the reference 1:1.
        float2 Uv(float4 position)
        {
            return float2(position.x / Size.x, 1 - (position.y / Size.y));
        }

        #define SAMPLE(tex, uv) tex.SampleLevel(LinearClamp, float2((uv).x, 1 - (uv).y), 0)
        """;

    private const string SplatPs = """
        Texture2D Source : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float2 p = uv - Point;
            p.x *= Aspect;
            return float4(SAMPLE(Source, uv).xyz + (exp(-dot(p, p) / Radius) * SplatColor.xyz), 1);
        }
        """;

    private const string AdvectPs = """
        Texture2D Velocity : register(t0);
        Texture2D Source : register(t1);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float2 back = uv - (Dt * SAMPLE(Velocity, uv).xy * VelTexel);
            return SAMPLE(Source, back) / (1 + (Dissipation * Dt));
        }
        """;

    private const string DivergencePs = """
        Texture2D Velocity : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float L = SAMPLE(Velocity, uv - float2(Texel.x, 0)).x;
            float R = SAMPLE(Velocity, uv + float2(Texel.x, 0)).x;
            float T = SAMPLE(Velocity, uv + float2(0, Texel.y)).y;
            float B = SAMPLE(Velocity, uv - float2(0, Texel.y)).y;
            float2 C = SAMPLE(Velocity, uv).xy;
            if (uv.x - Texel.x < 0) L = -C.x;
            if (uv.x + Texel.x > 1) R = -C.x;
            if (uv.y + Texel.y > 1) T = -C.y;
            if (uv.y - Texel.y < 0) B = -C.y;
            return float4(.5 * (R - L + T - B), 0, 0, 1);
        }
        """;

    private const string CurlPs = """
        Texture2D Velocity : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float L = SAMPLE(Velocity, uv - float2(Texel.x, 0)).y;
            float R = SAMPLE(Velocity, uv + float2(Texel.x, 0)).y;
            float T = SAMPLE(Velocity, uv + float2(0, Texel.y)).x;
            float B = SAMPLE(Velocity, uv - float2(0, Texel.y)).x;
            return float4(.5 * (R - L - T + B), 0, 0, 1);
        }
        """;

    private const string VorticityPs = """
        Texture2D Velocity : register(t0);
        Texture2D CurlField : register(t1);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float L = SAMPLE(CurlField, uv - float2(Texel.x, 0)).x;
            float R = SAMPLE(CurlField, uv + float2(Texel.x, 0)).x;
            float T = SAMPLE(CurlField, uv + float2(0, Texel.y)).x;
            float B = SAMPLE(CurlField, uv - float2(0, Texel.y)).x;
            float C = SAMPLE(CurlField, uv).x;
            float2 f = .5 * float2(abs(T) - abs(B), abs(R) - abs(L));
            f /= length(f) + 1e-4;
            f *= Curl * C;
            f.y *= -1;
            float2 v = SAMPLE(Velocity, uv).xy + (f * Dt);
            return float4(clamp(v, -1000.0, 1000.0), 0, 1);
        }
        """;

    private const string BuoyancyPs = """
        Texture2D Velocity : register(t0);
        Texture2D DyeField : register(t1);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float2 v = SAMPLE(Velocity, uv).xy;
            v.y += SAMPLE(DyeField, uv).r * Buoyancy * Dt;
            v *= 1 - (.15 * Dt);
            return float4(v, 0, 1);
        }
        """;

    private const string ClearPs = """
        Texture2D Source : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            return Value * SAMPLE(Source, Uv(position));
        }
        """;

    private const string PressurePs = """
        Texture2D Pressure : register(t0);
        Texture2D Divergence : register(t1);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float L = SAMPLE(Pressure, uv - float2(Texel.x, 0)).x;
            float R = SAMPLE(Pressure, uv + float2(Texel.x, 0)).x;
            float T = SAMPLE(Pressure, uv + float2(0, Texel.y)).x;
            float B = SAMPLE(Pressure, uv - float2(0, Texel.y)).x;
            return float4((L + R + B + T - SAMPLE(Divergence, uv).x) * .25, 0, 0, 1);
        }
        """;

    private const string GradientPs = """
        Texture2D Pressure : register(t0);
        Texture2D Velocity : register(t1);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float L = SAMPLE(Pressure, uv - float2(Texel.x, 0)).x;
            float R = SAMPLE(Pressure, uv + float2(Texel.x, 0)).x;
            float T = SAMPLE(Pressure, uv + float2(0, Texel.y)).x;
            float B = SAMPLE(Pressure, uv - float2(0, Texel.y)).x;
            float2 v = SAMPLE(Velocity, uv).xy - float2(R - L, T - B);
            return float4(v, 0, 1);
        }
        """;

    private const string DisplayPs = """
        Texture2D DyeField : register(t0);

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = Uv(position);
            float h = SAMPLE(DyeField, uv).r;
            float t = 1 - exp(-h * 1.4);

            // black, then blood red, orange, the yellow of the hottest gas
            float3 c = lerp(float3(0, 0, 0), float3(.45, .02, 0), saturate(t / .22));
            c = lerp(c, float3(1, .2, 0), saturate((t - .22) / .2));
            c = lerp(c, float3(1, .72, .06), saturate((t - .42) / .2));
            c = lerp(c, float3(1, .97, .7), saturate((t - .75) / .2));

            // nothing behind the fire: the dock shows through, so the alpha carries the shape
            float a = saturate(t * 6) * smoothstep(1, .6, uv.y) * Fade;
            return float4(c * a, a);
        }
        """;
}
