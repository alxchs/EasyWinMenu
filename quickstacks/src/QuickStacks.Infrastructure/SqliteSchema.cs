using Microsoft.Data.Sqlite;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Cria o schema se ainda nao existir. Sem migrations por enquanto: produto novo, schema
/// unico (ver plano de fases - versionamento de schema entra quando o formato precisar mudar).
/// </summary>
public static class SqliteSchema
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS MenuItems (
            Id              TEXT PRIMARY KEY,
            ParentId        TEXT NULL REFERENCES MenuItems(Id) ON DELETE CASCADE,
            Name            TEXT NOT NULL,
            Description     TEXT NULL,
            Type            TEXT NOT NULL,
            Path            TEXT NULL,
            Arguments       TEXT NULL,
            WorkingDirectory TEXT NULL,
            Icon            TEXT NULL,
            SortOrder       INTEGER NOT NULL DEFAULT 0,
            IsFavorite      INTEGER NOT NULL DEFAULT 0,
            LaunchCount     INTEGER NOT NULL DEFAULT 0,
            LastUsedUtc     TEXT NULL,
            CreatedAt       TEXT NOT NULL,
            UpdatedAt       TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_MenuItems_ParentId ON MenuItems(ParentId);

        CREATE TABLE IF NOT EXISTS Settings (
            Key   TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        -- Fase 4 (Temas): override de cor de fundo por pasta - so' existe uma linha aqui
        -- quando a pasta tem uma cor propria; sem linha = usa o tema global.
        CREATE TABLE IF NOT EXISTS FolderAppearance (
            FolderId          TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
            BackgroundColorHex TEXT NOT NULL
        );
        """;

    public static void EnsureCreated(SqliteConnection connection)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = CreateTableSql;
        command.ExecuteNonQuery();
    }
}
