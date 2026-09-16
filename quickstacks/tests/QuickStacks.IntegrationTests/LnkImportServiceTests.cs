using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class LnkImportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteMenuRepository _repository;

    public LnkImportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quickstacks-tests-{Guid.NewGuid():N}.db");
        _repository = new SqliteMenuRepository($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private sealed class FakeLnkResolver : ILnkResolver
    {
        public LnkShortcutInfo Resolve(string lnkFilePath) =>
            new(Path.GetFileNameWithoutExtension(lnkFilePath), $@"C:\Fake\{Path.GetFileName(lnkFilePath)}.exe", "-x", @"C:\Fake", null);
    }

    [Fact]
    public async Task ImportAsync_CreatesOneItemPerLnk_UnderTheGivenParent_InOrder()
    {
        var pasta = MenuItem.CreateFolder("Importados", null, 0);
        await _repository.AddAsync(pasta);

        var service = new LnkImportService(new FakeLnkResolver(), _repository);
        var created = await service.ImportAsync(["a.lnk", "b.lnk"], pasta.Id);

        Assert.Equal(2, created.Count);
        var filhos = await _repository.GetChildrenAsync(pasta.Id);
        Assert.Equal(2, filhos.Count);
        Assert.Equal("a", filhos[0].Name);
        Assert.Equal("b", filhos[1].Name);
        Assert.Equal(MenuItemType.Executable, filhos[0].Type);
        Assert.Equal("-x", filhos[0].Arguments);
    }

    [Fact]
    public async Task ImportAsync_AppendsAfterExistingSiblings_DoesNotOverwriteSortOrder()
    {
        var existente = MenuItem.CreateShortcut("Ja existia", null, MenuItemType.Executable, "calc.exe", 0);
        await _repository.AddAsync(existente);

        var service = new LnkImportService(new FakeLnkResolver(), _repository);
        await service.ImportAsync(["novo.lnk"], null);

        var raiz = await _repository.GetChildrenAsync(null);
        Assert.Equal(2, raiz.Count);
        Assert.Equal("Ja existia", raiz[0].Name);
        Assert.Equal("novo", raiz[1].Name);
    }
}
