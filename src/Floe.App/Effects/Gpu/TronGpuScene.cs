using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WpfPoint = System.Windows.Point;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Tron's cube mound in triangles. <see cref="Windows.TronCubes"/> still runs the mound and the scatter on the dock's
/// frame and projects every cube exactly as before; instead of filling WPF geometry (which the render thread had to
/// tessellate, thousands of small figures a frame) it writes the same quads, hairlines and outline rings here, in
/// the same ink ladders. Sharp ink first (sides, fronts, lids, edges, glints, flashes), then the bloom layer
/// (bloom outlines and flashes), which the GPU scene blurs.
/// </summary>
internal sealed class TronMesh
{
    public const int Grades = 8;
    public const int Side = 0, Front = 1, Top = 2, Edges = 3, Glint = 4, Flash = 5, Bloom = 6;
    private const int Sets = 7;

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector2 Position;
        public uint Color; // straight RGBA, a byte each: the ladders' alpha is a byte anyway
    }

    private readonly List<Vertex>[] _layers = new List<Vertex>[Sets * Grades];
    private readonly uint[] _colors = new uint[Sets * Grades];
    private readonly WpfPoint[] _outer = new WpfPoint[8];
    private readonly WpfPoint[] _inner = new WpfPoint[8];

    public TronMesh()
    {
        for (int i = 0; i < _layers.Length; i++) _layers[i] = new List<Vertex>(64);
    }

    /// <summary>The ink of one set and grade: straight rgb, its stepped alpha in W.</summary>
    public void SetInk(int set, Vector3 rgb, double max)
    {
        for (int g = 0; g < Grades; g++)
        {
            uint alpha = (byte)(255 * max * g / (Grades - 1.0));
            _colors[(set * Grades) + g] = (uint)Math.Round(rgb.X * 255) | ((uint)Math.Round(rgb.Y * 255) << 8) | ((uint)Math.Round(rgb.Z * 255) << 16) | (alpha << 24);
        }
    }

    public void Begin()
    {
        foreach (var layer in _layers) layer.Clear();
    }

    public void Quad(int set, int grade, WpfPoint[] q) => Quad(set, grade, q[0], q[1], q[2], q[3]);

    /// <summary>DerezzField.Batch.Segment: a straight line half wide either side.</summary>
    public void Segment(int set, int grade, WpfPoint from, WpfPoint to, double half)
    {
        var d = to - from;
        if (d.LengthSquared < 1e-6) return;
        d.Normalize();
        var n = new System.Windows.Vector(d.Y, -d.X) * half;
        Quad(set, grade, from + n, to + n, to - n, from - n);
    }

    /// <summary>DerezzField.Batch.Outline: a convex polygon's edges half wide either side, mitred, as a ring.</summary>
    public void Outline(int set, int grade, WpfPoint[] q, int count, double half)
    {
        if (count < 3) return;
        var centre = new WpfPoint();
        for (int c = 0; c < count; c++)
        {
            if ((q[(c + 1) % count] - q[c]).LengthSquared < 1e-6) return;
            centre.X += q[c].X / count;
            centre.Y += q[c].Y / count;
        }

        var first = Normal(q[0], q[1]);
        double side = System.Windows.Vector.Multiply(first, new WpfPoint((q[0].X + q[1].X) / 2, (q[0].Y + q[1].Y) / 2) - centre) < 0 ? -1 : 1;
        for (int c = 0; c < count; c++)
        {
            var prev = Normal(q[(c + count - 1) % count], q[c]) * side;
            var next = Normal(q[c], q[(c + 1) % count]) * side;
            double reach = half / Math.Max(.35, 1 + System.Windows.Vector.Multiply(prev, next));
            var miter = (prev + next) * reach;
            _outer[c] = q[c] + miter;
            _inner[c] = q[c] - miter;
        }

        for (int c = 0; c < count; c++)
        {
            int n = (c + 1) % count;
            Quad(set, grade, _outer[c], _outer[n], _inner[n], _inner[c]);
        }
    }

    /// <summary>
    /// Writes the frame in draw order into <paramref name="target"/> (grown as needed): the sharp ink, then the
    /// bloom layer. Returns the total; <paramref name="sharp"/> is where the bloom layer starts.
    /// </summary>
    public int Pack(ref Vertex[] target, out int sharp)
    {
        int total = 0;
        foreach (var layer in _layers) total += layer.Count;
        // the flashes go into both layers, as the WPF look fills them into both visuals
        for (int g = 1; g < Grades; g++) total += _layers[(Flash * Grades) + g].Count;
        if (target.Length < total) target = new Vertex[Math.Max(total, target.Length * 2)];
        int at = 0;
        foreach (var set in new[] { Side, Front, Top, Edges, Glint, Flash }) at = Copy(set, target, at);
        sharp = at;
        at = Copy(Bloom, target, at);
        at = Copy(Flash, target, at);
        return at;
    }

    private int Copy(int set, Vertex[] target, int at)
    {
        for (int g = 1; g < Grades; g++)
        {
            var layer = _layers[(set * Grades) + g];
            layer.CopyTo(target, at);
            at += layer.Count;
        }

        return at;
    }

    private void Quad(int set, int grade, WpfPoint a, WpfPoint b, WpfPoint c, WpfPoint d)
    {
        if (grade <= 0) return;
        int index = (set * Grades) + grade;
        var color = _colors[index];
        if (color >> 24 == 0) return;
        var layer = _layers[index];
        Vertex V(WpfPoint p) => new() { Position = new Vector2((float)p.X, (float)p.Y), Color = color };
        layer.Add(V(a)); layer.Add(V(b)); layer.Add(V(c));
        layer.Add(V(a)); layer.Add(V(c)); layer.Add(V(d));
    }

    private static System.Windows.Vector Normal(WpfPoint from, WpfPoint to)
    {
        var d = to - from;
        d.Normalize();
        return new System.Windows.Vector(d.Y, -d.X);
    }
}

/// <summary>
/// Draws the latest <see cref="TronMesh"/> the dock handed over: the sharp ink 4x multisampled, the bloom layer
/// at half size, blurred by a separable Gaussian (the WPF look's BlurEffect, radius 4 DIP) and laid over it.
/// The mesh is in the dock's canvas DIPs; <see cref="Submit"/> also carries the canvas-to-window transform.
/// </summary>
internal sealed class TronGpuScene : IGpuScene
{
    private const float BlurRadiusDip = 4;

    private readonly object _gate = new();
    private TronMesh.Vertex[] _pending = new TronMesh.Vertex[4096];
    private int _pendingCount, _pendingSharp;
    private Matrix3x2 _pendingTransform = Matrix3x2.Identity;
    private int _pendingVersion, _uploadedVersion = -1;
    private int _count, _sharp;
    private byte[] _bytes = Array.Empty<byte>();

    private ID3D11Device? _device;
    private ID3D11VertexShader? _meshVs, _fullscreenVs;
    private ID3D11PixelShader? _meshPs, _blurPs, _overPs;
    private ID3D11InputLayout? _layout;
    private ID3D11Buffer? _constants, _vertices;
    private int _vertexCapacity;
    private ID3D11BlendState? _over, _opaque;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11SamplerState? _linear;
    private ID3D11Texture2D? _msaa;
    private ID3D11RenderTargetView? _msaaView;
    private Target? _bloomA, _bloomB;
    private uint _samples = 1;
    private int _width, _height;

    private readonly float[] _cb = new float[16];
    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly ID3D11Buffer[] _vertexBuffer = new ID3D11Buffer[1];
    private readonly uint[] _stride = { (uint)Marshal.SizeOf<TronMesh.Vertex>() };
    private readonly uint[] _offset = { 0 };
    private readonly ID3D11SamplerState[] _sampler = new ID3D11SamplerState[1];
    private readonly ID3D11ShaderResourceView[] _input = new ID3D11ShaderResourceView[1];
    private readonly ID3D11ShaderResourceView[] _noInput = new ID3D11ShaderResourceView[1];

    /// <summary>
    /// Hands over one frame of the mound (from the dock's thread). <paramref name="transform"/> maps the mesh's
    /// DIPs to the window's DIPs; the render thread draws whatever arrived last.
    /// </summary>
    public void Submit(TronMesh mesh, Matrix3x2 transform)
    {
        lock (_gate)
        {
            _pendingCount = mesh.Pack(ref _pending, out _pendingSharp);
            _pendingTransform = transform;
            _pendingVersion++;
        }
    }

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        var vs = Compile("vs_4_0", "meshVs");
        _meshVs = device.CreateVertexShader(vs.Span, null);
        _layout = device.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 8, 0),
        }, vs.Span);
        _fullscreenVs = device.CreateVertexShader(Compile("vs_4_0", "fullscreenVs").Span, null);
        _meshPs = device.CreatePixelShader(Compile("ps_4_0", "meshPs").Span, null);
        _blurPs = device.CreatePixelShader(Compile("ps_4_0", "blurPs").Span, null);
        _overPs = device.CreatePixelShader(Compile("ps_4_0", "overPs").Span, null);
        _constants = device.CreateBuffer(new BufferDescription((uint)(_cb.Length * 4), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        _over = device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        _opaque = device.CreateBlendState(BlendDescription.Opaque);
        _raster = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = true });
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _linear = device.CreateSamplerState(new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, 0, float.MaxValue));
        _sampler[0] = _linear;
        _samples = device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, 4) > 0 ? 4u : 1u;
    }

    public void Resize(int width, int height)
    {
        if (_device is null || width < 2 || height < 2) return;
        ReleaseSized();
        _width = width;
        _height = height;
        _msaa = _device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, _samples, 0, ResourceOptionFlags.None));
        _msaaView = _device.CreateRenderTargetView(_msaa, null);
        _bloomA = new Target(_device, Math.Max(1, width / 2), Math.Max(1, height / 2));
        _bloomB = new Target(_device, Math.Max(1, width / 2), Math.Max(1, height / 2));
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertices?.Dispose(); _vertices = null; _vertexCapacity = 0;
        _meshVs?.Dispose(); _fullscreenVs?.Dispose(); _meshPs?.Dispose(); _blurPs?.Dispose(); _overPs?.Dispose();
        _meshVs = null; _fullscreenVs = null; _meshPs = null; _blurPs = null; _overPs = null;
        _layout?.Dispose(); _layout = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _over?.Dispose(); _opaque?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose(); _linear?.Dispose();
        _over = null; _opaque = null; _raster = null; _noDepth = null; _linear = null;
        _sampler[0] = null!;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void ReleaseSized()
    {
        _msaaView?.Dispose(); _msaaView = null;
        _msaa?.Dispose(); _msaa = null;
        _bloomA?.Dispose(); _bloomA = null;
        _bloomB?.Dispose(); _bloomB = null;
        _width = _height = 0;
    }

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _meshPs is null || frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_msaa is null || _msaaView is null || _bloomA is null || _bloomB is null) return;

        // the layer draws at the monitor's rate, the dock hands over a mesh at its own: upload only what is new
        Matrix3x2 transform;
        bool fresh = false;
        lock (_gate)
        {
            transform = _pendingTransform;
            if (_pendingVersion != _uploadedVersion || _vertices is null)
            {
                _uploadedVersion = _pendingVersion;
                _count = _pendingCount;
                _sharp = _pendingSharp;
                int bytes = _count * (int)_stride[0];
                if (_bytes.Length < bytes) _bytes = new byte[Math.Max(bytes, _bytes.Length * 2)];
                MemoryMarshal.AsBytes(_pending.AsSpan(0, _count)).CopyTo(_bytes);
                fresh = true;
            }
        }

        int count = _count, sharp = _sharp;

        float ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        var toPixels = transform * Matrix3x2.CreateScale(ppd);
        _cb[0] = frame.Width; _cb[1] = frame.Height; _cb[2] = Math.Clamp(frame.Fade, 0, 1); _cb[3] = 0;
        _cb[4] = toPixels.M11; _cb[5] = toPixels.M21; _cb[6] = toPixels.M31; _cb[7] = 0;
        _cb[8] = toPixels.M12; _cb[9] = toPixels.M22; _cb[10] = toPixels.M32; _cb[11] = 0;
        float sigma = BlurRadiusDip * ppd / 2 / 2; // half-size target; the radius spans about two sigmas
        _cb[12] = sigma; _cb[13] = 0; _cb[14] = 0; _cb[15] = 0;
        context.UpdateSubresource(_cb, _constants!);

        var empty = new Color4(0, 0, 0, 0);
        context.ClearRenderTargetView(_msaaView, empty);
        context.ClearRenderTargetView(_bloomA.View, empty);
        context.RSSetState(_raster);
        context.OMSetDepthStencilState(_noDepth, 0);
        context.VSSetConstantBuffers(0, 1, _constantBuffer);
        context.PSSetConstantBuffers(0, 1, _constantBuffer);
        context.PSSetSamplers(0, _sampler);

        if (count > 0)
        {
            if (fresh || _vertices is null) Upload(context, count);
            context.IASetInputLayout(_layout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _vertexBuffer[0] = _vertices!;
            context.IASetVertexBuffers(0, 1, _vertexBuffer, _stride, _offset);
            context.VSSetShader(_meshVs);
            context.PSSetShader(_meshPs);
            context.OMSetBlendState(_over);

            // the bloom layer, at half size
            if (count > sharp)
            {
                context.OMSetRenderTargets(_bloomA.View);
                context.RSSetViewport(new Viewport(0, 0, _bloomA.Width, _bloomA.Height, 0, 1));
                context.Draw((uint)(count - sharp), (uint)sharp);
            }

            context.OMSetRenderTargets(_msaaView);
            context.RSSetViewport(new Viewport(0, 0, frame.Width, frame.Height, 0, 1));
            if (sharp > 0) context.Draw((uint)sharp, 0);
        }

        if (count > sharp)
        {
            // blur across, then down, then lay the glow over the sharp ink
            context.IASetInputLayout(null);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_fullscreenVs);
            Pass(context, _bloomA, _bloomB, _blurPs!, _opaque!, 1, 0);
            Pass(context, _bloomB, _bloomA, _blurPs!, _opaque!, 0, 1);
            context.OMSetRenderTargets(_msaaView);
            context.RSSetViewport(new Viewport(0, 0, frame.Width, frame.Height, 0, 1));
            context.OMSetBlendState(_over);
            context.PSSetShader(_overPs);
            _input[0] = _bloomA.Srv;
            context.PSSetShaderResources(0, 1, _input);
            context.Draw(3, 0);
            context.PSSetShaderResources(0, 1, _noInput);
        }

        using var resource = target.Resource;
        if (_samples > 1) context.ResolveSubresource(resource, 0, _msaa, 0, Format.B8G8R8A8_UNorm);
        else context.CopyResource(resource, _msaa);
    }

    private void Pass(ID3D11DeviceContext context, Target from, Target to, ID3D11PixelShader shader, ID3D11BlendState blend, float dx, float dy)
    {
        _cb[13] = dx; _cb[14] = dy;
        context.UpdateSubresource(_cb, _constants!);
        context.OMSetRenderTargets(to.View);
        context.RSSetViewport(new Viewport(0, 0, to.Width, to.Height, 0, 1));
        context.OMSetBlendState(blend);
        context.PSSetShader(shader);
        _input[0] = from.Srv;
        context.PSSetShaderResources(0, 1, _input);
        context.Draw(3, 0);
        context.PSSetShaderResources(0, 1, _noInput);
    }

    private void Upload(ID3D11DeviceContext context, int count)
    {
        if (_vertices is null || _vertexCapacity < count)
        {
            _vertices?.Dispose();
            _vertexCapacity = Math.Max(count, _vertexCapacity * 2);
            _vertices = _device!.CreateBuffer(new BufferDescription((uint)(_vertexCapacity * _stride[0]), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.None, 0));
        }

        int bytes = count * (int)_stride[0];
        var mapped = context.Map(_vertices, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try { Marshal.Copy(_bytes, 0, mapped.DataPointer, bytes); }
        finally { context.Unmap(_vertices, 0); }
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string entry) =>
        Compiler.Compile(Source, entry, "Tron", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;

        public Target(ID3D11Device device, int width, int height)
        {
            Width = width;
            Height = height;
            _texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
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

    private const string Source = @"
cbuffer Frame : register(b0)
{
    float2 Size; float Fade; float Pad0;
    float4 RowX; float4 RowY;   // DIP to pixels
    float Sigma; float2 Step; float Pad1;
};
Texture2D Source : register(t0);
SamplerState Linear : register(s0);

struct VI { float2 pos : POSITION; float4 col : COLOR; };
struct VO { float4 pos : SV_Position; float4 col : COLOR; };
VO meshVs(VI i)
{
    VO o;
    float2 p = float2(dot(RowX.xyz, float3(i.pos, 1)), dot(RowY.xyz, float3(i.pos, 1)));
    o.pos = float4((p.x / Size.x) * 2 - 1, 1 - (p.y / Size.y) * 2, 0, 1);
    o.col = i.col;
    return o;
}
float4 meshPs(VO i) : SV_Target
{
    float a = i.col.a * Fade;
    return float4(i.col.rgb * a, a);
}

struct FO { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
FO fullscreenVs(uint id : SV_VertexID)
{
    FO o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4((uv.x * 2) - 1, 1 - (uv.y * 2), 0, 1);
    o.uv = uv;
    return o;
}
float4 blurPs(FO i) : SV_Target
{
    float w, h;
    Source.GetDimensions(w, h);
    float2 texel = Step / float2(w, h);
    float4 sum = 0;
    float total = 0;
    [unroll] for (int k = -6; k <= 6; k++)
    {
        float g = exp(-(k * k) / (2 * Sigma * Sigma));
        sum += Source.SampleLevel(Linear, i.uv + texel * k, 0) * g;
        total += g;
    }
    return sum / total;
}
float4 overPs(FO i) : SV_Target
{
    return Source.SampleLevel(Linear, i.uv, 0);
}";
}
