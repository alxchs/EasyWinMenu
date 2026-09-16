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

    public EditorViewModel(IMenuRepository repository, IConfigExportService exportService)
    {
        _repository = repository;
        _exportService = exportService;
    }

    public ObservableCollection<MenuTreeNodeViewModel> RootNodes { get; private set; } = [];

    [ObservableProperty]
    private MenuTreeNodeViewModel? _selectedNode;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(ct);
        RootNodes = MenuTreeNodeViewModel.BuildTree(all);
        OnPropertyChanged(nameof(RootNodes));
    }

    public async Task<MenuItem> AddFolderAsync(string name, MenuTreeNodeViewModel? parent, CancellationToken ct = default)
    {
        var siblingCount = parent is null ? RootNodes.Count : parent.Children.Count;
        var folder = MenuItem.CreateFolder(name, parent?.Id, siblingCount);
        await _repository.AddAsync(folder, ct);
        await LoadAsync(ct);
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
        var siblingCount = parent is null ? RootNodes.Count : parent.Children.Count;
        var item = MenuItem.CreateShortcut(name, parent?.Id, type, path, siblingCount);
        item.Arguments = arguments;
        item.WorkingDirectory = workingDirectory;
        await _repository.AddAsync(item, ct);
        await LoadAsync(ct);
        return item;
    }

    public async Task RenameAsync(MenuTreeNodeViewModel node, string newName, CancellationToken ct = default)
    {
        node.Item.Name = newName;
        await _repository.UpdateAsync(node.Item, ct);
        await LoadAsync(ct);
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
    }

    public async Task DeleteAsync(MenuTreeNodeViewModel node, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(node.Id, ct);
        await LoadAsync(ct);
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
    }
}
