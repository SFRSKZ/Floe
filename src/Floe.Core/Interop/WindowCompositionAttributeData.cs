using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

internal enum WindowCompositionAttribute
{
    WcaAccentPolicy = 19,
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowCompositionAttributeData
{
    public WindowCompositionAttribute Attribute;
    public IntPtr Data;
    public int SizeOfData;
}
