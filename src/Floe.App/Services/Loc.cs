using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Floe.App.Services;

/// <summary>
/// Turkish is the source language: a string is its own key, and <see cref="Map"/> gives its English. Code wraps what it
/// shows in <see cref="T"/>; a window of static text calls <see cref="Apply"/> once, and again when the language changes.
/// </summary>
public static partial class Loc
{
    private static Dictionary<string, string>? _reverse;

    public static bool English => GlassSettings.Language switch
    {
        AppLanguage.English => true,
        AppLanguage.Turkish => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr",
    };

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(English ? "en-US" : "tr-TR");

    public static string T(string turkish) => English && Map.TryGetValue(turkish, out var english) ? english : turkish;

    /// <summary>Puts every static text under <paramref name="root"/> in the current language, whichever it was in.</summary>
    public static void Apply(DependencyObject root)
    {
        switch (root)
        {
            case TextBlock block:
                if (Swap(block.Text) is var text && text != block.Text) block.Text = text;
                break;
            case ContentControl { Content: string content } control:
                control.Content = Swap(content);
                break;
        }

        if (root is FrameworkElement { ToolTip: string tip } element)
        {
            element.ToolTip = Swap(tip);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            Apply(child);
        }
    }

    private static string Swap(string text)
    {
        if (English) return T(text);
        _reverse ??= Map.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.First().Key);
        return _reverse.TryGetValue(text, out var turkish) ? turkish : text;
    }
}
