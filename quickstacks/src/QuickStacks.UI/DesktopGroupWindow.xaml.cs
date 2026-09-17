using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickStacks.Application;
using QuickStacks.Domain;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage;

namespace QuickStacks.UI;

/// <summary>
/// Grupo solto na área de trabalho (Fase 9, modo Full) - modo Panel: canvas livre com ícones
/// arrastáveis, um por pasta marcada <see cref="MenuItem.IsDesktopGroup"/>. Mostra só o nível
/// direto do grupo; entrar numa subpasta abre o <see cref="PopupWindow"/> já existente
/// (Fase 1) ali dentro em vez de duplicar navegação em trilha numa segunda janela - reaproveita
/// código testado em vez de recriar breadcrumbs para este caso.
/// </summary>
public sealed partial class DesktopGroupWindow : Window
{
    private const string DraggedItemFormat = "QuickStacksItemId";

    private readonly IMenuRepository _repository;
    private readonly string _groupId;
    private PopupWindow? _subfolderPopup;

    public DesktopGroupWindow(IMenuRepository repository, MenuItem group)
    {
        InitializeComponent();

        _repository = repository;
        _groupId = group.Id;
        TitleText.Text = group.Name;

        ThemeService.Register(RootGrid);
        HeaderBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.12 };
        AppWindow.Changed += AppWindow_Changed;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var placement = await _repository.GetDesktopGroupPlacementAsync(_groupId)
            ?? DesktopGroupPlacement.CreateDefault(_groupId, 80, 80);

        AppWindow.MoveAndResize(new RectInt32(
            (int)placement.X, (int)placement.Y, (int)placement.Width, (int)placement.Height));

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var children = await _repository.GetChildrenAsync(_groupId);
        var positions = await _repository.GetDesktopIconPositionsAsync(_groupId);

        IconCanvas.Children.Clear();

        var cascade = 0;
        foreach (var child in children.OrderBy(c => c.SortOrder))
        {
            var entry = new MenuEntryViewModel(child);
            var tile = BuildTile(entry);

            if (positions.TryGetValue(child.Id, out var position))
            {
                Canvas.SetLeft(tile, position.X);
                Canvas.SetTop(tile, position.Y);
            }
            else
            {
                Canvas.SetLeft(tile, 16 + (cascade % 4) * 84);
                Canvas.SetTop(tile, 16 + (cascade / 4) * 96);
                cascade++;
            }

            IconCanvas.Children.Add(tile);
        }
    }

    private FrameworkElement BuildTile(MenuEntryViewModel entry)
    {
        var stack = new StackPanel
        {
            Width = 76,
            Spacing = 4,
            Padding = new Thickness(4),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), // area clicavel/arrastavel inclui o espaco vazio ao redor do texto
        };
        stack.Children.Add(new FontIcon { Glyph = entry.Glyph, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = entry.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        AttachTileBehavior(stack, entry);
        return stack;
    }

    private void AttachTileBehavior(FrameworkElement tile, MenuEntryViewModel entry)
    {
        var dragging = false;
        Point dragStartPointer = default;
        Point tileStart = default;

        tile.DoubleTapped += async (_, _) =>
        {
            if (entry.IsFolder)
            {
                OpenSubfolderPopup(entry);
            }
            else
            {
                await LaunchService.LaunchAsync(_repository, entry);
            }
        };

        tile.PointerPressed += (_, e) =>
        {
            dragging = true;
            dragStartPointer = e.GetCurrentPoint(IconCanvas).Position;
            tileStart = new Point(Canvas.GetLeft(tile), Canvas.GetTop(tile));
            tile.CapturePointer(e.Pointer);
        };

        tile.PointerMoved += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var current = e.GetCurrentPoint(IconCanvas).Position;
            Canvas.SetLeft(tile, tileStart.X + (current.X - dragStartPointer.X));
            Canvas.SetTop(tile, tileStart.Y + (current.Y - dragStartPointer.Y));
        };

        tile.PointerReleased += async (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            tile.ReleasePointerCapture(e.Pointer);
            await _repository.SetDesktopIconPositionAsync(new DesktopIconPosition(entry.Id, Canvas.GetLeft(tile), Canvas.GetTop(tile)));
        };
    }

    private void OpenSubfolderPopup(MenuEntryViewModel folder)
    {
        _subfolderPopup ??= new PopupWindow(_repository);
        _ = _subfolderPopup.NavigateToFolderAsync(folder.Id, folder.Name);
        _subfolderPopup.Closed += (_, _) => _subfolderPopup = null;
        _subfolderPopup.ActivateNearCursor();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        _ = _repository.SetDesktopGroupPlacementAsync(new DesktopGroupPlacement(
            _groupId, sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height,
            DesktopGroupDisplayMode.Panel, DesktopGroupPlacement.DefaultIconScale, false));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ---- Arrastar arquivos do Explorer para dentro do grupo (equivalente ao EasyWinMenu) ----

    private void IconCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void IconCanvas_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var siblingCount = (await _repository.GetChildrenAsync(_groupId)).Count;

        foreach (var storageItem in items)
        {
            var type = storageItem is StorageFolder ? MenuItemType.Executable : MenuItemType.Executable;
            var launchItem = MenuItem.CreateShortcut(storageItem.Name, _groupId, type, storageItem.Path, siblingCount++);
            await _repository.AddAsync(launchItem);
        }

        await ReloadAsync();
    }
}
