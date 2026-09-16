namespace QuickStacks.Domain;

/// <summary>
/// Acesso a arvore de <see cref="MenuItem"/>. A implementacao real (SQLite) vive em
/// QuickStacks.Infrastructure; esta interface existe para a Application/UI nao depender
/// diretamente de Microsoft.Data.Sqlite.
/// </summary>
public interface IMenuRepository
{
    /// <summary>Filhos diretos de <paramref name="parentId"/> (null = raiz), ja ordenados por SortOrder.</summary>
    Task<IReadOnlyList<MenuItem>> GetChildrenAsync(string? parentId, CancellationToken ct = default);

    Task<MenuItem?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Caminho da raiz ate <paramref name="id"/> (exclusive), para montar a trilha/breadcrumb.</summary>
    Task<IReadOnlyList<MenuItem>> GetAncestorsAsync(string id, CancellationToken ct = default);

    Task AddAsync(MenuItem item, CancellationToken ct = default);

    Task UpdateAsync(MenuItem item, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Move <paramref name="itemId"/> para dentro de <paramref name="newParentId"/> (null = raiz).
    /// Lanca <see cref="InvalidOperationException"/> se o destino for o proprio item ou um descendente dele
    /// (mesma protecao contra ciclo que o editor de estrutura do EasyWinMenu ja validava).
    /// </summary>
    Task MoveAsync(string itemId, string? newParentId, CancellationToken ct = default);

    Task<bool> IsDescendantAsync(string candidateAncestorId, string itemId, CancellationToken ct = default);

    Task RegisterLaunchAsync(string itemId, CancellationToken ct = default);

    /// <summary>Toda a arvore, sem filtro de nivel - usado pelo editor (Fase 2) e por Exportar.</summary>
    Task<IReadOnlyList<MenuItem>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Reordena os irmaos de <paramref name="parentId"/> na ordem exata de <paramref name="orderedIds"/>.</summary>
    Task ReorderChildrenAsync(string? parentId, IReadOnlyList<string> orderedIds, CancellationToken ct = default);

    /// <summary>
    /// Apaga a arvore inteira e recria a partir de <paramref name="items"/> (usado por Importar -
    /// RF18). Os Ids sao preservados como vieram no arquivo, entao as referencias ParentId
    /// continuam validas.
    /// </summary>
    Task ReplaceAllAsync(IReadOnlyList<MenuItem> items, CancellationToken ct = default);

    /// <summary>
    /// Busca global (RF10) por substring no nome, em qualquer nivel da arvore - diferente do
    /// EasyWinMenu, cuja busca so olhava o nivel do grupo aberto no momento.
    /// </summary>
    Task<IReadOnlyList<MenuItem>> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>Itens marcados como favoritos (RF09/RF11), em qualquer nivel.</summary>
    Task<IReadOnlyList<MenuItem>> GetFavoritesAsync(CancellationToken ct = default);

    /// <summary>Os <paramref name="limit"/> itens executados mais recentemente (RF12).</summary>
    Task<IReadOnlyList<MenuItem>> GetRecentAsync(int limit, CancellationToken ct = default);

    /// <summary>Os <paramref name="limit"/> itens mais executados, por contagem (RF13).</summary>
    Task<IReadOnlyList<MenuItem>> GetMostUsedAsync(int limit, CancellationToken ct = default);

    Task SetFavoriteAsync(string itemId, bool isFavorite, CancellationToken ct = default);

    /// <summary>Cor de fundo customizada da pasta (Fase 4), em hex ("#RRGGBB"); null = usa o tema global.</summary>
    Task<string?> GetFolderBackgroundColorAsync(string folderId, CancellationToken ct = default);

    /// <summary><paramref name="hex"/> nulo remove o override e volta a usar o tema global.</summary>
    Task SetFolderBackgroundColorAsync(string folderId, string? hex, CancellationToken ct = default);
}
