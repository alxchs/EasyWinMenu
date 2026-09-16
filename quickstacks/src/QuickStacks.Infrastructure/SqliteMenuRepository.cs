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

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

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
}
