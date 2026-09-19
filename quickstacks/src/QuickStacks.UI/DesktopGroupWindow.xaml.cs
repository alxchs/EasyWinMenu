using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using QuickStacks.Localization;
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
public sealed partial class DesktopGroupWindow : Window, ICutVisualOwner
{
    private const string DraggedItemFormat = "QuickStacksItemId";

    private const double AppFolderTileWidth = 110;
    private const double AppFolderTileHeight = 140;
    private const int AppFolderMosaicCapacity = 9;

    private readonly IMenuRepository _repository;
    private readonly string _groupId;
    private readonly string _groupName;
    private readonly Dictionary<string, FrameworkElement> _tilesByEntry = [];
    private PopupWindow? _subfolderPopup;
    private PopupWindow? _sheetPopup;
    private DesktopGroupPlacement _placement;
    private MenuEntryViewModel? _selectedEntry;
    private FrameworkElement? _selectedTile;

    public string OwnerId => _groupId;

    public DesktopGroupWindow(IMenuRepository repository, MenuItem group)
    {
        InitializeComponent();

        _repository = repository;
        _groupId = group.Id;
        _groupName = group.Name;
        TitleText.Text = group.Name;
        Title = $"{group.Name} - {LocalizationService.Get("app.name")}";

        ThemeService.Register(RootGrid);
        ClipboardService.RegisterOwner(this);
        Closed += (_, _) => ClipboardService.UnregisterOwner(this);

        HeaderBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.12 };
        AppWindow.Changed += AppWindow_Changed;
        RootGrid.KeyDown += RootGrid_KeyDown;

        _placement = DesktopGroupPlacement.CreateDefault(_groupId, 80, 80);

        _ = InitializeAsync();
    }

    public void RefreshCutVisuals()
    {
        foreach (var (id, tile) in _tilesByEntry)
        {
            tile.Opacity = ClipboardService.Coordinator.IsCutPending(id) ? 0.5 : 1.0;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            _placement = await _repository.GetDesktopGroupPlacementAsync(_groupId)
                ?? DesktopGroupPlacement.CreateDefault(_groupId, 80, 80);

            await RescueIfUnreachableAsync();

            RefreshTexts();
            ApplyDisplayModeChrome();
            ResizeForCurrentMode();
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[DesktopGroupWindow] Falha ao inicializar grupo '{_groupId}': {ex}");
        }
    }

    /// <summary>
    /// Fase 12: se o monitor onde este grupo foi posicionado da ultima vez nao existe mais
    /// (desconectado, resolucao mudou), traz o grupo de volta pra dentro de uma area de
    /// trabalho de verdade em vez de deixa-lo preso fora da tela e inalcancavel.
    /// </summary>
    private async Task RescueIfUnreachableAsync()
    {
        var workAreas = DisplayInventory.GetWorkAreas();
        if (workAreas.Count == 0)
        {
            return;
        }

        var groupRect = new MonitorRect(_placement.X, _placement.Y, _placement.Width, _placement.Height);
        if (MonitorPlacement.IsReachable(groupRect, workAreas))
        {
            return;
        }

        var ownerIndex = MonitorPlacement.IndexOfOwner(groupRect, workAreas);
        if (ownerIndex < 0)
        {
            return;
        }

        var (x, y) = MonitorPlacement.ClampInto(_placement.X, _placement.Y, _placement.Width, _placement.Height, workAreas[ownerIndex]);
        _placement = _placement with { X = x, Y = y };
        await _repository.SetDesktopGroupPlacementAsync(_placement);
    }

    /// <summary>Win+Shift+seta move o grupo pro monitor vizinho (Fase 12), igual ao atalho nativo do Windows pra janelas comuns - so' funciona com a janela em foco, ja' que nao ha hook global (removido do EasyWinMenu por travar o sistema - decisao herdada, nao reaberta aqui).</summary>
    private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.F1)
        {
            App.OpenHelp();
            e.Handled = true;
            return;
        }

        var ctrlDown = IsKeyDown(Windows.System.VirtualKey.Control);
        if (ctrlDown && e.Key == Windows.System.VirtualKey.C)
        {
            if (_selectedEntry is { Path: { } path })
            {
                ClipboardService.Copy(path);
            }
            e.Handled = true;
            return;
        }

        if (ctrlDown && e.Key == Windows.System.VirtualKey.X)
        {
            if (_selectedEntry is { Path: { } path } entry)
            {
                ClipboardService.Cut(entry.Id, path, _groupId, DispatcherQueue);
            }
            e.Handled = true;
            return;
        }

        if (ctrlDown && e.Key == Windows.System.VirtualKey.V)
        {
            await ClipboardService.PasteAsync(_repository, _groupId, ReloadAsync);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right))
        {
            return;
        }

        var windowsDown = IsKeyDown(Windows.System.VirtualKey.LeftWindows) || IsKeyDown(Windows.System.VirtualKey.RightWindows);
        var shiftDown = IsKeyDown(Windows.System.VirtualKey.Shift);
        if (!windowsDown || !shiftDown)
        {
            return;
        }

        var workAreas = DisplayInventory.GetWorkAreas();
        var groupRect = new MonitorRect(_placement.X, _placement.Y, _placement.Width, _placement.Height);
        var currentIndex = MonitorPlacement.IndexOfOwner(groupRect, workAreas);
        var direction = e.Key == Windows.System.VirtualKey.Right ? 1 : -1;
        var targetIndex = MonitorPlacement.AdjacentIndex(currentIndex, workAreas, direction);
        if (targetIndex is null)
        {
            return;
        }

        var (x, y) = MonitorPlacement.MapBetween(groupRect, workAreas[currentIndex], workAreas[targetIndex.Value]);
        _placement = _placement with { X = x, Y = y };
        AppWindow.Move(new PointInt32((int)x, (int)y));
        await _repository.SetDesktopGroupPlacementAsync(_placement);
        e.Handled = true;
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void RefreshTexts()
    {
        ArrangeSubItem.Text = LocalizationService.Get("desktopGroup.arrangeMenu");
        ArrangeNoneItem.Text = LocalizationService.Get("desktopGroup.arrangeNone");
        ArrangeGridItem.Text = LocalizationService.Get("desktopGroup.arrangeGrid");
        ArrangeByNameItem.Text = LocalizationService.Get("desktopGroup.arrangeByName");
        ArrangeByTypeItem.Text = LocalizationService.Get("desktopGroup.arrangeByType");
    }

    /// <summary>Mostra o cabecalho/canvas (Panel) ou o ladrilho fechado (AppFolder) - a mesma janela, conteudo trocado, sem recriar nada.</summary>
    private void ApplyDisplayModeChrome()
    {
        var isAppFolder = _placement.DisplayMode == DesktopGroupDisplayMode.AppFolder;
        HeaderBar.Visibility = isAppFolder ? Visibility.Collapsed : Visibility.Visible;
        IconCanvas.Visibility = isAppFolder ? Visibility.Collapsed : Visibility.Visible;
        AppFolderRoot.Visibility = isAppFolder ? Visibility.Visible : Visibility.Collapsed;
        ToggleDisplayModeItem.Text = LocalizationService.Get(isAppFolder ? "desktopGroup.switchToPanel" : "desktopGroup.switchToAppFolder");
    }

    private void ResizeForCurrentMode()
    {
        var width = Math.Max(100, (int)_placement.Width);
        var height = Math.Max(100, (int)_placement.Height);
        var size = _placement.DisplayMode == DesktopGroupDisplayMode.AppFolder
            ? new SizeInt32((int)AppFolderTileWidth, (int)AppFolderTileHeight)
            : new SizeInt32(width, height);

        AppWindow.MoveAndResize(new RectInt32((int)_placement.X, (int)_placement.Y, size.Width, size.Height));
    }

    public async Task ReloadAsync()
    {
        try
        {
            var children = await _repository.GetChildrenAsync(_groupId);

            if (_placement.DisplayMode == DesktopGroupDisplayMode.AppFolder)
            {
                BuildAppFolderTile(children);
                return;
            }

            _tilesByEntry.Clear();
            IconCanvas.Children.Clear();

            // Fase 11: organizacao automatica "viva" - com um modo de arranjo ativo, a posicao
            // salva de cada icone (DesktopIconPosition) e' ignorada e a grade e' recalculada aqui
            // sempre que o conteudo/geometria muda; None e' o unico modo onde o usuario controla
            // a posicao pelo arrastar-e-soltar.
            var allowManualDrag = _placement.Arrangement == DesktopIconArrangement.None;
            var ordered = _placement.Arrangement switch
            {
                DesktopIconArrangement.ByName => children.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
                DesktopIconArrangement.ByType => children.OrderBy(c => c.Type).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
                DesktopIconArrangement.Grid => children.OrderBy(c => c.SortOrder),
                _ => children.OrderBy(c => c.SortOrder),
            };

            var positions = allowManualDrag ? await _repository.GetDesktopIconPositionsAsync(_groupId) : null;

            var cascade = 0;
            foreach (var child in ordered)
            {
                var entry = new MenuEntryViewModel(child, App.IconCache);
                var tile = BuildTile(entry, allowManualDrag);

                _tilesByEntry[child.Id] = tile;
                tile.Opacity = ClipboardService.Coordinator.IsCutPending(child.Id) ? 0.5 : 1.0;

                if (positions is not null && positions.TryGetValue(child.Id, out var position))
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
        catch (Exception ex)
        {
            App.Log($"[DesktopGroupWindow] Falha ao recarregar itens do grupo '{_groupId}': {ex}");
        }
    }

    /// <summary>Alterna o modo de organizacao automatica (Fase 11) - Grade tambem forca reflow em cascata (mesma logica de "sem posicao salva" acima), Nome/Tipo ordenam por esses criterios.</summary>
    private async void SetArrangement_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag } || !Enum.TryParse<DesktopIconArrangement>(tag, out var arrangement))
        {
            return;
        }

        _placement = _placement with { Arrangement = arrangement };
        await _repository.SetDesktopGroupPlacementAsync(_placement);
        await ReloadAsync();
    }

    /// <summary>
    /// Ladrilho fechado (Fase 10, modelo AppFolderTile.cs do EasyWinMenu): mosaico 3x3 dos
    /// primeiros icones + selo de contagem + nome do grupo. Nasce ja' na escala final (a
    /// janela inteira e' do tamanho do ladrilho, sem LayoutTransform) - item 14 do inventario
    /// documenta o bug de clique desalinhado que um LayoutTransform causaria aqui.
    /// </summary>
    private void BuildAppFolderTile(IReadOnlyList<MenuItem> children)
    {
        AppFolderRoot.Children.Clear();

        var stack = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };

        var plateSize = 76.0;
        var plate = new Border
        {
            Width = plateSize,
            Height = plateSize,
            CornerRadius = new CornerRadius(plateSize * 0.26),
            Padding = new Thickness(plateSize * 0.1),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.18 },
        };

        var mosaic = new Grid();
        for (var i = 0; i < 3; i++)
        {
            mosaic.RowDefinitions.Add(new RowDefinition());
            mosaic.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var cellIndex = 0;
        foreach (var child in children.OrderBy(c => c.SortOrder).Take(AppFolderMosaicCapacity))
        {
            var entry = new MenuEntryViewModel(child, App.IconCache);
            FrameworkElement cell;
            var bitmap = IconImageLoader.GetBitmap(entry.IconPath);
            if (entry.HasCustomIcon && bitmap is not null)
            {
                cell = new Image
                {
                    Source = bitmap,
                    Width = plateSize * 0.22,
                    Height = plateSize * 0.22,
                    Margin = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            else
            {
                cell = new FontIcon
                {
                    Glyph = entry.Glyph,
                    FontSize = plateSize * 0.16,
                    Margin = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            Grid.SetRow(cell, cellIndex / 3);
            Grid.SetColumn(cell, cellIndex % 3);
            mosaic.Children.Add(cell);
            cellIndex++;
        }

        plate.Child = mosaic;

        var plateLayer = new Grid { Width = plateSize, Height = plateSize };
        plateLayer.Children.Add(plate);

        if (children.Count > 0)
        {
            var badgeText = new TextBlock
            {
                Text = children.Count > 99 ? "99+" : children.Count.ToString(),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var badge = new Border
            {
                MinWidth = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5, 0, 5, 0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE5, 0x39, 0x35)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                Child = badgeText,
            };
            plateLayer.Children.Add(badge);
        }

        stack.Children.Add(plateLayer);
        stack.Children.Add(new TextBlock
        {
            Text = _groupName,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = plateSize + 24,
        });

        AppFolderRoot.Children.Add(stack);
    }

    /// <summary>Alterna Panel/AppFolder (Fase 10) - geometria de cada modo persistida separadamente nao existe ainda (so X/Y/Width/Height unicos); ao voltar pra Panel, o tamanho do painel e' preservado porque so' a janela e' redimensionada, nunca o registro em si.</summary>
    private async void ToggleDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        var newMode = _placement.DisplayMode == DesktopGroupDisplayMode.AppFolder
            ? DesktopGroupDisplayMode.Panel
            : DesktopGroupDisplayMode.AppFolder;

        _placement = _placement with { DisplayMode = newMode };
        await _repository.SetDesktopGroupPlacementAsync(_placement);

        ApplyDisplayModeChrome();
        ResizeForCurrentMode();
        await ReloadAsync();
    }

    private void AppFolderRoot_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _sheetPopup ??= new PopupWindow(_repository);
        _ = _sheetPopup.NavigateToFolderAsync(_groupId, _groupName);
        _sheetPopup.Closed += (_, _) => _sheetPopup = null;
        _sheetPopup.ActivateCentered();
    }

    private FrameworkElement BuildTile(MenuEntryViewModel entry, bool allowManualDrag)
    {
        var stack = new StackPanel
        {
            Width = 76,
            Spacing = 4,
            Padding = new Thickness(4),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), // area clicavel/arrastavel inclui o espaco vazio ao redor do texto
        };
        var bitmap = IconImageLoader.GetBitmap(entry.IconPath);
        if (entry.HasCustomIcon && bitmap is not null)
        {
            stack.Children.Add(new Image
            {
                Source = bitmap,
                Width = 28,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
        else
        {
            stack.Children.Add(new FontIcon { Glyph = entry.Glyph, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center });
        }
        stack.Children.Add(new TextBlock
        {
            Text = entry.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        AttachTileBehavior(stack, entry, allowManualDrag);
        return stack;
    }

    /// <summary>Com um modo de organizacao automatica ativo (Fase 11), so' o duplo-toque (abrir/navegar) fica ligado - arrastar manualmente entraria em conflito com o reflow que a proxima recarga vai forcar de qualquer forma.</summary>
    private void AttachTileBehavior(FrameworkElement tile, MenuEntryViewModel entry, bool allowManualDrag)
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

        // Selecao pra Ctrl+C/X (Fase 16) - independe do modo de organizacao automatica, por
        // isso fica antes do "return" abaixo que so' guarda o comportamento de arrastar.
        tile.Tapped += (_, _) =>
        {
            _selectedEntry = entry;
            _selectedTile = tile;
        };

        if (entry.CanRunAsAdministrator || entry.HasFileTarget)
        {
            tile.ContextFlyout = BuildTileContextMenu(entry);
        }

        if (!allowManualDrag)
        {
            return;
        }

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

    /// <summary>
    /// So' atualiza X/Y (posicao vale nos dois modos) e Width/Height quando em modo Panel - em
    /// AppFolder a janela e' redimensionada pra caber o ladrilho por ResizeForCurrentMode, e
    /// gravar esse tamanho por cima destruiria a geometria do Panel guardada pra quando o
    /// usuario voltar pra ele. Bug corrigido na Fase 12: esta funcao gravava DisplayMode=Panel
    /// e Arrangement=None fixos, apagando a escolha do usuario (App Folder/organizacao
    /// automatica) toda vez que a janela se movia - inclusive por causa dos proprios
    /// MoveAndResize programaticos deste arquivo (resgate de monitor, troca de modo).
    /// </summary>
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        if (sender.Size.Width <= 0 || sender.Size.Height <= 0)
        {
            return;
        }

        _placement = _placement.DisplayMode == DesktopGroupDisplayMode.Panel
            ? _placement with { X = sender.Position.X, Y = sender.Position.Y, Width = sender.Size.Width, Height = sender.Size.Height }
            : _placement with { X = sender.Position.X, Y = sender.Position.Y };

        _ = _repository.SetDesktopGroupPlacementAsync(_placement);
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

        await AddStorageItemsAsync(await e.DataView.GetStorageItemsAsync());
    }

    private async Task AddStorageItemsAsync(IReadOnlyList<IStorageItem> items)
    {
        var siblingCount = (await _repository.GetChildrenAsync(_groupId)).Count;
        var pastedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var storageItem in items)
        {
            var type = storageItem is StorageFolder ? MenuItemType.Executable : MenuItemType.Executable;
            var launchItem = MenuItem.CreateShortcut(storageItem.Name, _groupId, type, storageItem.Path, siblingCount++);
            await _repository.AddAsync(launchItem);
            pastedPaths.Add(storageItem.Path);
        }

        var consumed = ClipboardService.Coordinator.ConsumeMatching(pastedPaths);
        foreach (var cut in consumed)
        {
            await _repository.DeleteAsync(cut.ItemId);
        }

        await ReloadAsync();
    }

    private MenuFlyout BuildTileContextMenu(MenuEntryViewModel entry)
    {
        var flyout = new MenuFlyout();

        if (entry.CanRunAsAdministrator)
        {
            var runAsAdminItem = new MenuFlyoutItem { Text = LocalizationService.Get("item.runAsAdmin") };
            runAsAdminItem.Click += async (_, _) => await LaunchService.RunAsAdministratorAsync(_repository, entry);
            flyout.Items.Add(runAsAdminItem);
        }

        if (entry.HasFileTarget)
        {
            var revealItem = new MenuFlyoutItem { Text = LocalizationService.Get("item.revealInExplorer") };
            revealItem.Click += (_, _) => LaunchService.RevealInExplorer(entry);
            flyout.Items.Add(revealItem);

            var copyPathItem = new MenuFlyoutItem { Text = LocalizationService.Get("item.copyPath") };
            copyPathItem.Click += (_, _) =>
            {
                var path = LaunchService.TryResolvePhysicalPath(entry) ?? entry.Path;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    pkg.SetText(path);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
                }
            };
            flyout.Items.Add(copyPathItem);

            var propsItem = new MenuFlyoutItem { Text = LocalizationService.Get("item.properties") };
            propsItem.Click += (_, _) =>
            {
                var path = LaunchService.TryResolvePhysicalPath(entry) ?? entry.Path;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    ShellHelper.ShowProperties(path);
                }
            };
            flyout.Items.Add(propsItem);

            var standardMenuItem = new MenuFlyoutItem { Text = LocalizationService.Get("item.standardMenu") };
            standardMenuItem.Click += (_, _) =>
            {
                var path = LaunchService.TryResolvePhysicalPath(entry) ?? entry.Path;
                if (!string.IsNullOrWhiteSpace(path) && GetCursorPos(out var cursor))
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    App.ShellContextMenu?.TryShow(hwnd, path, cursor.X, cursor.Y);
                }
            };
            flyout.Items.Add(standardMenuItem);
        }

        return flyout;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);
}
