using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.Core.Models;
using Floe.Core.Services;
using Floe.Core.Services.MonoIcons;

namespace Floe.App.Services.MonoIcons;

/// <summary>
/// The black icon set of the Cyberpunk dock: each app's logo as a mask the dock paints in #111316. The dock never waits for
/// it. An app shows its own icon until its black one is ready; a worker prepares the missing ones one after another, once
/// per run checks that the ones it has still match their app, and tells the dock when something new landed.
/// </summary>
public static class MonoIconSet
{
    /// <summary>The dock ink the whole set is drawn in.</summary>
    public static readonly Color Ink = Color.FromRgb(0x11, 0x13, 0x16);

    private static readonly Lazy<MonoIconEngine> Engine = new(() =>
    {
        var cache = new MonoIconCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.Folder, "mono-icons"));
        var bank = MonoIconBank.Load(Path.Combine(AppContext.BaseDirectory, "IconBank"));
        return new MonoIconEngine(bank, cache, Path.Combine(Path.GetTempPath(), Product.Folder + "-mono"));
    });

    private static readonly Dictionary<string, ImageBrush?> Masks = new();
    private static readonly HashSet<string> Queued = new();
    private static readonly Queue<(string Key, DockApp App)> Pending = new();
    private static readonly object Gate = new();
    private static bool _running;

    /// <summary>Raised on a worker thread after new black icons were stored.</summary>
    public static event Action? Updated;

    /// <summary>The app's black icon as a mask, or nothing while it is not ready or the app keeps its own icon.</summary>
    public static ImageBrush? Mask(DockApp app)
    {
        var key = KeyOf(app);
        if (key.Length == 0) return null;
        lock (Gate)
        {
            if (Masks.TryGetValue(key, out var known)) return known;
        }

        try
        {
            var file = Engine.Value.Stored(key);
            var brush = file is null ? null : Load(file);
            lock (Gate) Masks[key] = brush;
            return brush;
        }
        catch
        {
            return null;
        }
        finally
        {
            Enqueue(key, app);
        }
    }

    private static string KeyOf(DockApp app) => MonoIconCache.KeyOf(string.IsNullOrWhiteSpace(app.Target) ? app.Name : app.Target);

    private static void Enqueue(string key, DockApp app)
    {
        lock (Gate)
        {
            if (!Queued.Add(key)) return;
            Pending.Enqueue((key, new DockApp { Name = app.Name, Target = app.Target, IconPath = app.IconPath }));
            if (_running) return;
            _running = true;
        }

        // low priority: a first launch with many apps must not slow the dock down
        var worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "MonoIcons" };
        worker.Start();
    }

    private static void Work()
    {
        while (true)
        {
            (string Key, DockApp App) job;
            lock (Gate)
            {
                if (Pending.Count == 0)
                {
                    _running = false;
                    return;
                }

                job = Pending.Dequeue();
            }

            var changed = false;
            try
            {
                var before = Engine.Value.Stored(job.Key);
                var written = before is null ? default : File.GetLastWriteTimeUtc(before);
                var after = Engine.Value.Prepare(job.Key, job.App.Name, job.App.Target, job.App.IconPath);
                // a new icon, a dropped one, or the same file rewritten because its app was updated
                changed = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase) || (after is not null && File.GetLastWriteTimeUtc(after) != written);
            }
            catch
            {
                // an app whose icon cannot be prepared keeps its own
            }

            if (!changed) continue;
            lock (Gate) Masks.Remove(job.Key);
            try { Updated?.Invoke(); } catch { /* the dock is going away */ }
        }
    }

    private static ImageBrush Load(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(file);
        image.DecodePixelWidth = 192;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        image.Freeze();
        var brush = new ImageBrush(image) { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return brush;
    }
}
