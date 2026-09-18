namespace QuickStacks.Domain;

/// <summary>
/// Metadados de versão e pacote fornecidos pelo servidor de atualização em domínio privado.
/// </summary>
public sealed record UpdateInfo(
    string Version,
    string DownloadUrl,
    string? Changelog = null,
    string? Sha256 = null,
    bool IsMandatory = false,
    DateTime? ReleaseDateUtc = null
);

/// <summary>
/// Resultado da verificação de nova versão em relação à versão local em execução.
/// </summary>
public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    UpdateInfo? UpdateInfo = null,
    string? ErrorMessage = null
)
{
    public static UpdateCheckResult Success(bool hasUpdate, string currentVersion, UpdateInfo? updateInfo) =>
        new(hasUpdate, currentVersion, updateInfo, null);

    public static UpdateCheckResult Failure(string currentVersion, string errorMessage) =>
        new(false, currentVersion, null, errorMessage);
}

