using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Floe.Core.Interop;

public static class ScreenshotKeyboard
{
    private static readonly HookProc Callback = Hook;
    private static readonly UIntPtr Marker = new(0x4D445243);
    private static readonly HashSet<uint> Swallowed = new();
    private static IntPtr _hook;
    private static Action<Action>? _capture;
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    public static void Start(Action<Action> capture)
    {
        _capture = capture;
        _hook = SetWindowsHookEx(13, Callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _capture = null;
        Swallowed.Clear();
    }

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data);
            if (key.ExtraInfo == Marker) return CallNextHookEx(_hook, code, message, data);
            var down = message.ToInt64() is 0x100 or 0x104;
            var up = message.ToInt64() is 0x101 or 0x105;
            if (up && Swallowed.Remove(key.Key)) return (IntPtr)1;
            if (down && Swallowed.Contains(key.Key)) return (IntPtr)1;
            if (down && (key.Key == 0x2C || (key.Key == 0x53 && (Down(0x5B) || Down(0x5C)) && Down(0x10))))
            {
                var modifiers = new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }.Where(Down).ToArray();
                Swallowed.Add(key.Key);
                _capture?.Invoke(() => Replay(key, modifiers));
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static void Replay(KeyboardData key, int[] modifiers)
    {
        var added = modifiers.Where(k => !Down(k)).ToArray();
        var inputs = new List<Input>();
        foreach (var modifier in added) inputs.Add(Key((uint)modifier, false, modifier is 0x5B or 0x5C));
        inputs.Add(Key(key.Key, false, (key.Flags & 1) != 0));
        inputs.Add(Key(key.Key, true, (key.Flags & 1) != 0));
        foreach (var modifier in added.Reverse()) inputs.Add(Key((uint)modifier, true, modifier is 0x5B or 0x5C));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    private static Input Key(uint key, bool up, bool extended) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KeyboardInput { Key = (ushort)key, Flags = (up ? 2u : 0) | (extended ? 1u : 0), ExtraInfo = Marker } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
