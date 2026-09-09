namespace EasyWinMenu.Core.Models;

public sealed class LaunchItem
{
    public required string Name { get; init; }
    public required LaunchItemType Type { get; init; }
    public required string Target { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
}
