using Microsoft.Data.Sqlite;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Tabela Settings (key/value) - hoje usada so para lembrar o tamanho da janela do popup
/// por pasta (requisito 3: redimensionar "como pasta do Windows"). Idioma/tema entram aqui
/// nas fases seguintes do roteiro.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _connectionString;

    public SettingsStore(string? connectionString = null)
    {
        _connectionString = connectionString ?? DatabasePathProvider.GetConnectionString();

        using var connection = Open();
        SqliteSchema.EnsureCreated(connection);
    }

    private SqliteConnection Open() => SqliteSchema.OpenConnection(_connectionString);

    public string? Get(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = @key;";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    public static string WindowSizeKey(string? folderId) => $"window.size.{folderId ?? "root"}";

    public const string LanguageKey = "language.code";

    public const string ThemeModeKey = "theme.mode";

    public const string ProductTierKey = "product.tier";

    public const string ShadowAngleKey = "desktopGroup.shadow.angle";

    public const string ShadowEnabledKey = "desktopGroup.shadow.enabled";
}
