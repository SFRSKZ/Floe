using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Floe.Core.Services;
using Floe.Core.Services.MonoIcons;

namespace Floe.App.Services.MonoIcons;

/// <summary>
/// Makes one app's black icon (#111316 on clear): the bank's verified logo when the app is in it, else the app's own icon
/// turned black by the bundled converter, else nothing, and the dock keeps the real icon. Nothing here touches WPF or
/// the UI thread, and nothing throws: any failure becomes an "own icon" record.
/// </summary>
public sealed class MonoIconEngine
{
    private readonly MonoIconBank _bank;
    private readonly MonoIconCache _cache;
    private readonly string _scratch;

    public MonoIconEngine(MonoIconBank bank, MonoIconCache cache, string scratch)
    {
        _bank = bank;
        _cache = cache;
        _scratch = scratch;
    }

    /// <summary>What the cache already holds for the app, whatever its age; cheap enough for the UI thread.</summary>
    public string? Stored(string key) => _cache.Known(key) is { File.Length: > 0 } record && File.Exists(_cache.PathOf(record.File)) ? _cache.PathOf(record.File) : null;

    /// <summary>
    /// Brings the app's record up to date and returns its PNG, or nothing when the app keeps its own icon. Work happens only
    /// when the source (the exe's identity, the package's logo, the bank entry) or the look has changed since the record.
    /// </summary>
    public string? Prepare(string key, string name, string target, string? iconPath)
    {
        try
        {
            var slug = _bank.Find(NameCandidates(name, target, iconPath));
            if (slug is not null && _bank.FileOf(slug) is { } ready)
            {
                var stamp = MonoIconCache.StampOf($"bank:{slug}|{new FileInfo(ready).Length}");
                return _cache.Current(key, stamp) is { } made ? PngOf(made) : Publish(key, stamp, "bank", ready);
            }

            var source = FindSource(target, iconPath);
            if (source is null)
            {
                var none = MonoIconCache.StampOf("none");
                return _cache.Current(key, none) is { } absent ? PngOf(absent) : Fail(key, none, "kaynak yok");
            }

            var sourceStamp = MonoIconCache.StampOf($"src:{source.Path}|{source.Index}|{Identity(source.Path)}");
            if (_cache.Current(key, sourceStamp) is { } known) return PngOf(known);
            return Convert(key, sourceStamp, source);
        }
        catch (Exception ex)
        {
            return Fail(key, MonoIconCache.StampOf("error"), ex.GetType().Name);
        }
    }

    private string? PngOf(MonoIconCache.Record record) => record.File.Length == 0 ? null : _cache.PathOf(record.File);

    private string? Convert(string key, string stamp, IconSource source)
    {
        var work = Path.Combine(_scratch, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var raw = Path.Combine(work, "source.png");
            if (!Extract(source, raw)) return Fail(key, stamp, "kaynak okunamadı");

            MonoIcon.Convert(raw, work, "icon", false);
            var made = Path.Combine(work, "PNG", "icon.png");
            if (Judge(made) is { } problem) return Fail(key, stamp, problem);
            return Publish(key, stamp, "converted", made);
        }
        catch (Exception ex)
        {
            return Fail(key, stamp, ex.Message.Length > 80 ? ex.GetType().Name : ex.Message);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { /* scratch only */ }
        }
    }

    private string? Publish(string key, string stamp, string kind, string png)
    {
        var file = MonoIconCache.FileFor(key);
        var destination = _cache.PathOf(file);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".tmp";
        File.Copy(png, temp, true);
        File.Move(temp, destination, true);
        _cache.Put(key, new MonoIconCache.Record { Stamp = stamp, File = file, Kind = kind });
        return destination;
    }

    private string? Fail(string key, string stamp, string reason)
    {
        _cache.Put(key, new MonoIconCache.Record { Stamp = stamp, Kind = "own", Note = reason });
        return null;
    }

    /// <summary>Why a converted PNG should not be used, or nothing.</summary>
    internal static string? Judge(string png)
    {
        using var bitmap = new Bitmap(png);
        var alpha = new byte[bitmap.Width * bitmap.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, data.Stride);
                for (var x = 0; x < bitmap.Width; x++) alpha[(y * bitmap.Width) + x] = row[(x * 4) + 3];
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return MonoIconQuality.Check(alpha, bitmap.Width, bitmap.Height);
    }

    // ---- where the icon comes from ----

    private sealed record IconSource(string Path, int Index);

    private static IEnumerable<string?> NameCandidates(string name, string target, string? iconPath)
    {
        yield return name;
        var exe = ExePath(target, iconPath);
        if (exe is null) yield break;
        string? product = null, description = null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            product = info.ProductName;
            description = info.FileDescription;
        }
        catch
        {
            // no version resource
        }

        // a bare "Launcher" or "Setup" names no brand
        if (product is { Length: >= 4 }) yield return product;
        if (description is { Length: >= 4 }) yield return description;
        yield return Path.GetFileNameWithoutExtension(exe);
    }

    private static string? ExePath(string target, string? iconPath)
    {
        var path = Expand(target);
        if (path.Length > 0 && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && Shortcut(path) is { Target: { } linked } && linked.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(linked)) return linked;
        return null;
    }

    private static string Expand(string? text) => Environment.ExpandEnvironmentVariables((text ?? string.Empty).Trim().Trim('"'));

    private static IconSource? FindSource(string target, string? iconPath)
    {
        // a picture or an icon location the app was pinned with
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var comma = iconPath.LastIndexOf(',');
            var index = 0;
            var location = iconPath;
            if (comma > 0 && int.TryParse(iconPath[(comma + 1)..].Trim(), out var parsed))
            {
                location = iconPath[..comma];
                index = parsed;
            }

            var file = Expand(location);
            if (File.Exists(file)) return new IconSource(file, index);
        }

        var path = Expand(target);
        if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)) return PackageLogo(path["shell:AppsFolder\\".Length..]);
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            var link = Shortcut(path);
            if (link?.Icon is { } icon && File.Exists(icon.Path)) return new IconSource(icon.Path, icon.Index);
            if (link?.Target is { } linked && File.Exists(linked)) return new IconSource(linked, 0);
            return null;
        }

        return File.Exists(path) ? new IconSource(path, 0) : null;
    }

    private static (string Target, (string Path, int Index)? Icon)? Shortcut(string lnk)
    {
        try
        {
            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell is null) return null;
            dynamic link = Activator.CreateInstance(shell)!;
            dynamic shortcut = link.CreateShortcut(lnk);
            string location = shortcut.IconLocation ?? string.Empty;
            (string, int)? icon = null;
            var comma = location.LastIndexOf(',');
            if (comma > 0 && int.TryParse(location[(comma + 1)..].Trim(), out var index) && Expand(location[..comma]) is { Length: > 0 } file) icon = (file, index);
            return ((string)(shortcut.TargetPath ?? string.Empty), icon);
        }
        catch
        {
            return null;
        }
    }

    // Store apps: the logo the package declares for its Start tile, from the package's current install folder
    private static IconSource? PackageLogo(string aumid)
    {
        var bang = aumid.IndexOf('!');
        var family = bang > 0 ? aumid[..bang] : aumid;
        var underscore = family.LastIndexOf('_');
        if (underscore <= 0) return null;
        var dir = StorePackages.CurrentDir(family[..underscore], family[(underscore + 1)..]);
        if (dir is null) return null;

        try
        {
            var applicationId = bang > 0 ? aumid[(bang + 1)..] : string.Empty;
            var manifest = XDocument.Load(Path.Combine(dir, "AppxManifest.xml"));
            var application = manifest.Descendants().Where(e => e.Name.LocalName == "Application").FirstOrDefault(e => (string?)e.Attribute("Id") == applicationId)
                ?? manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application");
            var visual = application?.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            var logo = (string?)visual?.Attribute("Square44x44Logo") ?? (string?)visual?.Attribute("Logo");
            if (string.IsNullOrWhiteSpace(logo)) return null;

            var basePath = Path.Combine(dir, logo.Replace('/', '\\'));
            var stem = Path.Combine(Path.GetDirectoryName(basePath) ?? dir, Path.GetFileNameWithoutExtension(basePath));
            var extension = Path.GetExtension(basePath);
            foreach (var suffix in new[] { ".targetsize-256_altform-unplated", ".targetsize-256_altform-lightunplated", ".targetsize-256", ".scale-400", ".scale-200", ".scale-100", string.Empty })
            {
                var candidate = stem + suffix + extension;
                if (File.Exists(candidate)) return new IconSource(candidate, 0);
            }
        }
        catch
        {
            // an unreadable manifest leaves the app its own icon
        }

        return null;
    }

    // what changes when the source is updated: the file's size and write time
    private static string Identity(string path)
    {
        var info = new FileInfo(path);
        return $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    // ---- reading the source into a PNG ----

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, [Out] IntPtr[] icons, [Out] uint[] ids, uint count, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    private static bool Extract(IconSource source, string png)
    {
        var extension = Path.GetExtension(source.Path).ToLowerInvariant();
        if (extension is ".png" or ".bmp" or ".jpg" or ".jpeg" or ".gif")
        {
            using var image = Image.FromFile(source.Path);
            image.Save(png, ImageFormat.Png);
            return true;
        }

        var handles = new IntPtr[1];
        try
        {
            var count = PrivateExtractIcons(source.Path, source.Index, 256, 256, handles, new uint[1], 1, 0);
            if (count == 0 || handles[0] == IntPtr.Zero) return false;
            using var borrowed = Icon.FromHandle(handles[0]);
            using var detached = (Icon)borrowed.Clone();
            using var bitmap = detached.ToBitmap();
            bitmap.Save(png, ImageFormat.Png);
            return true;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero) DestroyIcon(handles[0]);
        }
    }
}
