namespace EasyWinMenu.Core.Models;

public sealed class LauncherConfiguration : IMenuContainer
{
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];

    public LauncherConfiguration Clone()
    {
        return new LauncherConfiguration
        {
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())]
        };
    }

    public void ReplaceContentsWith(LauncherConfiguration source)
    {
        Items.Clear();
        Items.AddRange(source.Items.Select(item => item.Clone()));
        Categories.Clear();
        Categories.AddRange(source.Categories.Select(category => category.Clone()));
    }

    public static LauncherConfiguration CreateDefault()
    {
        return new LauncherConfiguration
        {
            Items =
            [
                new LaunchItem
                {
                    Name = "Bloco de Notas",
                    Type = LaunchItemType.Application,
                    Target = "notepad.exe"
                }
            ],
            Categories =
            [
                new MenuCategory
                {
                    Name = "Ferramentas do Windows",
                    Items =
                    [
                        new LaunchItem
                        {
                            Name = "Calculadora",
                            Type = LaunchItemType.Application,
                            Target = "calc.exe"
                        },
                        new LaunchItem
                        {
                            Name = "Paint",
                            Type = LaunchItemType.Application,
                            Target = "mspaint.exe"
                        }
                    ]
                }
            ]
        };
    }
}
