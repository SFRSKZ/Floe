using System.Windows.Media;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Color = System.Windows.Media.Color;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The MilkDrop loop for a full-screen look: one pass draws the new field from the last one into a float target,
/// a second pass shows that field. A scene brings the two pixel shaders (t0 is the field, s0 a linear clamp) and
/// the constants; this class owns the textures and the swap.
/// </summary>
public abstract class FeedbackGpuScene : IGpuScene
{
    private readonly string _feedbackSource, _showSource;
    private ID3D11Device? _device;
    private ID3D11VertexShader? _fullscreenVs;
    private ID3D11PixelShader? _feedbackPs, _showPs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private ID3D11Texture2D? _texture0, _texture1;
    private ID3D11RenderTargetView? _view0, _view1;
    private ID3D11ShaderResourceView? _read0, _read1;

    private readonly ID3D11Buffer[] _cb = new ID3D11Buffer[1];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _oneSrv = new ID3D11ShaderResourceView[1];
    private Viewport[] _viewports = Array.Empty<Viewport>();

    private readonly BeatTracker _beat = new();
    private int _width, _height;
    private bool _flip;
    private bool _clear = true;

    protected FeedbackGpuScene(int constantFloats, string feedbackSource, string showSource)
    {
        Data = new float[constantFloats];
        _feedbackSource = feedbackSource;
        _showSource = showSource;
    }

    /// <summary>The constants uploaded every frame; the scene fills them in <see cref="Fill"/>.</summary>
    protected float[] Data { get; }

    /// <summary>What the music did this frame.</summary>
    protected BeatTracker Beat => _beat;

    /// <summary>Moves the scene's own clock, once a frame, before <see cref="Fill"/>.</summary>
    protected abstract void Advance(GpuFrame frame);

    protected abstract void Fill(GpuFrame frame);

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        _fullscreenVs = device.CreateVertexShader(Compile("vs_5_0", FullscreenVs).Span, null);
        _feedbackPs = device.CreatePixelShader(Compile("ps_5_0", _feedbackSource).Span, null);
        _showPs = device.CreatePixelShader(Compile("ps_5_0", _showSource).Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)Data.Length * 4, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _cb[0] = _constants;
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;
    }

    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _width = width;
        _height = height;
        (_texture0, _view0, _read0) = MakeTarget(width, height);
        (_texture1, _view1, _read1) = MakeTarget(width, height);
        _viewports = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
        _clear = true;
    }

    private (ID3D11Texture2D, ID3D11RenderTargetView, ID3D11ShaderResourceView) MakeTarget(int width, int height)
    {
        var texture = _device!.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        return (texture, _device.CreateRenderTargetView(texture, null), _device.CreateShaderResourceView(texture, null));
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _feedbackPs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_view0 is null || _view1 is null) return;

        _beat.Update(frame.Audio, frame.Dt);
        Advance(frame);
        Fill(frame);
        context.UpdateSubresource(Data, _constants!);

        var last = _flip ? _read1! : _read0!;
        var next = _flip ? _view0 : _view1;
        var shown = _flip ? _read0! : _read1!;
        _flip = !_flip;

        if (_clear)
        {
            context.ClearRenderTargetView(_view0, new Color4(0, 0, 0, 0));
            context.ClearRenderTargetView(_view1, new Color4(0, 0, 0, 0));
            _clear = false;
        }

        context.RSSetViewports(_viewports);
        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.OMSetBlendState(_opaque);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_fullscreenVs);
        context.PSSetConstantBuffers(0, _cb);
        context.PSSetSamplers(0, _sampler);

        // a) the last frame, warped and eaten, with the field on top
        context.OMSetRenderTargets(next, null);
        context.PSSetShader(_feedbackPs);
        _oneSrv[0] = last;
        context.PSSetShaderResources(0, _oneSrv);
        context.Draw(3, 0);

        // b) that result as the picture
        context.OMSetRenderTargets(target, null);
        context.PSSetShader(_showPs);
        _oneSrv[0] = shown;
        context.PSSetShaderResources(0, _oneSrv);
        context.Draw(3, 0);

        _oneSrv[0] = null!;
        context.PSSetShaderResources(0, _oneSrv);
    }

    protected void Put(int at, Color color)
    {
        Data[at] = color.R / 255f;
        Data[at + 1] = color.G / 255f;
        Data[at + 2] = color.B / 255f;
        Data[at + 3] = 1;
    }

    protected void PutBands(int at, GpuFrame frame)
    {
        var bands = frame.Audio.Bands;
        for (int i = 0; i < 24; i++) Data[at + i] = Math.Clamp(bands[i * bands.Length / 24], 0, 1);
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _fullscreenVs?.Dispose(); _fullscreenVs = null;
        _feedbackPs?.Dispose(); _feedbackPs = null;
        _showPs?.Dispose(); _showPs = null;
        _constants?.Dispose(); _constants = null;
        _cb[0] = null!;
        _opaque?.Dispose(); _opaque = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _sampler[0] = null!;
        _device = null;
    }

    private void ReleaseSized()
    {
        _read0?.Dispose(); _read1?.Dispose();
        _view0?.Dispose(); _view1?.Dispose();
        _texture0?.Dispose(); _texture1?.Dispose();
        _read0 = _read1 = null;
        _view0 = _view1 = null;
        _texture0 = _texture1 = null;
        _viewports = Array.Empty<Viewport>();
        _width = _height = 0;
    }

    public void Dispose() => ReleaseDevice();

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Feedback", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    /// <summary>
    /// The analyser's onsets are one-frame values that a quiet or a dense song keeps far below 1, so a beat here
    /// is any onset, or the low end climbing away from its own recent average.
    /// </summary>
    protected sealed class BeatTracker
    {
        private float _slow, _slowTreble, _last, _lastKick, _lastSnare, _gap;

        /// <summary>True on the one frame a beat lands.</summary>
        public bool Hit { get; private set; }

        /// <summary>True when that beat was a snare rather than the low end.</summary>
        public bool Snare { get; private set; }

        /// <summary>1 on a beat, falling away over a quarter second.</summary>
        public float Pulse { get; private set; }

        /// <summary>0..1: how far the low end stands above its recent average, right now.</summary>
        public float Swell { get; private set; }

        /// <summary>0..1: the same for the top of the spectrum.</summary>
        public float Sizzle { get; private set; }

        public void Update(Floe.Core.Interop.SpectrumFrame audio, float dt)
        {
            float energy = (audio.Bass * .7f) + (audio.Mid * .3f);
            float settle = 1 - MathF.Exp(-dt * 3);
            _slow += (energy - _slow) * settle;
            _slowTreble += (audio.Treble - _slowTreble) * settle;
            float rise = energy - _slow;
            Swell = Math.Clamp(rise * 3.5f, 0, 1);
            Sizzle = Math.Clamp((audio.Treble - _slowTreble) * 5, 0, 1);

            bool kick = audio.Kick > .1f && _lastKick <= .1f;
            Snare = audio.Snare > .1f && _lastSnare <= .1f;
            bool climb = rise > .1f && energy - _last > .03f;
            _lastKick = audio.Kick;
            _lastSnare = audio.Snare;
            _last = energy;

            _gap += dt;
            Hit = (kick || Snare || climb) && _gap > .15f;
            if (Hit) _gap = 0;
            Pulse = Hit ? 1 : Pulse * MathF.Exp(-dt * 9);
        }
    }

    private const string FullscreenVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    /// <summary>The noise and the band lookup both looks use; Bands is the scene's own constant.</summary>
    protected const string ShaderHelpers = """
        Texture2D Last : register(t0);
        SamplerState Linear : register(s0);

        float Hash(float2 p)
        {
            float3 q = frac(p.xyx * .1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3 - (2 * f));
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                        lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }

        float Band(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = i + 1;
            return lerp(Bands[i / 4][i % 4], Bands[j / 4][j % 4], f - i);
        }

        """;
}
