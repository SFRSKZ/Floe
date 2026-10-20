using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

/// <summary>Opens files with a packaged (Store) app, which has no exe to pass arguments to.</summary>
public static class StoreAppActivator
{
    public static bool ActivateForFiles(string aumid, IReadOnlyList<string> paths)
    {
        if (string.IsNullOrWhiteSpace(aumid) || paths.Count == 0)
        {
            return false;
        }

        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            var ok = true;
            foreach (var path in paths)
            {
                // one call per file keeps it simple; packaged apps de-duplicate into one window
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) < 0)
                {
                    ok = false;
                    continue;
                }

                try
                {
                    if (SHCreateShellItemArrayFromShellItem(item, typeof(IShellItemArray).GUID, out var array) < 0)
                    {
                        ok = false;
                        continue;
                    }

                    try
                    {
                        ok &= manager.ActivateForFile(aumid, array, "open", out _) >= 0;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(array);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }

            return ok;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(IShellItem item, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray array);

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);

        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IShellItemArray itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IShellItemArray itemArray, out uint processId);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
    }
}
