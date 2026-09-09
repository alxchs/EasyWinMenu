namespace EasyWinMenu.Core.Models;

public sealed class MenuCategory : IMenuContainer
{
    public required string Name { get; set; }
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];

    public MenuCategory Clone()
    {
        return new MenuCategory
        {
            Name = Name,
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())]
        };
    }
}
