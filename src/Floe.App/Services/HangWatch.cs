using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>
/// Watches the UI thread from the side. When it stops answering for a few seconds the log gets the last step the
/// dock marked before it went quiet, and the first stall of a run leaves a dump next to the log, so a freeze that
/// Windows ends up killing still says where it was.
/// </summary>
public static class HangWatch
{
    private static Dispatcher? _ui;
    private static long _lastPong;
    private static string _mark = "";
    private static long _markAt;
    private static bool _dumped;
    private static string Tag => Product.Cp77 ? "cp77" : "floe";

    public static void Start(Dispatcher ui)
    {
        if (_ui is not null) return;
        _ui = ui;
        _lastPong = Stopwatch.GetTimestamp();
        new Thread(Watch) { IsBackground = true, Name = "FloeHangWatch" }.Start();
    }

    /// <summary>Names the step the UI thread is about to take; a stall reports the last one.</summary>
    public static void Mark(string step)
    {
        Volatile.Write(ref _mark, step);
        Interlocked.Exchange(ref _markAt, Stopwatch.GetTimestamp());
    }

    private static void Watch()
    {
        bool stuck = false;
        while (true)
        {
            Thread.Sleep(500);
            var ui = _ui!;
            if (ui.HasShutdownStarted) return;
            ui.BeginInvoke(DispatcherPriority.Send, new Action(() => Interlocked.Exchange(ref _lastPong, Stopwatch.GetTimestamp())));

            double quiet = Seconds(Interlocked.Read(ref _lastPong));
            if (!stuck && quiet > 2.5)
            {
                stuck = true;
                Log($"ui stuck {quiet:F1}s last='{Volatile.Read(ref _mark)}' {Seconds(Interlocked.Read(ref _markAt)):F1}s ago");
                if (!_dumped)
                {
                    _dumped = true;
                    WriteDump();
                }
            }
            else if (stuck && quiet < 1)
            {
                stuck = false;
                Log("ui back");
            }
        }
    }

    private static double Seconds(long since) => (Stopwatch.GetTimestamp() - since) / (double)Stopwatch.Frequency;

    private static void WriteDump()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"floe-hang-{Tag}.dmp");
            using var process = Process.GetCurrentProcess();
            using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            // full memory, so the managed stacks of every thread can be read back
            const int type = 0x2 | 0x4 | 0x800 | 0x1000; // FullMemory | HandleData | FullMemoryInfo | ThreadInfo
            bool ok = MiniDumpWriteDump(process.Handle, (uint)process.Id, file.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Log(ok ? $"hang dump {path}" : $"hang dump failed {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            Log($"hang dump failed {ex.Message}");
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "floe.log"), $"{DateTime.Now:HH:mm:ss} [{Tag}] {line}{Environment.NewLine}");
        }
        catch
        {
            // logging must never throw
        }
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, Microsoft.Win32.SafeHandles.SafeFileHandle file, int type, IntPtr exception, IntPtr userStream, IntPtr callback);
}
