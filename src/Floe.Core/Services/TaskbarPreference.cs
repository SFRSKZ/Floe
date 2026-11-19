namespace Floe.Core.Services;

/// <summary>Whether the dock hides Windows' taskbar: the saved choice, with the command line able to override it.</summary>
public static class TaskbarPreference
{
    /// <param name="hideChosen">The "hide Windows taskbar" setting. The dock's taskbar layout has no say in it.</param>
    /// <param name="args">--keep-taskbar keeps it whatever else is asked; --hide-taskbar hides it.</param>
    public static bool HidesTaskbar(bool hideChosen, IEnumerable<string> args)
    {
        var list = args as ICollection<string> ?? args.ToList();
        if (list.Any(a => a.Equals("--keep-taskbar", StringComparison.OrdinalIgnoreCase))) return false;
        return hideChosen || list.Any(a => a.Equals("--hide-taskbar", StringComparison.OrdinalIgnoreCase));
    }
}
