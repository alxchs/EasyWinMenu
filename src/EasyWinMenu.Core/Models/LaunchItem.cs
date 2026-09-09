namespace EasyWinMenu.Core.Models;

public sealed class LaunchItem
{
    public required string Name { get; set; }
    public required LaunchItemType Type { get; set; }
    public required string Target { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }

    public LaunchItem Clone()
    {
        return new LaunchItem
        {
            Name = Name,
            Type = Type,
            Target = Target,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory
        };
    }
}
