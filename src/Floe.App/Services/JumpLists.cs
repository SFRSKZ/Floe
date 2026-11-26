using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Floe.App.Services;

internal sealed record JumpEntry(int Id, string Title, ImageSource? Icon);

internal sealed record JumpGroup(string Title, IReadOnlyList<JumpEntry> Entries);

/// <summary>
/// The Jump List Windows keeps for an app (its tasks, its own categories such as Steam's recent games, and the
/// recent and pinned files), read the way Explorer's taskbar reads it. The shell objects behind it are not public
/// API: the interfaces and their slots follow Open-Shell's JumpLists.cpp and System Informer's appresolverp.h, and
/// the approach OpenTaskbar's NativeJumpListSession (MIT, see JumpLists.LICENSE.txt). Everything runs on one STA
/// thread of its own, so a slow or broken list never holds the dock; only frozen menu data comes back.
/// </summary>
internal static class JumpLists
{
    private static readonly Guid ResolverClass = new("660B90C8-73A9-4B58-8CAE-355B7F55341B");
    private static readonly Guid ResolverInterface = new("DE25675A-72DE-44B4-9373-05170450C140");
    private static readonly Guid DestinationClass = new("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
    private static readonly Guid DestinationInterface = new("507101CD-F6AD-46C8-8E20-EEB9E6BAC47F");
    private static readonly Guid AutomaticClass = new("F0AE1542-F497-484B-A175-A20DB09144BA");
    private static readonly Guid AutomaticInterface = new("E9C5EF8D-FD41-4F72-BA87-EB03BAD5817C");
    private static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid ObjectArrayInterface = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid PropertyStoreInterface = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid ContextMenuInterface = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid ImageFactoryInterface = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid UnknownInterface = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid BindContextMenu = new("3981E225-F559-11D3-8E3A-00C04F6837D5");
    private static readonly Guid BindPropertyStore = new("0384E1A4-1523-439C-A4C8-AB911052F586");
    private static readonly Guid AppsFolder = new("1E87508D-89C2-42F0-8A7E-645A0F50CA58");
    private static readonly PropertyKey AppIdKey = new(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly PropertyKey SeparatorKey = new(AppIdKey.Format, 6);
    private static readonly PropertyKey HostEnvironmentKey = new(AppIdKey.Format, 14);
    private static readonly PropertyKey ActivationContextKey = new(AppIdKey.Format, 20);
    private static readonly PropertyKey TitleKey = new(new("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private const int MaxPerGroup = 10;
    private const int MaxEntries = 40;

    private static readonly object Gate = new();
    private static Dispatcher? _worker;
    // the shell items behind the menu that is open now; a new read drops the old ones
    private static readonly Dictionary<int, Com> Commands = [];
    private static int _session;
    private static int _nextId;
    private static IntPtr _targetWindow;
    private static Dictionary<string, string>? _startMenuLinks;

    /// <summary>The groups of the app's list; empty when it has none or it can't be read.</summary>
    public static Task<IReadOnlyList<JumpGroup>> ReadAsync(IntPtr window, string? target)
    {
        var worker = Worker();
        return worker.InvokeAsync(() =>
        {
            Release();
            try
            {
                return Read(window, target);
            }
            catch (Exception ex)
            {
                Log($"jumplist read failed {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
                Release();
                return (IReadOnlyList<JumpGroup>)Array.Empty<JumpGroup>();
            }
        }).Task;
    }

    /// <summary>Runs an entry of the list read last, as the taskbar would.</summary>
    public static void Invoke(int id, IntPtr owner)
    {
        Worker().BeginInvoke(() =>
        {
            try
            {
                if (Commands.TryGetValue(id, out var item)) Run(item, owner);
            }
            catch (Exception ex)
            {
                Log($"jumplist invoke failed {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            }
        });
    }

    /// <summary>For the diagnostic: the list as text.</summary>
    public static string Describe(IntPtr window, string? target)
    {
        var groups = ReadAsync(window, target).GetAwaiter().GetResult();
        var text = new StringBuilder($"target={target} window={window} id={Worker().Invoke(() => Identity(window, target))}{Environment.NewLine}");
        foreach (var group in groups)
        {
            text.AppendLine($"[{group.Title}]");
            foreach (var entry in group.Entries) text.AppendLine($"  {(entry.Title.Length == 0 ? "----" : entry.Title)}{(entry.Icon is null ? "" : " (icon)")}");
        }

        return text.ToString();
    }

    private static Dispatcher Worker()
    {
        lock (Gate)
        {
            if (_worker is not null) return _worker;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                _worker = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true, Name = "FloeJumpLists" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return _worker!;
        }
    }

    private static void Release()
    {
        foreach (var item in Commands.Values) item.Dispose();
        Commands.Clear();
        _session++;
    }

    private static IReadOnlyList<JumpGroup> Read(IntPtr window, string? target)
    {
        _targetWindow = window;
        var appId = Identity(window, target);
        if (string.IsNullOrWhiteSpace(appId)) return Array.Empty<JumpGroup>();

        using var custom = Com.Create(DestinationClass, DestinationInterface);
        Check(custom.Method<SetAppId>(4)(custom.Handle, appId));
        using var automatic = Com.Create(AutomaticClass, AutomaticInterface);
        Check(automatic.Method<InitializeAutomatic>(3)(automatic.Handle, appId, 0, 0));

        var groups = new List<JumpGroup>();
        AddAutomatic(groups, automatic, 0, Loc.T("Sabitlenenler"));
        int result = custom.Method<GetCount>(6)(custom.Handle, out var count);
        if (Missing(result)) count = 0;
        else Check(result);

        bool known = false;
        for (uint index = 0; index < Math.Min(count, 32u) && Commands.Count < MaxEntries; index++)
        {
            Check(custom.Method<GetCategory>(7)(custom.Handle, index, 1, out var category));
            try
            {
                if (category.Type == 1)
                {
                    // a known category: Windows' own frequent (1) or recent (2) files
                    if (category.Kind is 1 or 2)
                    {
                        known = true;
                        AddAutomatic(groups, automatic, 3 - category.Kind, Loc.T(category.Kind == 1 ? "Sık kullanılanlar" : "Son kullanılanlar"));
                    }
                }
                else if (category.Type is 0 or 2)
                {
                    var iid = ObjectArrayInterface;
                    Check(custom.Method<EnumerateCategory>(9)(custom.Handle, index, ref iid, out var pointer));
                    using var array = new Com(pointer);
                    var title = category.Type == 2 ? Loc.T("Görevler") : IndirectText(Marshal.PtrToStringUni(category.NameOrKind));
                    AddGroup(groups, title, array);
                }
            }
            finally
            {
                if (category.Type == 0 && category.NameOrKind != 0) Marshal.FreeCoTaskMem(category.NameOrKind);
            }
        }

        if (!known) AddAutomatic(groups, automatic, 1, Loc.T("Son kullanılanlar"));
        return groups;
    }

    // ---- who the app is --------------------------------------------------------------------------------------

    private static string? Identity(IntPtr window, string? target)
    {
        if (window != IntPtr.Zero)
        {
            var explicitId = WindowAppId(window);
            if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;
            using var resolver = Com.Create(ResolverClass, ResolverInterface);
            if (resolver.Method<ResolveWindow>(5)(resolver.Handle, window, out var id, 0, 0, 0) >= 0) return TakeString(id);
        }

        if (string.IsNullOrWhiteSpace(target)) return null;
        var packaged = Floe.Core.Services.AppIdentity.TargetAppId(target);
        if (!string.IsNullOrEmpty(packaged)) return packaged;

        // an exe is known to the shell through the Start menu shortcut that launches it
        string? link = target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? target : StartMenuLink(target);
        return (link is null ? null : ShortcutAppId(link)) ?? ShortcutAppId(target);
    }

    private static string? WindowAppId(IntPtr window)
    {
        var iid = PropertyStoreInterface;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var pointer) < 0 || pointer == 0) return null;
        using var store = new Com(pointer);
        return ReadString(store, AppIdKey);
    }

    private static string? ShortcutAppId(string path)
    {
        var iid = ShellItemInterface;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var pointer) < 0 || pointer == 0) return null;
        using var item = new Com(pointer);
        using var resolver = Com.Create(ResolverClass, ResolverInterface);
        return resolver.Method<ResolveShortcut>(3)(resolver.Handle, item.Handle, out var id) >= 0 ? TakeString(id) : null;
    }

    private static string? StartMenuLink(string exe)
    {
        if (_startMenuLinks is null)
        {
            _startMenuLinks = new(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
            {
                if (!Directory.Exists(root)) continue;
                // the old localized names ("Programlar") are junctions that deny listing: skip them, not the walk
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var link in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    if (LinkTarget(link) is { } path && !_startMenuLinks.ContainsKey(path)) _startMenuLinks[path] = link;
                }
            }
        }

        return _startMenuLinks.TryGetValue(exe, out var found) ? found : null;
    }

    private static string? LinkTarget(string link)
    {
        try
        {
            var target = Floe.Core.Services.ShortcutReader.Read(link)?.Target;
            return string.IsNullOrWhiteSpace(target) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
        }
        catch
        {
            return null;
        }
    }

    // ---- the entries ------------------------------------------------------------------------------------------

    private static void AddAutomatic(List<JumpGroup> groups, Com automatic, int kind, string title)
    {
        var iid = ObjectArrayInterface;
        int result = automatic.Method<GetAutomaticList>(5)(automatic.Handle, kind, MaxPerGroup, 1, ref iid, out var pointer);
        if (Missing(result) || result < 0 || pointer == 0) return;
        using var array = new Com(pointer);
        AddGroup(groups, title, array);
    }

    private static void AddGroup(List<JumpGroup> groups, string? title, Com array)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        Check(array.Method<GetCount>(3)(array.Handle, out var count));
        var entries = new List<JumpEntry>();
        for (uint index = 0; index < count && entries.Count < MaxPerGroup && Commands.Count < MaxEntries; index++)
        {
            var iid = UnknownInterface;
            if (array.Method<GetAt>(4)(array.Handle, index, ref iid, out var pointer) < 0 || pointer == 0) continue;
            var item = new Com(pointer);
            bool kept = false;
            try
            {
                using var store = item.Query(PropertyStoreInterface);
                if (store is not null && IsSeparator(store)) continue;
                var label = store is null ? null : IndirectText(ReadString(store, TitleKey));
                if (string.IsNullOrWhiteSpace(label))
                {
                    using var shellItem = item.Query(ShellItemInterface);
                    if (shellItem is not null && shellItem.Method<GetDisplayName>(5)(shellItem.Handle, 0, out var name) >= 0) label = TakeString(name);
                }

                if (string.IsNullOrWhiteSpace(label)) continue;
                int id = ++_nextId;
                entries.Add(new JumpEntry(id, label, Icon(item)));
                Commands[id] = item;
                kept = true;
            }
            finally
            {
                if (!kept) item.Dispose();
            }
        }

        if (entries.Count > 0) groups.Add(new JumpGroup(title, entries));
    }

    private static ImageSource? Icon(Com item)
    {
        try
        {
            using var factory = item.Query(ImageFactoryInterface);
            if (factory is not null && factory.Method<GetImage>(3)(factory.Handle, new NativeSize(32, 32), 0, out var bitmap) >= 0 && bitmap != 0)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }

            // a task is usually a shell link that names its icon resource
            using var link = item.Query(ShellLinkInterface);
            if (link is null) return null;
            var path = new StringBuilder(1024);
            if (link.Method<GetIconLocation>(16)(link.Handle, path, path.Capacity, out var index) < 0) return null;
            if (path.Length == 0)
            {
                if (link.Method<GetLinkPath>(3)(link.Handle, path, path.Capacity, 0, 4) < 0 || path.Length == 0) return null;
                index = 0;
            }

            var file = Environment.ExpandEnvironmentVariables(path.ToString());
            var icons = new IntPtr[1];
            if (PrivateExtractIcons(file, index, 32, 32, icons, null, 1, 0) == 0 || icons[0] == 0) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(icons[0]);
            }
        }
        catch
        {
            return null;
        }
    }

    // ---- running one --------------------------------------------------------------------------------------------

    private static void Run(Com item, IntPtr owner)
    {
        using var store = item.Query(PropertyStoreInterface);
        var appId = store is null ? null : ReadString(store, AppIdKey);
        // a packaged app's task goes through its AppsFolder entry with the activation context it published
        if (appId?.Contains('!') == true)
        {
            var folder = AppsFolder;
            var iid = ShellItemInterface;
            Check(SHCreateItemInKnownFolder(ref folder, 0, appId, ref iid, out var pointer));
            using var target = new Com(pointer);
            using var properties = Bind(target, BindPropertyStore, PropertyStoreInterface);
            var key = HostEnvironmentKey;
            Check(properties.Method<GetValue>(5)(properties.Handle, ref key, out var host));
            try
            {
                if (host.Type == 19 && host.UInt32 != 0)
                {
                    using var menu = Bind(target, BindContextMenu, ContextMenuInterface);
                    InvokeDefault(menu, owner, store is null ? null : ReadString(store, ActivationContextKey));
                    return;
                }
            }
            finally
            {
                PropVariantClear(ref host);
            }
        }

        // with the app's own window as owner, a single-instance app keeps the task's flags (a private window)
        var contextOwner = _targetWindow != IntPtr.Zero ? _targetWindow : owner;
        using (var direct = item.Query(ContextMenuInterface))
        {
            if (direct is not null)
            {
                InvokeDefault(direct, contextOwner, null);
                return;
            }
        }

        using var shellItem = item.Query(ShellItemInterface) ?? throw new COMException("no way to run the entry");
        using var bound = Bind(shellItem, BindContextMenu, ContextMenuInterface);
        InvokeDefault(bound, contextOwner, null);
    }

    private static Com Bind(Com item, Guid handler, Guid iid)
    {
        Check(item.Method<BindHandler>(3)(item.Handle, 0, ref handler, ref iid, out var pointer));
        return new Com(pointer);
    }

    private static void InvokeDefault(Com menu, IntPtr owner, string? activationContext)
    {
        var popup = CreatePopupMenu();
        try
        {
            Check(menu.Method<QueryMenu>(3)(menu.Handle, popup, 0, 1, 0x7fff, 1));
            var command = GetMenuDefaultItem(popup, false, 0);
            if (command == uint.MaxValue || command == 0) throw new InvalidOperationException("no default command");
            var info = new InvokeInfo
            {
                Size = Marshal.SizeOf<InvokeInfo>(),
                Mask = 0x4000, // CMIC_MASK_UNICODE
                Window = owner,
                Verb = (nint)(command - 1),
                VerbW = (nint)(command - 1),
                Show = 1,
                Parameters = activationContext,
                ParametersW = activationContext,
            };
            Check(menu.Method<InvokeMenu>(4)(menu.Handle, ref info));
        }
        finally
        {
            DestroyMenu(popup);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static string? ReadString(Com store, PropertyKey key)
    {
        if (store.Method<GetValue>(5)(store.Handle, ref key, out var value) < 0) return null;
        try
        {
            return value.Type is 31 or 8 ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static bool IsSeparator(Com store)
    {
        var key = SeparatorKey;
        if (store.Method<GetValue>(5)(store.Handle, ref key, out var value) < 0) return false;
        try
        {
            return value.Type == 11 && value.Bool != 0;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static string? IndirectText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith('@')) return text;
        var resolved = new StringBuilder(1024);
        return SHLoadIndirectString(text, resolved, (uint)resolved.Capacity, 0) >= 0 ? resolved.ToString() : null;
    }

    private static string? TakeString(nint pointer)
    {
        if (pointer == 0) return null;
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static bool Missing(int result) => result is unchecked((int)0x80030002) or unchecked((int)0x80070002);

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "floe.log"), $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // logging must never throw
        }
    }

    /// <summary>A raw COM pointer, its methods called by vtable slot; lives and dies on the worker thread.</summary>
    private sealed class Com(nint handle) : IDisposable
    {
        public nint Handle { get; private set; } = handle;

        public static Com Create(Guid classId, Guid interfaceId)
        {
            Check(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out var pointer));
            return new Com(pointer);
        }

        public Com? Query(Guid interfaceId)
        {
            return Marshal.QueryInterface(Handle, in interfaceId, out var pointer) >= 0 && pointer != 0 ? new Com(pointer) : null;
        }

        public T Method<T>(int slot)
            where T : Delegate
        {
            ObjectDisposedException.ThrowIf(Handle == 0, this);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(Handle), slot * IntPtr.Size));
        }

        public void Dispose()
        {
            if (Handle == 0) return;
            Marshal.Release(Handle);
            Handle = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
        [FieldOffset(8)] public short Bool;
        [FieldOffset(8)] public uint UInt32;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct Category
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public nint NameOrKind;
        [FieldOffset(8)] public int Kind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public int Size;
        public uint Mask;
        public nint Window;
        public nint Verb;
        [MarshalAs(UnmanagedType.LPStr)] public string? Parameters;
        public nint Directory;
        public int Show;
        public uint HotKey;
        public nint Icon;
        public nint Title;
        public nint VerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ParametersW;
        public nint DirectoryW;
        public nint TitleW;
        public int PointX;
        public int PointY;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetAppId(nint self, [MarshalAs(UnmanagedType.LPWStr)] string id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCount(nint self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetCategory(nint self, uint index, int flags, out Category category);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumerateCategory(nint self, uint index, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitializeAutomatic(nint self, [MarshalAs(UnmanagedType.LPWStr)] string id, nint link, nint extra);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAutomaticList(nint self, int kind, uint max, uint flags, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResolveWindow(nint self, nint window, out nint id, nint prevented, nint explicitId, nint embedded);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResolveShortcut(nint self, nint item, out nint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetValue(nint self, ref PropertyKey key, out Variant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetAt(nint self, uint index, ref Guid iid, out nint item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDisplayName(nint self, uint kind, out nint name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetImage(nint self, NativeSize size, uint flags, out nint bitmap);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetIconLocation(nint self, StringBuilder path, int length, out int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int GetLinkPath(nint self, StringBuilder path, int length, nint findData, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int BindHandler(nint self, nint context, ref Guid handler, ref Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int QueryMenu(nint self, nint menu, uint index, uint first, uint last, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InvokeMenu(nint self, ref InvokeInfo info);

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId, out nint pointer);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, out nint store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out nint item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemInKnownFolder(ref Guid folder, uint flags, string path, ref Guid iid, out nint item);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint count, nint reserved);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Variant value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[]? ids, uint count, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] private static extern uint GetMenuDefaultItem(nint menu, bool byPosition, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
}
