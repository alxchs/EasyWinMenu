using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

public sealed class LnkImportService : ILnkImportService
{
    private readonly ILnkResolver _resolver;
    private readonly IMenuRepository _repository;

    public LnkImportService(ILnkResolver resolver, IMenuRepository repository)
    {
        _resolver = resolver;
        _repository = repository;
    }

    public async Task<IReadOnlyList<MenuItem>> ImportAsync(IReadOnlyList<string> lnkFilePaths, string? parentId, CancellationToken ct = default)
    {
        var existingSiblings = await _repository.GetChildrenAsync(parentId, ct);
        var nextSortOrder = existingSiblings.Count;

        var created = new List<MenuItem>();
        foreach (var lnkFilePath in lnkFilePaths)
        {
            ct.ThrowIfCancellationRequested();

            var info = _resolver.Resolve(lnkFilePath);

            // .lnk aponta pra um alvo real (exe, documento ou pasta) - ShellExecute (via
            // LaunchService, ja' usado desde a Fase 1) abre qualquer um desses igual, entao
            // nao ha necessidade de classificar o tipo aqui: sempre Executable, mesma
            // convencao ja adotada pelo seed de exemplo para pastas reais do Windows.
            var item = MenuItem.CreateShortcut(info.DisplayName, parentId, MenuItemType.Executable, info.TargetPath, nextSortOrder++);
            item.Arguments = info.Arguments;
            item.WorkingDirectory = info.WorkingDirectory;
            item.Icon = info.IconLocation;

            await _repository.AddAsync(item, ct);
            created.Add(item);
        }

        return created;
    }
}
