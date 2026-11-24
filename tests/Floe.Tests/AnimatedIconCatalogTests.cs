using Floe.Core.Services;

namespace Floe.Tests;

public sealed class AnimatedIconCatalogTests
{
    [Theory]
    [InlineData("Dosya Gezgini", null, AnimatedIconKind.FileExplorer)]
    [InlineData("File Explorer", "explorer", AnimatedIconKind.FileExplorer)]
    [InlineData("Görev Yöneticisi", null, AnimatedIconKind.TaskManager)]
    [InlineData("Task Manager", "Taskmgr", AnimatedIconKind.TaskManager)]
    [InlineData("Terminal", "wt", AnimatedIconKind.Terminal)]
    [InlineData("Windows PowerShell", "pwsh", AnimatedIconKind.Terminal)]
    [InlineData("Komut İstemi", "cmd", AnimatedIconKind.Terminal)]
    [InlineData("Not Defteri", "notepad", AnimatedIconKind.Notepad)]
    [InlineData("Hesap Makinesi", "calc", AnimatedIconKind.Calculator)]
    [InlineData("Calculator", "CalculatorApp", AnimatedIconKind.Calculator)]
    [InlineData("Medya Oynatıcı", "Microsoft.Media.Player", AnimatedIconKind.AppleMusic)]
    [InlineData("Ayarlar", "SystemSettings", AnimatedIconKind.WindowsSettings)]
    [InlineData("Kamera", null, AnimatedIconKind.CameraPhotos)]
    [InlineData("Fotoğraflar", null, AnimatedIconKind.CameraPhotos)]
    [InlineData("Ekran Alıntısı Aracı", "SnippingTool", AnimatedIconKind.SnippingTool)]
    [InlineData("Paint", "mspaint", AnimatedIconKind.Paint)]
    [InlineData("Kayıt Defteri Düzenleyicisi", "regedit", AnimatedIconKind.RegistryEditor)]
    [InlineData("Google Chrome", "chrome", AnimatedIconKind.Chrome)]
    [InlineData("Microsoft Edge", "msedge", AnimatedIconKind.Edge)]
    [InlineData("Mozilla Firefox", "firefox", AnimatedIconKind.Firefox)]
    [InlineData("Steam", "steam", AnimatedIconKind.Steam)]
    [InlineData("Spotify", "Spotify", AnimatedIconKind.Spotify)]
    [InlineData("Apple Music", "AppleMusic", AnimatedIconKind.AppleMusic)]
    [InlineData("Claude", "claude", AnimatedIconKind.Claude)]
    [InlineData("ChatGPT", "openai", AnimatedIconKind.ChatGPT)]
    [InlineData("OpenCode", "OpenCode", AnimatedIconKind.OpenCode)]
    [InlineData("Antigravity", "Antigravity", AnimatedIconKind.Antigravity)]
    [InlineData("Visual Studio Code - Insiders", "Code", AnimatedIconKind.VSCode)]
    [InlineData("GitHub Desktop", "GitHubDesktop", AnimatedIconKind.GitHub)]
    [InlineData("Blender 5.0", "blender-launcher", AnimatedIconKind.Blender)]
    [InlineData("Autodesk Fusion", "FusionLauncher", AnimatedIconKind.Fusion)]
    [InlineData("OrcaSlicer", "orca-slicer", AnimatedIconKind.OrcaSlicer)]
    [InlineData("Discord", "Discord", AnimatedIconKind.Discord)]
    [InlineData("MSI Afterburner", "MSIAfterburner", AnimatedIconKind.MSIAfterburner)]
    [InlineData("RivaTuner Statistics Server", "RTSS", AnimatedIconKind.RivaTuner)]
    [InlineData("Telegram Desktop", "Telegram", AnimatedIconKind.Telegram)]
    [InlineData("OBS Studio", "obs64", AnimatedIconKind.OBS)]
    [InlineData("7-Zip File Manager", "7zFM", AnimatedIconKind.SevenZip)]
    [InlineData("Some Unlisted Indie Tool", "indietool", AnimatedIconKind.GenericScan)]
    public void Match_ResolvesTurkishEnglishPopularAndUnknownApps(string name, string? process, AnimatedIconKind expected)
    {
        Assert.Equal(expected, AnimatedIconCatalog.Match(name, process));
    }

    [Theory]
    [InlineData(AnimatedIconKind.Chrome, IconMotionProfile.RotorSpin)]
    [InlineData(AnimatedIconKind.Blender, IconMotionProfile.RotorSpin)]
    [InlineData(AnimatedIconKind.WindowsSettings, IconMotionProfile.RotorSpin)]
    [InlineData(AnimatedIconKind.Steam, IconMotionProfile.PistonRock)]
    [InlineData(AnimatedIconKind.Notepad, IconMotionProfile.PistonRock)]
    [InlineData(AnimatedIconKind.Spotify, IconMotionProfile.AcousticPulse)]
    [InlineData(AnimatedIconKind.AppleMusic, IconMotionProfile.AcousticPulse)]
    [InlineData(AnimatedIconKind.Antigravity, IconMotionProfile.ZeroGLevitate)]
    [InlineData(AnimatedIconKind.Discord, IconMotionProfile.ZeroGLevitate)]
    [InlineData(AnimatedIconKind.FileExplorer, IconMotionProfile.FolderPop)]
    [InlineData(AnimatedIconKind.SevenZip, IconMotionProfile.FolderPop)]
    [InlineData(AnimatedIconKind.TaskManager, IconMotionProfile.TelemetryScan)]
    [InlineData(AnimatedIconKind.VSCode, IconMotionProfile.TelemetryScan)]
    [InlineData(AnimatedIconKind.Fusion, IconMotionProfile.PerspectiveFlipGlint)]
    [InlineData(AnimatedIconKind.RegistryEditor, IconMotionProfile.PerspectiveFlipGlint)]
    [InlineData(AnimatedIconKind.CameraPhotos, IconMotionProfile.OpticFocus)]
    [InlineData(AnimatedIconKind.Telegram, IconMotionProfile.DiagonalGlide)]
    [InlineData(AnimatedIconKind.GenericScan, IconMotionProfile.CyberSheen)]
    public void MotionFor_CategorizesEachProfileCleanly(AnimatedIconKind kind, IconMotionProfile expected)
    {
        Assert.Equal(expected, AnimatedIconCatalog.MotionFor(kind));
    }
}
