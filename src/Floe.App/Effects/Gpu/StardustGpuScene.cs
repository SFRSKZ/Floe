using System.Numerics;
using System.Runtime.InteropServices;
using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The star dust visualizer on the GPU. The simulation stays on the CPU, ported from
/// <see cref="Visualizer.StardustVisualizer"/>: the spectrum drives the dust and the wave exactly as it does there.
/// <para>
/// The drawing is three passes: the previous frame is lifted and dimmed into the other of two float textures
/// (MilkDrop's short feedback blur), the grains are splatted into it additively as instanced gaussian quads, and
/// one composite pass adds the aurora the CPU computed per column and writes the premultiplied strip.
/// </para>
/// </summary>
public sealed partial class StardustGpuScene : IGpuScene
{
    private const int Bands = 24;
    private const int MaxParticles = 2500;
    private const int MaxInstances = MaxParticles + 2400;
    private const int PaletteSize = 16;
    private const int InstanceFloats = 8;
    // Gaussian sprites for the grains, by blur: crisp far dust up to out-of-focus near dust.
    private static readonly float[] Sigmas = { .55f, .9f, 1.5f, 2.4f, 3.4f };

    // The wave: the same sines and springs as the CPU look.
    private static readonly float[] WaveFrequency = { .22f, .5f, .95f, 1.6f };
    private static readonly float[] WaveSpeed = { 1.8f, -3.1f, 4.6f, -6.5f };
    private const float SpringStiffness = 90;
    private const float SpringDamping = 18;

    private readonly Random _random = new(0x5DA57);

    // spectrum state, smoothed exactly like the CPU version
    private readonly float[] _band = new float[Bands];
    private double _bass, _level, _pulse, _shimmer;
    private double _time;
    private double _emitCarry;

    // particles, structure of arrays; depth 0 is far, 1 is near
    private readonly float[] _x = new float[MaxParticles];
    private readonly float[] _y = new float[MaxParticles];
    private readonly float[] _vx = new float[MaxParticles];
    private readonly float[] _vy = new float[MaxParticles];
    private readonly float[] _age = new float[MaxParticles];
    private readonly float[] _life = new float[MaxParticles];
    private readonly float[] _bright = new float[MaxParticles];
    private readonly float[] _phase = new float[MaxParticles];
    private readonly float[] _depth = new float[MaxParticles];
    private readonly float[] _flare = new float[MaxParticles];
    private readonly byte[] _hue = new byte[MaxParticles];
    private int _count;

    // the wave: two thirds of the grains ride the main wave, a third a fainter one flowing the other way
    private readonly float[] _waveAmp = new float[4];
    private float[] _wx = Array.Empty<float>(), _wy = Array.Empty<float>(), _wvx = Array.Empty<float>(), _wvy = Array.Empty<float>();
    private float[] _wPhase = Array.Empty<float>();
    private int _waveCount;
    private double _wavePhase;
    private float _waveWidth = -1;

    // the palette is the frame's, copied here so the CPU-side sprites and the aurora can read it
    private readonly float[] _palR = new float[PaletteSize], _palG = new float[PaletteSize], _palB = new float[PaletteSize];

    // DIP size of the strip and the pixels-per-DIP of the target it is drawn into
    private float _width, _height, _ppd = -1, _ppdCache = -1;
    private int _pixelW, _pixelH;
    private PerformanceLevel _performance;

    // CPU-side sprite setup: sigma and radius in pixels, and the light each gaussian carries
    private readonly float[] _sigma = new float[Sigmas.Length];
    private readonly float[] _radius = new float[Sigmas.Length];
    private readonly float[] _norm = new float[Sigmas.Length];

    // the aurora, per column: reach and colour, exactly what Aurora() builds
    private float[] _auraReach = Array.Empty<float>(), _auraR = Array.Empty<float>(), _auraG = Array.Empty<float>(), _auraB = Array.Empty<float>();
    private float[] _auraScratch = Array.Empty<float>();
    private float[] _auraData = Array.Empty<float>();

    private ID3D11Device? _device;
    private ID3D11VertexShader? _feedbackVs, _particleVs, _compositeVs;
    private ID3D11PixelShader? _feedbackPs, _particlePs, _compositePs;
    private ID3D11InputLayout? _particleLayout;
    private ID3D11Buffer? _constants, _instanceBuffer;
    private ID3D11BlendState? _additive, _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear, _point;
    private ID3D11Texture2D? _feedback0, _feedback1, _auraTexture;
    private ID3D11RenderTargetView? _feedbackRtv0, _feedbackRtv1;
    private ID3D11ShaderResourceView? _feedbackSrv0, _feedbackSrv1, _auraSrv;
    private Viewport[] _viewports = Array.Empty<Viewport>();

    // bound state, kept in arrays so a frame does not allocate
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11Buffer[] _vertexBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _readFeedback = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _compositeInputs = new ID3D11ShaderResourceView[2];
    private readonly ID3D11SamplerState[] _linearSampler = new ID3D11SamplerState[1];
    private readonly ID3D11SamplerState[] _pointSampler = new ID3D11SamplerState[1];
    private readonly uint[] _instanceStride = { InstanceFloats * sizeof(float) };
    private readonly uint[] _instanceOffset = { 0 };
    private readonly float[] _cb = new float[20];
    private readonly float[] _instanceData = new float[MaxInstances * InstanceFloats];
    private readonly byte[] _instanceBytes = new byte[MaxInstances * InstanceFloats * sizeof(float)];
    private int _instanceCount;

    private int _ping;
    private bool _needClear;

    // the MilkDrop dust: the wave's grains and their trails ride one flow, a slow zoom out of the middle plus
    // swirling cells, so the picture the feedback warps and the grains drawn on it move together
    private double _milkTime, _milkZoom, _milkSwirl;

    /// <summary>The dust wave in a MilkDrop flow (needs <see cref="GpuFrame.Wave"/> too).</summary>
    public bool Milk { get; set; }

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        var fullscreenVs = Compile("vs_4_0", FullscreenVs);
        _feedbackVs = device.CreateVertexShader(fullscreenVs.Span, null);
        _compositeVs = device.CreateVertexShader(fullscreenVs.Span, null);
        var particleVs = Compile("vs_4_0", ParticleVs);
        _particleVs = device.CreateVertexShader(particleVs.Span, null);
        _feedbackPs = device.CreatePixelShader(Compile("ps_4_0", FeedbackPs).Span, null);
        _particlePs = device.CreatePixelShader(Compile("ps_4_0", ParticlePs).Span, null);
        _compositePs = device.CreatePixelShader(Compile("ps_4_0", CompositePs).Span, null);

        _particleLayout = device.CreateInputLayout(new[]
        {
            new InputElementDescription("INSTANCE", 0, Format.R32G32B32A32_Float, 0, 0, InputClassification.PerInstanceData, 1),
            new InputElementDescription("INSTANCE", 1, Format.R32G32B32A32_Float, 16, 0, InputClassification.PerInstanceData, 1),
        }, particleVs.Span);

        _constants = device.CreateBuffer(new BufferDescription(80, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _instanceBuffer = device.CreateBuffer(new BufferDescription(MaxInstances * InstanceFloats * sizeof(float), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _vertexBuffer[0] = _instanceBuffer;
        _additive = device.CreateBlendState(AdditiveBlend());
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _point = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipPoint, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _linearSampler[0] = _linear;
        _pointSampler[0] = _point;
    }

    /// <summary>Textures are size-dependent; the simulation state survives.</summary>
    public void Resize(int width, int height)
    {
        if (_device is null || width < 1 || height < 1) return;
        ReleaseSized();
        _pixelW = width;
        _pixelH = height;

        _feedback0 = CreateFeedback(width, height);
        _feedback1 = CreateFeedback(width, height);
        _feedbackRtv0 = _device.CreateRenderTargetView(_feedback0, null);
        _feedbackRtv1 = _device.CreateRenderTargetView(_feedback1, null);
        _feedbackSrv0 = _device.CreateShaderResourceView(_feedback0, null);
        _feedbackSrv1 = _device.CreateShaderResourceView(_feedback1, null);

        _auraTexture = _device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, (uint)width, 1, 1, 1, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _auraSrv = _device.CreateShaderResourceView(_auraTexture, null);
        _auraData = new float[width * 4];

        _viewports = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
        _needClear = true;
    }

    /// <summary>Drops every GPU object, keeping the dust and the wave so a new device carries on where it was.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _particleLayout?.Dispose();
        _particleLayout = null;
        _feedbackVs?.Dispose(); _particleVs?.Dispose(); _compositeVs?.Dispose();
        _feedbackVs = _particleVs = _compositeVs = null;
        _feedbackPs?.Dispose(); _particlePs?.Dispose(); _compositePs?.Dispose();
        _feedbackPs = _particlePs = _compositePs = null;
        _constants?.Dispose(); _instanceBuffer?.Dispose();
        _constants = _instanceBuffer = null;
        _constantBuffer[0] = null!;
        _vertexBuffer[0] = null!;
        _additive?.Dispose(); _opaque?.Dispose();
        _additive = _opaque = null;
        _raster?.Dispose(); _noDepth?.Dispose();
        _raster = null; _noDepth = null;
        _linear?.Dispose(); _point?.Dispose();
        _linear = _point = null;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void ReleaseSized()
    {
        _feedbackRtv0?.Dispose(); _feedbackRtv1?.Dispose();
        _feedbackSrv0?.Dispose(); _feedbackSrv1?.Dispose(); _auraSrv?.Dispose();
        _feedbackRtv0 = _feedbackRtv1 = null;
        _feedbackSrv0 = _feedbackSrv1 = _auraSrv = null;
        _feedback0?.Dispose(); _feedback1?.Dispose(); _auraTexture?.Dispose();
        _feedback0 = _feedback1 = _auraTexture = null;
        _viewports = Array.Empty<Viewport>();
        _pixelW = _pixelH = 0;
    }

    private ID3D11Texture2D CreateFeedback(int width, int height)
    {
        var desc = new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None);
        return _device!.CreateTexture2D(desc);
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _feedbackPs is null || _compositePs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _pixelW || frame.Height != _pixelH) Resize(frame.Width, frame.Height);
        if (_feedbackRtv0 is null || _feedbackRtv1 is null || _auraTexture is null) return;

        _ppd = Math.Max(.1f, frame.PixelsPerDip);
        _width = frame.Width / _ppd;
        _height = frame.Height / _ppd;
        var dt = Math.Clamp(frame.Dt, .001f, .1f);

        // the shapes ride the wave's grains
        bool wave = frame.Wave || Shaped;
        EnsureCpu(frame.Performance, wave);

        // the simulation first: it leaves the particles where this frame draws them
        Simulate(dt, frame.Audio, frame.Palette, wave);
        BuildInstances(frame.Fade, wave);
        BuildAurora();

        if (_needClear)
        {
            var zero = new Color4(0, 0, 0, 0);
            context.ClearRenderTargetView(_feedbackRtv0, zero);
            context.ClearRenderTargetView(_feedbackRtv1, zero);
            _needClear = false;
        }

        // the frame constants: feedback reads Lift and Decay, the particles Size, the composite Size and Fade
        bool milk = Milk && frame.Wave;
        float lift = milk || Shaped ? 0 : (frame.Wave ? .6f : .35f) * _ppd * dt * 30;
        float decay = (float)Math.Exp(-dt / (Shaped ? ShapeDecayTau : milk ? .9 : frame.Wave ? .30 : .055));
        _cb[0] = frame.Width;
        _cb[1] = frame.Height;
        _cb[2] = 1f / frame.Width;
        _cb[3] = 1f / frame.Height;
        _cb[4] = lift;
        _cb[5] = decay;
        _cb[6] = Math.Clamp(frame.Fade, 0, 1);
        _cb[7] = 0;
        _cb[8] = milk ? 1 : 0;
        _cb[9] = _ppd;
        _cb[10] = dt;
        _cb[11] = (float)_milkTime;
        _cb[12] = (float)_milkZoom;
        _cb[13] = (float)_milkSwirl;
        _cb[14] = MilkKx;
        _cb[15] = MilkKy();
        _cb[16] = _width / 2;
        _cb[17] = _height * .6f;
        context.UpdateSubresource(_cb, _constants!);

        int source = _ping, dest = 1 - _ping;
        var sourceSrv = source == 0 ? _feedbackSrv0! : _feedbackSrv1!;
        var destSrv = dest == 0 ? _feedbackSrv0! : _feedbackSrv1!;
        var destRtv = dest == 0 ? _feedbackRtv0! : _feedbackRtv1!;

        context.RSSetViewports(_viewports);
        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.VSSetConstantBuffers(0, _constantBuffer);
        context.PSSetConstantBuffers(0, _constantBuffer);

        // a) the last frame, lifted and dimmed: the short motion blur the CPU keeps
        context.OMSetRenderTargets(destRtv, null);
        context.OMSetBlendState(_opaque);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_feedbackVs);
        context.PSSetShader(_feedbackPs);
        _readFeedback[0] = sourceSrv;
        context.PSSetShaderResources(0, _readFeedback);
        context.PSSetSamplers(0, _linearSampler);
        context.Draw(3, 0);

        // b) the grains, additively over that frame
        if (_instanceCount > 0)
        {
            context.OMSetBlendState(_additive);
            context.IASetInputLayout(_particleLayout);
            context.VSSetShader(_particleVs);
            context.PSSetShader(_particlePs);
            int bytes = _instanceCount * InstanceFloats * sizeof(float);
            Buffer.BlockCopy(_instanceData, 0, _instanceBytes, 0, bytes);
            var mapped = context.Map(_instanceBuffer!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(_instanceBytes, 0, mapped.DataPointer, bytes); }
            finally { context.Unmap(_instanceBuffer!); }
            context.IASetVertexBuffers(0, _vertexBuffer, _instanceStride, _instanceOffset);
            context.DrawInstanced(6, (uint)_instanceCount, 0, 0);
        }

        // c) aurora plus dust onto the strip: what Present() writes
        context.OMSetRenderTargets(target, null);
        context.OMSetBlendState(_opaque);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_compositeVs);
        context.PSSetShader(_compositePs);
        context.UpdateSubresource(new ReadOnlySpan<float>(_auraData, 0, _pixelW * 4), _auraTexture!, 0, (uint)(_pixelW * 16));
        _compositeInputs[0] = destSrv;
        _compositeInputs[1] = _auraSrv!;
        context.PSSetShaderResources(1, _compositeInputs);
        context.PSSetSamplers(1, _pointSampler);
        context.Draw(3, 0);

        _ping = dest;
    }

    // ---- setup ------------------------------------------------------------------------------------

    private void EnsureCpu(PerformanceLevel performance, bool wave)
    {
        _performance = performance;
        if (_ppd != _ppdCache)
        {
            _ppdCache = _ppd;
            // the sprite normals are the CPU sprite's at its full-resolution scale, so a grain keeps its peak light
            for (int s = 0; s < Sigmas.Length; s++)
            {
                float sigma = Sigmas[s];
                int radius = Math.Max(1, (int)MathF.Ceiling(sigma * 2.5f));
                float sum = 0;
                for (int y = -radius; y <= radius; y++)
                    for (int x = -radius; x <= radius; x++)
                        sum += MathF.Exp(-((x * x) + (y * y)) / (2 * sigma * sigma));
                _norm[s] = 1 / Math.Max(1, sum * .35f);
                _sigma[s] = sigma * _ppd;
                _radius[s] = MathF.Max(1, MathF.Ceiling(_sigma[s] * 2.5f));
            }

            // a crisp dot is a bilinear splat of 1.4x the colour in Dot(): the same light in total, not at its peak,
            // or the grains clip to white
            _norm[0] = 1.4f / (2 * MathF.PI * Sigmas[0] * Sigmas[0]);
        }

        if (wave && (_waveCount == 0 || MathF.Abs(_waveWidth - _width) > .5f))
        {
            // one grain every ~1.2 DIP of width
            int n = Shaped ? ShapeCount(performance) : Math.Clamp((int)(_width / 1.2f * (performance == PerformanceLevel.Lite ? .5f : 1)), 60, 2400);
            _waveCount = n;
            _waveWidth = _width;
            _wx = new float[n];
            _wy = new float[n];
            _wvx = new float[n];
            _wvy = new float[n];
            _wPhase = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!Shaped) Home(i, out _wx[i], out _wy[i]);
                _wPhase[i] = (float)(_random.NextDouble() * 100);
            }

            if (Shaped) ShapeInit(n);
        }

        if (_auraReach.Length != _pixelW)
        {
            _auraReach = new float[_pixelW];
            _auraR = new float[_pixelW];
            _auraG = new float[_pixelW];
            _auraB = new float[_pixelW];
            _auraScratch = new float[_pixelW];
        }
    }

    private void Simulate(float dt, SpectrumFrame f, Vector3[] palette, bool wave)
    {
        _time += dt;
        for (int i = 0; i < PaletteSize; i++)
        {
            _palR[i] = palette[i].X;
            _palG[i] = palette[i].Y;
            _palB[i] = palette[i].Z;
        }

        // bands: rise quickly, settle slowly
        float rise = (float)(1 - Math.Exp(-dt / .05)), settle = (float)(1 - Math.Exp(-dt / .28));
        for (int i = 0; i < Bands; i++)
        {
            float target = f.Bands[i];
            _band[i] += (target - _band[i]) * (target > _band[i] ? rise : settle);
        }

        _bass += (f.Bass - _bass) * (1 - Math.Exp(-dt / (f.Bass > _bass ? .04 : .25)));
        _level += (f.Level - _level) * (1 - Math.Exp(-dt / (f.Level > _level ? .05 : .35)));
        _pulse *= Math.Exp(-dt / .22);
        _shimmer *= Math.Exp(-dt / .15);
        if (f.Kick > 0)
        {
            if (Shaped) ShapeKick(f.Kick);
            else if (wave) Scatter(.15f + (.35f * (float)Math.Max(_bass, f.Kick)));
            _pulse = Math.Max(_pulse, f.Kick);
            Lift(f.Kick);
        }

        if (f.Snare > 0) _shimmer = Math.Max(_shimmer, f.Snare);

        if (wave && Milk) Flow(dt);
        if (Shaped) MoveShape(dt);
        else if (wave) MoveWave(dt);
        // the shapes are the whole picture; only the ferrofluid throws sparks of its own
        if (!Shaped) Emit(dt, f, wave);
        Move(dt);
    }

    // ---- the dust ---------------------------------------------------------------------------------

    private double Gaussian()
    {
        double u = 1 - _random.NextDouble(), v = _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    private byte Hue(double xn) => (byte)Math.Clamp((int)((xn * (PaletteSize - 4)) + _random.Next(4)), 0, PaletteSize - 1);

    // A kick lifts the dust already in the air, the near grains most.
    private void Lift(float kick)
    {
        for (int p = 0; p < _count; p++) _vy[p] -= kick * (8 + (14 * _depth[p]));
    }

    private void Emit(float dt, SpectrumFrame f, bool wave)
    {
        double width = _width, bottom = _height;
        double cx = width / 2;
        double density = Math.Clamp(width / 1600, .4, 1.6) * (_performance == PerformanceLevel.Lite ? .5 : 1);

        // each band sends dust up from its own place along the edge, mirrored about the middle
        for (int b = 0; b < Bands; b++)
        {
            double e = _band[b];
            _emitCarry += (wave ? (e * e * 7) + .2 : (e * e * 16) + .4) * density * dt;
            while (_emitCarry >= 1)
            {
                _emitCarry -= 1;
                double xn = (b + _random.NextDouble()) / Bands * .5;
                double x = cx + ((_random.Next(2) == 0 ? -1 : 1) * xn * width);
                double depth = Math.Pow(_random.NextDouble(), 1.8);
                // near dust drifts faster, far dust barely moves: parallax
                double speed = (6 + (22 * e)) * (.5 + depth);
                Spawn(x, bottom * (.55 + (.45 * _random.NextDouble())), Gaussian() * 3, -speed,
                    2.2 + (2 * _random.NextDouble()), (.35 + (.45 * e)) * (1 - (.55 * depth)), depth, Hue(xn * 2));
            }
        }

        if (f.Kick > 0)
        {
            // a soft puff of fine dust from the middle
            int n = (int)((10 + (25 * f.Kick)) * density);
            for (int i = 0; i < n; i++)
            {
                double x = cx + (Gaussian() * width * .12);
                double depth = _random.NextDouble() * .5;
                Spawn(x, bottom - (_random.NextDouble() * 6), Gaussian() * 12, -(20 + (35 * f.Kick * _random.NextDouble())),
                    1.4 + _random.NextDouble(), .6 + (.3 * f.Kick), depth, Hue(Math.Abs(x - cx) / cx));
            }
        }

        if (f.Hat > 0)
        {
            // a few grains flare up
            int flares = 1 + (int)(3 * f.Hat);
            for (int k = 0; k < flares && _count > 0; k++)
            {
                int p = _random.Next(_count);
                if (_depth[p] < .5f) _flare[p] = Math.Max(_flare[p], f.Hat);
            }
        }
    }

    private void Spawn(double x, double y, double vx, double vy, double life, double bright, double depth, byte hue)
    {
        if (_count >= MaxParticles) return;
        int p = _count++;
        _x[p] = (float)x;
        _y[p] = (float)y;
        _vx[p] = (float)vx;
        _vy[p] = (float)vy;
        _age[p] = 0;
        _life[p] = (float)life;
        _bright[p] = (float)bright;
        _phase[p] = (float)(_random.NextDouble() * 100);
        _depth[p] = (float)depth;
        _flare[p] = 0;
        _hue[p] = hue;
    }

    private void Move(float dt)
    {
        float d = dt, t = (float)_time;
        // the lift a kick gave settles back to a slow drift
        float drag = (float)Math.Exp(-dt * 1.4), flareDecay = (float)Math.Exp(-dt / .18);
        float width = _width, height = _height;
        for (int p = 0; p < _count;)
        {
            _age[p] += d;
            // a slow sway, so the dust meanders instead of rising straight
            _vx[p] += MathF.Sin((_y[p] * .05f) + (t * .5f) + (_phase[p] % 6.3f)) * 5 * d;
            _vy[p] -= (2 + (4 * _depth[p])) * d;
            _vx[p] *= drag;
            _vy[p] *= drag;
            float fx = 0, fy = 0;
            if (Milk) Field(_x[p], _y[p], out fx, out fy);
            _x[p] += (_vx[p] + fx) * d;
            _y[p] += (_vy[p] + fy) * d;
            _flare[p] *= flareDecay;
            if (_age[p] >= _life[p] || _y[p] < -6 || _y[p] > height + 6 || _x[p] < -8 || _x[p] > width + 8)
            {
                Remove(p);
                continue;
            }

            p++;
        }
    }

    private void Remove(int p)
    {
        int last = --_count;
        _x[p] = _x[last];
        _y[p] = _y[last];
        _vx[p] = _vx[last];
        _vy[p] = _vy[last];
        _age[p] = _age[last];
        _life[p] = _life[last];
        _bright[p] = _bright[last];
        _phase[p] = _phase[last];
        _depth[p] = _depth[last];
        _flare[p] = _flare[last];
        _hue[p] = _hue[last];
    }

    private static float Light(float c) => (c * .6f) + .4f;

    // ---- the wave ---------------------------------------------------------------------------------

    // Where grain i belongs on its wave right now.
    private void Home(int i, out float hx, out float hy)
    {
        int n = _waveCount, main = n * 2 / 3;
        bool second = i >= main;
        float u = second ? (i - main) / (float)Math.Max(1, n - main - 1) : i / (float)Math.Max(1, main - 1);
        float width = _width, height = _height;
        // about one long swell per 300 DIP, however wide the screen
        float x = u * width / 12;
        float phase = (float)_wavePhase, y = 0;
        for (int k = 0; k < 4; k++)
        {
            y += _waveAmp[k] * (second
                ? MathF.Sin((x * WaveFrequency[k]) - (phase * WaveSpeed[k] * .7f) + (k * 2.1f))
                : MathF.Sin((x * WaveFrequency[k]) + (phase * WaveSpeed[k]) + k));
        }

        hx = u * width;
        hy = Math.Clamp((height * .6f) + (y * height * .38f * (second ? .55f : 1)), 5, height - 3);
    }

    private void MoveWave(float dt)
    {
        if (_waveCount == 0) return;
        float Avg(int a, int b)
        {
            float sum = 0;
            for (int k = a; k <= b; k++) sum += _band[k];
            return sum / (b - a + 1);
        }

        // the same weights as the bands fall off: lows swing widest
        _waveAmp[0] = Avg(0, 3);
        _waveAmp[1] = Avg(4, 7) * .8f;
        _waveAmp[2] = Avg(8, 16) * .7f;
        _waveAmp[3] = Avg(17, 23) * .6f;
        // no sound, no flow
        _wavePhase += dt * _level * 1.6;

        // in the MilkDrop flow the springs are loose, so the grains drift off the line with the swirls and come back
        bool milk = Milk;
        float stiffness = milk ? 9 : SpringStiffness, damping = milk ? 4 : SpringDamping;
        for (int i = 0; i < _waveCount; i++)
        {
            Home(i, out float hx, out float hy);
            // a stiff spring, nearly critically damped: back home in about a tenth of a second
            _wvx[i] += ((stiffness * (hx - _wx[i])) - (damping * _wvx[i])) * dt;
            _wvy[i] += ((stiffness * (hy - _wy[i])) - (damping * _wvy[i])) * dt;
            float fx = 0, fy = 0;
            if (milk) Field(_wx[i], _wy[i], out fx, out fy);
            _wx[i] += (_wvx[i] + fx) * dt;
            _wy[i] += (_wvy[i] + fy) * dt;
        }
    }

    // ---- the MilkDrop flow ------------------------------------------------------------------------

    // swirl cells about 260 DIP wide and a little taller than the strip
    private const float MilkKx = 2 * MathF.PI / 260;
    private float MilkKy() => 2 * MathF.PI / Math.Max(40, _height * 1.3f);

    // The music drives the flow: the bass zooms out of the middle, the level spins the cells, a kick does both.
    private void Flow(float dt)
    {
        _milkTime += dt * (.35 + (1.4 * _level));
        double zoom = .08 + (.5 * _bass) + (.9 * _pulse);
        double swirl = (18 + (120 * _level) + (90 * _pulse)) * Math.Sin((_milkTime * .23) + .8);
        _milkZoom += (zoom - _milkZoom) * (1 - Math.Exp(-dt / .12));
        _milkSwirl += (swirl - _milkSwirl) * (1 - Math.Exp(-dt / .2));
    }

    // The flow at a point, DIP per second; FeedbackPs runs the same sum so the trails follow the grains.
    private void Field(float x, float y, out float vx, out float vy)
    {
        float t = (float)_milkTime, zoom = (float)_milkZoom, v = (float)_milkSwirl;
        float kx = MilkKx, ky = MilkKy();
        float dx = x - (_width / 2), dy = y - (_height * .6f);
        // a zoom out of the middle, flattened sideways so a wide strip does not tear apart at its ends
        vx = zoom * dx * .25f;
        vy = zoom * dy;
        // two layers of cells, the finer one turning the other way: a stream function, so nothing piles up
        float ax = (kx * x) + (t * .7f), ay = (ky * y) - (t * .5f);
        float bx = (kx * 2.3f * x) - (t * 1.1f), by = (ky * 1.7f * y) + (t * .8f);
        vx += v * ((MathF.Sin(ax) * MathF.Cos(ay)) - (.5f * MathF.Sin(bx) * MathF.Cos(by)));
        vy += v * ((-(kx / ky) * MathF.Cos(ax) * MathF.Sin(ay)) + (.5f * (2.3f * kx / (1.7f * ky)) * MathF.Cos(bx) * MathF.Sin(by)));
    }

    // A beat throws the grains off their wave; the springs pull them back.
    private void Scatter(float strength)
    {
        for (int i = 0; i < _waveCount; i++)
        {
            double angle = _random.NextDouble() * Math.PI * 2, speed = strength * 400 * _random.NextDouble();
            _wvx[i] += (float)(Math.Cos(angle) * speed);
            _wvy[i] += (float)(Math.Sin(angle) * speed);
        }
    }

    // ---- the sprite instances ---------------------------------------------------------------------

    private int Add(float x, float y, int s, float r, float g, float b)
    {
        int o = _instanceCount * InstanceFloats;
        _instanceData[o] = x;
        _instanceData[o + 1] = y;
        _instanceData[o + 2] = _radius[s];
        _instanceData[o + 3] = _sigma[s];
        _instanceData[o + 4] = r;
        _instanceData[o + 5] = g;
        _instanceData[o + 6] = b;
        _instanceData[o + 7] = Shaped ? 1 : 0;
        return _instanceCount + 1;
    }

    private void BuildInstances(float fade, bool wave)
    {
        float ppd = _ppd, t = (float)_time;
        float shimmer = (float)_shimmer;
        int n = 0;

        for (int p = 0; p < _count; p++)
        {
            float age = _age[p], life = _life[p];
            float envelope = Math.Min(1, age / .25f) * Math.Min(1, (life - age) / .45f);
            float twinkle = .75f + (.25f * MathF.Sin(_phase[p] + (t * (2.2f + (_phase[p] % 3)))));
            float depth = _depth[p];
            float flare = _flare[p];
            float i = (wave ? .9f : 1.9f) * _bright[p] * envelope * (twinkle + (shimmer * .8f * (1 - depth))) * (1 + (2.5f * flare));
            if (i <= .004f) continue;
            int hue = _hue[p];
            // near dust is out of focus: wide and faint, like bokeh
            int s = flare > .2f ? 2 : Math.Min(Sigmas.Length - 1, (int)(depth * Sigmas.Length));
            // a little lighter than the aurora behind, so the grains stand out from it
            _instanceCount = n;
            n = Add(_x[p] * ppd, _y[p] * ppd, s,
                Light(_palR[hue]) * i * _norm[s], Light(_palG[hue]) * i * _norm[s], Light(_palB[hue]) * i * _norm[s]);
        }

        if (Shaped)
        {
            n = ShapeInstances(n, fade);
        }
        else if (wave && _waveCount > 0)
        {
            int main = _waveCount * 2 / 3;
            float energy = .45f + (.55f * (float)_level);
            float trail = Math.Min(1, fade * 1.5f);
            for (int i = 0; i < _waveCount; i++)
            {
                bool second = i >= main;
                float twinkle = .7f + (.3f * MathF.Sin(_wPhase[i] + (t * (2.5f + (_wPhase[i] % 3)))));
                // a grain thrown far from home glows a little brighter
                float speed = MathF.Min(1, (MathF.Abs(_wvx[i]) + MathF.Abs(_wvy[i])) / 300);
                float b = (second ? .8f : 1.4f) * energy * twinkle * (1 + speed) * trail;
                float xn = MathF.Abs((_wx[i] / _width) - .5f) * 2;
                int hue = Math.Clamp((int)((second ? .55f + (.45f * xn) : xn * .6f) * (PaletteSize - 1)), 0, PaletteSize - 1);
                int s = second ? 1 : 0;
                _instanceCount = n;
                n = Add(_wx[i] * ppd, _wy[i] * ppd, s,
                    _palR[hue] * b * _norm[s], _palG[hue] * b * _norm[s], _palB[hue] * b * _norm[s]);
            }
        }

        _instanceCount = n;
    }

    // ---- the aurora -------------------------------------------------------------------------------

    // Exactly Aurora(): how high the light reaches and its colour, per column, then smoothed.
    private void BuildAurora()
    {
        int w = _auraReach.Length;
        if (w == 0) return;
        float height = _height * _ppd;
        float bass = (float)_bass, pulse = (float)_pulse, level = (float)_level;
        for (int x = 0; x < w; x++)
        {
            float xn = MathF.Abs((x / (float)w) - .5f) * 2;
            // bands laid out from the middle outwards, interpolated so the curve stays smooth
            float at = xn * (Bands - 1);
            int b0 = Math.Min(Bands - 2, (int)at);
            float band = _band[b0] + ((_band[b0 + 1] - _band[b0]) * (at - b0));
            float reach = (.13f + (.80f * MathF.Pow(band, 1.2f) * (.6f + (.4f * level))) + (.25f * (bass + pulse))) * height;
            _auraReach[x] = Math.Max(_ppd, reach);
            int hue = (int)(xn * (PaletteSize - 1));
            float glow = (.24f + (.40f * band) + (.28f * pulse)) * ShapeAura;
            _auraR[x] = _palR[hue] * glow;
            _auraG[x] = _palG[hue] * glow;
            _auraB[x] = _palB[hue] * glow;
        }

        // neighbouring bands can differ a lot; smooth the ridge into rolling light
        int radius = Math.Max(2, w / 70);
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(_auraReach, _auraScratch, radius);
            Array.Copy(_auraScratch, _auraReach, w);
            BoxBlur(_auraR, _auraScratch, radius);
            Array.Copy(_auraScratch, _auraR, w);
            BoxBlur(_auraG, _auraScratch, radius);
            Array.Copy(_auraScratch, _auraG, w);
            BoxBlur(_auraB, _auraScratch, radius);
            Array.Copy(_auraScratch, _auraB, w);
        }

        for (int x = 0; x < w; x++)
        {
            int o = x * 4;
            _auraData[o] = _auraR[x];
            _auraData[o + 1] = _auraG[x];
            _auraData[o + 2] = _auraB[x];
            _auraData[o + 3] = _auraReach[x];
        }
    }

    // Blurs src into dst along x, by a running sum, clamping at the ends.
    private static void BoxBlur(float[] src, float[] dst, int radius)
    {
        int n = src.Length;
        float sum = 0, inv = 1f / ((radius * 2) + 1);
        for (int k = -radius; k <= radius; k++) sum += src[Math.Clamp(k, 0, n - 1)];
        for (int x = 0; x < n; x++)
        {
            dst[x] = sum * inv;
            sum += src[Math.Min(n - 1, x + radius + 1)] - src[Math.Max(0, x - radius)];
        }
    }

    // ---- shaders and states -----------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Stardust", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

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

    private const string FeedbackPs = """
        cbuffer Frame : register(b0)
        {
            float2 Size;
            float2 Texel;
            float Lift;
            float Decay;
            float Fade;
            float Pad;
            float MilkOn;
            float Ppd;
            float Dt;
            float MilkT;
            float Zoom;
            float Swirl;
            float Kx;
            float Ky;
            float2 Centre;
        };

        Texture2D Dust : register(t0);
        SamplerState LinearClamp : register(s0);

        // StardustGpuScene.Field, in DIP per second
        float2 Field(float2 q)
        {
            float2 v = Zoom * (q - Centre) * float2(0.25, 1.0);
            float ax = Kx * q.x + MilkT * 0.7, ay = Ky * q.y - MilkT * 0.5;
            float bx = Kx * 2.3 * q.x - MilkT * 1.1, by = Ky * 1.7 * q.y + MilkT * 0.8;
            v.x += Swirl * (sin(ax) * cos(ay) - 0.5 * sin(bx) * cos(by));
            v.y += Swirl * (-(Kx / Ky) * cos(ax) * sin(ay) + 0.5 * (2.3 * Kx / (1.7 * Ky)) * cos(bx) * sin(by));
            return v;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            if (MilkOn > 0.5)
            {
                // MilkDrop: each pixel fetches what the flow carried here, softened a touch so the trails blur out
                float2 uv = (p - Field(p / Ppd) * Ppd * Dt) / Size;
                if (any(uv < 0) || any(uv > 1)) return 0;
                float4 c = Dust.Sample(LinearClamp, uv) * 0.6
                    + (Dust.Sample(LinearClamp, uv + float2(Texel.x, 0)) + Dust.Sample(LinearClamp, uv - float2(Texel.x, 0))
                    + Dust.Sample(LinearClamp, uv + float2(0, Texel.y)) + Dust.Sample(LinearClamp, uv - float2(0, Texel.y))) * 0.1;
                return c * Decay;
            }

            // sample from below: the picture slides up, exactly like the CPU blur's lift
            if (p.y + Lift >= Size.y - 0.5) return 0;
            float2 uv = (p + float2(0, Lift)) / Size;
            return Dust.Sample(LinearClamp, uv) * Decay;
        }
        """;

    private const string ParticleVs = """
        cbuffer Frame : register(b0)
        {
            float2 Size;
            float2 Texel;
            float Lift;
            float Decay;
            float Fade;
            float Pad;
        };

        struct Instance
        {
            float4 a : INSTANCE0;
            float4 b : INSTANCE1;
        };

        struct VSOut
        {
            float4 position : SV_Position;
            float2 local : TEXCOORD0;
            float3 color : COLOR0;
            float sigma : TEXCOORD1;
        };

        VSOut main(uint id : SV_VertexID, Instance instance)
        {
            // the two triangles of the quad, from the vertex id; the dust and wave looks were tuned with the
            // second one collapsed (half a quad), so only grains flagged in b.w get the whole sprite
            float2 corner = instance.b.w > .5
                ? float2((id == 1 || id == 3 || id == 4) ? 1 : -1, (id == 2 || id == 4 || id == 5) ? 1 : -1)
                : float2((id == 1 || id == 4 || id == 5) ? 1 : -1, (id < 2) ? -1 : 1);
            float2 p = instance.a.xy + (corner * instance.a.z);
            VSOut o;
            o.position = float4((p.x / Size.x * 2) - 1, 1 - (p.y / Size.y * 2), 0, 1);
            o.local = corner * instance.a.z;
            o.color = instance.b.rgb;
            o.sigma = instance.a.w;
            return o;
        }
        """;

    private const string ParticlePs = """
        struct VSOut
        {
            float4 position : SV_Position;
            float2 local : TEXCOORD0;
            float3 color : COLOR0;
            float sigma : TEXCOORD1;
        };

        float4 main(VSOut i) : SV_Target
        {
            float v = exp(-dot(i.local, i.local) / (2 * i.sigma * i.sigma));
            return float4(i.color * v, 0);
        }
        """;

    private const string CompositePs = """
        cbuffer Frame : register(b0)
        {
            float2 Size;
            float2 Texel;
            float Lift;
            float Decay;
            float Fade;
            float Pad;
        };

        Texture2D Dust : register(t1);
        Texture2D Aura : register(t2);
        SamplerState PointClamp : register(s1);

        float Knee(float v)
        {
            return v <= 0 ? 0 : (v < .7 ? v : min(1, .7 + (.3 * (1 - exp(-(v - .7) * 3)))));
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 p = position.xy;
            float2 uv = p / Size;
            // the aurora is one column per pixel, so point sampling keeps the CPU values exact
            float4 aura = Aura.SampleLevel(PointClamp, float2(uv.x, .5), 0);
            float3 dust = Knee(Dust.SampleLevel(PointClamp, uv, 0).rgb);

            // a soft column of light, brightest on the edge and fading smoothly upwards
            float above = Size.y - p.y;
            float u = above / max(aura.a, 1);
            float glow = u < 3 ? exp(-u * u * 1.6) : 0;
            // the dust sits in front of the aurora: where a grain is, the light behind it is covered
            float cover = 1 - max(dust.r, max(dust.g, dust.b));
            // everything thins out towards the top, so nothing ends on the strip's edge
            float top = smoothstep(0, 1, min(1, p.y / (Size.y * .45))) * Fade;
            float3 rgb = (dust + (aura.rgb * glow * cover)) * top;
            float alpha = min(1, max(rgb.r, max(rgb.g, rgb.b)));
            return float4(rgb, alpha);
        }
        """;
}
