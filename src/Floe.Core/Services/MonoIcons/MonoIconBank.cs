using System.Text.Json;

namespace Floe.Core.Services.MonoIcons;

/// <summary>
/// The verified logo bank: ready black masks keyed by an app's name. A name matches only when it is the same name once
/// case, spaces and punctuation are gone, and never when two bank entries share that name, so an unclear match falls
/// back to the app's own icon instead of the wrong brand.
/// </summary>
public sealed class MonoIconBank
{
    // what an English Windows calls the apps the bank knows by their Turkish names
    private static readonly (string Alias, string Bank)[] Aliases =
    {
        ("File Explorer", "Dosya Gezgini"),
        ("Command Prompt", "Komut İstemi"),
        ("Camera", "Kamera"),
        ("Sticky Notes", "Yapışkan Notlar"),
        ("Settings", "Ayarlar"),
        ("Task Manager", "Görev Yöneticisi"),
        ("Photos", "Fotoğraflar"),
        ("Snipping Tool", "Ekran Alıntısı Aracı"),
        ("Control Panel", "Denetim Masası"),
        ("Registry Editor", "Kayıt Defteri Düzenleyicisi"),
        ("Weather", "Hava Durumu"),
        ("News", "Haberler"),
        ("Remote Desktop Connection", "Uzak Masaüstü Bağlantısı"),
        ("Magnifier", "Büyüteç"),
        ("Run", "Çalıştır"),
    };

    private readonly Dictionary<string, string?> _slugs = new();
    private readonly string _folder;

    private MonoIconBank(string folder) => _folder = folder;

    public int Count => _slugs.Count(entry => entry.Value is not null);

    /// <summary>Reads <c>index.json</c> of the bank folder; a missing or broken bank is simply an empty one.</summary>
    public static MonoIconBank Load(string folder)
    {
        var bank = new MonoIconBank(folder);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "index.json")));
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var name = entry.TryGetProperty("name", out var n) ? n.GetString() : null;
                var slug = entry.TryGetProperty("slug", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) continue;
                var key = MonoIconNames.Plain(name);
                if (key.Length == 0) continue;
                // two entries behind one name: neither can be told from the other
                bank._slugs[key] = bank._slugs.ContainsKey(key) && bank._slugs[key] != slug ? null : slug;
            }
        }
        catch
        {
            // no bank, no ready logos; the converter still runs
        }

        return bank;
    }

    /// <summary>The slug of the first candidate name the bank knows, or nothing.</summary>
    public string? Find(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            var key = MonoIconNames.Plain(candidate);
            if (key.Length == 0) continue;
            if (_slugs.TryGetValue(key, out var slug)) return slug;
            foreach (var (alias, bank) in Aliases)
            {
                if (key == MonoIconNames.Plain(alias) && _slugs.TryGetValue(MonoIconNames.Plain(bank), out slug)) return slug;
            }
        }

        return null;
    }

    /// <summary>The bank's PNG for a slug, when the file is there.</summary>
    public string? FileOf(string slug)
    {
        var path = Path.Combine(_folder, "png", slug + ".png");
        return File.Exists(path) ? path : null;
    }
}

public static class MonoIconNames
{
    /// <summary>Lower case letters and digits only, so "Blender 5.0" and "blender-5.0" are the same name.</summary>
    public static string Plain(string? name) =>
        name is null ? string.Empty : new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
