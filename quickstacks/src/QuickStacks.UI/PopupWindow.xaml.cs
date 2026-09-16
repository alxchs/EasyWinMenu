using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

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

    public PopupWindow(IMenuRepository repository)
    {
        InitializeComponent();

        ViewModel = new FolderNavigationViewModel(repository);
        Repository = repository;

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
                break;
            case VirtualKey.Back:
                if (ViewModel.NavigateUpCommand.CanExecute(null))
                {
                    await ViewModel.NavigateUpCommand.ExecuteAsync(null);
                    ApplyRememberedSizeForCurrentFolder();
                }

                e.Handled = true;
                break;
        }
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
