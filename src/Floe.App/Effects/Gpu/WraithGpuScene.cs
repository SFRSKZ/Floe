using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The wraith wind visualizer: the pale grey-green fog of the ring world, rising from the bottom of the strip.
/// <para>
/// It is one feedback field, not a simulation: every frame the previous float texture is read at a point pulled
/// toward the bottom middle (the wind noise and the beat widen that pull), dimmed a little, and the fog source is
/// added along the bottom where the spectrum is loud. A second pass blurs and tone-maps the field and writes the
/// premultiplied strip, so the fog is a transparent layer over the desktop.
/// </para>
/// </summary>
public sealed class WraithGpuScene : IGpuScene
{
    private const int Bands = 24;
    private const int PaletteSize = 16;

    // the spectrum, smoothed on the CPU; the shader only sees the result
    private readonly float[] _bands = new float[Bands];
    private float _beat, _level;

    private readonly float[] _bandData = new float[Bands];
    private readonly float[] _paletteData = new float[PaletteSize * 4];

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _feedbackPs, _displayPs;
    private ID3D11Buffer? _constants;
    private ID3D11BlendState? _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private ID3D11Texture2D? _feedback0, _feedback1, _bandTexture, _paletteTexture;
    private ID3D11RenderTargetView? _feedbackRtv0, _feedbackRtv1;
    private ID3D11ShaderResourceView? _feedbackSrv0, _feedbackSrv1, _bandSrv, _paletteSrv;
    private Viewport[] _viewports = Array.Empty<Viewport>();

    // bound state, kept in arrays so a frame does not allocate
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11ShaderResourceView[] _feedbackInput = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _sourceInputs = new ID3D11ShaderResourceView[2];
    private readonly ID3D11SamplerState[] _linearSampler = new ID3D11SamplerState[1];
    private readonly float[] _cb = new float[8];

    private int _ping;
    private bool _needClear;
    private int _pixelW, _pixelH;

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        var vs = Compile("vs_4_0", FullscreenVs);
        _vertexShader = device.CreateVertexShader(vs.Span, null);
        _feedbackPs = device.CreatePixelShader(Compile("ps_4_0", FrameConstants + FeedbackPs).Span, null);
        _displayPs = device.CreatePixelShader(Compile("ps_4_0", FrameConstants + DisplayPs).Span, null);

        _constants = device.CreateBuffer(new BufferDescription(32, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _linearSampler[0] = _linear;

        // one texel per band, low bands in the middle once the shader mirrors the lookup
        _bandTexture = device.CreateTexture2D(new Texture2DDescription(Format.R32_Float, Bands, 1, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _bandSrv = device.CreateShaderResourceView(_bandTexture, null);
        _paletteTexture = device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float, PaletteSize, 1, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _paletteSrv = device.CreateShaderResourceView(_paletteTexture, null);
    }

    /// <summary>Textures are size-dependent: both feedback buffers start empty again.</summary>
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

        _viewports = new[] { new Viewport { Width = width, Height = height, MinDepth = 0, MaxDepth = 1 } };
        _needClear = true;
    }

    /// <summary>Drops every GPU object, keeping the smoothed spectrum so a new device carries on where it was.</summary>
    public void ReleaseDevice()
    {
        ReleaseSized();
        _bandSrv?.Dispose(); _paletteSrv?.Dispose();
        _bandSrv = _paletteSrv = null;
        _bandTexture?.Dispose(); _paletteTexture?.Dispose();
        _bandTexture = _paletteTexture = null;
        _vertexShader?.Dispose(); _vertexShader = null;
        _feedbackPs?.Dispose(); _displayPs?.Dispose();
        _feedbackPs = _displayPs = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose();
        _opaque = null; _raster = null; _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void ReleaseSized()
    {
        _feedbackRtv0?.Dispose(); _feedbackRtv1?.Dispose();
        _feedbackSrv0?.Dispose(); _feedbackSrv1?.Dispose();
        _feedbackRtv0 = _feedbackRtv1 = null;
        _feedbackSrv0 = _feedbackSrv1 = null;
        _feedback0?.Dispose(); _feedback1?.Dispose();
        _feedback0 = _feedback1 = null;
        _viewports = Array.Empty<Viewport>();
        _pixelW = _pixelH = 0;
    }

    private ID3D11Texture2D CreateFeedback(int width, int height) =>
        _device!.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _feedbackPs is null || _displayPs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _pixelW || frame.Height != _pixelH) Resize(frame.Width, frame.Height);
        if (_feedbackRtv0 is null || _feedbackRtv1 is null || _bandTexture is null || _paletteTexture is null) return;

        // the reference ran at 60 fps; everything per frame is scaled by how long this one really lasted
        var dt = Math.Clamp(frame.Dt, .001f, .1f);
        float dt60 = dt * 60;
        UpdateAudio(dt, frame.Audio);

        for (int i = 0; i < PaletteSize; i++)
        {
            var c = frame.Palette[i];
            int o = i * 4;
            _paletteData[o] = c.X;
            _paletteData[o + 1] = c.Y;
            _paletteData[o + 2] = c.Z;
            _paletteData[o + 3] = 1;
        }

        context.UpdateSubresource(new ReadOnlySpan<float>(_bandData), _bandTexture, 0, (uint)(Bands * sizeof(float)));
        context.UpdateSubresource(new ReadOnlySpan<float>(_paletteData), _paletteTexture, 0, (uint)(PaletteSize * 4 * sizeof(float)));

        if (_needClear)
        {
            var zero = new Color4(0, 0, 0, 0);
            context.ClearRenderTargetView(_feedbackRtv0, zero);
            context.ClearRenderTargetView(_feedbackRtv1, zero);
            _needClear = false;
        }

        _cb[0] = frame.Width;
        _cb[1] = frame.Height;
        _cb[2] = frame.Time;
        _cb[3] = _beat;
        _cb[4] = _level;
        _cb[5] = dt60;
        _cb[6] = MathF.Pow(.95f, dt60);
        _cb[7] = Math.Clamp(frame.Fade, 0, 1);
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
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetSamplers(0, _linearSampler);

        // a) the last frame, pulled in and dimmed, plus this frame's fog along the bottom
        context.OMSetRenderTargets(destRtv, null);
        context.OMSetBlendState(_opaque);
        context.PSSetShader(_feedbackPs);
        _feedbackInput[0] = sourceSrv;
        context.PSSetShaderResources(0, _feedbackInput);
        _sourceInputs[0] = _bandSrv!;
        _sourceInputs[1] = _paletteSrv!;
        context.PSSetShaderResources(1, _sourceInputs);
        context.Draw(3, 0);

        // b) the fog and its bloom onto the strip: premultiplied, no background
        context.OMSetRenderTargets(target, null);
        context.PSSetShader(_displayPs);
        _feedbackInput[0] = destSrv;
        context.PSSetShaderResources(0, _feedbackInput);
        context.Draw(3, 0);

        _ping = dest;
    }

    // ---- audio ------------------------------------------------------------------------------------

    private void UpdateAudio(float dt, SpectrumFrame audio)
    {
        var source = audio.Bands;
        float smooth = 1 - MathF.Exp(-dt * 12);
        for (int i = 0; i < Bands; i++)
        {
            float target = i < source.Length ? Math.Clamp(source[i], 0, 1) : 0;
            _bands[i] += (target - _bands[i]) * smooth;
            _bandData[i] = _bands[i];
        }

        // the beat is the kick onset pulsing out, never a jump down
        _beat = MathF.Max(_beat * MathF.Exp(-dt * 5), Math.Clamp(audio.Kick, 0, 1));
        _level += (Math.Clamp(audio.Level, 0, 1) - _level) * (1 - MathF.Exp(-dt * 3));
    }

    // ---- shaders ----------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", "Wraith", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

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

    // Shared by both passes: the uniforms and the reference's noise helpers, in its y-up unit square.
    private const string FrameConstants = """
        cbuffer Frame : register(b0)
        {
            float2 Size;
            float Time;
            float Beat;
            float Level;
            float Dt60;
            float Decay;
            float Fade;
        };

        float h(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float n(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3 - (2 * f));
            float a = h(i);
            float b = h(i + float2(1, 0));
            float c = h(i + float2(0, 1));
            float d = h(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float fbm(float2 p)
        {
            float sum = 0, amp = .5;
            for (int k = 0; k < 4; k++)
            {
                sum += amp * n(p);
                p = (p * 2.03) + float2(1.7, 9.2);
                amp *= .5;
            }
            return sum;
        }

        float lu(float3 c)
        {
            return dot(c, float3(.3, .59, .11));
        }
        """;

    private const string FeedbackPs = """
        Texture2D Prev : register(t0);
        Texture2D Bands : register(t1);
        Texture2D Palette : register(t2);
        SamplerState LinearClamp : register(s0);

        float3 Fetch(float2 uv)
        {
            // the reference's uv is y-up; the texture is stored the D3D way, y down
            return Prev.SampleLevel(LinearClamp, float2(uv.x, 1 - uv.y), 0).rgb;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = float2(position.x / Size.x, 1 - (position.y / Size.y));
            float aspect = Size.x / Size.y;
            float2 d = uv - float2(.5, 0);
            float2 q = uv * float2(5, 2.5);

            // the wind, and the suck toward the bottom middle that a beat widens
            float2 wind = float2(fbm(q + float2(Time * .5, 0)) - .5, fbm(q + float2(9, Time * .4)) - .5) * (.007 + (.02 * Level));
            float2 push = (d * (.004 + (.035 * Beat))) + wind + float2(0, -.005);
            float3 pc = Fetch(uv + (push * Dt60));

            // it fades and loses its colour as it goes
            pc = lerp(pc, lu(pc).xxx, .05 * Dt60) * Decay - (.0015 * Dt60);

            // the source: the fog hills the bands make along the bottom, the low bands in the middle
            float band = Bands.SampleLevel(LinearClamp, float2(abs((uv.x * 2) - 1), .5), 0).r;
            float s = fbm(float2(uv.x * aspect * 1.4, (uv.y * 3) - (Time * 1.3)));
            float hill = .08 + (.6 * band);
            float3 tint = Palette.Load(int3(min(15, (int)(uv.x * 15)), 0, 0)).rgb;
            float3 base = lerp(float3(.75, .88, .82), tint, .35);
            // torn into wisps, so the fog reads as threads of smoke and not a blanket
            float wisp = fbm(float2(uv.x * aspect * 3, (uv.y * 6) - (Time * 2)));
            wisp = saturate((wisp - .35) * 2.2);
            float3 src = base * smoothstep(0, .3, hill - uv.y + (s * .4) - .18) * .065 * (.3 + band) * (.15 + (wisp * wisp * 1.6));

            return float4(max(pc, 0) + (src * Dt60), 1);
        }
        """;

    private const string DisplayPs = """
        Texture2D Fog : register(t0);
        SamplerState LinearClamp : register(s0);

        float3 Fetch(float2 uv)
        {
            return Fog.SampleLevel(LinearClamp, float2(uv.x, 1 - uv.y), 0).rgb;
        }

        float4 main(float4 position : SV_Position) : SV_Target
        {
            float2 uv = float2(position.x / Size.x, 1 - (position.y / Size.y));
            float aspect = Size.x / Size.y;

            // the fog, a wide bloom of the same field, and a copy a touch up and right for a soft streak
            float3 f = Fetch(uv);
            float3 bl = 0;
            for (int i = 0; i < 10; i++)
            {
                float a = i * .628;
                bl += Fetch(uv + (float2(cos(a) / aspect, sin(a)) * .045));
            }
            bl /= 10;
            float3 g = Fetch(uv + float2(.012 * Beat, .004));

            float3 c = (f * .75) + (bl * .55) + (g * .25);
            c = lerp(c, lu(c) * float3(.78, .92, .86), .55);
            c = 1 - exp(-c * 2.2);

            // grain only where there is light, so the empty air stays clean
            float light = max(c.r, max(c.g, c.b));
            c += (h((uv * Size) + (frac(Time * 7) * 100)) - .5) * .045 * light;

            // thin out toward the top, then the host's fade; premultiplied over the desktop
            c *= (1 - smoothstep(.45, 1.08, uv.y)) * Fade;
            float alpha = min(1, max(c.r, max(c.g, c.b)));
            return float4(c, alpha);
        }
        """;
}
