using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu.Desktop;

/// <summary>
/// The pipeline the desktop scenes share: thousands of instanced quads summed into a float target, a light glow
/// at a quarter and a sixteenth of the size, then the scene's own tone map onto the screen. A scene supplies the
/// shaders and fills its part of the constants; the base fills the music and the camera.
/// <para>
/// Constants, in floats: 0 size.xy, 2 dip, 3 fade, 4 time, 5 level, 6 bass, 7 kick, 8 treble, 9 snare, 10 hat,
/// 11 exposure, 12..15 the scene's; 16..39 the 24 bands; 40..55 the view-projection as rows; 56..71 the scene's.
/// </para>
/// </summary>
public abstract class DesktopPointScene : IGpuScene
{
    protected const int ConstantFloats = 72;

    protected readonly float[] Data = new float[ConstantFloats];
    private readonly float[] _pass = new float[8];
    private readonly ID3D11Buffer[] _cb = new ID3D11Buffer[2];
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _one = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _three = new ID3D11ShaderResourceView[3];
    private readonly ID3D11ShaderResourceView[] _none = new ID3D11ShaderResourceView[3];

    private ID3D11Device? _device;
    private ID3D11VertexShader? _dotVs, _fullVs;
    private ID3D11PixelShader? _dotPs, _downPs, _blurPs, _finalPs;
    private ID3D11Buffer? _constants, _passConstants;
    private ID3D11BlendState? _additive, _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private Target? _hdr, _q1, _q2, _e1, _e2;
    private int _width, _height;
    private bool _started;

    /// <summary>Seconds since the scene started, from the host's clock on the first frame.</summary>
    protected float Clock { get; private set; }

    /// <summary>The number of quads drawn each frame.</summary>
    protected abstract int Instances { get; }

    /// <summary>The quads' vertex shader, after <see cref="Header"/> and <see cref="Noise"/>.</summary>
    protected abstract string VertexShader { get; }

    /// <summary>The quads' pixel shader, after <see cref="Header"/>.</summary>
    protected abstract string PixelShader { get; }

    /// <summary>The tone map, after <see cref="Header"/>; it reads t0 (the sum), t1 and t2 (the glows).</summary>
    protected abstract string FinalShader { get; }

    /// <summary>Moves the scene's own state on by a frame and fills its constants (12..15, 40..71).</summary>
    protected abstract void Advance(GpuFrame frame, float dt);

    /// <summary>
    /// Points a scene lays out on the CPU once (four floats each), handed to its vertex shader as
    /// <c>StructuredBuffer&lt;float4&gt; Points : register(t0)</c>; null when it has none.
    /// </summary>
    protected virtual float[]? Points => null;

    /// <summary>
    /// True when every quad is a square dot: the vertex shader then runs once per dot (compiled with POINT_EXPAND,
    /// where <c>Corners</c> are all zero and it writes the dot's half size into <c>Half</c>) and a geometry shader
    /// spreads it into its quad, instead of the whole dot being worked out again for each of six corners.
    /// </summary>
    protected virtual bool ExpandPoints => false;

    /// <summary>The geometry shader that spreads the dots, after <see cref="Header"/>; a scene may draw more than one quad per run.</summary>
    protected virtual string ExpandShader => ExpandGs;

    private ID3D11GeometryShader? _dotGs;

    private ID3D11Buffer? _points;
    private readonly ID3D11ShaderResourceView[] _pointViews = new ID3D11ShaderResourceView[1];
    private ID3D11ShaderResourceView? _pointsSrv;

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        string expand = ExpandPoints ? "#define POINT_EXPAND 1" + Environment.NewLine : "";
        _dotVs = device.CreateVertexShader(Compile("vs_5_0", expand + Header + Noise + VertexShader).Span, null);
        _dotPs = device.CreatePixelShader(Compile("ps_5_0", expand + Header + PixelShader).Span, null);
        if (ExpandPoints) _dotGs = device.CreateGeometryShader(Compile("gs_5_0", expand + Header + ExpandShader).Span, null);
        _fullVs = device.CreateVertexShader(Compile("vs_5_0", FullVs).Span, null);
        _downPs = device.CreatePixelShader(Compile("ps_5_0", PassHeader + DownPs).Span, null);
        _blurPs = device.CreatePixelShader(Compile("ps_5_0", PassHeader + BlurPs).Span, null);
        _finalPs = device.CreatePixelShader(Compile("ps_5_0", Header + FinalShader).Span, null);
        _constants = device.CreateBuffer(new BufferDescription(ConstantFloats * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _passConstants = device.CreateBuffer(new BufferDescription((uint)_pass.Length * 4, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _cb[0] = _constants;
        _cb[1] = _passConstants;

        _additive = device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;

        if (Points is { Length: >= 4 } points)
        {
            int count = points.Length / 4;
            // structured buffers need the misc flag and the stride, or every view of them is rejected
            _points = device.CreateBuffer(points.AsSpan(0, count * 4),
                new BufferDescription((uint)(count * 16), BindFlags.ShaderResource, ResourceUsage.Immutable, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
            _pointsSrv = device.CreateShaderResourceView(_points, new ShaderResourceViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Buffer = new BufferShaderResourceView { FirstElement = 0, NumElements = (uint)count },
            });
            _pointViews[0] = _pointsSrv;
        }
    }

    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _width = width;
        _height = height;
        _hdr = new Target(_device, width, height);
        int qw = Math.Max(1, width / 4), qh = Math.Max(1, height / 4);
        int ew = Math.Max(1, width / 16), eh = Math.Max(1, height / 16);
        _q1 = new Target(_device, qw, qh);
        _q2 = new Target(_device, qw, qh);
        _e1 = new Target(_device, ew, eh);
        _e2 = new Target(_device, ew, eh);
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _dotVs is null || _dotPs is null || _fullVs is null || _downPs is null || _blurPs is null || _finalPs is null) return;
        if (frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_hdr is null || _q1 is null || _q2 is null || _e1 is null || _e2 is null) return;

        float dt = Math.Clamp(frame.Dt, 0f, .1f);
        if (!_started)
        {
            // start where the host's clock is, so an offline render can pick its moment by its start time
            _started = true;
            Clock = frame.Time;
        }
        else
        {
            Clock += dt;
        }

        FillShared(frame);
        Advance(frame, dt);
        context.UpdateSubresource(Data, _constants!);

        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetConstantBuffers(0, _cb);
        context.PSSetConstantBuffers(0, _cb);
        context.PSSetSamplers(0, _sampler);

        context.PSSetShaderResources(0, _none);
        context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
        context.ClearRenderTargetView(_hdr.Rtv, new Color4(0, 0, 0, 0));
        context.OMSetRenderTargets(_hdr.Rtv, null);
        context.OMSetBlendState(_additive);
        context.VSSetShader(_dotVs);
        if (_pointsSrv is not null) context.VSSetShaderResources(0, _pointViews);
        context.PSSetShader(_dotPs);
        if (_dotGs is not null)
        {
            context.IASetPrimitiveTopology(PrimitiveTopology.PointList);
            context.GSSetShader(_dotGs);
            context.DrawInstanced(1, (uint)Instances, 0, 0);
            context.GSSetShader(null);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        }
        else
        {
            context.DrawInstanced(6, (uint)Instances, 0, 0);
        }

        context.OMSetBlendState(_opaque);
        context.VSSetShader(_fullVs);
        Pass(context, _downPs, _hdr, _q1, 0, 0);
        Pass(context, _blurPs, _q1, _q2, 1, 0);
        Pass(context, _blurPs, _q2, _q1, 0, 1);
        Pass(context, _downPs, _q1, _e1, 0, 0);
        Pass(context, _blurPs, _e1, _e2, 1, 0);
        Pass(context, _blurPs, _e2, _e1, 0, 1);

        context.OMSetRenderTargets(target, null);
        context.RSSetViewport(new Viewport(0, 0, _width, _height, 0, 1));
        context.PSSetShader(_finalPs);
        _three[0] = _hdr.Srv;
        _three[1] = _q1.Srv;
        _three[2] = _e1.Srv;
        context.PSSetShaderResources(0, _three);
        context.Draw(3, 0);
        context.PSSetShaderResources(0, _none);
    }

    private void FillShared(GpuFrame frame)
    {
        var d = Data;
        var a = frame.Audio;
        d[0] = frame.Width;
        d[1] = frame.Height;
        d[2] = Math.Max(.5f, frame.PixelsPerDip);
        d[3] = Math.Clamp(frame.Fade, 0, 1);
        d[4] = Clock;
        d[5] = Math.Clamp(a.Level, 0, 1);
        d[6] = Math.Clamp(a.Bass, 0, 1);
        d[7] = Math.Clamp(a.Kick, 0, 1);
        d[8] = Math.Clamp(Math.Max(a.Treble, a.Hat), 0, 1);
        d[9] = Math.Clamp(a.Snare, 0, 1);
        d[10] = Math.Clamp(a.Hat, 0, 1);
        d[11] = 8;
        var bands = a.Bands;
        for (int i = 0; i < 24; i++) d[16 + i] = bands.Length == 0 ? 0 : Math.Clamp(bands[i * bands.Length / 24], 0, 1);
    }

    /// <summary>Writes a camera at <paramref name="eye"/> looking at <paramref name="centre"/> into 40..55.</summary>
    protected void SetCamera(GpuFrame frame, float[] eye, float[] centre, float fovDegrees)
    {
        float aspect = frame.Width / (float)Math.Max(1, frame.Height);
        float fov = fovDegrees * MathF.PI / 180 * (aspect > 1 ? .78f : 1);
        var vp = Multiply(Perspective(fov, aspect, .05f, 60), LookAt(eye, centre));
        // the column-major GL matrix as four rows, so the shader can dot them
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++) Data[40 + (row * 4) + col] = vp[(col * 4) + row];
        }
    }

    protected static float Smooth(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - (2 * x));
    }

    protected static float Mix(float a, float b, float t) => a + ((b - a) * t);

    protected static float[] Lerp(float[] a, float[] b, float t) => new[] { Mix(a[0], b[0], t), Mix(a[1], b[1], t), Mix(a[2], b[2], t) };

    private static float[] Perspective(float fov, float aspect, float near, float far)
    {
        float t = 1 / MathF.Tan(fov / 2);
        return new[] { t / aspect, 0, 0, 0, 0, t, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, 2 * far * near / (near - far), 0 };
    }

    private static float[] LookAt(float[] eye, float[] centre)
    {
        static float[] Norm(float[] a) { float l = MathF.Sqrt((a[0] * a[0]) + (a[1] * a[1]) + (a[2] * a[2])); return new[] { a[0] / l, a[1] / l, a[2] / l }; }
        static float[] Cross(float[] a, float[] b) => new[] { (a[1] * b[2]) - (a[2] * b[1]), (a[2] * b[0]) - (a[0] * b[2]), (a[0] * b[1]) - (a[1] * b[0]) };
        static float Dot(float[] a, float[] b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);
        var z = Norm(new[] { eye[0] - centre[0], eye[1] - centre[1], eye[2] - centre[2] });
        var x = Norm(Cross(new[] { 0f, 1f, 0f }, z));
        var y = Cross(z, x);
        return new[] { x[0], y[0], z[0], 0, x[1], y[1], z[1], 0, x[2], y[2], z[2], 0, -Dot(x, eye), -Dot(y, eye), -Dot(z, eye), 1 };
    }

    private static float[] Multiply(float[] a, float[] b)
    {
        var o = new float[16];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                for (int k = 0; k < 4; k++) o[(j * 4) + i] += a[(k * 4) + i] * b[(j * 4) + k];
            }
        }

        return o;
    }

    private void Pass(ID3D11DeviceContext context, ID3D11PixelShader shader, Target source, Target destination, float dirX, float dirY)
    {
        context.PSSetShaderResources(0, _none);
        _pass[0] = dirX / source.Width;
        _pass[1] = dirY / source.Height;
        _pass[4] = destination.Width;
        _pass[5] = destination.Height;
        context.UpdateSubresource(_pass, _passConstants!);
        context.OMSetRenderTargets(destination.Rtv, null);
        context.RSSetViewport(new Viewport(0, 0, destination.Width, destination.Height, 0, 1));
        context.PSSetShader(shader);
        _one[0] = source.Srv;
        context.PSSetShaderResources(0, _one);
        context.Draw(3, 0);
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _dotVs?.Dispose(); _dotVs = null;
        _dotPs?.Dispose(); _dotPs = null;
        _dotGs?.Dispose(); _dotGs = null;
        _fullVs?.Dispose(); _fullVs = null;
        _downPs?.Dispose(); _downPs = null;
        _blurPs?.Dispose(); _blurPs = null;
        _finalPs?.Dispose(); _finalPs = null;
        _constants?.Dispose(); _constants = null;
        _pointsSrv?.Dispose(); _pointsSrv = null;
        _points?.Dispose(); _points = null;
        _passConstants?.Dispose(); _passConstants = null;
        _cb[0] = null!;
        _cb[1] = null!;
        _additive?.Dispose(); _additive = null;
        _opaque?.Dispose(); _opaque = null;
        _raster?.Dispose(); _raster = null;
        _noDepth?.Dispose(); _noDepth = null;
        _linear?.Dispose(); _linear = null;
        _sampler[0] = null!;
        _device = null;
    }

    private void ReleaseSized()
    {
        _hdr?.Dispose(); _hdr = null;
        _q1?.Dispose(); _q1 = null;
        _q2?.Dispose(); _q2 = null;
        _e1?.Dispose(); _e1 = null;
        _e2?.Dispose(); _e2 = null;
        _width = _height = 0;
    }

    public void Dispose()
    {
        ReleaseDevice();
        GC.SuppressFinalize(this);
    }

    private ReadOnlyMemory<byte> Compile(string profile, string source) =>
        Compiler.Compile(source, "main", GetType().Name, profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private sealed class Target : IDisposable
    {
        public Target(ID3D11Device device, int width, int height)
        {
            Width = width;
            Height = height;
            Texture = device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default));
            Rtv = device.CreateRenderTargetView(Texture, null);
            Srv = device.CreateShaderResourceView(Texture, null);
        }

        public int Width { get; }
        public int Height { get; }
        public ID3D11Texture2D Texture { get; }
        public ID3D11RenderTargetView Rtv { get; }
        public ID3D11ShaderResourceView Srv { get; }

        public void Dispose()
        {
            Srv.Dispose();
            Rtv.Dispose();
            Texture.Dispose();
        }
    }

    protected const string Header = """
        cbuffer Scene : register(b0)
        {
            float2 Size;
            float Dip;
            float Fade;
            float Time;
            float Level;
            float Bass;
            float Kick;
            float Treble;
            float Snare;
            float Hat;
            float Expo;
            float4 Own0;
            float4 Bands[6];
            float4 VP0;
            float4 VP1;
            float4 VP2;
            float4 VP3;
            float4 Own1;
            float4 Own2;
            float4 Own3;
            float4 Own4;
        };

        struct DotOut
        {
            float4 Pos : SV_Position;
            float2 Uv : TEXCOORD0;
            float3 Color : COLOR0;
            float Sharp : TEXCOORD1;
        #ifdef POINT_EXPAND
            float2 Half : TEXCOORD2;
        #endif
        };

        #ifdef POINT_EXPAND
        static const float2 Corners[6] = { float2(0, 0), float2(0, 0), float2(0, 0), float2(0, 0), float2(0, 0), float2(0, 0) };
        #else
        static const float2 Corners[6] =
        {
            float2(-1, -1), float2(1, -1), float2(-1, 1),
            float2(-1, 1), float2(1, -1), float2(1, 1),
        };
        #endif

        float4 Project(float3 p) { float4 P = float4(p, 1.); return float4(dot(VP0, P), dot(VP1, P), dot(VP2, P), dot(VP3, P)); }

        """;

    protected const string Noise = """
        float Hash(float2 p)
        {
            float3 q = frac(p.xyx * .1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }

        float3 Mod289(float3 x) { return x - floor(x * (1. / 289.)) * 289.; }
        float4 Mod289(float4 x) { return x - floor(x * (1. / 289.)) * 289.; }
        float4 Perm(float4 x) { return Mod289(((x * 34.) + 1.) * x); }
        float4 Tis(float4 r) { return 1.79284291400159 - .85373472095314 * r; }

        // 3D simplex noise, about -1..1
        float Snoise(float3 v)
        {
            const float2 C = float2(1. / 6., 1. / 3.);
            const float4 D = float4(0., .5, 1., 2.);
            float3 i = floor(v + dot(v, C.yyy));
            float3 x0 = v - i + dot(i, C.xxx);
            float3 g = step(x0.yzx, x0.xyz);
            float3 l = 1. - g;
            float3 i1 = min(g.xyz, l.zxy);
            float3 i2 = max(g.xyz, l.zxy);
            float3 x1 = x0 - i1 + C.xxx;
            float3 x2 = x0 - i2 + C.yyy;
            float3 x3 = x0 - D.yyy;
            i = Mod289(i);
            float4 p = Perm(Perm(Perm(i.z + float4(0., i1.z, i2.z, 1.)) + i.y + float4(0., i1.y, i2.y, 1.)) + i.x + float4(0., i1.x, i2.x, 1.));
            float n_ = .142857142857;
            float3 ns = n_ * D.wyz - D.xzx;
            float4 j = p - 49. * floor(p * ns.z * ns.z);
            float4 x_ = floor(j * ns.z);
            float4 y_ = floor(j - 7. * x_);
            float4 x = x_ * ns.x + ns.yyyy;
            float4 y = y_ * ns.x + ns.yyyy;
            float4 h = 1. - abs(x) - abs(y);
            float4 b0 = float4(x.xy, y.xy);
            float4 b1 = float4(x.zw, y.zw);
            float4 s0 = floor(b0) * 2. + 1.;
            float4 s1 = floor(b1) * 2. + 1.;
            float4 sh = -step(h, 0.);
            float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
            float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
            float3 p0 = float3(a0.xy, h.x);
            float3 p1 = float3(a0.zw, h.y);
            float3 p2 = float3(a1.xy, h.z);
            float3 p3 = float3(a1.zw, h.w);
            float4 nm = Tis(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
            p0 *= nm.x; p1 *= nm.y; p2 *= nm.z; p3 *= nm.w;
            float4 m = max(.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.);
            m = m * m;
            return 42. * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
        }

        float Band(float u)
        {
            float f = saturate(u) * 22.99;
            int i = (int)f;
            int j = min(i + 1, 23);
            return lerp(Bands[i / 4][i % 4], Bands[j / 4][j % 4], f - i);
        }

        """;

    // one dot in, its quad out; a culled dot (behind the eye) or one with no light makes nothing
    private const string ExpandGs = """
        static const float2 Quad[4] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };

        [maxvertexcount(4)]
        void main(point DotOut dot[1], inout TriangleStream<DotOut> quad)
        {
            DotOut v = dot[0];
            if (v.Pos.z < 0. || max(v.Color.r, max(v.Color.g, v.Color.b)) < 1e-5) return;
            [unroll] for (int k = 0; k < 4; k++)
            {
                DotOut o = v;
                o.Pos.xy = v.Pos.xy + Quad[k] * v.Half;
                o.Uv = Quad[k];
                quad.Append(o);
            }
        }
        """;

    private const string FullVs = """
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
        }
        """;

    private const string PassHeader = """
        cbuffer PassInfo : register(b1)
        {
            float2 Dir;
            float2 Pad2;
            float2 Dst;
            float2 Pad3;
        };
        Texture2D Src : register(t0);
        SamplerState Linear : register(s0);

        """;

    private const string DownPs = """
        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Dst;
            float2 o = .25 / Dst;
            float3 c = Src.Sample(Linear, uv + float2(-o.x, -o.y)).rgb + Src.Sample(Linear, uv + float2(o.x, -o.y)).rgb
                     + Src.Sample(Linear, uv + float2(-o.x, o.y)).rgb + Src.Sample(Linear, uv + float2(o.x, o.y)).rgb;
            return float4(c * .25, 1.);
        }
        """;

    private const string BlurPs = """
        float4 main(float4 pos : SV_Position) : SV_Target
        {
            float2 uv = pos.xy / Dst;
            float3 c = 0.;
            float w = 0.;
            [unroll] for (int i = -8; i <= 8; i++)
            {
                float k = exp(-(float)(i * i) / 22.);
                c += Src.Sample(Linear, uv + Dir * i).rgb * k;
                w += k;
            }
            return float4(c / w, 1.);
        }
        """;
}
