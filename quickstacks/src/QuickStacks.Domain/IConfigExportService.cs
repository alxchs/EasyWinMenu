namespace QuickStacks.Domain;

/// <summary>Exportar/Importar a configuracao inteira como arquivo (RF17-RF20).</summary>
public interface IConfigExportService
{
    Task ExportAsync(string filePath, CancellationToken ct = default);

    Task ImportAsync(string filePath, CancellationToken ct = default);
}
