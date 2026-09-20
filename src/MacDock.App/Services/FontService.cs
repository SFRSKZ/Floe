using System.Windows.Media;

namespace MacDock.App.Services;

public static class FontService
{
    private static readonly HashSet<string> Installed = new(StringComparer.OrdinalIgnoreCase);

    static FontService()
    {
        try
        {
            foreach (var family in Fonts.SystemFontFamilies)
            {
                Installed.Add(family.Source);
            }
        }
        catch
        {
            // font enumeration may fail; fall back to defaults
        }
    }

    public static FontFamily? LoadFromFile(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            var glyph = new GlyphTypeface(new Uri(path));
            string? name = null;
            foreach (var value in glyph.FamilyNames.Values)
            {
                name = value;
                break;
            }

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var directory = System.IO.Path.GetDirectoryName(path)! + System.IO.Path.DirectorySeparatorChar;
            return new FontFamily(new Uri(directory), "./#" + name);
        }
        catch
        {
            return null;
        }
    }

    public static string Resolve(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Installed.Contains(candidate))
            {
                return candidate;
            }
        }

        return candidates.Length > 0 ? candidates[^1] : "Segoe UI";
    }
}
