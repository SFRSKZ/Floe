namespace Floe.Core.Services;

/// <summary>Identifier for matching Windows and popular apps to tailored animation motion &amp; shader profiles.</summary>
public enum AnimatedIconKind
{
    GenericScan,
    FileExplorer,
    TaskManager,
    Terminal,
    Notepad,
    Calculator,
    WindowsSettings,
    CameraPhotos,
    SnippingTool,
    Paint,
    RegistryEditor,
    Chrome,
    Edge,
    Firefox,
    Steam,
    Spotify,
    AppleMusic,
    Claude,
    ChatGPT,
    OpenCode,
    Antigravity,
    VSCode,
    GitHub,
    Blender,
    Fusion,
    OrcaSlicer,
    Discord,
    MSIAfterburner,
    RivaTuner,
    Telegram,
    OBS,
    SevenZip,
}

/// <summary>Physics &amp; silhouette-shader choreography applied to the unmodified original icon pixels.</summary>
public enum IconMotionProfile
{
    CyberSheen,
    RotorSpin,
    PistonRock,
    AcousticPulse,
    ZeroGLevitate,
    FolderPop,
    TelemetryScan,
    PerspectiveFlipGlint,
    OpticFocus,
    DiagonalGlide,
}

/// <summary>Matches dock apps by name or process name to tailored animation choreography presets.</summary>
public static class AnimatedIconCatalog
{
    private const int LooseMinLength = 5;

    private static readonly Dictionary<string, AnimatedIconKind> Aliases = new()
    {
        ["dosyagezgini"] = AnimatedIconKind.FileExplorer,
        ["fileexplorer"] = AnimatedIconKind.FileExplorer,
        ["explorer"] = AnimatedIconKind.FileExplorer,

        ["gorevyoneticisi"] = AnimatedIconKind.TaskManager,
        ["taskmanager"] = AnimatedIconKind.TaskManager,
        ["taskmgr"] = AnimatedIconKind.TaskManager,

        ["windowsterminal"] = AnimatedIconKind.Terminal,
        ["terminal"] = AnimatedIconKind.Terminal,
        ["wt"] = AnimatedIconKind.Terminal,
        ["cmd"] = AnimatedIconKind.Terminal,
        ["komutistemi"] = AnimatedIconKind.Terminal,
        ["powershell"] = AnimatedIconKind.Terminal,
        ["windowspowershell"] = AnimatedIconKind.Terminal,
        ["pwsh"] = AnimatedIconKind.Terminal,

        ["notdefteri"] = AnimatedIconKind.Notepad,
        ["notepad"] = AnimatedIconKind.Notepad,
        ["notepadplusplus"] = AnimatedIconKind.Notepad,

        ["hesapmakinesi"] = AnimatedIconKind.Calculator,
        ["calculator"] = AnimatedIconKind.Calculator,
        ["calculatorapp"] = AnimatedIconKind.Calculator,
        ["calc"] = AnimatedIconKind.Calculator,

        ["ayarlar"] = AnimatedIconKind.WindowsSettings,
        ["settings"] = AnimatedIconKind.WindowsSettings,
        ["systemsettings"] = AnimatedIconKind.WindowsSettings,
        ["control"] = AnimatedIconKind.WindowsSettings,
        ["denetimmasasi"] = AnimatedIconKind.WindowsSettings,
        ["controlpanel"] = AnimatedIconKind.WindowsSettings,

        ["kamera"] = AnimatedIconKind.CameraPhotos,
        ["camera"] = AnimatedIconKind.CameraPhotos,
        ["fotograflar"] = AnimatedIconKind.CameraPhotos,
        ["photos"] = AnimatedIconKind.CameraPhotos,
        ["windowscamera"] = AnimatedIconKind.CameraPhotos,

        ["ekranalintisiaraci"] = AnimatedIconKind.SnippingTool,
        ["ekranalintisivevaslak"] = AnimatedIconKind.SnippingTool,
        ["snippingtool"] = AnimatedIconKind.SnippingTool,

        ["paint"] = AnimatedIconKind.Paint,
        ["mspaint"] = AnimatedIconKind.Paint,

        ["regedit"] = AnimatedIconKind.RegistryEditor,
        ["registryeditor"] = AnimatedIconKind.RegistryEditor,
        ["kayitdefteriduzenleyicisi"] = AnimatedIconKind.RegistryEditor,

        ["chrome"] = AnimatedIconKind.Chrome,
        ["googlechrome"] = AnimatedIconKind.Chrome,

        ["msedge"] = AnimatedIconKind.Edge,
        ["microsoftedge"] = AnimatedIconKind.Edge,
        ["edge"] = AnimatedIconKind.Edge,

        ["firefox"] = AnimatedIconKind.Firefox,
        ["mozillafirefox"] = AnimatedIconKind.Firefox,

        ["steam"] = AnimatedIconKind.Steam,
        ["steamclientbootstrapper"] = AnimatedIconKind.Steam,

        ["spotify"] = AnimatedIconKind.Spotify,
        ["spotifymusic"] = AnimatedIconKind.Spotify,

        ["applemusic"] = AnimatedIconKind.AppleMusic,
        ["applemusicwin"] = AnimatedIconKind.AppleMusic,
        ["itunes"] = AnimatedIconKind.AppleMusic,
        ["medyaoynatici"] = AnimatedIconKind.AppleMusic,
        ["microsoftmediaplayer"] = AnimatedIconKind.AppleMusic,
        ["mediaplayer"] = AnimatedIconKind.AppleMusic,
        ["zunemusic"] = AnimatedIconKind.AppleMusic,

        ["claude"] = AnimatedIconKind.Claude,

        ["chatgpt"] = AnimatedIconKind.ChatGPT,
        ["openai"] = AnimatedIconKind.ChatGPT,

        ["opencode"] = AnimatedIconKind.OpenCode,

        ["antigravity"] = AnimatedIconKind.Antigravity,

        ["code"] = AnimatedIconKind.VSCode,
        ["visualstudiocode"] = AnimatedIconKind.VSCode,
        ["vscode"] = AnimatedIconKind.VSCode,
        ["cursor"] = AnimatedIconKind.VSCode,
        ["devenv"] = AnimatedIconKind.VSCode,
        ["visualstudio"] = AnimatedIconKind.VSCode,

        ["github"] = AnimatedIconKind.GitHub,
        ["githubdesktop"] = AnimatedIconKind.GitHub,

        ["blender"] = AnimatedIconKind.Blender,
        ["blenderlauncher"] = AnimatedIconKind.Blender,

        ["fusion"] = AnimatedIconKind.Fusion,
        ["fusion360"] = AnimatedIconKind.Fusion,
        ["fusionlauncher"] = AnimatedIconKind.Fusion,
        ["autodeskfusion"] = AnimatedIconKind.Fusion,
        ["autodeskfusion360"] = AnimatedIconKind.Fusion,

        ["orcaslicer"] = AnimatedIconKind.OrcaSlicer,
        ["bambustudio"] = AnimatedIconKind.OrcaSlicer,
        ["prusaslicer"] = AnimatedIconKind.OrcaSlicer,

        ["discord"] = AnimatedIconKind.Discord,
        ["vesktop"] = AnimatedIconKind.Discord,

        ["msiafterburner"] = AnimatedIconKind.MSIAfterburner,

        ["rtss"] = AnimatedIconKind.RivaTuner,
        ["rivatuner"] = AnimatedIconKind.RivaTuner,
        ["rivatunerstatisticsserver"] = AnimatedIconKind.RivaTuner,

        ["telegram"] = AnimatedIconKind.Telegram,
        ["telegramdesktop"] = AnimatedIconKind.Telegram,

        ["obs"] = AnimatedIconKind.OBS,
        ["obs64"] = AnimatedIconKind.OBS,
        ["obsstudio"] = AnimatedIconKind.OBS,

        ["7zfm"] = AnimatedIconKind.SevenZip,
        ["7zip"] = AnimatedIconKind.SevenZip,
        ["7zipfilemanager"] = AnimatedIconKind.SevenZip,
        ["winrar"] = AnimatedIconKind.SevenZip,
    };

    private static readonly KeyValuePair<string, AnimatedIconKind>[] LooseAliases = Aliases
        .Where(kv => kv.Key.Length >= LooseMinLength)
        .OrderByDescending(kv => kv.Key.Length)
        .ToArray();

    /// <summary>Matches an application display name and optional process name to an animated icon kind.</summary>
    public static AnimatedIconKind Match(string? name, string? process) =>
        Find(Plain(name)) ?? Find(Plain(process)) ?? AnimatedIconKind.GenericScan;

    /// <summary>Returns the unified original-icon motion &amp; shader choreography profile for <paramref name="kind"/>.</summary>
    public static IconMotionProfile MotionFor(AnimatedIconKind kind) => kind switch
    {
        AnimatedIconKind.Chrome
            or AnimatedIconKind.Edge
            or AnimatedIconKind.Firefox
            or AnimatedIconKind.Blender
            or AnimatedIconKind.Claude
            or AnimatedIconKind.ChatGPT
            or AnimatedIconKind.OBS
            or AnimatedIconKind.MSIAfterburner
            or AnimatedIconKind.WindowsSettings => IconMotionProfile.RotorSpin,

        AnimatedIconKind.Steam
            or AnimatedIconKind.Notepad
            or AnimatedIconKind.Paint => IconMotionProfile.PistonRock,

        AnimatedIconKind.Spotify
            or AnimatedIconKind.AppleMusic => IconMotionProfile.AcousticPulse,

        AnimatedIconKind.Antigravity
            or AnimatedIconKind.Discord
            or AnimatedIconKind.GitHub => IconMotionProfile.ZeroGLevitate,

        AnimatedIconKind.FileExplorer
            or AnimatedIconKind.SevenZip
            or AnimatedIconKind.Calculator => IconMotionProfile.FolderPop,

        AnimatedIconKind.TaskManager
            or AnimatedIconKind.Terminal
            or AnimatedIconKind.VSCode
            or AnimatedIconKind.OpenCode
            or AnimatedIconKind.OrcaSlicer
            or AnimatedIconKind.RivaTuner => IconMotionProfile.TelemetryScan,

        AnimatedIconKind.Fusion
            or AnimatedIconKind.RegistryEditor => IconMotionProfile.PerspectiveFlipGlint,

        AnimatedIconKind.CameraPhotos
            or AnimatedIconKind.SnippingTool => IconMotionProfile.OpticFocus,

        AnimatedIconKind.Telegram => IconMotionProfile.DiagonalGlide,

        _ => IconMotionProfile.CyberSheen,
    };

    private static AnimatedIconKind? Find(string plain)
    {
        if (plain.Length == 0) return null;
        if (Aliases.TryGetValue(plain, out var exact)) return exact;
        if (plain.Length < LooseMinLength) return null;

        foreach (var entry in LooseAliases)
        {
            if (plain.Contains(entry.Key, StringComparison.Ordinal) || entry.Key.Contains(plain, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static string Plain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        Span<char> buffer = raw.Length <= 128 ? stackalloc char[raw.Length] : new char[raw.Length];
        int len = 0;
        foreach (char ch in raw)
        {
            char normalized = NormalizeTurkish(char.ToLowerInvariant(ch));
            if (char.IsLetterOrDigit(normalized))
            {
                buffer[len++] = normalized;
            }
        }
        return new string(buffer[..len]);
    }

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
}
