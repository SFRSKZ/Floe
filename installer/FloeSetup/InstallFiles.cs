using System.IO.Compression;

namespace FloeSetup;

/// <summary>
/// The setup's file work, kept apart so it can be tested: a new build is unpacked and checked beside the running
/// one and only then swapped in, and an uninstall says which files it could not remove instead of assuming.
/// </summary>
internal static class InstallFiles
{
    /// <summary>Unpacks <paramref name="zip"/> into a fresh <paramref name="stage"/> folder and checks every file arrived whole.</summary>
    /// <exception cref="InvalidDataException">The payload is incomplete or lacks <paramref name="requiredFiles"/>.</exception>
    public static void Stage(Stream zip, string stage, params string[] requiredFiles)
    {
        if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        Directory.CreateDirectory(stage);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        if (archive.Entries.Count == 0) throw new InvalidDataException("empty payload");
        archive.ExtractToDirectory(stage, overwriteFiles: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var file = new FileInfo(Path.Combine(stage, entry.FullName));
            if (!file.Exists || file.Length != entry.Length)
            {
                throw new InvalidDataException($"{entry.FullName} did not unpack whole");
            }
        }

        foreach (var required in requiredFiles)
        {
            if (!File.Exists(Path.Combine(stage, required))) throw new InvalidDataException($"{required} is missing");
        }
    }

    /// <summary>
    /// Puts the checked <paramref name="stage"/> in place of <paramref name="install"/>. The old build is moved
    /// aside first and comes back if the new one cannot be moved in, so a failed update leaves the old build working.
    /// </summary>
    /// <returns>The old build's folder, still to be deleted, or null when there was none.</returns>
    public static string? Swap(string stage, string install)
    {
        string? previous = null;
        if (Directory.Exists(install))
        {
            previous = install + ".old-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            MoveWithRetry(install, previous);
        }

        try
        {
            MoveWithRetry(stage, install);
        }
        catch
        {
            if (previous is not null && !Directory.Exists(install)) MoveWithRetry(previous, install);
            throw;
        }

        return previous;
    }

    /// <summary>Old builds left aside by earlier updates (one whose files were still held), removed when possible.</summary>
    public static void RemoveLeftovers(string install)
    {
        var parent = Path.GetDirectoryName(install);
        if (parent is null || !Directory.Exists(parent)) return;
        var name = Path.GetFileName(install);
        // an update that failed twice over (new build not moved in, old one not moved back) left no app folder: the
        // newest old build is the only working copy, so it goes back in place instead of being cleared
        if (!Directory.Exists(install))
        {
            var newest = Directory.EnumerateDirectories(parent, name + ".old-*").OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
            if (newest is not null)
            {
                try { Directory.Move(newest, install); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(parent, name + ".old-*").Concat(Directory.EnumerateDirectories(parent, name + ".new-*")))
        {
            RemoveTree(dir, attempts: 1);
        }
    }

    /// <summary>Deletes a folder, retrying while files are still held; returns what is left (empty when it is gone).</summary>
    public static IReadOnlyList<string> RemoveTree(string dir, int attempts = 5, int delayMs = 400)
    {
        for (var attempt = 0; attempt < attempts && Directory.Exists(dir); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                if (attempt + 1 < attempts) Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt + 1 < attempts) Thread.Sleep(delayMs);
            }
        }

        return Directory.Exists(dir)
            ? Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).ToList()
            : Array.Empty<string>();
    }

    /// <summary>
    /// The folder the product owns and an uninstall may remove: exactly <paramref name="folderName"/> right under
    /// <paramref name="local"/>, nothing above or beside it.
    /// </summary>
    public static string ProductRoot(string local, string folderName)
    {
        var root = Path.GetFullPath(Path.Combine(local, folderName));
        var parent = Path.GetFullPath(local).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.IsNullOrWhiteSpace(folderName) || folderName.IndexOfAny(new[] { '\\', '/', '.' }) >= 0
            || !root.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(root), folderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("unsafe install folder");
        }

        return root;
    }

    private static void MoveWithRetry(string from, string to)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException ex) when (attempt < 10 && ex is not DirectoryNotFoundException)
            {
                // a dock that just quit, or a scanner reading the new files, can hold the folder for a moment
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }
}
