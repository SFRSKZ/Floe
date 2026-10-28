using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FloeSetup;

/// <summary>
/// Installs Floe for the current user into %LOCALAPPDATA%\Floe\app. Run from Explorer, this lands in the real
/// AppData, not in the private copy an MSIX-packaged app (such as the Claude desktop app) sees, so the dock no
/// longer closes when that app updates. Settings that were written inside such a package come along.
/// </summary>
internal static class Program
{
    private const string Title = "Floe kurulumu";

    [STAThread]
    private static int Main()
    {
        if (Ask("Floe kurulsun mu? Açıksa kapatılıp yenisi başlatılır.", MbOkCancel | MbIconQuestion) != IdOk) return 1;

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var install = Path.Combine(local, "Floe", "app");

            StopFloe();
            Extract(install);
            int moved = BringSettings(local, Path.Combine(roaming, "Floe"));
            Shortcut(install);
            Process.Start(new ProcessStartInfo(Path.Combine(install, "Floe.exe")) { UseShellExecute = true, WorkingDirectory = install });

            Ask($"Floe kuruldu:\n{install}" + (moved > 0 ? $"\n\n{moved} ayar dosyası taşındı." : string.Empty), MbIconInformation);
            return 0;
        }
        catch (Exception ex)
        {
            Ask("Kurulum olmadı:\n" + ex.Message, MbIconError);
            return 2;
        }
    }

    // MacDock is the name builds ran under before the rename
    private static void StopFloe()
    {
        foreach (var process in Process.GetProcessesByName("Floe").Concat(Process.GetProcessesByName("MacDock")))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
                // already gone, or not ours to stop; the copy below will say if a file is still held
            }
        }
    }

    // a clean folder each time, so files a newer build dropped do not linger
    private static void Extract(string install)
    {
        if (Directory.Exists(install)) Directory.Delete(install, recursive: true);
        Directory.CreateDirectory(install);
        using var payload = typeof(Program).Assembly.GetManifestResourceStream("Floe.zip")
            ?? throw new InvalidOperationException("Kurulum dosyasının içi boş.");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        zip.ExtractToDirectory(install, overwriteFiles: true);
    }

    // Packaged apps see AppData redirected into Packages\<name>\LocalCache; a Floe started from inside one saved
    // its settings there. The newest copy of each file wins, so settings made since are kept.
    private static int BringSettings(string local, string target)
    {
        var packages = Path.Combine(local, "Packages");
        if (!Directory.Exists(packages)) return 0;

        int moved = 0;
        foreach (var package in Directory.EnumerateDirectories(packages))
        {
            var source = Path.Combine(package, "LocalCache", "Roaming", "Floe");
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                if (File.Exists(destination) && File.GetLastWriteTimeUtc(destination) >= File.GetLastWriteTimeUtc(file)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
                moved++;
            }
        }

        return moved;
    }

    // a Start menu entry; Floe adds itself to autostart when it runs. COM through dynamic does not survive
    // trimming, so Windows PowerShell writes the shortcut.
    private static void Shortcut(string install)
    {
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Floe.lnk");
        var target = Path.Combine(install, "Floe.exe");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $"$l=(New-Object -ComObject WScript.Shell).CreateShortcut({Quote(link)});" +
            $"$l.TargetPath={Quote(target)};$l.WorkingDirectory={Quote(install)};$l.Save()";
        using var shell = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        shell?.WaitForExit(15000);
    }

    // plain Win32 message boxes, so the setup needs no UI framework and trims down small
    private const uint MbOkCancel = 0x1;
    private const uint MbIconError = 0x10;
    private const uint MbIconQuestion = 0x20;
    private const uint MbIconInformation = 0x40;
    private const int IdOk = 1;

    private static int Ask(string text, uint flags) => MessageBoxW(IntPtr.Zero, text, Title, flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
}
