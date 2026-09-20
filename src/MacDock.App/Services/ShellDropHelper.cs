using System.Runtime.InteropServices;
using System.Text;

namespace MacDock.App.Services;

public static class ShellDropHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    public static IReadOnlyList<string> ParseIdListArray(byte[] data)
    {
        var result = new List<string>();

        if (data.Length < 8)
        {
            return result;
        }

        var count = BitConverter.ToUInt32(data, 0);
        if (count == 0 || count > 4096)
        {
            return result;
        }

        var offsets = new int[count + 1];
        var offsetBase = 4;
        for (var i = 0; i <= count; i++)
        {
            var position = offsetBase + (i * 4);
            if (position + 4 > data.Length)
            {
                return result;
            }

            offsets[i] = (int)BitConverter.ToUInt32(data, position);
        }

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var start = handle.AddrOfPinnedObject();
            var parent = IntPtr.Add(start, offsets[0]);

            for (var i = 1; i <= count; i++)
            {
                if (offsets[i] < 0 || offsets[i] >= data.Length)
                {
                    continue;
                }

                var child = IntPtr.Add(start, offsets[i]);
                var combined = ILCombine(parent, child);
                if (combined == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var buffer = new StringBuilder(1024);
                    if (SHGetPathFromIDList(combined, buffer))
                    {
                        result.Add(buffer.ToString());
                    }
                }
                finally
                {
                    ILFree(combined);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return result;
    }
}
