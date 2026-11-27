using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Floe.Core.Services;
using Vortice.D3DCompiler;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Runtime HLSL compiles, kept on disk. The GPU looks build their shaders from source, and the big ones (the Blade
/// Runner strips) take one to nine seconds in the compiler: every start and every change of look froze the strip
/// for that long, and the dock's UI with it while a new layer waited for its device. The compiled bytecode is keyed
/// by the source, entry, profile, flags and compiler build, so an edited shader simply misses and compiles anew.
/// Two threads asking for the same shader at once share one compile.
/// </summary>
internal static class GpuShaderCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Folder, "shader-cache");

    private static readonly string CompilerBuild = typeof(Compiler).Assembly.GetName().Version?.ToString() ?? "?";
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> InFlight = new();

    /// <summary>The same as <see cref="Compiler.Compile(string, string, string, string, ShaderFlags, EffectFlags)"/>, from disk when it was compiled before.</summary>
    public static ReadOnlyMemory<byte> Compile(string source, string entry, string sourceName, string profile, ShaderFlags flags, EffectFlags effectFlags)
    {
        string key = Key(source, entry, profile, flags, effectFlags);
        var lazy = InFlight.GetOrAdd(key, k => new Lazy<byte[]>(() => Load(k, source, entry, sourceName, profile, flags, effectFlags), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return lazy.Value;
        }
        catch
        {
            // a failed compile is not remembered: the next ask tries again (and reports its own error)
            InFlight.TryRemove(key, out _);
            throw;
        }
    }

    private static byte[] Load(string key, string source, string entry, string sourceName, string profile, ShaderFlags flags, EffectFlags effectFlags)
    {
        string path = Path.Combine(Dir, key + ".cso");
        try
        {
            if (File.Exists(path))
            {
                var cached = File.ReadAllBytes(path);
                if (cached.Length > 0) return cached;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var watch = Stopwatch.StartNew();
        var code = Compiler.Compile(source, entry, sourceName, profile, flags, effectFlags).ToArray();
        Debug.WriteLine($"[Floe.Gpu] compiled {sourceName} {profile} in {watch.ElapsedMilliseconds} ms");
        try
        {
            Directory.CreateDirectory(Dir);
            // written aside and moved in, so a reader never meets half a file
            string temp = path + "." + Environment.ProcessId + "." + Environment.CurrentManagedThreadId + ".tmp";
            File.WriteAllBytes(temp, code);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return code;
    }

    private static string Key(string source, string entry, string profile, ShaderFlags flags, EffectFlags effectFlags)
    {
        var text = $"{CompilerBuild}\n{entry}\n{profile}\n{(int)flags}\n{(int)effectFlags}\n{source}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..40];
    }
}
