namespace EasyWinMenu.Core.Models;

public sealed class MenuCategory : IMenuContainer
{
    public required string Name { get; set; }
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];
    public MenuTheme? ThemeOverride { get; set; }
    public double DesktopX { get; set; } = 40;
    public double DesktopY { get; set; } = 40;
    public double DesktopWidth { get; set; } = 220;
    public double DesktopHeight { get; set; } = 180;

    public MenuCategory Clone()
    {
        return new MenuCategory
        {
            Name = Name,
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())],
            ThemeOverride = ThemeOverride?.Clone(),
            DesktopX = DesktopX,
            DesktopY = DesktopY,
            DesktopWidth = DesktopWidth,
            DesktopHeight = DesktopHeight
        };
    }
}
