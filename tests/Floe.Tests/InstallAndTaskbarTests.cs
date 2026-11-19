using System.IO.Compression;
using FloeSetup;
using Floe.Core.Services;

namespace Floe.Tests;

public class TaskbarPreferenceTests
{
    [Fact]
    public void TaskbarStaysUnlessHidingWasChosen() => Assert.False(TaskbarPreference.HidesTaskbar(false, Array.Empty<string>()));

    [Fact]
    public void ChosenHidingHides() => Assert.True(TaskbarPreference.HidesTaskbar(true, Array.Empty<string>()));

    [Fact]
    public void KeepTaskbarWinsOverEverything()
    {
        Assert.False(TaskbarPreference.HidesTaskbar(true, new[] { "--keep-taskbar" }));
        Assert.False(TaskbarPreference.HidesTaskbar(true, new[] { "--hide-taskbar", "--KEEP-TASKBAR" }));
    }

    [Fact]
    public void HideTaskbarArgumentHides() => Assert.True(TaskbarPreference.HidesTaskbar(false, new[] { "--hide-taskbar" }));
}

public sealed class InstallFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "floe-install-tests-" + Guid.NewGuid().ToString("N"));

    public InstallFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a held test file is the OS's to clear */ }
    }

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private string OldInstall()
    {
        var install = Path.Combine(_root, "app");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Floe.exe"), "old");
        File.WriteAllText(Path.Combine(install, "Floe.dll"), "old");
        return install;
    }

    [Fact]
    public void BrokenPayloadNeverTouchesTheInstalledBuild()
    {
        var install = OldInstall();
        var stage = install + ".new-test";
        // the dll a scanner took away: the payload is not whole
        Assert.Throws<InvalidDataException>(() => InstallFiles.Stage(Zip(("Floe.exe", "new")), stage, "Floe.exe", "Floe.dll"));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Floe.dll")));
    }

    [Fact]
    public void EmptyPayloadIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => InstallFiles.Stage(Zip(), Path.Combine(_root, "stage")));
    }

    [Fact]
    public void GoodUpdateReplacesTheBuildAndSetsTheOldAside()
    {
        var install = OldInstall();
        var stage = install + ".new-test";
        InstallFiles.Stage(Zip(("Floe.exe", "new"), ("Floe.dll", "new"), ("sub/x.txt", "x")), stage, "Floe.exe", "Floe.dll");
        var previous = InstallFiles.Swap(stage, install);

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Floe.dll")));
        Assert.True(File.Exists(Path.Combine(install, "sub", "x.txt")));
        Assert.NotNull(previous);
        Assert.Empty(InstallFiles.RemoveTree(previous!));
        Assert.False(Directory.Exists(stage));
    }

    [Fact]
    public void FailedSwapPutsTheOldBuildBack()
    {
        var install = OldInstall();
        // the staged build is not there (say a scanner removed the folder): the move in fails
        var missingStage = install + ".new-gone";
        Assert.ThrowsAny<IOException>(() => InstallFiles.Swap(missingStage, install));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Floe.exe")));
    }

    [Fact]
    public void RemoveTreeReportsHeldFilesInsteadOfClaimingSuccess()
    {
        var dir = Path.Combine(_root, "held");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "Floe.dll");
        File.WriteAllText(file, "x");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var left = InstallFiles.RemoveTree(dir, attempts: 2, delayMs: 10);
            Assert.Contains(left, path => path.EndsWith("Floe.dll", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Empty(InstallFiles.RemoveTree(dir, attempts: 2, delayMs: 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(@"..\Other")]
    [InlineData("Floe/../..")]
    public void OnlyTheProductsOwnFolderMayBeRemoved(string folder)
    {
        Assert.Throws<InvalidOperationException>(() => InstallFiles.ProductRoot(_root, folder));
    }

    [Fact]
    public void ProductRootIsRightUnderLocalAppData()
    {
        Assert.Equal(Path.Combine(_root, "FloeCP77"), InstallFiles.ProductRoot(_root, "FloeCP77"));
    }

    [Fact]
    public void LeftoversOfEarlierUpdatesAreCleared()
    {
        var install = OldInstall();
        Directory.CreateDirectory(install + ".old-20260101000000");
        Directory.CreateDirectory(install + ".new-20260101000000");
        InstallFiles.RemoveLeftovers(install);
        Assert.False(Directory.Exists(install + ".old-20260101000000"));
        Assert.False(Directory.Exists(install + ".new-20260101000000"));
        Assert.True(Directory.Exists(install));
    }

    [Fact]
    public void AMissingAppFolderGetsTheNewestOldBuildBack()
    {
        var install = OldInstall();
        Directory.Move(install, install + ".old-20260102000000");
        Directory.CreateDirectory(install + ".old-20260101000000");
        InstallFiles.RemoveLeftovers(install);
        Assert.True(Directory.Exists(install));
        Assert.False(Directory.Exists(install + ".old-20260102000000"));
        Assert.False(Directory.Exists(install + ".old-20260101000000"));
    }
}
