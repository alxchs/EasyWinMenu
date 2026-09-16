using System.Text.Json;
using System.Text.Json.Serialization;
using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>Exportar/Importar (RF17/RF18/RF19/RF20): a arvore inteira como um unico arquivo JSON.</summary>
public sealed class ConfigExportService : IConfigExportService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IMenuRepository _repository;

    public ConfigExportService(IMenuRepository repository)
    {
        _repository = repository;
    }

    public async Task ExportAsync(string filePath, CancellationToken ct = default)
    {
        var items = await _repository.GetAllAsync(ct);
        var json = JsonSerializer.Serialize(items, Options);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    /// <summary>Substitui a arvore inteira pelo conteudo do arquivo (backup/restauracao - RF19/RF20).</summary>
    public async Task ImportAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var items = JsonSerializer.Deserialize<List<MenuItem>>(json, Options)
            ?? throw new InvalidDataException($"'{filePath}' nao contem uma configuracao valida do QuickStacks.");

        await _repository.ReplaceAllAsync(items, ct);
    }
}
