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

            // the new build is unpacked and checked beside the running one; until it is whole nothing is touched
            InstallFiles.RemoveLeftovers(install);
            var stage = install + ".new-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                using var payload = typeof(Program).Assembly.GetManifestResourceStream("Floe.zip")
                    ?? throw new InvalidDataException(Text("Kurulum dosyasının içi boş.", "The installer payload is empty."));
                InstallFiles.Stage(payload, stage, ExeName, Path.ChangeExtension(ExeName, ".dll"));
            }
            catch (Exception ex)
            {
                InstallFiles.RemoveTree(stage, attempts: 1);
                throw new InvalidOperationException(Text("Yeni sürüm açılamadı; kurulu sürüme dokunulmadı.\n", "The new version could not be unpacked; the installed one was left as it was.\n") + ex.Message, ex);
            }

            StopFloe();
            ShowTaskbar();
            string? previous;
            try
            {
                previous = InstallFiles.Swap(stage, install);
            }
            catch (Exception ex)
            {
                InstallFiles.RemoveTree(stage, attempts: 1);
                // the old build is back in place: it runs again as it did before the update
                var oldExe = Path.Combine(install, ExeName);
                if (File.Exists(oldExe)) Process.Start(new ProcessStartInfo(oldExe) { UseShellExecute = true, WorkingDirectory = install });
                throw new InvalidOperationException(Text("Yeni sürüm yerine konamadı; eski sürüm korundu.\n", "The new version could not be put in place; the old one was kept.\n") + ex.Message, ex);
            }

            // a file still held keeps the old folder until the next update clears it
            if (previous is not null) InstallFiles.RemoveTree(previous, attempts: 3);
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
        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);
        if (Ask(Text($"{ProductName} kaldırılsın mı?\n\nAyarlar silinmez, burada kalır:\n{settings}", $"Uninstall {ProductName}?\n\nSettings are not removed; they stay in:\n{settings}"), MbOkCancel | MbIconQuestion) != IdOk) return 1;

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string root;
            try
            {
                root = InstallFiles.ProductRoot(local, FolderName);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(Text("Kurulum klasörü güvenli değil; hiçbir dosya silinmedi.", "The install folder is not safe; no files were removed."));
            }

            // only this dock goes: the other product, if it runs, is left running, and so is its hold on the taskbar
            var otherRuns = StopFloe(onlyThisProduct: true);
            if (!otherRuns)
            {
                ShowTaskbar();
                RestoreTaskbarAlignment();
            }

            RemoveAutostart();

            // the app's files must really be gone before the uninstaller and its entry go; otherwise both stay so
            // the uninstall can simply be run again
            var app = Path.Combine(root, "app");
            InstallFiles.RemoveLeftovers(app);
            var left = InstallFiles.RemoveTree(app);
            if (left.Count > 0)
            {
                Ask(Text($"{ProductName} tam kaldırılamadı; {left.Count} dosya silinemedi, örneğin:\n{left[0]}\n\nKaldırıcı ve kaydı yerinde bırakıldı. {ProductName}'u kapatıp (gerekirse bilgisayarı yeniden başlatıp) kaldırmayı yeniden çalıştır.",
                    $"{ProductName} could not be fully removed; {left.Count} file(s) could not be deleted, e.g.:\n{left[0]}\n\nThe uninstaller and its entry were kept. Close {ProductName} (restart if needed) and run the uninstall again."), MbIconError);
                return 3;
            }

            RemoveTemporaryLogs(local);
            RemoveStartMenuShortcuts();
            RemoveUninstallRegistration();
            // only this product's folder is left, holding this uninstaller: it goes once this process has exited
            ScheduleInstallFolderRemoval(root);
            Ask(Text($"{ProductName} kaldırıldı.\n\nAyarlar silinmedi, burada duruyor:\n{settings}", $"{ProductName} was removed.\n\nSettings were kept in:\n{settings}"), MbIconInformation);
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

    // the two docks share these temp files: they go only when the other product is not installed
    private static void RemoveTemporaryLogs(string local)
    {
#if CP77
        const string other = "Floe";
#else
        const string other = "FloeCP77";
#endif
        if (Directory.Exists(Path.Combine(local, other, "app"))) return;
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

    // An install stops both docks: they want the same screen edge, and the one installed is started again. An
    // uninstall stops only its own (MacDock is the name the plain dock's builds ran under before the rename) and
    // says whether the other product is still running.
    private static bool StopFloe(bool onlyThisProduct = false)
    {
        var own = Path.GetFileNameWithoutExtension(ExeName);
        var names = onlyThisProduct
            ? (own == "Floe" ? new[] { "Floe", "MacDock" } : new[] { own })
            : new[] { "Floe", "FloeCP77", "MacDock" };
        foreach (var process in names.SelectMany(Process.GetProcessesByName))
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

        if (!onlyThisProduct) return false;
        var other = own == "Floe" ? "FloeCP77" : "Floe";
        var running = Process.GetProcessesByName(other);
        foreach (var process in running) process.Dispose();
        return running.Length > 0;
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

    // The dock turns the taskbar's alignment left while it is a taskbar and keeps the value Windows had under its
    // own key (Floe.Core TaskbarAlignment); a dock that was killed never put it back, so the uninstall does.
    private static void RestoreTaskbarAlignment()
    {
        try
        {
            var savedName = FolderName + ".TaskbarAlBefore";
            using var floe = Registry.CurrentUser.OpenSubKey(@"Software\Floe", writable: true);
            if (floe?.GetValue(savedName) is not int saved) return;
            using (var explorer = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
            {
                explorer.SetValue("TaskbarAl", saved, RegistryValueKind.DWord);
            }

            floe.DeleteValue(savedName, false);
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "TraySettings", 0x0002, 500, out _);
        }
        catch
        {
            // the alignment is the user's to set again; the uninstall itself goes on
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

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
