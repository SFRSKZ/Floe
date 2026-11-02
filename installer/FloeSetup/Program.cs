using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FloeSetup;

/// <summary>
/// Installs Floe for the current user into %LOCALAPPDATA%\Floe\app. Run from Explorer, this lands in the real
/// AppData, not in the private copy an MSIX-packaged app (such as the Claude desktop app) sees, so the dock no
/// longer closes when that app updates. Settings that were written inside such a package come along.
/// </summary>
internal static class Program
{
#if CP77
    private const string ProductName = "Floe CP77";
    private const string FolderName = "FloeCP77";
    private const string ExeName = "FloeCP77.exe";
#else
    private const string ProductName = "Floe";
    private const string FolderName = "Floe";
    private const string ExeName = "Floe.exe";
#endif
    private static string Title => Text($"{ProductName} kurulumu", $"{ProductName} Setup");

    private static string Text(string tr, string en) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? tr : en;

    [STAThread]
    private static int Main()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            return Uninstall();
        }

        if (Ask(Text($"{ProductName} kurulsun mu? Açıksa kapatılıp yenisi başlatılır.", $"Install {ProductName}? If it is running, it will be closed and started again."), MbOkCancel | MbIconQuestion) != IdOk) return 1;

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var productRoot = Path.Combine(local, FolderName);
            var install = Path.Combine(productRoot, "app");
            var settings = Path.Combine(roaming, FolderName);

            StopFloe();
            ShowTaskbar();
            Extract(install);
#if CP77
            // a fresh CP77 dock starts with the plain dock's settings, unless it already has pins of its own
            int moved = CopyFloeSettings(roaming, settings);
#else
            int moved = 0;
#endif
            moved += BringSettings(local, settings);
            Shortcut(install);
            InstallUninstaller(productRoot, install);
            Process.Start(new ProcessStartInfo(Path.Combine(install, ExeName)) { UseShellExecute = true, WorkingDirectory = install });

            Ask(Text($"{ProductName} kuruldu:\n{install}", $"{ProductName} installed:\n{install}")
                + (moved > 0 ? Text($"\n\n{moved} ayar dosyası taşındı.", $"\n\n{moved} settings file(s) moved.") : string.Empty), MbIconInformation);
            return 0;
        }
        catch (Exception ex)
        {
            Ask(Text("Kurulum olmadı:\n", "Installation failed:\n") + ex.Message, MbIconError);
            return 2;
        }
    }

    private static int Uninstall()
    {
        if (Ask(Text($"{ProductName} kaldırılsın mı? Ayarlar AppData'da bırakılır.", $"Uninstall {ProductName}? Settings will be kept in AppData."), MbOkCancel | MbIconQuestion) != IdOk) return 1;

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.GetFullPath(Path.Combine(local, FolderName));
            var expectedParent = Path.GetFullPath(local).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(root), FolderName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Text("Kurulum klasörü güvenli değil; hiçbir dosya silinmedi.", "The install folder is not safe; no files were removed."));
            }

            StopFloe();
            ShowTaskbar();
            RemoveTemporaryLogs();
            ScheduleInstallFolderRemoval(root);
            RemoveAutostart();
            RemoveStartMenuShortcuts();
            RemoveUninstallRegistration();
            Ask(Text($"{ProductName} kaldırılıyor. Ayarlar AppData klasöründe bırakıldı.", $"{ProductName} is being removed. Settings remain in AppData."), MbIconInformation);
            return 0;
        }
        catch (Exception ex)
        {
            Ask(Text("Kaldırma olmadı:\n", "Uninstall failed:\n") + ex.Message, MbIconError);
            return 2;
        }
    }

    private static void InstallUninstaller(string productRoot, string install)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException(Text("Kurulum programı bulunamadı.", "The setup executable could not be found."));
        var uninstaller = Path.Combine(productRoot, UninstallerName);
        File.Copy(source, uninstaller, overwrite: true);
        UninstallShortcut(productRoot, uninstaller);

        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{FolderName}", writable: true)
            ?? throw new InvalidOperationException(Text("Kaldırma kaydı oluşturulamadı.", "Could not register the uninstaller."));
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayIcon", Path.Combine(install, ExeName));
        key.SetValue("InstallLocation", install);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string UninstallerName
    {
        get
        {
#if CP77
            return "FloeCP77Uninstall.exe";
#else
            return "FloeUninstall.exe";
#endif
        }
    }

    private static void UninstallShortcut(string productRoot, string uninstaller)
    {
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + " kaldır.lnk");
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $"$l=(New-Object -ComObject WScript.Shell).CreateShortcut({Quote(link)});" +
            $"$l.TargetPath={Quote(uninstaller)};$l.Arguments='--uninstall';$l.WorkingDirectory={Quote(productRoot)};$l.Save()";
        using var shell = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        shell?.WaitForExit(15000);
    }

    private static void RemoveAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(FolderName, false);
            // Older builds used this entry before the Floe rename.
            key?.DeleteValue("MacDock", false);
        }
        catch
        {
            // best effort; the installed app is still removed
        }
    }

    private static void RemoveStartMenuShortcuts()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        foreach (var name in new[] { ProductName + ".lnk", ProductName + " kaldır.lnk" })
        {
            try { File.Delete(Path.Combine(programs, name)); }
            catch { /* best effort */ }
        }
    }

    private static void RemoveUninstallRegistration()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{FolderName}", false);
        }
        catch
        {
            // best effort; this key contains only this product's uninstall entry
        }
    }

    private static void RemoveTemporaryLogs()
    {
        foreach (var name in new[] { "floe.log", "floe-match.log", "floe-colors.log", "floe-click.wav", "floe-bench.txt" })
        {
            try { File.Delete(Path.Combine(Path.GetTempPath(), name)); }
            catch { /* best effort */ }
        }
    }

    private static void ScheduleInstallFolderRemoval(string root)
    {
        var pid = Environment.ProcessId;
        var escaped = root.Replace("'", "''");
        var script = $"$p={pid};$d='{escaped}';try{{Wait-Process -Id $p -ErrorAction SilentlyContinue}}catch{{}};Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var cleanup = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded },
            CreateNoWindow = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException(Text("Kaldırma temizliği başlatılamadı.", "Could not start uninstall cleanup."));
        cleanup.Dispose();
    }

    // Both docks want the same screen edge, so either one running would hold the files; MacDock is the
    // name builds ran under before the rename.
    private static void StopFloe()
    {
        foreach (var process in Process.GetProcessesByName("Floe")
                     .Concat(Process.GetProcessesByName("FloeCP77"))
                     .Concat(Process.GetProcessesByName("MacDock")))
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

    private const int SwShow = 5;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    private static void ShowTaskbar()
    {
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) ShowWindow(primary, SwShow);

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            ShowWindow(secondary, SwShow);
        }
    }

    // a clean folder each time, so files a newer build dropped do not linger
    private static void Extract(string install)
    {
        if (Directory.Exists(install)) Directory.Delete(install, recursive: true);
        Directory.CreateDirectory(install);
        using var payload = typeof(Program).Assembly.GetManifestResourceStream("Floe.zip")
            ?? throw new InvalidOperationException(Text("Kurulum dosyasının içi boş.", "The installer payload is empty."));
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
            var source = Path.Combine(package, "LocalCache", "Roaming", FolderName);
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

#if CP77
    // The plain dock's settings carried over once; after CP77 has pins of its own it is a separate product.
    private static int CopyFloeSettings(string roaming, string target)
    {
        var source = Path.Combine(roaming, "Floe");
        if (File.Exists(Path.Combine(target, "pinned.json")) || !Directory.Exists(source)) return 0;

        int copied = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            copied++;
        }

        return copied;
    }
#endif

    // a Start menu entry; Floe adds itself to autostart when it runs. COM through dynamic does not survive
    // trimming, so Windows PowerShell writes the shortcut.
    private static void Shortcut(string install)
    {
        var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk");
        var target = Path.Combine(install, ExeName);
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
