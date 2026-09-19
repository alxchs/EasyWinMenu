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
            UpdatedAt       TEXT NOT NULL,
            ExecutionMode   INTEGER NOT NULL DEFAULT 0
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
            BackgroundColorHex TEXT NULL,
            ThemeJson          TEXT NULL
        );

        -- Fase 9 (modo Full): geometria da janela solta de um grupo - so' existe uma linha
        -- aqui para pastas com IsDesktopGroup=true.
        CREATE TABLE IF NOT EXISTS DesktopGroupPlacement (
            GroupId      TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
            X            REAL NOT NULL,
            Y            REAL NOT NULL,
            Width        REAL NOT NULL,
            Height       REAL NOT NULL,
            DisplayMode  TEXT NOT NULL,
            IconScale    REAL NOT NULL,
            IsCollapsed  INTEGER NOT NULL DEFAULT 0,
            Arrangement  TEXT NOT NULL DEFAULT 'None'
        );

        -- Fase 9: posicao livre de cada item pinado no canvas do grupo que o contem.
        CREATE TABLE IF NOT EXISTS DesktopIconPosition (
            ItemId TEXT PRIMARY KEY REFERENCES MenuItems(Id) ON DELETE CASCADE,
            X      REAL NOT NULL,
            Y      REAL NOT NULL
        );
        """;

    /// <summary>
    /// Tempo que uma conexao espera por um lock antes de desistir com SQLITE_BUSY. O modo Full
    /// abre varias janelas (popup, editor e uma por grupo solto), cada uma com seu proprio
    /// repositorio contra o mesmo arquivo; sem isto, uma escrita concorrente estoura
    /// "database is locked" na cara do usuario.
    /// </summary>
    private const int BusyTimeoutMilliseconds = 5000;

    /// <summary>
    /// Abre uma conexao com os PRAGMAs que precisam ser reaplicados a cada conexao
    /// (<c>foreign_keys</c> e <c>busy_timeout</c> nao sao persistidos no arquivo).
    /// </summary>
    public static SqliteConnection OpenConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {BusyTimeoutMilliseconds};";
        pragma.ExecuteNonQuery();

        return connection;
    }

    public static void EnsureCreated(SqliteConnection connection)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        // WAL fica gravado no proprio arquivo, entao basta ligar uma vez - mas ligar de novo e'
        // barato e garante que um banco criado por uma versao anterior tambem seja convertido.
        // Leitor e escritor deixam de se bloquear, que e' o padrao de acesso real deste app.
        // synchronous=NORMAL e' o par recomendado do WAL: mantem a durabilidade em queda do
        // processo e so abre mao do fsync por commit (risco restrito a queda de energia).
        using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode = WAL;";
        journal.ExecuteScalar();

        using var sync = connection.CreateCommand();
        sync.CommandText = "PRAGMA synchronous = NORMAL;";
        sync.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = CreateTableSql;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "FolderAppearance", "ThemeJson", "TEXT NULL");
        EnsureColumn(connection, "MenuItems", "IsDesktopGroup", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "MenuItems", "ExecutionMode", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DesktopGroupPlacement", "Arrangement", "TEXT NOT NULL DEFAULT 'None'");
    }

    /// <summary>
    /// SQLite nao tem "ADD COLUMN IF NOT EXISTS" - confere via PRAGMA table_info antes de
    /// tentar adicionar uma coluna que uma fase posterior precisou numa tabela que ja
    /// existia em bancos reais de fases anteriores.
    /// </summary>
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string columnDefinition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @column;";
            check.Parameters.AddWithValue("@column", column);
            var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;
            if (exists)
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
