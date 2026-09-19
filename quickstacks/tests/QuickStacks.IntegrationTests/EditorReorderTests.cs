using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

/// <summary>
/// Reordenar irmaos por arraste (pendencia anotada na Fase 2): o gesto na TreeView decide a
/// posicao, e o SortOrder do nivel inteiro e' regravado em lote.
/// </summary>
public class EditorReorderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteMenuRepository _repository;
    private readonly EditorViewModel _editor;

    public EditorReorderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quickstacks-reorder-{Guid.NewGuid():N}.db");
        _repository = new SqliteMenuRepository($"Data Source={_dbPath}");
        _editor = new EditorViewModel(
            _repository,
            new ConfigExportService(_repository),
            new LnkImportService(new LnkResolver(), _repository));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private async Task<List<MenuItem>> SeedSiblingsAsync(params string[] names)
    {
        var created = new List<MenuItem>();
        for (var i = 0; i < names.Length; i++)
        {
            var item = MenuItem.CreateShortcut(names[i], null, MenuItemType.Executable, $"{names[i]}.exe", i);
            await _repository.AddAsync(item);
            created.Add(item);
        }

        return created;
    }

    private async Task<string[]> CurrentOrderAsync(string? parentId = null)
    {
        var children = await _repository.GetChildrenAsync(parentId);
        return children.Select(c => c.Name).ToArray();
    }

    [Fact]
    public async Task TryReorder_MovingDown_PlacesItemAfterTarget()
    {
        var items = await SeedSiblingsAsync("A", "B", "C", "D");

        var ok = await _editor.TryReorderAsync(items[0].Id, items[2].Id, insertAfter: true);

        Assert.True(ok);
        Assert.Equal(["B", "C", "A", "D"], await CurrentOrderAsync());
    }

    [Fact]
    public async Task TryReorder_MovingUp_PlacesItemBeforeTarget()
    {
        var items = await SeedSiblingsAsync("A", "B", "C", "D");

        var ok = await _editor.TryReorderAsync(items[3].Id, items[1].Id, insertAfter: false);

        Assert.True(ok);
        Assert.Equal(["A", "D", "B", "C"], await CurrentOrderAsync());
    }

    [Fact]
    public async Task TryReorder_PersistsContiguousSortOrder()
    {
        var items = await SeedSiblingsAsync("A", "B", "C", "D");

        await _editor.TryReorderAsync(items[0].Id, items[3].Id, insertAfter: true);

        var children = await _repository.GetChildrenAsync(null);
        Assert.Equal([0, 1, 2, 3], children.Select(c => c.SortOrder));
    }

    [Fact]
    public async Task TryReorder_AcrossParents_MovesIntoTargetLevelAtTheRightPosition()
    {
        var folder = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(folder);

        var inside = new List<MenuItem>();
        for (var i = 0; i < 3; i++)
        {
            var item = MenuItem.CreateShortcut($"Dentro{i}", folder.Id, MenuItemType.Executable, $"d{i}.exe", i);
            await _repository.AddAsync(item);
            inside.Add(item);
        }

        var outsider = MenuItem.CreateShortcut("DeFora", null, MenuItemType.Executable, "fora.exe", 1);
        await _repository.AddAsync(outsider);

        var ok = await _editor.TryReorderAsync(outsider.Id, inside[1].Id, insertAfter: false);

        Assert.True(ok);
        Assert.Equal(["Dentro0", "DeFora", "Dentro1", "Dentro2"], await CurrentOrderAsync(folder.Id));
        Assert.Equal(["Pasta"], await CurrentOrderAsync());
    }

    [Fact]
    public async Task TryReorder_OntoItself_IsRejected()
    {
        var items = await SeedSiblingsAsync("A", "B");

        Assert.False(await _editor.TryReorderAsync(items[0].Id, items[0].Id, insertAfter: true));
        Assert.Equal(["A", "B"], await CurrentOrderAsync());
    }

    [Fact]
    public async Task TryReorder_FolderOntoItsOwnDescendant_IsRejectedWithoutChangingAnything()
    {
        var parent = MenuItem.CreateFolder("Pai", null, 0);
        await _repository.AddAsync(parent);
        var child = MenuItem.CreateFolder("Filho", parent.Id, 0);
        await _repository.AddAsync(child);
        var grandChild = MenuItem.CreateShortcut("Neto", child.Id, MenuItemType.Executable, "neto.exe", 0);
        await _repository.AddAsync(grandChild);

        var ok = await _editor.TryReorderAsync(parent.Id, grandChild.Id, insertAfter: true);

        Assert.False(ok);
        Assert.Equal(["Pai"], await CurrentOrderAsync());
        Assert.Equal(["Filho"], await CurrentOrderAsync(parent.Id));
        Assert.Equal(["Neto"], await CurrentOrderAsync(child.Id));
    }
}
