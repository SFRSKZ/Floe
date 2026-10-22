using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.ActiveIcon;

/// <summary>Diagnostic-only software D3D11 readback; no window or dock state changes.</summary>
internal sealed class ClickGlassRenderer : IDisposable
{
    private readonly int _width, _height;
    private IntPtr _device, _context, _target, _view, _readback, _vertexShader, _pixelShader, _constants;

    internal ClickGlassRenderer(System.Windows.Media.ImageSource icon, int size)
        : this(CreateInput(icon, size), (BitmapSource)IconField.For(icon, size).ImageSource,
            "Floe.ClickGlass.hlsl", "float Progress", "float Dist") { }

    internal ClickGlassRenderer(BitmapSource mask, BitmapSource backdrop)
        : this(mask, backdrop, "Floe.DropLens.hlsl", "float2 Box", "float Height") { }

    private ClickGlassRenderer(BitmapSource input, BitmapSource field, string resource, string firstConstant, string firstFunction)
    {
        _width = input.PixelWidth;
        _height = input.PixelHeight;
        try
        {
            // WARP works without a hardware adapter, including remote/headless sessions.
            Check(D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                out _device, out _, out _context));
            var texture = new TextureDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1,
                Format = 87, SampleCount = 1, BindFlags = 0x20 // BGRA8, RENDER_TARGET
            };
            Check(Call<CreateTexture>(_device, 5)(_device, ref texture, IntPtr.Zero, out _target));
            Check(Call<CreateView>(_device, 9)(_device, _target, IntPtr.Zero, out _view));
            texture.Usage = 3; texture.BindFlags = 0; texture.CpuAccessFlags = 0x20000;
            Check(Call<CreateTexture>(_device, 5)(_device, ref texture, IntPtr.Zero, out _readback));
            var vertex = ShaderCompiler.CompilePreview(vertex: true);
            var pixel = CompilePixel(resource, firstConstant, firstFunction);
            Check(Call<CreateShader>(_device, 12)(_device, vertex, (nuint)vertex.Length, IntPtr.Zero, out _vertexShader));
            Check(Call<CreateShader>(_device, 15)(_device, pixel, (nuint)pixel.Length, IntPtr.Zero, out _pixelShader));
            var buffer = new BufferDescription { ByteWidth = 6 * 16, BindFlags = 4 }; // CONSTANT_BUFFER
            Check(Call<CreateBuffer>(_device, 3)(_device, ref buffer, IntPtr.Zero, out _constants));
            Upload(input, 0);
            Upload(field, 1);
            var sampler = new SamplerDescription { Filter = 0x15, AddressU = 3, AddressV = 3, AddressW = 3, MaxLod = float.MaxValue };
            Check(Call<CreateSampler>(_device, 23)(_device, ref sampler, out _sampler));
            Call<SetBuffers>(_context, 10)(_context, 0, 1, new[] { _sampler });
            Call<SetShader>(_context, 11)(_context, _vertexShader, IntPtr.Zero, 0);
            Call<SetShader>(_context, 9)(_context, _pixelShader, IntPtr.Zero, 0);
            Call<SetBuffers>(_context, 16)(_context, 0, 1, new[] { _constants });
            Call<SetTopology>(_context, 24)(_context, 4); // TRIANGLELIST
            Call<SetTargets>(_context, 33)(_context, 1, new[] { _view }, IntPtr.Zero);
            var viewport = new Viewport { Width = _width, Height = _height, MaxDepth = 1 };
            Call<SetViewport>(_context, 44)(_context, 1, ref viewport);
        }
        catch { Dispose(); throw; }
    }

    internal BitmapSource Render(double progress, double tone, double palette = 0, Color? a = null, Color? b = null)
    {
        Color first = a ?? Colors.White, second = b ?? Colors.White;
        return Render(new float[]
        {
            (float)progress, 0, 0, 0, (float)tone, 0, 0, 0, _width, 0, 0, 0, (float)palette, 0, 0, 0,
            first.R / 255f, first.G / 255f, first.B / 255f, 1, second.R / 255f, second.G / 255f, second.B / 255f, 1,
        });
    }

    internal BitmapSource Render(float[] constants)
    {
        if (constants.Length > 24) throw new ArgumentException("At most six float4 constants are supported.", nameof(constants));
        var packed = new float[24];
        constants.CopyTo(packed, 0);
        Call<Update>(_context, 48)(_context, _constants, 0, IntPtr.Zero, packed, 0, 0);
        Call<Draw>(_context, 13)(_context, 3, 0); // Full-screen triangle from SV_VertexID.
        Call<Copy>(_context, 47)(_context, _readback, _target);
        Check(Call<Map>(_context, 14)(_context, _readback, 0, 1, 0, out var mapped));
        var pixels = new byte[_width * _height * 4];
        try
        {
            for (int row = 0; row < _height; row++)
                Marshal.Copy(IntPtr.Add(mapped.Data, checked(row * (int)mapped.RowPitch)), pixels, row * _width * 4, _width * 4);
        }
        finally { Call<Unmap>(_context, 15)(_context, _readback, 0); }
        var bitmap = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Pbgra32, null, pixels, _width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        Release(ref _sampler);
        foreach (var resource in _resources) Marshal.Release(resource);
        _resources.Clear();
        Release(ref _constants); Release(ref _pixelShader); Release(ref _vertexShader);
        Release(ref _readback); Release(ref _view); Release(ref _target);
        Release(ref _context); Release(ref _device);
    }

    private readonly List<IntPtr> _resources = new();
    private IntPtr _sampler;

    private static BitmapSource CreateInput(ImageSource icon, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(icon, new Rect(ActiveIconEffect.Pad, ActiveIconEffect.Pad, size, size));
        int side = size + 2 * (int)ActiveIconEffect.Pad;
        var input = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        input.Render(visual);
        return input;
    }

    private static byte[] CompilePixel(string resource, string firstConstant, string firstFunction)
    {
        using var stream = typeof(ClickGlassRenderer).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        source = source.Replace("sampler2D Input : register(s0);", "Texture2D Input : register(t0); SamplerState LinearSampler : register(s0);")
            .Replace("sampler2D Field : register(s1);", "Texture2D Field : register(t1);");
        int start = source.IndexOf(firstConstant, StringComparison.Ordinal);
        int end = source.IndexOf(firstFunction, StringComparison.Ordinal);
        var constants = System.Text.RegularExpressions.Regex.Replace(source[start..end], @"register\(c(\d+)\)", "packoffset(c$1)");
        source = source[..start] + "cbuffer Parameters : register(b0) {\n" + constants + "};\n" + source[end..];
        source = source.Replace("tex2D(Input,", "Input.Sample(LinearSampler,").Replace("tex2D(Field,", "Field.Sample(LinearSampler,")
            .Replace(": COLOR0", ": SV_Target")
            .Replace("main(float2 uv : TEXCOORD0)", "main(float4 position : SV_Position, float2 uv : TEXCOORD0)");
        return ShaderCompiler.CompileBytes(System.Text.Encoding.UTF8.GetBytes(source), "ps_4_0");
    }

    private void Upload(BitmapSource bitmap, uint slot)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        IntPtr texture = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;
        try
        {
            var description = new TextureDescription
            {
                Width = (uint)bitmap.PixelWidth, Height = (uint)bitmap.PixelHeight, MipLevels = 1, ArraySize = 1,
                Format = 87, SampleCount = 1, BindFlags = 8,
            };
            var data = new SubresourceData { Data = pin.AddrOfPinnedObject(), RowPitch = (uint)bitmap.PixelWidth * 4 };
            Check(Call<CreateTextureData>(_device, 5)(_device, ref description, ref data, out texture));
            Check(Call<CreateView>(_device, 7)(_device, texture, IntPtr.Zero, out view));
            _resources.Add(texture); texture = IntPtr.Zero;
            _resources.Add(view);
            Call<SetBuffers>(_context, 8)(_context, slot, 1, new[] { view });
            view = IntPtr.Zero;
        }
        finally { pin.Free(); Release(ref texture); Release(ref view); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubresourceData { public IntPtr Data; public uint RowPitch, SlicePitch; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SamplerDescription
    {
        public uint Filter, AddressU, AddressV, AddressW;
        public float MipBias;
        public uint MaxAnisotropy, Comparison;
        public float BorderR, BorderG, BorderB, BorderA, MinLod, MaxLod;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTextureData(IntPtr self, ref TextureDescription description, ref SubresourceData data, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSampler(IntPtr self, ref SamplerDescription description, out IntPtr sampler);

    private static void Release(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero) { Marshal.Release(pointer); pointer = IntPtr.Zero; }
    }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    // COM vtable slots from ID3D11Device / ID3D11DeviceContext (d3d11.h).
    private static T Call<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality;
        public uint Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BufferDescription { public uint ByteWidth, Usage, BindFlags, CpuAccessFlags, MiscFlags, StructureByteStride; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Viewport { public float X, Y, Width, Height, MinDepth, MaxDepth; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MappedResource { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags,
        IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out uint level, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr data, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr self, IntPtr resource, IntPtr description, out IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateShader(IntPtr self, byte[] code, nuint length, IntPtr linkage, out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBuffer(IntPtr self, ref BufferDescription description, IntPtr data, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetShader(IntPtr self, IntPtr shader, IntPtr instances, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetBuffers(IntPtr self, uint start, uint count, IntPtr[] buffers);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetTopology(IntPtr self, uint topology);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetTargets(IntPtr self, uint count, IntPtr[] targets, IntPtr depth);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetViewport(IntPtr self, uint count, ref Viewport viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Update(IntPtr self, IntPtr resource, uint subresource, IntPtr box, float[] data, uint rowPitch, uint depthPitch);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Draw(IntPtr self, uint vertices, uint start);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Copy(IntPtr self, IntPtr destination, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Map(IntPtr self, IntPtr resource, uint subresource, uint type, uint flags, out MappedResource mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Unmap(IntPtr self, IntPtr resource, uint subresource);
}
