namespace MacDock.Core.Models;

public sealed class DockApp
{
    public string Name { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? IconPath { get; set; }
}
