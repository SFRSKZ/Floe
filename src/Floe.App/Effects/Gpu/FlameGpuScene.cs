using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The eye flame visualizer: Sauron's fire, black, red and yellow, running out from under the dock to both sides.
/// <para>
/// It is one feedback field of heat, not a simulation: every frame the previous field is read a step closer to the
/// middle and lower down, so the fire travels outward and up; the bass feeds it under the dock and each band along
/// the edge. A second pass tears it into tongues and maps heat to the fire's colours, premultiplied.
/// </para>
/// </summary>
public sealed class FlameGpuScene : IGpuScene
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
        _cb[6] = MathF.Pow(.935f, dt60);
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
        Compiler.Compile(source, "main", "Flame", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

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
            float side = uv.x < .5 ? -1 : 1;
            float2 q = uv * float2(9, 4);

            // the fire runs out from under the dock to both sides and licks upward; a beat throws it faster
            float2 turb = float2(fbm(q + float2(0, -Time * 2.2)) - .5, fbm(q + float2(7, -Time * 1.7)) - .5) * .012;
            float2 push = float2(side * (.003 + (.022 * Beat) + (.006 * Level)), .0035) + turb;
            float heat = Fetch(uv - (push * Dt60)).r * Decay - (.002 * Dt60);

            // fed under the dock by the bass, and along the bottom edge by each band
            float band = Bands.SampleLevel(LinearClamp, float2(abs((uv.x * 2) - 1), .5), 0).r;
            float x = (uv.x - .5) * aspect;
            float core = exp(-x * x * 1.4) * exp(-uv.y * 7) * (.25 + Level + (1.4 * Beat));
            float edge = band * band * exp(-uv.y * 16) * (.5 + fbm(float2(uv.x * aspect * 4, Time * 3)));
            heat += ((core * .05) + (edge * .03)) * Dt60;

            return float4(max(heat, 0), 0, 0, 1);
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

            // the flame's tongues: the heat field torn by fast rising noise
            float heat = (Fetch(uv).r * .7) + (Fetch(uv + float2(0, -.02)).r * .3);
            // the tongues drift outward with the fire, so the waves read as running along the dock
            float ax = abs(uv.x - .5) * aspect;
            float tongue = fbm(float2((ax * 2.6) - (Time * 1.6), (uv.y * 5) - (Time * 2.8)));
            tongue = saturate((tongue - .28) * 2.4);
            heat *= .15 + (1.5 * tongue * tongue);
            float t = 1 - exp(-heat * 1.7);

            // black, then blood red, orange, the yellow of the eye, and a white-hot core
            float3 c = lerp(float3(0, 0, 0), float3(.38, .02, 0), saturate(t / .3));
            c = lerp(c, float3(1, .22, 0), saturate((t - .3) / .25));
            c = lerp(c, float3(1, .72, .08), saturate((t - .55) / .25));
            c = lerp(c, float3(1, .97, .7), saturate((t - .82) / .18));

            // the cold rim stays as soot: dark but not see-through, so the fire reads black-edged on any wallpaper
            float alpha = saturate(t * 4.5) * (.8 + (.2 * saturate(t * 3)));
            alpha *= (1 - smoothstep(.5, 1.05, uv.y)) * Fade;
            return float4(c * alpha, alpha);
        }
        """;
}
