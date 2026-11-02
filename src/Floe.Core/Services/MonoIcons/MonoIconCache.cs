using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Floe.Core.Services.MonoIcons;

/// <summary>
/// The black icons made so far, in the user's own folder: one PNG per app and an index that remembers what each came from.
/// A record is current while its stamp (the source's identity plus the theme version) is unchanged. A failure is a record
/// too, so a source that cannot be turned black is not tried again until it changes.
/// </summary>
public sealed class MonoIconCache
{
    /// <summary>Bumped when the look changes (colour, padding, checks); every record is made again.</summary>
    public const int ThemeVersion = 1;

    private readonly string _folder;
    private readonly string _indexPath;
    private readonly object _gate = new();
    private Dictionary<string, Record> _records;

    public MonoIconCache(string folder)
    {
        _folder = folder;
        _indexPath = Path.Combine(folder, "index.json");
        _records = Read();
    }

    public sealed class Record
    {
        public string Stamp { get; set; } = string.Empty;

        /// <summary>The PNG's file name in the cache folder; empty when the app keeps its own icon.</summary>
        public string File { get; set; } = string.Empty;

        /// <summary>"bank", "converted" or "own" (with the reason in <see cref="Note"/>).</summary>
        public string Kind { get; set; } = "own";

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>The app's key: the same for the same target whatever its case.</summary>
    public static string KeyOf(string target) => target.Trim().ToLowerInvariant();

    public string PathOf(string file) => Path.Combine(_folder, file);

    public static string FileFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + ".png";
    }

    public static string StampOf(string source) => $"v{ThemeVersion}|{source}";

    /// <summary>The record when it was made from this very source and look, else nothing.</summary>
    public Record? Current(string key, string stamp)
    {
        lock (_gate)
        {
            return _records.TryGetValue(key, out var record) && record.Stamp == stamp && (record.File.Length == 0 || System.IO.File.Exists(PathOf(record.File))) ? record : null;
        }
    }

    /// <summary>Whatever is stored for the key, even when its source has changed since; the dock shows it until the refresh lands.</summary>
    public Record? Known(string key)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault(key);
        }
    }

    public void Put(string key, Record record)
    {
        lock (_gate)
        {
            _records[key] = record;
            Save();
        }
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_indexPath, JsonSerializer.Serialize(_records));
        }
        catch
        {
            // a cache that cannot be written is made again next run
        }
    }

    private Dictionary<string, Record> Read()
    {
        try
        {
            if (System.IO.File.Exists(_indexPath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Record>>(System.IO.File.ReadAllText(_indexPath)) ?? new();
            }
        }
        catch
        {
            // a broken index is an empty one
        }

        return new();
    }
}
