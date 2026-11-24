using System;
using System.Security.Cryptography;
using System.Text;

namespace Floe.Core.Services;

/// <summary>
/// Names a compiled pixel shader by its HLSL source and profile. The shaders are compiled once at build time
/// (tools/compile-shaders.py) into Effects/ShaderCache/&lt;key&gt;.cso; an edited source gets a new key, so a stale
/// blob is never picked and the dock compiles that one itself.
/// </summary>
public static class ShaderKey
{
    public static string For(byte[] source, string target)
    {
        // line endings depend on the checkout, the bytecode does not
        var bytes = new byte[source.Length + target.Length + 1];
        int n = 0;
        foreach (var b in source)
        {
            if (b != (byte)'\r') bytes[n++] = b;
        }

        bytes[n++] = (byte)'|';
        n += Encoding.ASCII.GetBytes(target, 0, target.Length, bytes, n);
        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, n)))[..16].ToLowerInvariant();
    }
}
