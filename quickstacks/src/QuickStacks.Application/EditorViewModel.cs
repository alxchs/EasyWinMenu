using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>
/// Editor de estrutura (RF14/UC04/UC05): CRUD completo + mover (reparentar/reordenar) +
/// exportar/importar a configuracao inteira. Referencia funcional: o SettingsWindow do
/// EasyWinMenu resolvia o mesmo conjunto de problemas em WPF - a logica, nao o codigo, e' o
/// que foi reaproveitado aqui.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly IMenuRepository _repository;
    private readonly IConfigExportService _exportService;
    private readonly ILnkImportService _lnkImportService;

    public EditorViewModel(IMenuRepository repository, IConfigExportService exportService, ILnkImportService lnkImportService)
    {
        _repository = repository;
        _exportService = exportService;
        _lnkImportService = lnkImportService;
    }

    public ObservableCollection<MenuTreeNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    private MenuTreeNodeViewModel? _selectedNode;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct);
        var newRoots = MenuTreeNodeViewModel.BuildTree(all);
        SelectedNode = null;
        RootNodes.Clear();
        foreach (var node in newRoots)
        {
            RootNodes.Add(node);
        }
        OnPropertyChanged(nameof(RootNodes));
    }

    public async Task<MenuItem> AddFolderAsync(string name, MenuTreeNodeViewModel? parent, CancellationToken ct = default)
    {
        var parentId = ResolveTargetParentId(parent);
        var siblingCount = (await _repository.GetChildrenAsync(parentId, ct)).Count;
        var folder = MenuItem.CreateFolder(name, parentId, siblingCount);
        await _repository.AddAsync(folder, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
        return folder;
    }

    public async Task<MenuItem> AddItemAsync(
        string name,
        MenuItemType type,
        string path,
        string? arguments,
        string? workingDirectory,
        MenuTreeNodeViewModel? parent,
        CancellationToken ct = default)
    {
        var parentId = ResolveTargetParentId(parent);
        var siblingCount = (await _repository.GetChildrenAsync(parentId, ct)).Count;
        var item = MenuItem.CreateShortcut(name, parentId, type, path, siblingCount);
        item.Arguments = arguments;
        item.WorkingDirectory = workingDirectory;
        await _repository.AddAsync(item, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
        return item;
    }

    /// <summary>
    /// Uma pasta selecionada recebe o novo item dentro dela; um item-folha selecionado (que
    /// nao pode ter filhos) recebe o novo item ao lado dele, no mesmo pai - como o Explorer
    /// faz quando "Novo" e' acionado com um arquivo (nao uma pasta) selecionado.
    /// </summary>
    private static string? ResolveTargetParentId(MenuTreeNodeViewModel? selected) =>
        selected is null ? null : selected.IsFolder ? selected.Id : selected.Item.ParentId;

    public async Task RenameAsync(MenuTreeNodeViewModel node, string newName, CancellationToken ct = default)
    {
        node.Item.Name = newName;
        await _repository.UpdateAsync(node.Item, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
    }

    public async Task UpdateItemAsync(
        MenuTreeNodeViewModel node,
        string name,
        string? path,
        string? arguments,
        string? workingDirectory,
        CancellationToken ct = default)
    {
        node.Item.Name = name;
        node.Item.Path = path;
        node.Item.Arguments = arguments;
        node.Item.WorkingDirectory = workingDirectory;
        await _repository.UpdateAsync(node.Item, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
    }

    public async Task DeleteAsync(MenuTreeNodeViewModel node, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(node.Id, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
    }

    /// <summary>Reparenta um no arrastado (protegido contra ciclo pelo repositorio).</summary>
    public async Task<bool> TryMoveAsync(string draggedItemId, string? newParentId, CancellationToken ct = default)
    {
        if (draggedItemId == newParentId)
        {
            return false;
        }

        try
        {
            await _repository.MoveAsync(draggedItemId, newParentId, ct);
            await LoadAsync(ct);
            DataChangeNotifier.NotifyChanged();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public Task ExportAsync(string filePath, CancellationToken ct = default) => _exportService.ExportAsync(filePath, ct);

    public async Task ImportAsync(string filePath, CancellationToken ct = default)
    {
        await _exportService.ImportAsync(filePath, ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
    }

    /// <summary>Importa .lnk como itens novos dentro do no selecionado (raiz se nada selecionado) - RF08.</summary>
    public async Task<IReadOnlyList<MenuItem>> ImportLnkFilesAsync(IReadOnlyList<string> lnkFilePaths, CancellationToken ct = default)
    {
        var created = await _lnkImportService.ImportAsync(lnkFilePaths, ResolveTargetParentId(SelectedNode), ct);
        await LoadAsync(ct);
        DataChangeNotifier.NotifyChanged();
        return created;
    }

    /// <summary>Nome real de para onde a importacao vai (para o texto de confirmacao) - resolve o mesmo alvo que <see cref="ImportLnkFilesAsync"/> usaria.</summary>
    public async Task<string> GetEffectiveImportTargetNameAsync(string rootLabel, CancellationToken ct = default)
    {
        var parentId = ResolveTargetParentId(SelectedNode);
        if (parentId is null)
        {
            return rootLabel;
        }

        var parent = await _repository.GetByIdAsync(parentId, ct);
        return parent?.Name ?? rootLabel;
    }
}
