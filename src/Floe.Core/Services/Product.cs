namespace Floe.Core.Services;

/// <summary>
/// The one place the two docks get their names. Both are built from this codebase; only the Cyber
/// code reaches the screen in CP77, and this says which product the running build is.
/// </summary>
public static class Product
{
#if CP77
    public static bool Cp77 => true;

    public const string Name = "Floe CP77";

    public const string Folder = "FloeCP77";

    public const string Exe = "FloeCP77";

    public const string Sibling = "Floe";

    public const string QuitLabel = "Floe CP77'yi kapat";
#else
    public static bool Cp77 => false;

    public const string Name = "Floe";

    public const string Folder = "Floe";

    public const string Exe = "Floe";

    public const string Sibling = "FloeCP77";

    public const string QuitLabel = "Floe'yu kapat";
#endif
}
