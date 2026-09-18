namespace QuickStacks.Domain;

/// <summary>
/// Contrato do serviço de auto-atualização para verificação e sincronização de versões.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// URL padrão do endpoint de manifesto em domínio privado.
    /// </summary>
    string DefaultFeedUrl { get; }

    /// <summary>
    /// Verifica se uma versão mais recente está publicada no servidor de atualizações.
    /// </summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(string? feedUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compara duas versões no formato SemVer. Retorna positivo se remote for maior que local.
    /// </summary>
    int CompareVersions(string remoteVersion, string localVersion);
}

