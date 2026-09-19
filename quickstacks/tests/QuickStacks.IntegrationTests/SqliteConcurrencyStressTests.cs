using System.Diagnostics;
using Microsoft.Data.Sqlite;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace QuickStacks.IntegrationTests;

/// <summary>
/// Testes de stress do acesso concorrente ao banco.
///
/// O QuickStacks no modo Full abre varias janelas ao mesmo tempo (PopupWindow, EditorWindow e
/// uma DesktopGroupWindow por grupo solto), e cada uma fala com o mesmo arquivo SQLite por
/// instancias separadas de SqliteMenuRepository. Estes testes reproduzem esse padrao real:
/// varios "donos de janela" escrevendo e lendo em paralelo no mesmo arquivo.
/// </summary>
public class SqliteConcurrencyStressTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ITestOutputHelper _output;

    public SqliteConcurrencyStressTests(ITestOutputHelper output)
    {
        _output = output;
        _dbPath = Path.Combine(Path.GetTempPath(), $"quickstacks-stress-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Schema_EnablesWalJournalMode()
    {
        _ = new SqliteMenuRepository(_connectionString);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = Convert.ToString(command.ExecuteScalar());

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task ConcurrentWriters_FromSeparateRepositories_AllSucceed()
    {
        const int windows = 8;
        const int itemsPerWindow = 40;

        var root = MenuItem.CreateFolder("Raiz", null, 0);
        var seed = new SqliteMenuRepository(_connectionString);
        await seed.AddAsync(root);

        var stopwatch = Stopwatch.StartNew();
        var failures = new List<Exception>();

        var tasks = Enumerable.Range(0, windows).Select(window => Task.Run(async () =>
        {
            // Cada "janela" tem seu proprio repositorio, como acontece no app real.
            var repository = new SqliteMenuRepository(_connectionString);
            for (var i = 0; i < itemsPerWindow; i++)
            {
                try
                {
                    var item = MenuItem.CreateShortcut(
                        $"Janela{window}-Item{i}",
                        root.Id,
                        MenuItemType.Executable,
                        $"app{window}-{i}.exe",
                        i);
                    await repository.AddAsync(item);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        _output.WriteLine($"{windows} escritores x {itemsPerWindow} itens em {stopwatch.ElapsedMilliseconds} ms");
        if (failures.Count > 0)
        {
            _output.WriteLine($"Primeira falha: {failures[0].GetType().Name}: {failures[0].Message}");
        }

        Assert.Empty(failures);

        var children = await seed.GetChildrenAsync(root.Id);
        Assert.Equal(windows * itemsPerWindow, children.Count);
    }

    [Fact]
    public async Task ReadersDuringWrites_NeverFail()
    {
        const int readers = 6;
        const int writers = 4;
        const int writesPerWriter = 30;

        var root = MenuItem.CreateFolder("Raiz", null, 0);
        var seed = new SqliteMenuRepository(_connectionString);
        await seed.AddAsync(root);

        var failures = new List<Exception>();
        using var done = new CancellationTokenSource();

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(async () =>
        {
            var repository = new SqliteMenuRepository(_connectionString);
            while (!done.IsCancellationRequested)
            {
                try
                {
                    await repository.GetChildrenAsync(root.Id);
                    await repository.SearchAsync("Item");
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }
                    return;
                }
            }
        })).ToArray();

        var writerTasks = Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
        {
            var repository = new SqliteMenuRepository(_connectionString);
            for (var i = 0; i < writesPerWriter; i++)
            {
                try
                {
                    var item = MenuItem.CreateShortcut(
                        $"Item-{writer}-{i}",
                        root.Id,
                        MenuItemType.Executable,
                        $"w{writer}-{i}.exe",
                        i);
                    await repository.AddAsync(item);
                    await repository.SetFavoriteAsync(item.Id, true);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(writerTasks);
        done.Cancel();
        await Task.WhenAll(readerTasks);

        if (failures.Count > 0)
        {
            _output.WriteLine($"{failures.Count} falhas. Primeira: {failures[0].GetType().Name}: {failures[0].Message}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task LargeTree_StaysResponsive()
    {
        const int folders = 20;
        const int itemsPerFolder = 100;

        var repository = new SqliteMenuRepository(_connectionString);
        var root = MenuItem.CreateFolder("Raiz", null, 0);
        await repository.AddAsync(root);

        var build = Stopwatch.StartNew();
        for (var f = 0; f < folders; f++)
        {
            var folder = MenuItem.CreateFolder($"Pasta{f}", root.Id, f);
            await repository.AddAsync(folder);
            for (var i = 0; i < itemsPerFolder; i++)
            {
                await repository.AddAsync(MenuItem.CreateShortcut(
                    $"Item{f}-{i}",
                    folder.Id,
                    MenuItemType.Executable,
                    $"p{f}i{i}.exe",
                    i));
            }
        }

        build.Stop();

        var read = Stopwatch.StartNew();
        var all = await repository.GetAllAsync();
        read.Stop();

        var search = Stopwatch.StartNew();
        var found = await repository.SearchAsync("Item7-5");
        search.Stop();

        _output.WriteLine($"Inserir {folders * itemsPerFolder + folders + 1} itens: {build.ElapsedMilliseconds} ms");
        _output.WriteLine($"GetAllAsync: {read.ElapsedMilliseconds} ms ({all.Count} itens)");
        _output.WriteLine($"SearchAsync: {search.ElapsedMilliseconds} ms ({found.Count} resultados)");

        Assert.Equal(folders * itemsPerFolder + folders + 1, all.Count);
        Assert.NotEmpty(found);
        Assert.True(read.ElapsedMilliseconds < 2000, $"GetAllAsync demorou {read.ElapsedMilliseconds} ms");
        Assert.True(search.ElapsedMilliseconds < 1000, $"SearchAsync demorou {search.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task MixedWorkload_UnderSustainedLoad_LeavesDatabaseIntact()
    {
        const int windows = 16;
        const int operationsPerWindow = 60;

        var seed = new SqliteMenuRepository(_connectionString);
        var root = MenuItem.CreateFolder("Raiz", null, 0);
        await seed.AddAsync(root);

        var failures = new List<Exception>();
        var operations = 0;
        var stopwatch = Stopwatch.StartNew();

        // Cada janela faz a mistura real de operacoes: cria, marca favorito, lanca, busca,
        // le a arvore e apaga - tudo contra o mesmo arquivo, ao mesmo tempo.
        var tasks = Enumerable.Range(0, windows).Select(window => Task.Run(async () =>
        {
            var repository = new SqliteMenuRepository(_connectionString);
            var created = new List<string>();

            for (var i = 0; i < operationsPerWindow; i++)
            {
                try
                {
                    switch (i % 6)
                    {
                        case 0:
                            var item = MenuItem.CreateShortcut($"W{window}-{i}", root.Id, MenuItemType.Executable, $"w{window}-{i}.exe", i);
                            await repository.AddAsync(item);
                            created.Add(item.Id);
                            break;
                        case 1 when created.Count > 0:
                            await repository.SetFavoriteAsync(created[^1], true);
                            break;
                        case 2 when created.Count > 0:
                            await repository.RegisterLaunchAsync(created[^1]);
                            break;
                        case 3:
                            await repository.SearchAsync($"W{window}");
                            break;
                        case 4:
                            await repository.GetChildrenAsync(root.Id);
                            break;
                        case 5 when created.Count > 2:
                            await repository.DeleteAsync(created[0]);
                            created.RemoveAt(0);
                            break;
                    }

                    Interlocked.Increment(ref operations);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        stopwatch.Stop();

        _output.WriteLine($"{windows} janelas x {operationsPerWindow} operacoes: {operations} concluidas em {stopwatch.ElapsedMilliseconds} ms");
        if (failures.Count > 0)
        {
            _output.WriteLine($"{failures.Count} falhas. Primeira: {failures[0].GetType().Name}: {failures[0].Message}");
        }

        Assert.Empty(failures);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        var integrity = Convert.ToString(check.ExecuteScalar());
        _output.WriteLine($"PRAGMA integrity_check: {integrity}");

        Assert.Equal("ok", integrity, ignoreCase: true);
    }

    [Fact]
    public async Task ConcurrentReorder_KeepsSortOrderContiguous()
    {
        const int rounds = 25;

        var repository = new SqliteMenuRepository(_connectionString);
        var folder = MenuItem.CreateFolder("Grupo", null, 0);
        await repository.AddAsync(folder);

        var ids = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var item = MenuItem.CreateShortcut($"Item{i}", folder.Id, MenuItemType.Executable, $"i{i}.exe", i);
            await repository.AddAsync(item);
            ids.Add(item.Id);
        }

        var failures = new List<Exception>();

        // Duas janelas reordenando o mesmo nivel ao mesmo tempo - o cenario que a persistencia
        // em lote de SortOrder precisa aguentar sem deixar o nivel inconsistente.
        var tasks = Enumerable.Range(0, 2).Select(worker => Task.Run(async () =>
        {
            var windowRepository = new SqliteMenuRepository(_connectionString);
            var rng = new Random(worker + 1);
            for (var r = 0; r < rounds; r++)
            {
                try
                {
                    var shuffled = ids.OrderBy(_ => rng.Next()).ToList();
                    await windowRepository.ReorderChildrenAsync(folder.Id, shuffled);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }
                }
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        if (failures.Count > 0)
        {
            _output.WriteLine($"{failures.Count} falhas. Primeira: {failures[0].GetType().Name}: {failures[0].Message}");
        }

        Assert.Empty(failures);

        var children = await repository.GetChildrenAsync(folder.Id);
        Assert.Equal(ids.Count, children.Count);
        Assert.Equal(Enumerable.Range(0, ids.Count), children.Select(c => c.SortOrder));
    }
}
