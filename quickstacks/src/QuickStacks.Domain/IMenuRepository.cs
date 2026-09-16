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
}
