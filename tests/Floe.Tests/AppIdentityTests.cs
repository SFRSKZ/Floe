using Floe.Core.Services;

namespace Floe.Tests;

public class AppIdentityTests
{
    private const string Calculator = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
    private const string Photos = @"shell:AppsFolder\Microsoft.Windows.Photos_8wekyb3d8bbwe!App";

    private static WindowFacts Window(string process, string path = "", string appId = "") => new(process, path, appId);

    [Fact]
    public void TitleNamingTheAppIsNoMatch()
    {
        // a Chrome tab titled "Steam - Store" must not light up the Steam item: only Chrome's process is there
        Assert.False(AppIdentity.Matches("steam", @"C:\Steam\steam.exe", @"C:\Steam\steam.exe",
            Window("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe")));
    }

    [Fact]
    public void SameExePathMatches()
    {
        Assert.True(AppIdentity.Matches("app", @"C:\Tools\app.exe", @"C:\Tools\app.exe", Window("app", @"C:\TOOLS\APP.EXE")));
    }

    [Fact]
    public void PackagedAppMatchesByAppIdNotByFrameHost()
    {
        Assert.True(AppIdentity.Matches("App", Calculator, null,
            Window("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe\CalculatorApp.exe",
                "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")));
    }

    [Fact]
    public void TwoPackagedAppsWithTheSameEntryNameStayApart()
    {
        var calculator = Window("CalculatorApp", @"C:\Program Files\WindowsApps\Microsoft.WindowsCalculator_11.2_x64__8wekyb3d8bbwe\CalculatorApp.exe",
            "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.False(AppIdentity.Matches("App", Photos, null, calculator));
        Assert.NotEqual(AppIdentity.PinnedKey(Calculator, "App"), AppIdentity.PinnedKey(Photos, "App"));
    }

    [Fact]
    public void TwoAppsOfOnePackageStayApart()
    {
        // one package, two apps, one process folder: the full ids decide, not the package or the folder
        var excel = Window("EXCEL", @"C:\Program Files\WindowsApps\Microsoft.Office.Desktop_16.0_x86__8wekyb3d8bbwe\Office16\EXCEL.EXE",
            "Microsoft.Office.Desktop_8wekyb3d8bbwe!Excel");
        Assert.False(AppIdentity.Matches("WINWORD", @"shell:AppsFolder\Microsoft.Office.Desktop_8wekyb3d8bbwe!Word", null, excel));
        Assert.True(AppIdentity.Matches("EXCEL", @"shell:AppsFolder\Microsoft.Office.Desktop_8wekyb3d8bbwe!Excel", null, excel));
    }

    [Fact]
    public void ProcessNamePrefixIsNoMatch()
    {
        // the old prefix rule took notepad++ for Notepad
        Assert.False(AppIdentity.Matches("notepad", @"C:\Windows\notepad.exe", @"C:\Windows\notepad.exe", Window("notepad++", @"C:\npp\notepad++.exe")));
    }

    [Fact]
    public void KnownHelperBelongsToItsApp()
    {
        Assert.True(AppIdentity.Matches("steam", @"C:\Steam\steam.exe", @"C:\Steam\steam.exe",
            Window("steamwebhelper", @"C:\Steam\bin\cef\cef.win7x64\steamwebhelper.exe")));
        Assert.Equal(AppIdentity.RunningKey(Window("steam", @"C:\Steam\steam.exe")),
            AppIdentity.RunningKey(Window("steamwebhelper", @"C:\Steam\bin\cef\steamwebhelper.exe")));
    }

    [Theory]
    [InlineData("SomeAppHelper")]
    [InlineData("msedgewebview2")]
    [InlineData("WebViewHost")]
    public void HelperOrWebViewInTheNameIsNotAShellProcess(string process)
    {
        Assert.False(AppIdentity.IsShellProcess(process));
    }

    [Theory]
    [InlineData("ShellExperienceHost")]
    [InlineData("SearchHost")]
    [InlineData("TextInputHost")]
    public void ShellProcessesAreKnown(string process)
    {
        Assert.True(AppIdentity.IsShellProcess(process));
    }

    [Fact]
    public void UnreadablePathKeepsTheProcessName()
    {
        var key = AppIdentity.RunningKey(Window("ElevatedTool"));
        Assert.Equal("proc:elevatedtool", key);
        Assert.NotEqual(key, AppIdentity.RunningKey(Window("OtherElevatedTool")));
    }

    [Fact]
    public void PackagedRunningAppsKeyByAppId()
    {
        Assert.NotEqual(
            AppIdentity.RunningKey(Window("App", "", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")),
            AppIdentity.RunningKey(Window("App", "", "Microsoft.Windows.Photos_8wekyb3d8bbwe!App")));
    }

    [Fact]
    public void PinnedKeyIsNeverEmptyAndDiffersPerTarget()
    {
        Assert.NotEqual(AppIdentity.PinnedKey(@"C:\a\x.exe", "x"), AppIdentity.PinnedKey(@"C:\b\x.exe", "x"));
        Assert.False(string.IsNullOrEmpty(AppIdentity.PinnedKey("", "")));
    }

    [Fact]
    public void PackageNameNeedsAPackage()
    {
        Assert.Equal("Microsoft.WindowsCalculator", AppIdentity.PackageName(Calculator));
        Assert.Equal(string.Empty, AppIdentity.PackageName(@"shell:AppsFolder\Chrome"));
        Assert.Equal(string.Empty, AppIdentity.PackageName(@"C:\x.exe"));
    }

    [Fact]
    public void AliasesStillMatch()
    {
        Assert.True(AppIdentity.Matches("VisualStudioCode", @"shell:AppsFolder\Microsoft.VisualStudioCode", null, Window("Code", @"C:\VS Code\Code.exe")));
    }
}
