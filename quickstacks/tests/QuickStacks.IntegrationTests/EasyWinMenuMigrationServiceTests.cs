using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class EasyWinMenuMigrationServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempConfigPath;
    private readonly SqliteMenuRepository _repository;

    public EasyWinMenuMigrationServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quickstacks-test-mig-{Guid.NewGuid():N}.db");
        _tempConfigPath = Path.Combine(Path.GetTempPath(), $"easywinmenu-config-{Guid.NewGuid():N}.json");
        _repository = new SqliteMenuRepository($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
        if (File.Exists(_tempConfigPath))
        {
            File.Delete(_tempConfigPath);
        }
    }

    [Fact]
    public async Task MigrateAsync_ImportsCategoriesAndItemsCorrectly()
    {
        var json = """
        {
          "Categories": [
            {
              "Name": "Taskbar",
              "IsDesktopGroup": true,
              "DesktopX": 300,
              "DesktopY": 150,
              "DesktopWidth": 800,
              "DesktopHeight": 400,
              "DisplayMode": "Panel",
              "Items": [
                {
                  "Name": "Google Chrome",
                  "Type": "Application",
                  "Target": "chrome.exe"
                },
                {
                  "Name": "Documentos Online",
                  "Type": "WebUrl",
                  "Target": "https://docs.google.com"
                }
              ]
            },
            {
              "Name": "Utilitários",
              "IsDesktopGroup": false,
              "Items": [
                {
                  "Name": "Calculadora",
                  "Type": "Application",
                  "Target": "calc.exe"
                }
              ]
            }
          ]
        }
        """;

        await File.WriteAllTextAsync(_tempConfigPath, json);

        var imported = await EasyWinMenuMigrationService.MigrateAsync(_repository, _tempConfigPath);
        Assert.Equal(3, imported);

        var roots = await _repository.GetChildrenAsync(null);
        Assert.Equal(2, roots.Count);

        var taskbar = roots.First(r => r.Name == "Taskbar");
        Assert.True(taskbar.IsDesktopGroup);

        var placement = await _repository.GetDesktopGroupPlacementAsync(taskbar.Id);
        Assert.NotNull(placement);
        Assert.Equal(300, placement.X);
        Assert.Equal(150, placement.Y);
        Assert.Equal(800, placement.Width);
        Assert.Equal(400, placement.Height);

        var taskbarItems = await _repository.GetChildrenAsync(taskbar.Id);
        Assert.Equal(2, taskbarItems.Count);
        Assert.Equal("Google Chrome", taskbarItems[0].Name);
        Assert.Equal(MenuItemType.Executable, taskbarItems[0].Type);
        Assert.Equal("Documentos Online", taskbarItems[1].Name);
        Assert.Equal(MenuItemType.Url, taskbarItems[1].Type);

        var utilitarios = roots.First(r => r.Name == "Utilitários");
        Assert.False(utilitarios.IsDesktopGroup);
    }
}

