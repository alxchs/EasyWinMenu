namespace QuickStacks.Domain;

/// <summary>Importa um ou mais .lnk como itens novos dentro de <paramref name="parentId"/> (RF08).</summary>
public interface ILnkImportService
{
    Task<IReadOnlyList<MenuItem>> ImportAsync(IReadOnlyList<string> lnkFilePaths, string? parentId, CancellationToken ct = default);
}
