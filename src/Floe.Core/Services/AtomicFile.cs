namespace Floe.Core.Services;

public static class AtomicFile
{
    // Writes to a temp file next to the target, then swaps it in, so a crash mid-write
    // never leaves a truncated config behind.
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // best effort
            }

            throw;
        }
    }
}
