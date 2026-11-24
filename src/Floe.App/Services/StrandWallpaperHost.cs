using System;
using System.Threading.Tasks;
using Floe.App.Effects.Gpu;

namespace Floe.App.Services;

/// <summary>
/// Keeps the strand curtain on the desktop while its setting is on: started and stopped off the UI thread,
/// since joining the shell's wallpaper layer and building a device can take a moment.
/// </summary>
internal static class StrandWallpaperHost
{
    private static readonly object Gate = new();
    private static DesktopWallpaper? _wallpaper;
    private static bool _attached, _starting;
    // the scene the running (or starting) wallpaper plays
    private static string _scene = "";

    public static void Start()
    {
        if (!_attached)
        {
            _attached = true;
            GlassSettings.Changed += Apply;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
        }

        Apply();
    }

    /// <summary>Takes the curtain off and waits for the desktop to get its picture back.</summary>
    public static void Stop()
    {
        DesktopWallpaper? old;
        lock (Gate)
        {
            old = _wallpaper;
            _wallpaper = null;
        }

        old?.Dispose();
    }

    private static void Apply()
    {
        bool want = GlassSettings.StrandWallpaper;
        string scene = GlassSettings.DesktopScene;
        lock (Gate)
        {
            bool on = _wallpaper is not null || _starting;
            if (_starting) return;
            if (want == on && (!want || scene == _scene)) return;
            if (want)
            {
                _starting = true;
                _scene = scene;
                var old = _wallpaper;
                _wallpaper = null;
                Task.Run(() =>
                {
                    // another scene: the old one gives the desktop back before the new one takes it
                    old?.Dispose();
                    var made = new DesktopWallpaper(() => Effects.Gpu.Desktop.DesktopScenes.Create(scene) ?? new StrandCurtainGpuScene(desktop: true));
                    bool keep;
                    lock (Gate)
                    {
                        _starting = false;
                        keep = _wallpaper is null && GlassSettings.StrandWallpaper && GlassSettings.DesktopScene == scene;
                        if (keep) _wallpaper = made;
                    }

                    if (!keep)
                    {
                        made.Dispose();
                        // the setting moved on while this one started; catch up with it
                        Apply();
                    }
                });
                return;
            }
        }

        Task.Run(Stop);
    }
}
