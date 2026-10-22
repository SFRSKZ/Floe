using System;
using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class ShellContextMenu
{
    private const uint CmfNormal = 0x00000000;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const int ExtrasIdBase = 0x8000;
    private const int SwShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CmInvokeCommandInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        void EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        void QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CmInvokeCommandInfo pici);
        void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    public static bool Show(
        IntPtr ownerHwnd,
        string path,
        int screenX,
        int screenY,
        IReadOnlyList<(string Label, Action Action)>? extras = null,
        bool showMenu = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var pidl = IntPtr.Zero;
        var parent = IntPtr.Zero;
        var child = IntPtr.Zero;
        var childArray = IntPtr.Zero;
        var menu = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;

        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            var iidShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
            if (SHBindToParent(pidl, ref iidShellFolder, out parent, out child) != 0 || parent == IntPtr.Zero)
            {
                return false;
            }

            folder = (IShellFolder)Marshal.GetObjectForIUnknown(parent);

            // GetUIObjectOf expects a pointer to an array of item-id lists (cidl entries).
            childArray = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(childArray, child);

            var iidContextMenu = new Guid("000214E4-0000-0000-C000-000000000046");
            folder.GetUIObjectOf(ownerHwnd, 1, childArray, ref iidContextMenu, IntPtr.Zero, out var contextMenuPtr);
            if (contextMenuPtr == IntPtr.Zero)
            {
                return false;
            }

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            menu = CreatePopupMenu();
            contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, CmfNormal);

            if (extras is { Count: > 0 })
            {
                AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                for (var i = 0; i < extras.Count; i++)
                {
                    AppendMenu(menu, MfString, (UIntPtr)(uint)(ExtrasIdBase + i), extras[i].Label);
                }
            }

            if (!showMenu)
            {
                return true;
            }

            WindowStyling.SetNoActivate(ownerHwnd, false);
            WindowStyling.SetTopmost(ownerHwnd, false);
            SetForegroundWindow(ownerHwnd);

            var command = TrackPopupMenuEx(
                menu,
                TpmLeftAlign | TpmReturnCmd | TpmRightButton,
                screenX,
                screenY,
                ownerHwnd,
                IntPtr.Zero);

            if (command >= ExtrasIdBase && extras is not null && command - ExtrasIdBase < extras.Count)
            {
                extras[command - ExtrasIdBase].Action();
            }
            else if (command > 0)
            {
                var invoke = new CmInvokeCommandInfo
                {
                    cbSize = Marshal.SizeOf<CmInvokeCommandInfo>(),
                    fMask = 0,
                    hwnd = ownerHwnd,
                    lpVerb = (IntPtr)(command - 1),
                    nShow = SwShowNormal,
                };
                contextMenu.InvokeCommand(ref invoke);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            WindowStyling.SetNoActivate(ownerHwnd, true);
            WindowStyling.SetTopmost(ownerHwnd, true);

            if (childArray != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(childArray);
            }

            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenu is not null)
            {
                Marshal.ReleaseComObject(contextMenu);
            }

            if (folder is not null)
            {
                Marshal.ReleaseComObject(folder);
            }

            if (pidl != IntPtr.Zero)
            {
                ILFree(pidl);
            }
        }
    }
}
