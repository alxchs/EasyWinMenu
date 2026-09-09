namespace EasyWinMenu.Core.Models;

public sealed class MenuCategory
{
    public required string Name { get; init; }
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];
}
