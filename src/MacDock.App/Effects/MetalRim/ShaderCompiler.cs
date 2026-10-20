// Ported from metal-fx by Jakub Antalik (MIT), https://github.com/Jakubantalik/Libraries.dev
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Effects;

namespace MacDock.App.Effects.MetalRim;

internal static class ShaderCompiler
{
    // Cache failures as well as successes; never repeatedly compile on the UI thread.
    private static readonly Lazy<byte[]?> Cached = new(Compile);
    internal static byte[]? Bytecode => Cached.Value;
    internal static string? Error { get; private set; }

    // RenderTargetBitmap cannot execute ps_3_0. The diagnostic runs the same
    // HLSL math on D3D11 WARP, changing only constant-buffer and output bindings.
    internal static byte[] CompilePreview(bool vertex)
    {
        string source;
        if (vertex)
        {
            source = "struct V { float4 position : SV_Position; float2 uv : TEXCOORD0; }; " +
                "V main(uint id : SV_VertexID) { V o; o.uv = float2((id << 1) & 2, id & 2); " +
                "o.position = float4(o.uv.x * 2 - 1, 1 - o.uv.y * 2, 0, 1); return o; }";
        }
        else
        {
            using var resource = typeof(ShaderCompiler).Assembly.GetManifestResourceStream("MacDock.MetalRim.hlsl")!;
            using var reader = new StreamReader(resource);
            source = reader.ReadToEnd();
            int start = source.IndexOf("float Time", StringComparison.Ordinal);
            int end = source.IndexOf("float4 main", StringComparison.Ordinal);
            string constants = System.Text.RegularExpressions.Regex.Replace(source[start..end], @"register\(c(\d+)\)", "packoffset(c$1)");
            source = source[..start] + "cbuffer Parameters : register(b0) {\n" + constants + "};\n" + source[end..];
            source = source.Replace(": COLOR0", ": SV_Target", StringComparison.Ordinal)
                .Replace("main(float2 uv : TEXCOORD0)", "main(float4 position : SV_Position, float2 uv : TEXCOORD0)", StringComparison.Ordinal);
        }
        return CompileBytes(System.Text.Encoding.UTF8.GetBytes(source), vertex ? "vs_4_0" : "ps_4_0");
    }

    internal static PixelShader CreatePixelShader(out bool available)
    {
        available = false;
        try
        {
            if (Bytecode is not { } code) return CreateIdentityShader();
            var shader = new PixelShader();
            using var stream = new MemoryStream(code, writable: false);
            shader.SetStreamSource(stream);
            shader.Freeze();
            available = true;
            return shader;
        }
        catch (Exception ex)
        {
            Log($"Loading pixel shader failed: {ex}");
            return CreateIdentityShader();
        }
    }

    private static PixelShader CreateIdentityShader()
    {
        // A null ShaderEffect.PixelShader throws when WPF renders it. This tiny
        // precompiled ps_2_0 identity works even if d3dcompiler_47.dll is missing.
        // Source: sampler2D input : register(s0);
        // float4 main(float2 uv : TEXCOORD0) : COLOR0 { return tex2D(input, uv); }
        const string identity = "AAL///7/HwBDVEFCHAAAAE8AAAAAAv//AQAAABwAAAAAgQAASAAAADAAAAADAAAAAQACADgAAAAAAAAAaW5wdXQAq6sEAAwAAQABAAEAAAAAAAAAcHNfMl8wAE1pY3Jvc29mdCAoUikgSExTTCBTaGFkZXIgQ29tcGlsZXIgMTAuMQCrHwAAAgAAAIAAAAOwHwAAAgAAAJAACA+gQgAAAwAAD4AAAOSwAAjkoAEAAAIACA+AAADkgP//AAA=";
        var shader = new PixelShader();
        using var stream = new MemoryStream(Convert.FromBase64String(identity), writable: false);
        shader.SetStreamSource(stream);
        shader.Freeze();
        return shader;
    }

    private static byte[]? Compile()
    {
        try
        {
            using var resource = typeof(ShaderCompiler).Assembly.GetManifestResourceStream("MacDock.MetalRim.hlsl")
                ?? throw new InvalidOperationException("Embedded MetalRim.hlsl was not found.");
            using var source = new MemoryStream();
            resource.CopyTo(source);
            return CompileBytes(source.ToArray(), "ps_3_0");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log($"Metal rim disabled: {ex}");
            return null;
        }
    }

    private static byte[] CompileBytes(byte[] bytes, string target)
    {
        IntPtr code = IntPtr.Zero, errors = IntPtr.Zero;
        try
        {
            // Optimization level 3. The ps_3_0 compiler enforces its 512-slot limit.
            int hr = D3DCompile(bytes, (nuint)bytes.Length, "MetalRim.hlsl", IntPtr.Zero, IntPtr.Zero,
                "main", target, 1u << 15, 0, out code, out errors);
            string diagnostics = errors == IntPtr.Zero ? "" : System.Text.Encoding.UTF8.GetString(ReadBlob(errors)).TrimEnd('\0');
            if (hr < 0 || code == IntPtr.Zero)
                throw new InvalidOperationException($"D3DCompile {target} 0x{hr:X8}: {diagnostics}");
            if (!string.IsNullOrWhiteSpace(diagnostics)) Log(diagnostics);
            return ReadBlob(code);
        }
        finally
        {
            if (errors != IntPtr.Zero) Marshal.Release(errors);
            if (code != IntPtr.Zero) Marshal.Release(code);
        }
    }

    internal static void Log(string message)
    {
        Trace.TraceError($"MetalRim: {message}");
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "macdock.log"), $"{DateTime.Now:O} MetalRim: {message}{Environment.NewLine}"); }
        catch { /* Diagnostics must never terminate the dock. */ }
    }

    private static byte[] ReadBlob(IntPtr blob)
    {
        IntPtr table = Marshal.ReadIntPtr(blob);
        var pointer = Marshal.GetDelegateForFunctionPointer<BlobPointer>(Marshal.ReadIntPtr(table, 3 * IntPtr.Size))(blob);
        var size = Marshal.GetDelegateForFunctionPointer<BlobSize>(Marshal.ReadIntPtr(table, 4 * IntPtr.Size))(blob);
        var bytes = new byte[checked((int)size)];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nuint BlobSize(IntPtr self);

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] source, nuint length, string sourceName, IntPtr defines, IntPtr include,
        string entryPoint, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
}
