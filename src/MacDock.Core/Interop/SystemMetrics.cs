using System;
using System.Runtime.InteropServices;

namespace MacDock.Core.Interop;

public static class SystemMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    private static ulong _prevIdle;
    private static ulong _prevKernel;
    private static ulong _prevUser;

    /// <summary>Approximate system CPU usage (0..100) since the previous call.</summary>
    public static int GetCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleValue = ToUlong(idle);
        var kernelValue = ToUlong(kernel);
        var userValue = ToUlong(user);

        var idleDelta = idleValue - _prevIdle;
        var totalDelta = (kernelValue - _prevKernel) + (userValue - _prevUser);

        _prevIdle = idleValue;
        _prevKernel = kernelValue;
        _prevUser = userValue;

        if (totalDelta == 0 || idleDelta > totalDelta)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta), 0, 100);
    }

    private static ulong ToUlong(FileTime time) => ((ulong)time.High << 32) | time.Low;
}
