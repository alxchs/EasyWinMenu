using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>
/// Estado de navegacao "como pasta do Windows" dentro do popup: uma unica janela, uma
/// trilha (breadcrumb) e os filhos do nivel atual - substitui o padrao inconsistente do
/// EasyWinMenu (janela nova por subpasta no modo Panel vs. trilha in-place no App Folder).
/// Aqui a navegacao e' SEMPRE in-place.
/// </summary>
public sealed partial class FolderNavigationViewModel : ObservableObject
{
    private const string RootLabel = "QuickStacks";

    private readonly IMenuRepository _repository;

    public FolderNavigationViewModel(IMenuRepository repository)
    {
        _repository = repository;
        Breadcrumb.Add(new BreadcrumbNodeViewModel(null, RootLabel));
    }

    public ObservableCollection<MenuEntryViewModel> Items { get; } = [];

    public ObservableCollection<BreadcrumbNodeViewModel> Breadcrumb { get; } = [];

    [ObservableProperty]
    private string? _currentFolderId;

    public bool CanNavigateUp => CurrentFolderId is not null;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var children = await _repository.GetChildrenAsync(CurrentFolderId, ct);

        Items.Clear();
        foreach (var child in children.OrderBy(c => c.SortOrder))
        {
            Items.Add(new MenuEntryViewModel(child));
        }
    }

    /// <summary>Duplo clique/Enter numa pasta: empurra a trilha e recarrega in-place.</summary>
    [RelayCommand]
    public async Task NavigateIntoAsync(MenuEntryViewModel folder)
    {
        if (!folder.IsFolder)
        {
            return;
        }

        Breadcrumb.Add(new BreadcrumbNodeViewModel(folder.Id, folder.Name));
        CurrentFolderId = folder.Id;
        await LoadAsync();
    }

    /// <summary>Clique num nivel da BreadcrumbBar: volta direto para aquele nivel.</summary>
    [RelayCommand]
    public async Task NavigateToBreadcrumbAsync(BreadcrumbNodeViewModel target)
    {
        var index = Breadcrumb.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        while (Breadcrumb.Count > index + 1)
        {
            Breadcrumb.RemoveAt(Breadcrumb.Count - 1);
        }

        CurrentFolderId = target.Id;
        await LoadAsync();
    }

    /// <summary>Alt+Esquerda / botao "voltar": sobe um nivel (paridade com o Explorer real).</summary>
    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    public async Task NavigateUpAsync()
    {
        if (Breadcrumb.Count <= 1)
        {
            return;
        }

        await NavigateToBreadcrumbAsync(Breadcrumb[^2]);
    }

    /// <summary>
    /// Move um item (arrastado) para dentro da pasta atualmente aberta - usado tanto por
    /// drop num item-pasta da lista quanto por drop na propria BreadcrumbBar (subir de nivel
    /// arrastando). Reflete os dois lados do requisito 2 (grupo-para-grupo).
    /// </summary>
    public async Task<bool> TryMoveIntoCurrentFolderAsync(string draggedItemId)
    {
        if (draggedItemId == CurrentFolderId)
        {
            return false;
        }

        try
        {
            await _repository.MoveAsync(draggedItemId, CurrentFolderId);
            await LoadAsync();
            return true;
        }
        catch (InvalidOperationException)
        {
            // ciclo (pasta solta dentro de si mesma/descendente) - drop ignorado, sem crash.
            return false;
        }
    }

    public async Task<bool> TryMoveIntoFolderAsync(string draggedItemId, MenuEntryViewModel targetFolder)
    {
        if (!targetFolder.IsFolder || draggedItemId == targetFolder.Id)
        {
            return false;
        }

        try
        {
            await _repository.MoveAsync(draggedItemId, targetFolder.Id);
            await LoadAsync();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    partial void OnCurrentFolderIdChanged(string? value) => NavigateUpCommand.NotifyCanExecuteChanged();
}
