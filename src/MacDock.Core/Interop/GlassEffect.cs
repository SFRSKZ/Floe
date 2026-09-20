using System.Runtime.InteropServices;
using MacDock.Core.Models;

namespace MacDock.Core.Interop;

public static class GlassEffect
{
    private const int WcaAccentPolicy = 19;

    private enum AccentState
    {
        Disabled = 0,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public static void Apply(IntPtr hwnd, GlassMode mode, byte a, byte r, byte g, byte b)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var state = mode switch
        {
            GlassMode.BlurBehind => AccentState.EnableBlurBehind,
            GlassMode.Acrylic => AccentState.EnableAcrylicBlurBehind,
            _ => AccentState.Disabled,
        };

        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = 2,
            GradientColor = ToAbgr(a, r, g, b),
        };

        ApplyPolicy(hwnd, policy);
    }

    public static void Disable(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyPolicy(hwnd, new AccentPolicy { AccentState = AccentState.Disabled });
    }

    private static void ApplyPolicy(IntPtr hwnd, AccentPolicy policy)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, ptr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = (WindowCompositionAttribute)WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };

            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static uint ToAbgr(byte a, byte r, byte g, byte b)
        => (uint)((a << 24) | (b << 16) | (g << 8) | r);
}
