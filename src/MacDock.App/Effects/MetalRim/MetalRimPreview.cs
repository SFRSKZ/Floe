// Ported from metal-fx by Jakub Antalik (MIT), https://github.com/Jakubantalik/Libraries.dev
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock.App.Effects.MetalRim;

public static class MetalRimPreview
{
    public static bool TryRender(string[] args)
    {
        const string option = "--rendermetal=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        try
        {
            string folder = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(folder);
            using var renderer = new OffscreenRenderer(640, 80);
            foreach (var (name, dark, bend) in new[]
            {
                ("silver", true, false), ("gold", true, false), ("chromatic", true, false),
                ("silver", false, false), ("silver", true, true)
            })
            {
                var effect = MetalRimEffect.ApplyPreset(name, dark);
                effect.Size = new Size(640, 80);
                effect.Time = 4;
                effect.CursorX = 0.3;
                effect.CursorY = 0;
                effect.BendStrength = bend ? 0.74 : 0;
                if (!effect.IsAvailable) throw new InvalidOperationException(ShaderCompiler.Error ?? "Shader unavailable.");

                // RenderTargetBitmap is software-only and skips ps_3_0 effects.
                // Execute the same HLSL/constants in D3D11 WARP, then composite
                // that premultiplied surface with the dock fill using WPF.
                BitmapSource rim = renderer.Render(effect);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var fill = new LinearGradientBrush(Color.FromRgb(67, 70, 76), Color.FromRgb(30, 33, 39), 90);
                    dc.DrawRoundedRectangle(fill, null, new Rect(MetalRimEffect.GlowPadding, MetalRimEffect.GlowPadding, 640, 80), 40, 40);
                    dc.DrawImage(rim, new Rect(0, 0, rim.PixelWidth, rim.PixelHeight));
                }
                var target = new RenderTargetBitmap(rim.PixelWidth, rim.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                target.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                string filename = $"{name}-{(dark ? "dark" : "light")}{(bend ? "-bend" : "")}.png";
                using var stream = File.Create(Path.Combine(folder, filename));
                encoder.Save(stream);
            }
            File.WriteAllText(Path.Combine(folder, "render-info.txt"),
                $"ps_3_0; {ShaderCompiler.Bytecode!.Length} byte shader; D3D11 WARP (same HLSL, ps_4_0 bindings) + RenderTargetBitmap; " +
                "640x80 capsule, 24px transparent glow margin (688x128 PNG); Time=4; bend=0.74 at (0.3,0).\n");
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Preview failed: {ex}");
            Environment.ExitCode = 1;
        }
        return true;
    }

    /// <summary>Diagnostic-only software D3D11 readback; no window or dock state changes.</summary>
    internal sealed class OffscreenRenderer : IDisposable
    {
        private readonly int _width, _height;
        private IntPtr _device, _context, _target, _view, _readback, _vertexShader, _pixelShader, _constants;

        internal OffscreenRenderer(int width, int height)
        {
            _width = width + 2 * MetalRimEffect.GlowPadding;
            _height = height + 2 * MetalRimEffect.GlowPadding;
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
                var pixel = ShaderCompiler.CompilePreview(vertex: false);
                Check(Call<CreateShader>(_device, 12)(_device, vertex, (nuint)vertex.Length, IntPtr.Zero, out _vertexShader));
                Check(Call<CreateShader>(_device, 15)(_device, pixel, (nuint)pixel.Length, IntPtr.Zero, out _pixelShader));
                var buffer = new BufferDescription { ByteWidth = 21 * 16, BindFlags = 4 }; // CONSTANT_BUFFER
                Check(Call<CreateBuffer>(_device, 3)(_device, ref buffer, IntPtr.Zero, out _constants));
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

        internal BitmapSource Render(MetalRimEffect effect)
        {
            Call<Update>(_context, 48)(_context, _constants, 0, IntPtr.Zero, effect.GetShaderConstants(), 0, 0);
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
            Release(ref _constants); Release(ref _pixelShader); Release(ref _vertexShader);
            Release(ref _readback); Release(ref _view); Release(ref _target);
            Release(ref _context); Release(ref _device);
        }

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
}
