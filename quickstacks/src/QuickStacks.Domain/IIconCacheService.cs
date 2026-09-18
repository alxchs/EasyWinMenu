namespace QuickStacks.Domain;

/// <summary>
/// Extrai e armazena em cache no disco icones reais de executaveis e arquivos (Fase 17).
/// Mantem transparencia per-pixel atraves do formato PNG, evitando artefatos de canal alfa.
/// </summary>
public interface IIconCacheService
{
    /// <summary>
    /// Retorna o caminho do arquivo PNG no cache em disco para o alvo especificado,
    /// ou null se o alvo nao for um arquivo valido ou a extracao falhar.
    /// </summary>
    string? GetIconPath(string? targetPath);
}

