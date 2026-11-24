using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Floe.App.Services;

/// <summary>A bundled icon set: its names.csv and PNGs, matched to apps by name or process.</summary>
internal sealed class IconCatalog
{
    private const int Pixels = 192;
    private const int LooseMatch = 5;

    // what Windows and the running apps call themselves where the sets were named after the Start menu
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["fileexplorer"] = "dosya-gezgini",
        ["explorer"] = "dosya-gezgini",
        ["code"] = "visual-studio-code",
        ["vscode"] = "visual-studio-code",
        ["chrome"] = "google-chrome",
        ["msedge"] = "microsoft-edge",
        ["edge"] = "microsoft-edge",
        ["windowsterminal"] = "terminal",
        ["wt"] = "terminal",
        ["cmd"] = "komut-istemi",
        ["commandprompt"] = "komut-istemi",
        ["settings"] = "ayarlar",
        ["systemsettings"] = "ayarlar",
        ["control"] = "denetim-masasi",
        ["controlpanel"] = "denetim-masasi",
        ["taskmanager"] = "gorev-yoneticisi",
        ["taskmgr"] = "gorev-yoneticisi",
        ["photos"] = "fotograflar",
        ["microsoftphotos"] = "fotograflar",
        ["windowsphotos"] = "fotograflar",
        ["camera"] = "kamera",
        ["windowscamera"] = "kamera",
        ["snippingtool"] = "ekran-alintisi-araci",
        ["screenclippinghost"] = "ekran-alintisi-araci",
        ["eadesktop"] = "ea",
        ["eaapp"] = "ea",
        ["microsoftmediaplayer"] = "medya-oynatici",
        ["mediaplayer"] = "medya-oynatici",
        ["zunemusic"] = "medya-oynatici",
        ["microsoftzunemusic"] = "medya-oynatici",
        ["zunevideo"] = "medya-oynatici",
        ["videoui"] = "medya-oynatici",
        ["wmplayer"] = "windows-media-player-legacy",
        ["vlc"] = "vlc-media-player",
        ["soundrec"] = "ses-kaydedici",
        ["soundrecorder"] = "ses-kaydedici",
        ["windowssoundrecorder"] = "ses-kaydedici",
        ["voicerecorder"] = "ses-kaydedici",
        ["winstoreapp"] = "microsoft-store",
        ["winstore"] = "microsoft-store",
        ["windowsstore"] = "microsoft-store",
        ["microsoftnotes"] = "yapiskan-notlar",
        ["stickynotes"] = "yapiskan-notlar",
        ["microsoftstickynotes"] = "yapiskan-notlar",
        ["mspaint"] = "paint",
        ["pbrush"] = "paint",
        ["regedit"] = "kayit-defteri-duzenleyicisi",
        ["registryeditor"] = "kayit-defteri-duzenleyicisi",
        ["pwsh"] = "windows-powershell",
        ["powershell"] = "windows-powershell",
        ["powershellise"] = "windows-powershell-ise",
        ["clipchamp"] = "microsoft-clipchamp",
        ["msteams"] = "microsoft-teams",
        ["teams"] = "microsoft-teams",
        ["todo"] = "microsoft-to-do",
        ["todos"] = "microsoft-to-do",
        ["bingweather"] = "hava-durumu",
        ["weather"] = "hava-durumu",
        ["bingnews"] = "haberler",
        ["news"] = "haberler",
        ["phoneexperiencehost"] = "telefon-baglantisi",
        ["yourphone"] = "telefon-baglantisi",
        ["phonelink"] = "telefon-baglantisi",
        ["appledevices"] = "apple-aygitlari",
        ["applemusicwin"] = "apple-music",
        ["spotifymusic"] = "spotify",
        ["dtssoundunbound"] = "dts-sound-unbound",
        ["dolbyaccess"] = "dolby-access",
        ["rtss"] = "rivatuner-statistics-server",
        ["rivatuner"] = "rivatuner-statistics-server",
        ["bambustudio"] = "bambu-studio",
        ["goggalaxy"] = "gog-galaxy",
        ["galaxyclient"] = "gog-galaxy",
        ["upc"] = "ubisoft-connect",
        ["ubisoftconnect"] = "ubisoft-connect",
        ["nvidiaapp"] = "nvidia-app",
        ["nvidiaoverlay"] = "nvidia-app",
        ["msicenter"] = "msi-center",
        ["ngenuity"] = "hyperx-ngenuity",
        ["hyperxngenuity"] = "hyperx-ngenuity",
        ["wallpaper32"] = "wallpaper-engine",
        ["wallpaper64"] = "wallpaper-engine",
        ["eventvwr"] = "olay-goruntuleyicisi",
        ["perfmon"] = "performans-izleyicisi",
        ["resmon"] = "kaynak-izleyicisi",
        ["msinfo32"] = "sistem-bilgisi",
        ["msconfig"] = "sistem-yapilandirmasi",
        ["cleanmgr"] = "disk-temizleme",
        ["dfrgui"] = "suruculeri-birlestir-ve-iyilestir",
        ["charmap"] = "karakter-eslem",
        ["osk"] = "ekran-klavyesi",
        ["magnify"] = "buyutec",
        ["narrator"] = "ekran-okuyucusu",
        ["psr"] = "adim-kaydedicisi",
        ["mstsc"] = "uzak-masaustu-baglantisi",
        ["mdsched"] = "windows-bellek-tanilama",
        ["services"] = "hizmetler",
        ["taskschd"] = "gorev-zamanlayici",
        ["compmgmt"] = "bilgisayar-yonetimi",
        ["notepadplusplus"] = "notepad",
    };

    private readonly string _folder;
    private readonly Lazy<(string Name, string File)[]> _names;
    private readonly Lazy<Dictionary<string, string>> _catalog;
    private readonly Dictionary<string, string?> _matches = new();
    private readonly Dictionary<string, BitmapImage> _loaded = new();

    public IconCatalog(string folder)
    {
        _folder = $"pack://application:,,,/{typeof(IconCatalog).Assembly.GetName().Name};component/Assets/{folder}/";
        _names = new(ReadNames);
        _catalog = new(ReadCatalog);
    }

    /// <summary>The whole set, in catalog order.</summary>
    public IReadOnlyList<(string Name, string File)> All => _names.Value;

    /// <summary>The set's file for an app, or nothing when the set does not know it.</summary>
    public string? Match(string name, string? process)
    {
        var key = $"{name}|{process}";
        if (_matches.TryGetValue(key, out var known)) return known;
        return _matches[key] = Find(Plain(name)) ?? Find(Plain(process ?? string.Empty));
    }

    /// <summary>One icon of the set by its file name.</summary>
    public BitmapImage Load(string file)
    {
        if (_loaded.TryGetValue(file, out var known)) return known;
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"{_folder}{file}.png");
        image.DecodePixelWidth = Pixels;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return _loaded[file] = image;
    }

    private string? Find(string plain)
    {
        if (plain.Length == 0) return null;
        if (Aliases.TryGetValue(plain, out var alias) && _names.Value.Any(entry => entry.File == alias)) return alias;
        if (_catalog.Value.TryGetValue(plain, out alias)) return alias;
        if (plain.Length < LooseMatch) return null;

        // "Blender" finds "Blender 5.0" and "OrcaSlicer 2.3" finds "OrcaSlicer"; the longest name is the closest
        return _catalog.Value.Where(entry => entry.Key.Length >= LooseMatch && (entry.Key.Contains(plain) || plain.Contains(entry.Key)))
            .OrderByDescending(entry => entry.Key.Length).Select(entry => entry.Value).FirstOrDefault();
    }

    private static string Plain(string name) => new(name.ToLowerInvariant().Select(NormalizeTurkish).Where(char.IsLetterOrDigit).ToArray());

    private static char NormalizeTurkish(char ch) => ch switch
    {
        'ç' => 'c',
        'ğ' => 'g',
        'ı' => 'i',
        'ö' => 'o',
        'ş' => 's',
        'ü' => 'u',
        _ => ch,
    };

    private Dictionary<string, string> ReadCatalog()
    {
        var catalog = new Dictionary<string, string>();
        foreach (var (name, file) in _names.Value)
        {
            catalog[Plain(name)] = file;
            catalog[Plain(file)] = file;
        }

        return catalog;
    }

    private (string Name, string File)[] ReadNames()
    {
        var names = new List<(string, string)>();
        var stream = Application.GetResourceStream(new Uri($"{_folder}names.csv"))?.Stream;
        if (stream is null) return Array.Empty<(string, string)>();
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(';');
            if (parts.Length == 2) names.Add((parts[0], parts[1]));
        }

        return names.ToArray();
    }
}
