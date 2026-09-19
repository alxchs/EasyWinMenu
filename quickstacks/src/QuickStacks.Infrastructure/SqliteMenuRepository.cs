using System.Text.Json;
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

    // 'foreign_keys' e 'busy_timeout' sao configuracoes por conexao, nao persistidas no
    // arquivo - sem isto aqui, ON DELETE CASCADE nunca seria aplicado de verdade, e
    // escritas concorrentes falhariam de imediato com "database is locked".
    private static SqliteConnection Open(string connectionString) => SqliteSchema.OpenConnection(connectionString);

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
                 SortOrder, IsFavorite, LaunchCount, LastUsedUtc, CreatedAt, UpdatedAt, IsDesktopGroup)
            VALUES
                (@Id, @ParentId, @Name, @Description, @Type, @Path, @Arguments, @WorkingDirectory, @Icon,
                 @SortOrder, @IsFavorite, @LaunchCount, @LastUsedUtc, @CreatedAt, @UpdatedAt, @IsDesktopGroup);
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
                LastUsedUtc = @LastUsedUtc, UpdatedAt = @UpdatedAt, IsDesktopGroup = @IsDesktopGroup
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

    public async Task<string?> GetFolderBackgroundColorAsync(string folderId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT BackgroundColorHex FROM FolderAppearance WHERE FolderId = @folderId;";
        command.Parameters.AddWithValue("@folderId", folderId);
        return (await command.ExecuteScalarAsync(ct)) as string;
    }

    public async Task SetFolderBackgroundColorAsync(string folderId, string? hex, CancellationToken ct = default)
    {
        if (hex is not null && !HexColor.IsValid(hex))
        {
            throw new ArgumentException($"'{hex}' nao e' uma cor valida (esperado #RRGGBB).", nameof(hex));
        }

        // Sem DELETE quando hex e' nulo: a linha tambem pode estar guardando o tema completo
        // do modo Full (ThemeJson) - apagar a linha inteira aqui perderia esse tema junto.
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FolderAppearance (FolderId, BackgroundColorHex) VALUES (@folderId, @hex)
            ON CONFLICT(FolderId) DO UPDATE SET BackgroundColorHex = excluded.BackgroundColorHex;
            """;
        command.Parameters.AddWithValue("@folderId", folderId);
        command.Parameters.AddWithValue("@hex", (object?)hex ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);

        await DeleteFolderAppearanceRowIfEmptyAsync(connection, folderId, ct);
    }

    public async Task<FolderTheme> GetFolderThemeAsync(string folderId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT BackgroundColorHex, ThemeJson FROM FolderAppearance WHERE FolderId = @folderId;";
        command.Parameters.AddWithValue("@folderId", folderId);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return FolderTheme.Empty;
        }

        var backgroundHex = reader.IsDBNull(0) ? null : reader.GetString(0);
        var themeJson = reader.IsDBNull(1) ? null : reader.GetString(1);
        var rest = themeJson is null ? FolderTheme.Empty : JsonSerializer.Deserialize<FolderTheme>(themeJson) ?? FolderTheme.Empty;
        return rest with { BackgroundColorHex = backgroundHex };
    }

    public async Task SetFolderThemeAsync(string folderId, FolderTheme theme, CancellationToken ct = default)
    {
        if (theme.BackgroundColorHex is not null && !HexColor.IsValid(theme.BackgroundColorHex))
        {
            throw new ArgumentException($"'{theme.BackgroundColorHex}' nao e' uma cor valida (esperado #RRGGBB).", nameof(theme));
        }

        // BackgroundColorHex fica na sua propria coluna (Fase 4, so' precisa dela a maioria
        // do tempo); o resto do tema completo (Fase 8/modo Full) vai serializado - evita 17
        // colunas soltas pra um recurso que so' o modo Full usa.
        var restJson = JsonSerializer.Serialize(theme with { BackgroundColorHex = null });

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FolderAppearance (FolderId, BackgroundColorHex, ThemeJson) VALUES (@folderId, @hex, @themeJson)
            ON CONFLICT(FolderId) DO UPDATE SET BackgroundColorHex = excluded.BackgroundColorHex, ThemeJson = excluded.ThemeJson;
            """;
        command.Parameters.AddWithValue("@folderId", folderId);
        command.Parameters.AddWithValue("@hex", (object?)theme.BackgroundColorHex ?? DBNull.Value);
        command.Parameters.AddWithValue("@themeJson", restJson);
        await command.ExecuteNonQueryAsync(ct);

        await DeleteFolderAppearanceRowIfEmptyAsync(connection, folderId, ct);
    }

    /// <summary>Sem cor de fundo simples e sem tema completo, a linha nao serve mais pra nada - some, como antes da Fase 8.</summary>
    private static async Task DeleteFolderAppearanceRowIfEmptyAsync(SqliteConnection connection, string folderId, CancellationToken ct)
    {
        using var cleanup = connection.CreateCommand();
        cleanup.CommandText = """
            DELETE FROM FolderAppearance
            WHERE FolderId = @folderId AND BackgroundColorHex IS NULL AND (ThemeJson IS NULL OR ThemeJson = @emptyThemeJson);
            """;
        cleanup.Parameters.AddWithValue("@folderId", folderId);
        cleanup.Parameters.AddWithValue("@emptyThemeJson", JsonSerializer.Serialize(FolderTheme.Empty));
        await cleanup.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MenuItem>> GetDesktopGroupsAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM MenuItems WHERE ParentId IS NULL AND IsDesktopGroup = 1 ORDER BY Name;";
        return await ReadAllAsync(command, ct);
    }

    public async Task SetIsDesktopGroupAsync(string folderId, bool isDesktopGroup, CancellationToken ct = default)
    {
        using var connection = Open();
        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE MenuItems SET IsDesktopGroup = @flag, UpdatedAt = @updatedAt WHERE Id = @id;";
            update.Parameters.AddWithValue("@flag", isDesktopGroup ? 1 : 0);
            update.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("@id", folderId);
            await update.ExecuteNonQueryAsync(ct);
        }

        if (!isDesktopGroup)
        {
            return;
        }

        using var checkPlacement = connection.CreateCommand();
        checkPlacement.CommandText = "SELECT COUNT(*) FROM DesktopGroupPlacement WHERE GroupId = @id;";
        checkPlacement.Parameters.AddWithValue("@id", folderId);
        var hasPlacement = Convert.ToInt64(await checkPlacement.ExecuteScalarAsync(ct)) > 0;
        if (!hasPlacement)
        {
            // Cascata de janelas: cada grupo novo nasce um pouco deslocado do ultimo, pra nao
            // empilhar tudo exatamente no mesmo canto da tela.
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM DesktopGroupPlacement;";
            var existing = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
            var offset = (existing % 10) * 28;
            await SetDesktopGroupPlacementAsync(DesktopGroupPlacement.CreateDefault(folderId, 80 + offset, 80 + offset), ct);
        }
    }

    public async Task<DesktopGroupPlacement?> GetDesktopGroupPlacementAsync(string groupId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT X, Y, Width, Height, DisplayMode, IconScale, IsCollapsed FROM DesktopGroupPlacement WHERE GroupId = @groupId;";
        command.Parameters.AddWithValue("@groupId", groupId);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new DesktopGroupPlacement(
            groupId,
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            Enum.Parse<DesktopGroupDisplayMode>(reader.GetString(4)),
            reader.GetDouble(5),
            reader.GetInt32(6) != 0);
    }

    public async Task SetDesktopGroupPlacementAsync(DesktopGroupPlacement placement, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DesktopGroupPlacement (GroupId, X, Y, Width, Height, DisplayMode, IconScale, IsCollapsed)
            VALUES (@groupId, @x, @y, @width, @height, @displayMode, @iconScale, @isCollapsed)
            ON CONFLICT(GroupId) DO UPDATE SET
                X = excluded.X, Y = excluded.Y, Width = excluded.Width, Height = excluded.Height,
                DisplayMode = excluded.DisplayMode, IconScale = excluded.IconScale, IsCollapsed = excluded.IsCollapsed;
            """;
        command.Parameters.AddWithValue("@groupId", placement.GroupId);
        command.Parameters.AddWithValue("@x", placement.X);
        command.Parameters.AddWithValue("@y", placement.Y);
        command.Parameters.AddWithValue("@width", placement.Width);
        command.Parameters.AddWithValue("@height", placement.Height);
        command.Parameters.AddWithValue("@displayMode", placement.DisplayMode.ToString());
        command.Parameters.AddWithValue("@iconScale", placement.IconScale);
        command.Parameters.AddWithValue("@isCollapsed", placement.IsCollapsed ? 1 : 0);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, DesktopIconPosition>> GetDesktopIconPositionsAsync(string groupId, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.ItemId, p.X, p.Y FROM DesktopIconPosition p
            INNER JOIN MenuItems m ON m.Id = p.ItemId
            WHERE m.ParentId = @groupId;
            """;
        command.Parameters.AddWithValue("@groupId", groupId);

        var result = new Dictionary<string, DesktopIconPosition>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var itemId = reader.GetString(0);
            result[itemId] = new DesktopIconPosition(itemId, reader.GetDouble(1), reader.GetDouble(2));
        }

        return result;
    }

    public async Task SetDesktopIconPositionAsync(DesktopIconPosition position, CancellationToken ct = default)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DesktopIconPosition (ItemId, X, Y) VALUES (@itemId, @x, @y)
            ON CONFLICT(ItemId) DO UPDATE SET X = excluded.X, Y = excluded.Y;
            """;
        command.Parameters.AddWithValue("@itemId", position.ItemId);
        command.Parameters.AddWithValue("@x", position.X);
        command.Parameters.AddWithValue("@y", position.Y);
        await command.ExecuteNonQueryAsync(ct);
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
        command.Parameters.AddWithValue("@IsDesktopGroup", item.IsDesktopGroup ? 1 : 0);
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
            IsDesktopGroup = reader.GetInt32(reader.GetOrdinal("IsDesktopGroup")) != 0,
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
                     SortOrder, IsFavorite, LaunchCount, LastUsedUtc, CreatedAt, UpdatedAt, IsDesktopGroup)
                VALUES
                    (@Id, @ParentId, @Name, @Description, @Type, @Path, @Arguments, @WorkingDirectory, @Icon,
                     @SortOrder, @IsFavorite, @LaunchCount, @LastUsedUtc, @CreatedAt, @UpdatedAt, @IsDesktopGroup);
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
