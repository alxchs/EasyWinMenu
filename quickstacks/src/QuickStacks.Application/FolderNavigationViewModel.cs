using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>Fonte dos itens exibidos no popup - navegacao normal ou uma das vistas globais da Fase 3.</summary>
public enum BrowseMode
{
    Folder,
    Favorites,
    Recent,
    MostUsed,
    Search,
}

/// <summary>
/// Estado de navegacao "como pasta do Windows" dentro do popup: uma unica janela, uma
/// trilha (breadcrumb) e os filhos do nivel atual - substitui o padrao inconsistente do
/// EasyWinMenu (janela nova por subpasta no modo Panel vs. trilha in-place no App Folder).
/// Aqui a navegacao e' SEMPRE in-place.
///
/// Fase 3: alem de navegar pasta a pasta, tambem alimenta os Items a partir de uma busca
/// global ou de uma das listas globais (favoritos/recentes/mais usados - RF09/RF11/RF12/RF13).
/// </summary>
public sealed partial class FolderNavigationViewModel : ObservableObject
{
    private const string RootLabel = "QuickStacks";
    private const int GlobalListLimit = 20;

    private readonly IMenuRepository _repository;
    private readonly IIconCacheService? _iconCache;

    public FolderNavigationViewModel(IMenuRepository repository, IIconCacheService? iconCache = null)
    {
        _repository = repository;
        _iconCache = iconCache;
        Breadcrumb.Add(new BreadcrumbNodeViewModel(null, RootLabel));
    }

    public ObservableCollection<MenuEntryViewModel> Items { get; } = [];

    public ObservableCollection<BreadcrumbNodeViewModel> Breadcrumb { get; } = [];

    [ObservableProperty]
    private string? _currentFolderId;

    [ObservableProperty]
    private BrowseMode _mode = BrowseMode.Folder;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public bool CanNavigateUp => Mode == BrowseMode.Folder && CurrentFolderId is not null;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IReadOnlyList<MenuItem> source = Mode switch
        {
            BrowseMode.Favorites => await _repository.GetFavoritesAsync(ct),
            BrowseMode.Recent => await _repository.GetRecentAsync(GlobalListLimit, ct),
            BrowseMode.MostUsed => await _repository.GetMostUsedAsync(GlobalListLimit, ct),
            BrowseMode.Search => string.IsNullOrWhiteSpace(SearchQuery)
                ? Array.Empty<MenuItem>()
                : await _repository.SearchAsync(SearchQuery, ct),
            _ => (await _repository.GetChildrenAsync(CurrentFolderId, ct)).OrderBy(c => c.SortOrder).ToList(),
        };

        Items.Clear();
        foreach (var entry in source)
        {
            Items.Add(new MenuEntryViewModel(entry, _iconCache));
        }
    }

    [RelayCommand]
    public async Task ShowFolderAsync()
    {
        Mode = BrowseMode.Folder;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task ShowFavoritesAsync()
    {
        Mode = BrowseMode.Favorites;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task ShowRecentAsync()
    {
        Mode = BrowseMode.Recent;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task ShowMostUsedAsync()
    {
        Mode = BrowseMode.MostUsed;
        await LoadAsync();
    }

    /// <summary>
    /// Busca global (RF10) - diferente do EasyWinMenu, nao se limita ao nivel atualmente
    /// aberto. O type-ahead (jump-to-match ao digitar sem abrir a busca) continua sendo um
    /// recurso separado, resolvido na UI (PopupWindow), so' no nivel corrente - mesma
    /// distincao de duas camadas que o EasyWinMenu ja usava.
    /// </summary>
    public async Task SearchAsync(string query)
    {
        SearchQuery = query;
        Mode = BrowseMode.Search;
        await LoadAsync();
    }

    public async Task ToggleFavoriteAsync(MenuEntryViewModel entry)
    {
        await _repository.SetFavoriteAsync(entry.Id, !entry.IsFavorite);
        await LoadAsync();
    }

    /// <summary>
    /// Navega direto para uma pasta conhecida pelo Id, reconstruindo a trilha real ate' ela -
    /// usado pela janela de grupo solto (Fase 9) para abrir uma subpasta no popup existente
    /// em vez de duplicar navegacao em trilha numa segunda janela.
    /// </summary>
    public async Task NavigateToFolderIdAsync(string folderId, string folderName)
    {
        var ancestors = await _repository.GetAncestorsAsync(folderId);
        Breadcrumb.Clear();
        Breadcrumb.Add(new BreadcrumbNodeViewModel(null, RootLabel));
        foreach (var ancestor in ancestors)
        {
            Breadcrumb.Add(new BreadcrumbNodeViewModel(ancestor.Id, ancestor.Name));
        }

        Breadcrumb.Add(new BreadcrumbNodeViewModel(folderId, folderName));
        Mode = BrowseMode.Folder;
        CurrentFolderId = folderId;
        await LoadAsync();
    }

    /// <summary>Duplo clique/Enter numa pasta: empurra a trilha e recarrega in-place.</summary>
    [RelayCommand]
    public async Task NavigateIntoAsync(MenuEntryViewModel folder)
    {
        if (!folder.IsFolder)
        {
            return;
        }

        if (Mode != BrowseMode.Folder)
        {
            // Veio de favoritos/recentes/busca: reconstroi a trilha real ate essa pasta em
            // vez de simplesmente empilhar em cima da trilha antiga (que nao tem relacao
            // com o caminho real do resultado).
            var ancestors = await _repository.GetAncestorsAsync(folder.Id);
            Breadcrumb.Clear();
            Breadcrumb.Add(new BreadcrumbNodeViewModel(null, RootLabel));
            foreach (var ancestor in ancestors)
            {
                Breadcrumb.Add(new BreadcrumbNodeViewModel(ancestor.Id, ancestor.Name));
            }

            Mode = BrowseMode.Folder;
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

    partial void OnModeChanged(BrowseMode value) => NavigateUpCommand.NotifyCanExecuteChanged();
}
