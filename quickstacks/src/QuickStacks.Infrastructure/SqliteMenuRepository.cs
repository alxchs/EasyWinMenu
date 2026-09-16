using Microsoft.Data.Sqlite;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

public sealed class SqliteMenuRepository : IMenuRepository
{
    private readonly string _connectionString;

    public SqliteMenuRepository(string? connectionString = null)
    {
        _connectionString = connectionString ?? DatabasePathProvider.GetConnectionString();

        using var connection = Open();
        SqliteSchema.EnsureCreated(connection);
    }

    private static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 'foreign_keys' e' uma configuracao por conexao, nao persistida no arquivo - sem
        // isto aqui, ON DELETE CASCADE nunca seria aplicado de verdade em nenhuma operacao
        // normal (so a conexao descartavel do EnsureCreated no construtor tinha isto ligado).
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private SqliteConnection Open() => Open(_connectionString);

    public async Task<IReadOnlyList<MenuItem>> GetChildrenAsync(string? parentId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = parentId is null
            ? "SELECT * FROM MenuItems WHERE ParentId IS NULL ORDER BY SortOrder;"
            : "SELECT * FROM MenuItems WHERE ParentId = @parentId ORDER BY SortOrder;";
        if (parentId is not null)
        {
            command.Parameters.AddWithValue("@parentId", parentId);
        }

        var results = new List<MenuItem>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<MenuItem?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<MenuItem>> GetAncestorsAsync(string id, CancellationToken ct = default)
    {
        var chain = new List<MenuItem>();
        var current = await GetByIdAsync(id, ct);
        if (current is null)
        {
            return chain;
        }

        var parentId = current.ParentId;
        while (parentId is not null)
        {
            var parent = await GetByIdAsync(parentId, ct);
            if (parent is null)
            {
                break;
            }

            chain.Insert(0, parent);
            parentId = parent.ParentId;
        }

        return chain;
    }

    public async Task AddAsync(MenuItem item, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO MenuItems
                (Id, ParentId, Name, Description, Type, Path, Arguments, WorkingDirectory, Icon,
                 SortOrder, IsFavorite, LaunchCount, LastUsedUtc, CreatedAt, UpdatedAt)
            VALUES
                (@Id, @ParentId, @Name, @Description, @Type, @Path, @Arguments, @WorkingDirectory, @Icon,
                 @SortOrder, @IsFavorite, @LaunchCount, @LastUsedUtc, @CreatedAt, @UpdatedAt);
            """;
        Bind(command, item);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(MenuItem item, CancellationToken ct = default)
    {
        item.UpdatedAt = DateTimeOffset.UtcNow;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MenuItems SET
                ParentId = @ParentId, Name = @Name, Description = @Description, Type = @Type,
                Path = @Path, Arguments = @Arguments, WorkingDirectory = @WorkingDirectory, Icon = @Icon,
                SortOrder = @SortOrder, IsFavorite = @IsFavorite, LaunchCount = @LaunchCount,
                LastUsedUtc = @LastUsedUtc, UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;
        Bind(command, item);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MenuItems WHERE Id = @id;";
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MoveAsync(string itemId, string? newParentId, CancellationToken ct = default)
    {
        if (newParentId is not null)
        {
            if (newParentId == itemId)
            {
                throw new InvalidOperationException("Uma pasta nao pode ser movida para dentro de si mesma.");
            }

            if (await IsDescendantAsync(itemId, newParentId, ct))
            {
                throw new InvalidOperationException("Nao e possivel mover uma pasta para dentro de um dos seus proprios descendentes.");
            }
        }

        var item = await GetByIdAsync(itemId, ct)
            ?? throw new InvalidOperationException($"Item '{itemId}' nao encontrado.");

        item.ParentId = newParentId;
        await UpdateAsync(item, ct);
    }

    public async Task<bool> IsDescendantAsync(string candidateAncestorId, string itemId, CancellationToken ct = default)
    {
        // true se 'itemId' esta dentro da subarvore de 'candidateAncestorId' (mesma
        // protecao contra ciclo que IsCategoryOrDescendant validava no EasyWinMenu).
        var current = await GetByIdAsync(itemId, ct);
        while (current?.ParentId is not null)
        {
            if (current.ParentId == candidateAncestorId)
            {
                return true;
            }

            current = await GetByIdAsync(current.ParentId, ct);
        }

        return false;
    }

    public async Task RegisterLaunchAsync(string itemId, CancellationToken ct = default)
    {
        var item = await GetByIdAsync(itemId, ct);
        if (item is null)
        {
            return;
        }

        item.RegisterLaunch();
        await UpdateAsync(item, ct);
    }

    public async Task<IReadOnlyList<MenuItem>> SearchAsync(string query, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE Name LIKE @pattern ESCAPE '\\' ORDER BY Name;";
        command.Parameters.AddWithValue("@pattern", "%" + EscapeLike(query) + "%");
        return await ReadAllAsync(command, ct);
    }

    public async Task<IReadOnlyList<MenuItem>> GetFavoritesAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE IsFavorite = 1 ORDER BY Name;";
        return await ReadAllAsync(command, ct);
    }

    public async Task<IReadOnlyList<MenuItem>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE LastUsedUtc IS NOT NULL ORDER BY LastUsedUtc DESC LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", limit);
        return await ReadAllAsync(command, ct);
    }

    public async Task<IReadOnlyList<MenuItem>> GetMostUsedAsync(int limit, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE LaunchCount > 0 ORDER BY LaunchCount DESC LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", limit);
        return await ReadAllAsync(command, ct);
    }

    public async Task SetFavoriteAsync(string itemId, bool isFavorite, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MenuItems SET IsFavorite = @isFavorite, UpdatedAt = @updatedAt WHERE Id = @id;";
        command.Parameters.AddWithValue("@isFavorite", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@id", itemId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<MenuItem>> ReadAllAsync(SqliteCommand command, CancellationToken ct)
    {
        var results = new List<MenuItem>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static void Bind(SqliteCommand command, MenuItem item)
    {
        command.Parameters.AddWithValue("@Id", item.Id);
        command.Parameters.AddWithValue("@ParentId", (object?)item.ParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Name", item.Name);
        command.Parameters.AddWithValue("@Description", (object?)item.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@Type", item.Type.ToString());
        command.Parameters.AddWithValue("@Path", (object?)item.Path ?? DBNull.Value);
        command.Parameters.AddWithValue("@Arguments", (object?)item.Arguments ?? DBNull.Value);
        command.Parameters.AddWithValue("@WorkingDirectory", (object?)item.WorkingDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("@Icon", (object?)item.Icon ?? DBNull.Value);
        command.Parameters.AddWithValue("@SortOrder", item.SortOrder);
        command.Parameters.AddWithValue("@IsFavorite", item.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("@LaunchCount", item.LaunchCount);
        command.Parameters.AddWithValue("@LastUsedUtc", (object?)item.LastUsedUtc?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@UpdatedAt", item.UpdatedAt.ToString("O"));
    }

    private static MenuItem Map(SqliteDataReader reader)
    {
        return new MenuItem
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            ParentId = reader.IsDBNull(reader.GetOrdinal("ParentId")) ? null : reader.GetString(reader.GetOrdinal("ParentId")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
            Type = Enum.Parse<MenuItemType>(reader.GetString(reader.GetOrdinal("Type"))),
            Path = reader.IsDBNull(reader.GetOrdinal("Path")) ? null : reader.GetString(reader.GetOrdinal("Path")),
            Arguments = reader.IsDBNull(reader.GetOrdinal("Arguments")) ? null : reader.GetString(reader.GetOrdinal("Arguments")),
            WorkingDirectory = reader.IsDBNull(reader.GetOrdinal("WorkingDirectory")) ? null : reader.GetString(reader.GetOrdinal("WorkingDirectory")),
            Icon = reader.IsDBNull(reader.GetOrdinal("Icon")) ? null : reader.GetString(reader.GetOrdinal("Icon")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
            IsFavorite = reader.GetInt32(reader.GetOrdinal("IsFavorite")) != 0,
            LaunchCount = reader.GetInt32(reader.GetOrdinal("LaunchCount")),
            LastUsedUtc = reader.IsDBNull(reader.GetOrdinal("LastUsedUtc")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LastUsedUtc"))),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
        };
    }

    public async Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems ORDER BY ParentId, SortOrder;";

        var results = new List<MenuItem>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task ReorderChildrenAsync(string? parentId, IReadOnlyList<string> orderedIds, CancellationToken ct = default)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        for (var i = 0; i < orderedIds.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE MenuItems SET SortOrder = @sortOrder, UpdatedAt = @updatedAt WHERE Id = @id AND (ParentId = @parentId OR (@parentId IS NULL AND ParentId IS NULL));";
            command.Parameters.AddWithValue("@sortOrder", i);
            command.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("@id", orderedIds[i]);
            command.Parameters.AddWithValue("@parentId", (object?)parentId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
    }

    public async Task ReplaceAllAsync(IReadOnlyList<MenuItem> items, CancellationToken ct = default)
    {
        using var connection = Open();

        // 'PRAGMA foreign_keys' e' no-op dentro de uma transacao ja aberta - por isso isto
        // roda ANTES do BeginTransaction. Import substitui a arvore inteira e a ordem de
        // 'items' nao garante que o pai sempre venha antes do filho, entao a FK fica
        // desligada so durante a carga.
        using (var pragmaOff = connection.CreateCommand())
        {
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF;";
            pragmaOff.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM MenuItems;";
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO MenuItems
                    (Id, ParentId, Name, Description, Type, Path, Arguments, WorkingDirectory, Icon,
                     SortOrder, IsFavorite, LaunchCount, LastUsedUtc, CreatedAt, UpdatedAt)
                VALUES
                    (@Id, @ParentId, @Name, @Description, @Type, @Path, @Arguments, @WorkingDirectory, @Icon,
                     @SortOrder, @IsFavorite, @LaunchCount, @LastUsedUtc, @CreatedAt, @UpdatedAt);
                """;
            Bind(insert, item);
            await insert.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();

        using (var pragmaOn = connection.CreateCommand())
        {
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaOn.ExecuteNonQuery();
        }
    }
}
