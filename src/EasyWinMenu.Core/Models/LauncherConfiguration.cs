namespace EasyWinMenu.Core.Models;

public sealed class LauncherConfiguration
{
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
            ]
        };
    }
}
