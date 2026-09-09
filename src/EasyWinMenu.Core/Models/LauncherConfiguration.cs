namespace EasyWinMenu.Core.Models;

public sealed class LauncherConfiguration
{
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];

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
