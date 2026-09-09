namespace EasyWinMenu.Core.Models;

public interface IMenuContainer
{
    List<MenuCategory> Categories { get; }
    List<LaunchItem> Items { get; }
}
