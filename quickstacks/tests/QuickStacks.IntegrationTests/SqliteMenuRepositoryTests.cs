using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class SqliteMenuRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteMenuRepository _repository;

    public SqliteMenuRepositoryTests()
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

    [Fact]
    public async Task AddAndGetChildren_ReturnsItemsOrderedBySortOrder()
    {
        var folder = MenuItem.CreateFolder("Desenvolvimento", null, 0);
        await _repository.AddAsync(folder);

        var second = MenuItem.CreateShortcut("Git", folder.Id, MenuItemType.Executable, "git.exe", 1);
        var first = MenuItem.CreateShortcut("Visual Studio", folder.Id, MenuItemType.Executable, "devenv.exe", 0);
        await _repository.AddAsync(second);
        await _repository.AddAsync(first);

        var children = await _repository.GetChildrenAsync(folder.Id);

        Assert.Equal(2, children.Count);
        Assert.Equal("Visual Studio", children[0].Name);
        Assert.Equal("Git", children[1].Name);
    }

    [Fact]
    public async Task MoveAsync_MovesItemToNewParent()
    {
        var origem = MenuItem.CreateFolder("Origem", null, 0);
        var destino = MenuItem.CreateFolder("Destino", null, 1);
        var item = MenuItem.CreateShortcut("Notepad", origem.Id, MenuItemType.Executable, "notepad.exe", 0);
        await _repository.AddAsync(origem);
        await _repository.AddAsync(destino);
        await _repository.AddAsync(item);

        await _repository.MoveAsync(item.Id, destino.Id);

        Assert.Empty(await _repository.GetChildrenAsync(origem.Id));
        var movido = Assert.Single(await _repository.GetChildrenAsync(destino.Id));
        Assert.Equal(item.Id, movido.Id);
    }

    [Fact]
    public async Task MoveAsync_IntoOwnDescendant_ThrowsAndDoesNotMove()
    {
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateFolder("Filho", pai.Id, 0);
        await _repository.AddAsync(pai);
        await _repository.AddAsync(filho);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.MoveAsync(pai.Id, filho.Id));

        // 'Pai' continua na raiz - o move nao deve ter tido efeito parcial.
        var raiz = await _repository.GetChildrenAsync(null);
        Assert.Contains(raiz, i => i.Id == pai.Id);
    }

    [Fact]
    public async Task MoveAsync_IntoSelf_Throws()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.MoveAsync(pasta.Id, pasta.Id));
    }

    [Fact]
    public async Task DeleteAsync_OnFolder_CascadesToChildren()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        var filho = MenuItem.CreateShortcut("Notepad", pasta.Id, MenuItemType.Executable, "notepad.exe", 0);
        await _repository.AddAsync(pasta);
        await _repository.AddAsync(filho);

        await _repository.DeleteAsync(pasta.Id);

        Assert.Null(await _repository.GetByIdAsync(pasta.Id));
        Assert.Null(await _repository.GetByIdAsync(filho.Id));
    }

    [Fact]
    public async Task ReorderChildrenAsync_UpdatesSortOrder()
    {
        var a = MenuItem.CreateFolder("A", null, 0);
        var b = MenuItem.CreateFolder("B", null, 1);
        await _repository.AddAsync(a);
        await _repository.AddAsync(b);

        await _repository.ReorderChildrenAsync(null, [b.Id, a.Id]);

        var raiz = await _repository.GetChildrenAsync(null);
        Assert.Equal("B", raiz[0].Name);
        Assert.Equal("A", raiz[1].Name);
    }

    [Fact]
    public async Task ReplaceAllAsync_RebuildsTreeEvenWithChildBeforeParentInList()
    {
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateShortcut("Filho", pai.Id, MenuItemType.Executable, "calc.exe", 0);

        // Proposital: filho antes do pai na lista, para provar que o import nao depende
        // da ordem (FK fica desligada so durante a carga - ver SqliteMenuRepository.ReplaceAllAsync).
        await _repository.ReplaceAllAsync([filho, pai]);

        var raiz = await _repository.GetChildrenAsync(null);
        var doPai = await _repository.GetChildrenAsync(pai.Id);
        Assert.Single(raiz);
        Assert.Equal("Pai", raiz[0].Name);
        Assert.Single(doPai);
        Assert.Equal("Filho", doPai[0].Name);
    }

    [Fact]
    public async Task ReplaceAllAsync_ThenDelete_StillCascades()
    {
        // Garante que a FK volta a ficar ligada depois do import (nao fica OFF para sempre).
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateShortcut("Filho", pai.Id, MenuItemType.Executable, "calc.exe", 0);
        await _repository.ReplaceAllAsync([pai, filho]);

        await _repository.DeleteAsync(pai.Id);

        Assert.Null(await _repository.GetByIdAsync(filho.Id));
    }
}
