using System.Numerics;
using System.Runtime.InteropServices;
using MediaColor = System.Windows.Media.Color;
using Floe.App.Effects.Visualizer;
using Floe.App.Services;
using Floe.Core.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// The cube look on the GPU: the same glass cubes as <see cref="CubeVisualizer"/>, stacked into columns that rise
/// with the music and throw their top cube on a beat. The columns are stepped on the CPU exactly as the WPF look
/// steps them; every face, rim, gloss and glint becomes two triangles in one vertex buffer, drawn in the WPF
/// look's brush order into a 4x multisampled target, so the edges keep their antialiasing.
/// </summary>
public sealed class CubeGpuScene : IGpuScene
{
    // the CPU look's level shaping
    private const float GainDecaySeconds = 4;
    private const float GainFloor = .002f;
    private const float SilenceFloor = .0003f;
    private const float AttackSeconds = .05f;
    private const float ReleaseSeconds = .30f;

    // the CPU look's geometry, in DIP
    private const float Gap = 1.2f;
    private const float Rim = .6f;
    private const float Gloss = .32f;
    private const int MaxColumns = 400;
    private const int MaxRows = 9;
    private const float RiseSeconds = .06f;
    private const float FallSeconds = .35f;
    private const float Gravity = 900;
    private const float Kick = 26;
    private const int Grades = 8;
    private const int Shades = 6;

    // Draw order, as the WPF look fills its batches: the pool, sides and lids (per shade), fronts, gloss, rims,
    // the streak and the glint.
    private const int PoolLayer = 0;
    private const int FrontLayer = 1 + (2 * Shades);
    private const int GlossLayer = FrontLayer + Shades;
    private const int RimLayer = GlossLayer + 1;
    private const int StreakLayer = RimLayer + Shades;
    private const int GlintLayer = StreakLayer + 1;
    private const int Layers = GlintLayer + 1;

    private static readonly MediaColor BaseBody = MediaColor.FromRgb(0x2A, 0x6F, 0xD6);
    private static readonly MediaColor BaseCore = MediaColor.FromRgb(0x6F, 0xB8, 0xFF);
    private static readonly MediaColor BaseTip = MediaColor.FromRgb(0xEA, 0xF6, 0xFF);

    private static readonly Vector3[] Corners =
    {
        new(-.5f, -.5f, -.5f), new(.5f, -.5f, -.5f), new(.5f, .5f, -.5f), new(-.5f, .5f, -.5f),
        new(-.5f, -.5f, .5f), new(.5f, -.5f, .5f), new(.5f, .5f, .5f), new(-.5f, .5f, .5f),
    };

    private static readonly int[][] Faces =
    {
        new[] { 4, 5, 6, 7 }, new[] { 5, 4, 7, 6 }, new[] { 5, 1, 2, 6 },
        new[] { 0, 4, 7, 3 }, new[] { 7, 6, 2, 3 }, new[] { 0, 1, 5, 4 },
    };

    private static readonly Vector3[] Normals =
    {
        new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector4 Color;
        public Vector3 Extra; // u, v and mode: 1 fades the radial pool out towards its rim
    }

    private ID3D11Device? _device;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _layout;
    private ID3D11Buffer? _constants;
    private ID3D11Buffer? _vertices;
    private int _vertexCapacity;
    private ID3D11BlendState? _blend;
    private ID3D11RasterizerState? _raster;
    private ID3D11DepthStencilState? _noDepth;
    private ID3D11Texture2D? _msaa;
    private ID3D11RenderTargetView? _msaaView;
    private uint _samples = 1;
    private Viewport[] _viewport = Array.Empty<Viewport>();
    private int _width, _height;

    private readonly ID3D11Buffer[] _constantBuffer = new ID3D11Buffer[1];
    private readonly float[] _cb = new float[4];
    private readonly ID3D11Buffer[] _vertexBuffer = new ID3D11Buffer[1];
    private readonly uint[] _stride = { (uint)Marshal.SizeOf<Vertex>() };
    private readonly uint[] _offset = { 0 };

    // CPU state, as in CubeVisualizer
    private readonly float[] _heightOf = new float[MaxColumns];
    private readonly float[] _peak = new float[MaxColumns];
    private readonly float[] _peakVelocity = new float[MaxColumns];
    private readonly float[] _peakSpin = new float[MaxColumns];
    private readonly float[] _seed = new float[MaxColumns];
    private readonly Vector3[] _axis = new Vector3[MaxColumns];
    private float _level, _gainPeak, _time;

    // per frame
    private readonly VertexList[] _layers = new VertexList[Layers];
    private Vertex[] _all = new Vertex[4096];
    private byte[] _bytes = Array.Empty<byte>();
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _projected = new Vector2[8];
    private int _paletteVersion = -1;
    private int _paletteGrade = -1;
    private readonly Vector4[] _front = new Vector4[Shades], _side = new Vector4[Shades], _top = new Vector4[Shades], _rim = new Vector4[Shades], _rimLow = new Vector4[Shades];
    private Vector4 _gloss, _streak, _glint;
    private Vector3 _poolColor;
    private MediaColor _tip;
    private readonly MediaColor[] _faces = new MediaColor[Shades];

    public CubeGpuScene()
    {
        for (int i = 0; i < Layers; i++) _layers[i] = new VertexList();
        for (int i = 0; i < MaxColumns; i++)
        {
            _seed[i] = (float)Hash01(i, 0x2F91A3);
            _axis[i] = Vector3.Normalize(new Vector3(1, (float)(Hash01(i, 0x51ED27) - .5) * 1.2f, (float)(Hash01(i, 0x7C3B19) - .5) * 1.2f));
        }
    }

    /// <summary>Offscreen renders: 0..1 holds that level without audio; -1 (default) follows the music.</summary>
    public float DebugLevel { get; set; } = -1;

    private static float Cell => (float)GlassSettings.CubeSize;

    private static Vector2 Depth => new(Cell * .30f, -Cell * .22f);

    // ---- lifecycle --------------------------------------------------------------------------------

    public void Initialize(ID3D11Device device)
    {
        _device = device;
        var vs = Compile("vs_4_0", "vs");
        _vertexShader = device.CreateVertexShader(vs.Span, null);
        _pixelShader = device.CreatePixelShader(Compile("ps_4_0", "ps").Span, null);
        _layout = device.CreateInputLayout(new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32B32_Float, 24, 0),
        }, vs.Span);
        _constants = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _constantBuffer[0] = _constants;
        // premultiplied "over", the way WPF lays one brush over the next
        var blend = new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha);
        _blend = device.CreateBlendState(blend);
        _raster = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = true, AntialiasedLineEnable = false });
        _noDepth = device.CreateDepthStencilState(DepthStencilDescription.None);
        _samples = device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, 4) > 0 ? 4u : 1u;
        _paletteVersion = -1;
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
        _viewport = new[] { new Viewport(0, 0, width, height, 0, 1) };
    }

    public void ReleaseDevice()
    {
        ReleaseSized();
        _vertices?.Dispose(); _vertices = null; _vertexCapacity = 0;
        _vertexShader?.Dispose(); _vertexShader = null;
        _pixelShader?.Dispose(); _pixelShader = null;
        _layout?.Dispose(); _layout = null;
        _constants?.Dispose(); _constants = null;
        _constantBuffer[0] = null!;
        _blend?.Dispose(); _raster?.Dispose(); _noDepth?.Dispose();
        _blend = null; _raster = null; _noDepth = null;
        _device = null;
    }

    public void Dispose() => ReleaseDevice();

    private void ReleaseSized()
    {
        _msaaView?.Dispose(); _msaaView = null;
        _msaa?.Dispose(); _msaa = null;
        _width = _height = 0;
    }

    // ---- one frame --------------------------------------------------------------------------------

    public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
    {
        if (_device is null || _pixelShader is null || frame.Width < 2 || frame.Height < 2) return;
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        if (_msaa is null || _msaaView is null) return;

        float dt = Math.Clamp(frame.Dt, 0, .1f);
        float ppd = frame.PixelsPerDip > 0 ? frame.PixelsPerDip : 1;
        float fade = DebugLevel >= 0 ? 1 : Math.Clamp(frame.Fade, 0, 1);
        UpdateAudio(dt, frame.Audio);
        // the strip is the capsule; the cubes stand on its bottom edge
        float width = frame.Width / ppd, height = frame.Height / ppd;
        Step(dt, width, height);

        int count = Build(width, height, fade);
        context.ClearRenderTargetView(_msaaView, new Color4(0, 0, 0, 0));
        if (count > 0)
        {
            Upload(context, count);
            _cb[0] = frame.Width; _cb[1] = frame.Height; _cb[2] = ppd; _cb[3] = 0;
            context.UpdateSubresource(_cb, _constants!);
            context.OMSetRenderTargets(_msaaView);
            context.RSSetViewports(_viewport);
            context.RSSetState(_raster);
            context.OMSetDepthStencilState(_noDepth, 0);
            context.OMSetBlendState(_blend);
            context.IASetInputLayout(_layout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _vertexBuffer[0] = _vertices!;
            context.IASetVertexBuffers(0, 1, _vertexBuffer, _stride, _offset);
            context.VSSetShader(_vertexShader);
            context.VSSetConstantBuffers(0, 1, _constantBuffer);
            context.PSSetShader(_pixelShader);
            context.Draw((uint)count, 0);
        }

        using var resource = target.Resource;
        if (_samples > 1) context.ResolveSubresource(resource, 0, _msaa, 0, Format.B8G8R8A8_UNorm);
        else context.CopyResource(resource, _msaa);
    }

    private void UpdateAudio(float dt, SpectrumFrame audio)
    {
        _time += dt;
        if (DebugLevel >= 0)
        {
            _level = DebugLevel;
            return;
        }

        // auto gain against a slowly decaying recent peak, as the CPU look does with its meter
        float raw = Math.Clamp(audio.Level, 0, 1);
        _gainPeak = MathF.Max(raw, _gainPeak * MathF.Exp(-dt / GainDecaySeconds));
        float target = raw < SilenceFloor ? 0 : Math.Clamp(raw / MathF.Max(_gainPeak, GainFloor), 0, 1);
        float tau = target > _level ? AttackSeconds : ReleaseSeconds;
        _level += (target - _level) * (1 - MathF.Exp(-dt / tau));
    }

    private int ColumnCount(float width, float height, out int rows, out float spacing)
    {
        rows = 0;
        spacing = Cell;
        if (!float.IsFinite(width) || width < Cell * 2 || height < Cell) return 0;
        rows = Math.Clamp((int)((height - (Cell * .4f)) / Cell), 1, MaxRows);
        int count = Math.Min(MaxColumns, (int)(width / Cell));
        spacing = width / count;
        return count;
    }

    // CubeVisualizer.Step, line for line
    private void Step(float dt, float width, float height)
    {
        int count = ColumnCount(width, height, out int rows, out _);
        if (count == 0) return;
        double level = Math.Clamp(_level, 0, 1);
        double amplitude = .2 + (1.25 * Math.Pow(level, .7));
        double t = _time;
        double rise = 1 - Math.Exp(-dt / RiseSeconds), fall = 1 - Math.Exp(-dt / FallSeconds);
        for (int i = 0; i < count; i++)
        {
            double xn = ((i + .5) / count) - .5;
            double envelope = .30 + (.70 * Math.Exp(-(xn * xn) / (2 * .2 * .2)));
            double seed = _seed[i];
            double noise = (.6 * ValueNoise((t * (1.4 + seed)) + (seed * 40), (int)(seed * 100003)))
                + (.4 * ValueNoise((t * 3.1) + (i * .35), 0x1F3D));
            double target = Math.Clamp(envelope * amplitude * (.25 + (.85 * noise)), 0, 1) * rows;
            _heightOf[i] += (float)((target - _heightOf[i]) * (target > _heightOf[i] ? rise : fall));

            if (_heightOf[i] > _peak[i])
            {
                float jump = _heightOf[i] - _peak[i];
                _peak[i] = _heightOf[i];
                _peakVelocity[i] = MathF.Max(_peakVelocity[i], MathF.Min(jump * Kick, 16));
            }

            _peakVelocity[i] -= Gravity / Cell * dt;
            _peak[i] += _peakVelocity[i] * dt;
            _peakSpin[i] += MathF.Abs(_peakVelocity[i]) * dt * 1.6f;
            if (_peak[i] <= _heightOf[i])
            {
                _peak[i] = _heightOf[i];
                _peakVelocity[i] = 0;
                _peakSpin[i] = MathF.Round(_peakSpin[i] / (MathF.PI / 2)) * (MathF.PI / 2);
            }
        }
    }

    // CubeVisualizer.OnRender, into vertex layers; returns the vertex count
    private int Build(float width, float height, float fade)
    {
        int count = ColumnCount(width, height, out int rows, out float spacing);
        if (fade <= .001f || count == 0) return 0;
        foreach (var layer in _layers) layer.Clear();

        float level = Math.Clamp(_level, 0, 1);
        float energy = fade * (.35f + (.65f * MathF.Sqrt(level)));
        int grade = Math.Clamp((int)MathF.Round(energy * (Grades - 1)), 1, Grades - 1);
        EnsurePalette(grade);
        float floor = height;

        float poolAlpha = Math.Clamp((.25f + (.45f * MathF.Sqrt(level))) * fade, 0, 1);
        float poolOpacity = .45f * (int)MathF.Round(poolAlpha * 15) / 15f;
        float rx = width * .5f, ry = Cell * (1.2f + (1.4f * level));
        Pool(new Vector2(width / 2, floor), rx, ry, new Vector4(_poolColor, poolOpacity));

        for (int i = 0; i < count; i++)
        {
            float x = (i * spacing) + ((spacing - Cell) / 2);
            float h = _heightOf[i];
            float neighbour = i + 1 < count ? _heightOf[i + 1] : 0;
            int stack = Math.Min(rows, (int)MathF.Round(h));
            for (int r = 0; r < stack; r++)
            {
                int shade = Math.Clamp((int)(r * Shades / (double)rows), 0, Shades - 1);
                Box(x, floor - (r * Cell), shade, r == stack - 1, r + 1 > neighbour);
            }

            float lift = _peak[i] - h;
            if (lift > .15f && stack < rows)
            {
                int shade = Math.Clamp((int)(_peak[i] * Shades / rows), 0, Shades - 1);
                Tumbling(i, new Vector2(x + (Cell / 2), floor - ((stack + lift) * Cell) - (Cell / 2)), shade);
            }
        }

        int total = 0;
        foreach (var layer in _layers) total += layer.Count;
        if (_all.Length < total) _all = new Vertex[Math.Max(total, _all.Length * 2)];
        int at = 0;
        foreach (var layer in _layers)
        {
            Array.Copy(layer.Items, 0, _all, at, layer.Count);
            at += layer.Count;
        }

        return total;
    }

    private void Box(float x, float bottom, int shade, bool top, bool side)
    {
        float cell = Cell;
        var tl = new Vector2(x + Gap, bottom - cell + Gap);
        var tr = new Vector2(x + cell - Gap, bottom - cell + Gap);
        var br = new Vector2(x + cell - Gap, bottom - Gap);
        var bl = new Vector2(x + Gap, bottom - Gap);
        var depth = Depth;
        if (side) Quad(1 + (2 * shade), tr, br, br + depth, tr + depth, _side[shade]);
        if (top)
        {
            Quad(2 + (2 * shade), tl, tr, tr + depth, tl + depth, _top[shade]);
            Line(GlintLayer, tl, tr, .55f, _glint);
        }

        Quad(FrontLayer + shade, tl, tr, br, bl, _front[shade]);

        float w = tr.X - tl.X, h = bl.Y - tl.Y;
        var rim = _rim[shade];
        Bar(RimLayer + shade, tl.X, tl.Y, w, Rim, rim);
        // The lower edge catches less light than the upper and side edges.
        Bar(RimLayer + shade, tl.X, bl.Y - Rim, w, Rim, _rimLow[shade]);
        Bar(RimLayer + shade, tl.X, tl.Y + Rim, Rim, h - (2 * Rim), rim);
        Bar(RimLayer + shade, tr.X - Rim, tl.Y + Rim, Rim, h - (2 * Rim), rim);
        Bar(GlossLayer, tl.X + Rim, tl.Y + Rim, w - (2 * Rim), (h - (2 * Rim)) * Gloss, _gloss);
        Quad(StreakLayer, new Vector2(tl.X + (w * .2f), tl.Y + Rim), new Vector2(tl.X + (w * .34f), tl.Y + Rim),
            new Vector2(tl.X + Rim, tl.Y + (h * .34f)), new Vector2(tl.X + Rim, tl.Y + (h * .2f)), _streak);
    }

    private void Tumbling(int i, Vector2 centre, int shade)
    {
        var rotation = Quaternion.CreateFromAxisAngle(_axis[i], _peakSpin[i]);
        float size = Cell - (2 * Gap);
        var depth = Depth;
        for (int c = 0; c < Corners.Length; c++)
        {
            var p = Vector3.Transform(Corners[c], rotation);
            _projected[c] = new Vector2(centre.X + (p.X * size) - (p.Z * depth.X), centre.Y + (p.Y * size) - (p.Z * depth.Y));
        }

        for (int f = 0; f < Faces.Length; f++)
        {
            var n = Vector3.Transform(Normals[f], rotation);
            if (n.Z <= .02f) continue;
            var face = Faces[f];
            if (n.Y < -.3f) Quad(2 + (2 * shade), _projected[face[0]], _projected[face[1]], _projected[face[2]], _projected[face[3]], _top[shade]);
            else if (n.Z > .7f) Quad(FrontLayer + shade, _projected[face[0]], _projected[face[1]], _projected[face[2]], _projected[face[3]], _front[shade]);
            else Quad(1 + (2 * shade), _projected[face[0]], _projected[face[1]], _projected[face[2]], _projected[face[3]], _side[shade]);
        }
    }

    private void Bar(int layer, float x, float y, float w, float h, Vector4 color)
    {
        if (w <= 0 || h <= 0) return;
        Quad(layer, new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h), color);
    }

    private void Line(int layer, Vector2 from, Vector2 to, float half, Vector4 color)
    {
        var d = to - from;
        if (d.LengthSquared() < 1e-6f) return;
        d = Vector2.Normalize(d);
        var n = new Vector2(d.Y, -d.X) * half;
        Quad(layer, from + n, to + n, to - n, from - n, color);
    }

    private void Quad(int layer, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color)
    {
        if (color.W <= 0) return;
        var list = _layers[layer];
        list.Add(new Vertex { Position = a, Color = color });
        list.Add(new Vertex { Position = b, Color = color });
        list.Add(new Vertex { Position = c, Color = color });
        list.Add(new Vertex { Position = a, Color = color });
        list.Add(new Vertex { Position = c, Color = color });
        list.Add(new Vertex { Position = d, Color = color });
    }

    private void Pool(Vector2 centre, float rx, float ry, Vector4 color)
    {
        if (color.W <= 0) return;
        var list = _layers[PoolLayer];
        Vertex V(float u, float v) => new() { Position = centre + new Vector2(u * rx, v * ry), Color = color, Extra = new Vector3(u, v, 1) };
        list.Add(V(-1, -1)); list.Add(V(1, -1)); list.Add(V(1, 1));
        list.Add(V(-1, -1)); list.Add(V(1, 1)); list.Add(V(-1, 1));
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
        if (_bytes.Length < bytes) _bytes = new byte[Math.Max(bytes, _bytes.Length * 2)];
        MemoryMarshal.AsBytes(_all.AsSpan(0, count)).CopyTo(_bytes);
        var mapped = context.Map(_vertices, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try { Marshal.Copy(_bytes, 0, mapped.DataPointer, bytes); }
        finally { context.Unmap(_vertices, 0); }
    }

    // CubeVisualizer.EnsurePalette for one grade: straight colours, the brush's stepped alpha in W
    private void EnsurePalette(int grade)
    {
        if (_paletteVersion != GlassSettings.ColorVersion)
        {
            _paletteVersion = GlassSettings.ColorVersion;
            _paletteGrade = -1;
            var body = HologramVisualizer.Tint(BaseBody);
            var core = HologramVisualizer.Tint(BaseCore);
            var tip = HologramVisualizer.Tint(BaseTip);
            for (int s = 0; s < Shades; s++)
            {
                double k = s / (Shades - 1.0);
                _faces[s] = k < .5 ? Mix(body, core, k * 2) : Mix(core, tip, (k - .5) * 1.4);
            }

            _tip = tip;
            _poolColor = Rgb(core);
        }

        if (_paletteGrade == grade) return;
        _paletteGrade = grade;
        var black = MediaColor.FromRgb(0, 0, 0);
        var white = MediaColor.FromRgb(255, 255, 255);
        for (int s = 0; s < Shades; s++)
        {
            var face = _faces[s];
            _front[s] = Ladder(face, .34, grade);
            _side[s] = Ladder(Mix(face, black, .42), .46, grade);
            _top[s] = Ladder(Mix(face, _tip, .38), .64, grade);
            _rim[s] = Ladder(Mix(face, _tip, .42), .64, grade);
            _rimLow[s] = Ladder(Mix(face, _tip, .42), .64, Math.Max(1, grade - 2));
        }

        _gloss = Ladder(_tip, .10, grade);
        _streak = Ladder(_tip, .32, grade);
        _glint = Ladder(white, .85, grade);
    }

    private static Vector4 Ladder(MediaColor color, double max, int grade) =>
        new(Rgb(color), (byte)(255 * max * grade / (Grades - 1.0)) / 255f);

    private static Vector3 Rgb(MediaColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    private static MediaColor Mix(MediaColor a, MediaColor b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return MediaColor.FromRgb((byte)(a.R + ((b.R - a.R) * k)), (byte)(a.G + ((b.G - a.G) * k)), (byte)(a.B + ((b.B - a.B) * k)));
    }

    private static double ValueNoise(double x, int seed)
    {
        double floor = Math.Floor(x);
        int i = (int)floor;
        double f = x - floor;
        double smooth = f * f * (3 - (2 * f));
        double a = Hash01(i, seed);
        double b = Hash01(i + 1, seed);
        return a + ((b - a) * smooth);
    }

    private static double Hash01(int x, int seed)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (seed * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFF) / 16777215.0;
        }
    }

    private static ReadOnlyMemory<byte> Compile(string profile, string entry) =>
        Compiler.Compile(Source, entry, "Cubes", profile, ShaderFlags.OptimizationLevel3, EffectFlags.None);

    private sealed class VertexList
    {
        public Vertex[] Items = new Vertex[256];
        public int Count;

        public void Clear() => Count = 0;

        public void Add(in Vertex vertex)
        {
            if (Count == Items.Length) Array.Resize(ref Items, Items.Length * 2);
            Items[Count++] = vertex;
        }
    }

    // Positions arrive in DIP; the pool fades linearly from its centre to its rim like the WPF radial brush.
    private const string Source = @"
cbuffer Frame : register(b0) { float2 Size; float Ppd; float Pad; };
struct VI { float2 pos : POSITION; float4 col : COLOR; float3 ex : TEXCOORD0; };
struct VO { float4 pos : SV_Position; float4 col : COLOR; float3 ex : TEXCOORD0; };
VO vs(VI i)
{
    VO o;
    float2 p = i.pos * Ppd;
    o.pos = float4((p.x / Size.x) * 2 - 1, 1 - (p.y / Size.y) * 2, 0, 1);
    o.col = i.col;
    o.ex = i.ex;
    return o;
}
float4 ps(VO i) : SV_Target
{
    float a = i.col.a;
    if (i.ex.z > .5) a *= saturate(1 - length(i.ex.xy));
    return float4(i.col.rgb * a, a);
}";
}
