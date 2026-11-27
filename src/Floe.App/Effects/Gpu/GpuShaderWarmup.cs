using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Floe.App.Services;

namespace Floe.App.Effects.Gpu;

/// <summary>
/// Compiles the GPU looks' shaders into <see cref="GpuShaderCache"/> on one low-priority background thread, a while
/// after the dock starts: the chosen look first, then the rest of the theme's round. Once they are on disk a change
/// of look (by hand or by the automatic round) builds its scene in milliseconds instead of seconds.
/// </summary>
internal static class GpuShaderWarmup
{
    private static int _started;

    public static void Start(VisualizerStyle first, bool cyber)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        var looks = GpuSceneCatalog.Round(cyber, _ => true).Where(look => look != first).Prepend(first).Where(GpuSceneCatalog.Has).ToArray();
        var thread = new Thread(() =>
        {
            // let the dock finish starting first
            Thread.Sleep(TimeSpan.FromSeconds(8));
            var watch = Stopwatch.StartNew();
            foreach (var look in looks)
            {
                try
                {
                    using var scene = GpuSceneCatalog.Create(look);
                    (scene as FeedbackGpuScene)?.WarmShaders();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Floe.Gpu] warm {look}: {ex.Message}");
                }
            }

            Debug.WriteLine($"[Floe.Gpu] warmed {looks.Length} looks in {watch.ElapsedMilliseconds} ms");
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.Lowest,
            Name = "FloeShaderWarmup",
        };
        thread.Start();
    }
}
