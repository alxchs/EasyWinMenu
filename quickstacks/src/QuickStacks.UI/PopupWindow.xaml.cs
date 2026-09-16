using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace QuickStacks.UI;

/// <summary>
/// O popup unico do QuickStacks: navegacao em trilha (requisito 1), drag-and-drop entre
/// pastas e para o Explorer (requisito 2), e uma janela de verdade redimensionavel com
/// reflow automatico dos icones (requisito 3).
/// </summary>
public sealed partial class PopupWindow : Window
{
    private const string DraggedItemFormat = "QuickStacksItemId";
    private const int DefaultWidth = 420;
    private const int DefaultHeight = 340;

    private readonly SettingsStore _settings = new();
    private string _typeAheadBuffer = string.Empty;
    private DateTime _typeAheadLastKeyUtc = DateTime.MinValue;

    public PopupWindow(IMenuRepository repository)
    {
        InitializeComponent();

        ViewModel = new FolderNavigationViewModel(repository);
        Repository = repository;

        ThemeService.Register(RootGrid);

        // Microsoft.UI.Xaml.Window nao e' um FrameworkElement, entao x:Bind com Mode=OneWay
        // no conteudo raiz de uma Window nao compila (o codigo gerado precisa de um
        // FrameworkElement para o hook de Loaded/atualizacao) - por isso a visibilidade da
        // trilha e' atualizada aqui a mao, reagindo a troca de Mode.
        ViewModel.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName == nameof(FolderNavigationViewModel.Mode))
            {
                Breadcrumb.Visibility = ModeToVisibility(ViewModel.Mode);
            }

            if (args.PropertyName is nameof(FolderNavigationViewModel.Mode) or nameof(FolderNavigationViewModel.CurrentFolderId))
            {
                await ApplyFolderBackgroundAsync();
            }
        };

        AppWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));
        AppWindow.Changed += AppWindow_Changed;

        _ = InitializeAsync();
    }

    public FolderNavigationViewModel ViewModel { get; }

    private IMenuRepository Repository { get; }

    private async Task InitializeAsync()
    {
        await ViewModel.LoadAsync();
        ApplyRememberedSizeForCurrentFolder();
        await ApplyFolderBackgroundAsync();
    }

    public void ActivateNearCursor()
    {
        if (GetCursorPos(out var cursor))
        {
            AppWindow.Move(new PointInt32(cursor.X, cursor.Y));
        }

        Activate();
    }

    // ---- Navegacao (requisito 1) ----

    private async void Breadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        var target = ViewModel.Breadcrumb[args.Index];
        await ViewModel.NavigateToBreadcrumbCommand.ExecuteAsync(target);
        ApplyRememberedSizeForCurrentFolder();
    }

    /// <summary>So' a BreadcrumbBar depende do nivel atual - nas vistas globais da Fase 3 (favoritos/recentes/busca) ela nao faz sentido.</summary>
    private Visibility ModeToVisibility(BrowseMode mode) => mode == BrowseMode.Folder ? Visibility.Visible : Visibility.Collapsed;

    private async void ItemsGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        await ActivateSelectedAsync();
    }

    private async void ItemsGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                await ActivateSelectedAsync();
                e.Handled = true;
                return;
            case VirtualKey.Back:
                if (ViewModel.NavigateUpCommand.CanExecute(null))
                {
                    await ViewModel.NavigateUpCommand.ExecuteAsync(null);
                    ApplyRememberedSizeForCurrentFolder();
                }

                e.Handled = true;
                return;
        }

        // Type-ahead estilo Explorer (RF08): digitar sem abrir nenhuma caixa de busca pula a
        // selecao para o primeiro item cujo nome comeca com o texto digitado, so' no nivel
        // atual - diferente da busca global da caixa de pesquisa (RF10), que varre a arvore
        // inteira. Buffer reiniciado apos ~1s sem digitar, igual ao Explorer real.
        var character = VirtualKeyToChar(e.Key);
        if (character is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _typeAheadBuffer = (now - _typeAheadLastKeyUtc).TotalSeconds > 1 ? string.Empty : _typeAheadBuffer;
        _typeAheadBuffer += character;
        _typeAheadLastKeyUtc = now;

        var match = ViewModel.Items.FirstOrDefault(i => i.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            ItemsGrid.SelectedItem = match;
            ItemsGrid.ScrollIntoView(match);
        }

        e.Handled = true;
    }

    private static char? VirtualKeyToChar(VirtualKey key)
    {
        return key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => (char)('A' + (key - VirtualKey.A)),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => (char)('0' + (key - VirtualKey.Number0)),
            VirtualKey.Space => ' ',
            _ => null,
        };
    }

    // ---- Busca global e vistas (Fase 3: RF09-RF13) ----

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            await ViewModel.ShowFolderCommand.ExecuteAsync(null);
        }
        else
        {
            await ViewModel.SearchAsync(SearchBox.Text);
        }
    }

    private async void ShowFavorites_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        await ViewModel.ShowFavoritesCommand.ExecuteAsync(null);
    }

    private async void ShowRecent_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        await ViewModel.ShowRecentCommand.ExecuteAsync(null);
    }

    private async void ShowMostUsed_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        await ViewModel.ShowMostUsedCommand.ExecuteAsync(null);
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MenuEntryViewModel entry })
        {
            await ViewModel.ToggleFavoriteAsync(entry);
        }
    }

    // ---- Tema (Fase 4): cor de fundo por pasta ----

    private async void SetFolderColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MenuEntryViewModel { IsFolder: true } folder })
        {
            return;
        }

        var current = await Repository.GetFolderBackgroundColorAsync(folder.Id);
        var textBox = new TextBox { Header = "Cor em hex (ex: #1E3A5F) - vazio remove a cor customizada", Text = current ?? string.Empty };
        var dialog = new ContentDialog
        {
            Title = $"Cor de fundo - {folder.Name}",
            Content = textBox,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var hex = string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim();
        if (hex is not null && !HexColor.IsValid(hex))
        {
            return; // cor invalida - ignora silenciosamente por enquanto (Fase 4 e' o MVP disto).
        }

        await Repository.SetFolderBackgroundColorAsync(folder.Id, hex);

        if (folder.Id == ViewModel.CurrentFolderId)
        {
            await ApplyFolderBackgroundAsync();
        }
    }

    private async Task ApplyFolderBackgroundAsync()
    {
        string? hex = null;
        if (ViewModel.Mode == BrowseMode.Folder && ViewModel.CurrentFolderId is not null)
        {
            hex = await Repository.GetFolderBackgroundColorAsync(ViewModel.CurrentFolderId);
        }

        RootGrid.Background = hex is not null && TryParseHexColor(hex, out var color)
            ? new SolidColorBrush(color)
            : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["LayerFillColorDefaultBrush"];
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !byte.TryParse(value[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(value[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(value[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Color.FromArgb(255, r, g, b);
        return true;
    }

    private async Task ActivateSelectedAsync()
    {
        if (ItemsGrid.SelectedItem is not MenuEntryViewModel entry)
        {
            return;
        }

        if (entry.IsFolder)
        {
            await ViewModel.NavigateIntoCommand.ExecuteAsync(entry);
            ApplyRememberedSizeForCurrentFolder();
        }
        else
        {
            await LaunchService.LaunchAsync(Repository, entry);
        }
    }

    // ---- Drag-and-drop entre pastas e para o Explorer (requisito 2) ----

    private void ItemsGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 0 || e.Items[0] is not MenuEntryViewModel entry)
        {
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetData(DraggedItemFormat, entry.Id);

        // Quando o alvo e' um arquivo/pasta real do disco, oferece tambem o formato nativo
        // de arraste de arquivo (StorageItems) - e' o que faz o Explorer aceitar o drop de
        // verdade, sem nenhum interop COM manual. Itens sem um caminho real no disco (URL,
        // comando) so carregam o formato interno, evitando um drop que pareceria aceito no
        // Explorer mas nao geraria nada util.
        var path = entry.Path;
        if (!entry.IsFolder && path is not null && (File.Exists(path) || Directory.Exists(path)))
        {
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    IStorageItem storageItem = File.Exists(path)
                        ? await StorageFile.GetFileFromPathAsync(path)
                        : await StorageFolder.GetFolderFromPathAsync(path);
                    request.SetData(new List<IStorageItem> { storageItem });
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
    }

    private void ItemContainer_DragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MenuEntryViewModel { IsFolder: true } } && e.DataView.Contains(DraggedItemFormat))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void ItemContainer_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MenuEntryViewModel { IsFolder: true } target })
        {
            return;
        }

        if (!e.DataView.Contains(DraggedItemFormat))
        {
            return;
        }

        var draggedId = (string)await e.DataView.GetDataAsync(DraggedItemFormat);
        await ViewModel.TryMoveIntoFolderAsync(draggedId, target);
    }

    // ---- Redimensionamento "como pasta do Windows" (requisito 3) ----
    // O reflow em si e' automatico (GridView usa um ItemsWrapGrid nativo); aqui so
    // lembramos o tamanho por pasta, mesma ideia da geometria por grupo do EasyWinMenu.

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            _settings.Set(SettingsStore.WindowSizeKey(ViewModel.CurrentFolderId), $"{sender.Size.Width},{sender.Size.Height}");
        }
    }

    private void ApplyRememberedSizeForCurrentFolder()
    {
        var raw = _settings.Get(SettingsStore.WindowSizeKey(ViewModel.CurrentFolderId));
        if (raw is null)
        {
            return;
        }

        var parts = raw.Split(',');
        if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
        {
            AppWindow.Resize(new SizeInt32(width, height));
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }
}
