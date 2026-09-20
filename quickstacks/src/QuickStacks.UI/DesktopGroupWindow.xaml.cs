using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using QuickStacks.Localization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;

namespace QuickStacks.UI;

/// <summary>
/// Grupo solto na área de trabalho: exclusivo para atalhos (aplicativos, arquivos, URLs e atalhos especiais
/// de sistema como Modo Deus e Painel de Controle). Sem barras nativas "WinUI Desktop", cantos arredondados,
/// arrasto fluido via Win32, auto-organização imediata com redimensionamento automático, sombra configurável
/// e busca integrada CTRL+F com lista suspensa.
/// </summary>
public sealed partial class DesktopGroupWindow : Window
{
    private const string DraggedItemFormat = "QuickStacksItemId";
    private const string SourceGroupFormat = "QuickStacksSourceGroupId";

    private const double AppFolderTileWidth = 110;
    private const double AppFolderTileHeight = 140;
    private const int AppFolderMosaicCapacity = 9;

    private static readonly List<DesktopGroupWindow> _openGroups = new();

    private readonly IMenuRepository _repository;
    private readonly SettingsStore _settings = new();
    private readonly string _groupId;
    private readonly string _groupName;

    private PopupWindow? _sheetPopup;
    private DesktopGroupPlacement _placement;
    private List<MenuEntryViewModel> _currentItems = new();
    private int _selectedIndex = -1;
    private readonly List<Border> _tileBorders = new();

    private SpriteVisual? _shadowVisual;

    public DesktopGroupWindow(IMenuRepository repository, MenuItem group)
    {
        App.Log($"DesktopGroupWindow constructor: Initializing for group '{group.Name}' ({group.Id})...");
        InitializeComponent();

        _repository = repository;
        _groupId = group.Id;
        _groupName = group.Name;
        Title = group.Name;
        AppWindow.Title = group.Name;
        TitleText.Text = group.Name;

        ThemeService.Register(RootGrid);
        HeaderBar.Background = new SolidColorBrush(Colors.Black) { Opacity = 0.15 };

        // Remove barra de título e moldura nativa do Windows (sem "WinUI Desktop", sem _ □ ✕)
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = false;
        }
        try
        {
            AppWindow.IsShownInSwitchers = false;
        }
        catch
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        AppWindow.Changed += AppWindow_Changed;
        RootGrid.SizeChanged += RootGrid_SizeChanged;
        Activated += (_, _) => RootGrid.Focus(FocusState.Programmatic);

        _placement = DesktopGroupPlacement.CreateDefault(_groupId, 80, 80);

        _openGroups.Add(this);
        Closed += (_, _) => _openGroups.Remove(this);

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            App.Log($"DesktopGroupWindow.InitializeAsync: Starting for '{_groupName}'...");
            _placement = await _repository.GetDesktopGroupPlacementAsync(_groupId)
                ?? DesktopGroupPlacement.CreateDefault(_groupId, 80, 80);

            ApplyDisplayModeChrome();
            InitShadowFromSettings();
            await ReloadAsync();
            App.Log($"DesktopGroupWindow.InitializeAsync: Completed for '{_groupName}'.");
        }
        catch (Exception ex)
        {
            App.Log($"DesktopGroupWindow.InitializeAsync ERROR for '{_groupName}': {ex}");
        }
    }

    // ---- Arrasto Nativo Fluido Win32 (WM_NCLBUTTONDOWN + HTCAPTION) ----

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_CONTROL = 0x11;

    private void StartWindowDrag()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        var pos = AppWindow.Position;
        _placement = _placement with { X = pos.X, Y = pos.Y };
        _ = _repository.SetDesktopGroupPlacementAsync(_placement);
    }

    private void HeaderBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(HeaderBar).Properties.IsLeftButtonPressed)
        {
            StartWindowDrag();
        }
    }

    private void AppFolderRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(AppFolderRoot).Properties.IsLeftButtonPressed)
        {
            StartWindowDrag();
        }
    }

    // ---- Sombra Moderna com Ângulo Configurável ----

    private void InitShadowFromSettings()
    {
        var angleStr = _settings.Get(SettingsStore.ShadowAngleKey);
        var enabledStr = _settings.Get(SettingsStore.ShadowEnabledKey);

        if (enabledStr == "false" || angleStr == "-1")
        {
            ApplyShadow(-1);
            return;
        }

        if (double.TryParse(angleStr, out var angle))
        {
            ApplyShadow(angle);
        }
        else
        {
            ApplyShadow(135); // Padrão natural 135° (Inferior Direito)
        }
    }

    private void ApplyShadow(double angleDegrees)
    {
        if (angleDegrees < 0)
        {
            _settings.Set(SettingsStore.ShadowEnabledKey, "false");
            _settings.Set(SettingsStore.ShadowAngleKey, "-1");
            UpdateShadowMenuCheck(-1);

            if (_shadowVisual != null)
            {
                _shadowVisual.IsVisible = false;
            }
            return;
        }

        _settings.Set(SettingsStore.ShadowEnabledKey, "true");
        _settings.Set(SettingsStore.ShadowAngleKey, angleDegrees.ToString());
        UpdateShadowMenuCheck(angleDegrees);

        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(RootGrid).Compositor;
            var dropShadow = compositor.CreateDropShadow();
            dropShadow.BlurRadius = 22f;
            dropShadow.Opacity = 0.45f;
            dropShadow.Color = Color.FromArgb(255, 0, 0, 0);

            var rad = angleDegrees * Math.PI / 180.0;
            float distance = 10f;
            dropShadow.Offset = new System.Numerics.Vector3((float)(distance * Math.Cos(rad)), (float)(distance * Math.Sin(rad)), 0);

            if (_shadowVisual == null)
            {
                _shadowVisual = compositor.CreateSpriteVisual();
                ElementCompositionPreview.SetElementChildVisual(RootGrid, _shadowVisual);
            }

            _shadowVisual.Shadow = dropShadow;
            _shadowVisual.IsVisible = true;
            _shadowVisual.Size = new System.Numerics.Vector2((float)RootGrid.ActualWidth, (float)RootGrid.ActualHeight);
        }
        catch
        {
            // Proteção se a API de composição não for compatível na sessão
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_shadowVisual != null && _shadowVisual.IsVisible)
        {
            _shadowVisual.Size = new System.Numerics.Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        }
    }

    private void UpdateShadowMenuCheck(double angleDegrees)
    {
        Shadow135Item.IsChecked = Math.Abs(angleDegrees - 135) < 0.1;
        Shadow90Item.IsChecked = Math.Abs(angleDegrees - 90) < 0.1;
        Shadow225Item.IsChecked = Math.Abs(angleDegrees - 225) < 0.1;
        Shadow45Item.IsChecked = Math.Abs(angleDegrees - 45) < 0.1;
        ShadowNoneItem.IsChecked = angleDegrees < 0;
    }

    private void ShadowAngle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && double.TryParse(tag, out var angle))
        {
            ApplyShadow(angle);
        }
    }

    // ---- Auto-Organização e Redimensionamento Imediato ----

    public async Task ReloadAsync()
    {
        var rawChildren = await _repository.GetChildrenAsync(_groupId);
        // Regra do grupo: estritamente atalhos (arquivos, URLs, executáveis, comandos de sistema)
        _currentItems = rawChildren.OrderBy(c => c.SortOrder).Select(c => new MenuEntryViewModel(c)).ToList();

        if (_placement.DisplayMode == DesktopGroupDisplayMode.AppFolder)
        {
            BuildAppFolderTile(rawChildren);
            ResizeForAppFolder();
            return;
        }

        await AutoArrangeAndResizeAsync();
    }

    private async Task AutoArrangeAndResizeAsync()
    {
        IconCanvas.Children.Clear();
        _tileBorders.Clear();
        _selectedIndex = -1;

        const int tileW = 76;
        const int tileH = 84;
        const int gapX = 8;
        const int gapY = 8;
        const int padX = 12;
        const int padY = 10;
        const int headerH = 34;

        var count = _currentItems.Count;
        int columns;

        if (count <= 1)
        {
            columns = 1;
        }
        else if (count <= 4)
        {
            columns = count;
        }
        else if (count <= 8)
        {
            columns = 4;
        }
        else
        {
            columns = Math.Min(6, Math.Max(4, (int)Math.Ceiling(Math.Sqrt(count * 1.35))));
        }

        var rows = Math.Max(1, (int)Math.Ceiling((double)count / columns));

        for (var i = 0; i < count; i++)
        {
            var item = _currentItems[i];
            var col = i % columns;
            var row = i / columns;
            var x = padX + col * (tileW + gapX);
            var y = padY + row * (tileH + gapY);

            var tile = BuildShortcutTile(item, i);
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            IconCanvas.Children.Add(tile);

            // Persiste posição calculada
            _ = _repository.SetDesktopIconPositionAsync(new DesktopIconPosition(item.Id, x, y));
        }

        // Calcula tamanho exato para abraçar os atalhos sem sobras
        var contentW = padX * 2 + columns * tileW + (columns - 1) * gapX;
        var contentH = padY * 2 + rows * tileH + (rows - 1) * gapY;
        var totalW = Math.Max(220, contentW);
        var totalH = headerH + contentH + (SearchBarRow.Visibility == Visibility.Visible ? 40 : 0);

        // Clampa ao monitor mais próximo
        var displayArea = DisplayArea.GetFromPoint(new PointInt32((int)_placement.X, (int)_placement.Y), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var posX = Math.Max(workArea.X + 8, Math.Min((int)_placement.X, workArea.X + workArea.Width - totalW - 8));
        var posY = Math.Max(workArea.Y + 8, Math.Min((int)_placement.Y, workArea.Y + workArea.Height - totalH - 8));

        _placement = _placement with { X = posX, Y = posY, Width = totalW, Height = totalH };
        AppWindow.MoveAndResize(new RectInt32(posX, posY, totalW, totalH));
        AppWindow.Show();

        await _repository.SetDesktopGroupPlacementAsync(_placement);
    }

    private void ResizeForAppFolder()
    {
        var displayArea = DisplayArea.GetFromPoint(new PointInt32((int)_placement.X, (int)_placement.Y), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var posX = Math.Max(workArea.X + 8, Math.Min((int)_placement.X, workArea.X + workArea.Width - (int)AppFolderTileWidth - 8));
        var posY = Math.Max(workArea.Y + 8, Math.Min((int)_placement.Y, workArea.Y + workArea.Height - (int)AppFolderTileHeight - 8));

        AppWindow.MoveAndResize(new RectInt32(posX, posY, (int)AppFolderTileWidth, (int)AppFolderTileHeight));
        AppWindow.Show();
    }

    // ---- Construção dos Ladrilhos de Atalhos ----

    private FrameworkElement BuildShortcutTile(MenuEntryViewModel entry, int index)
    {
        var border = new Border
        {
            Width = 76,
            Height = 84,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Background = new SolidColorBrush(Colors.Transparent),
            Padding = new Thickness(4),
            ContextFlyout = BuildItemContextFlyout(entry),
            CanDrag = true,
        };

        var stack = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        stack.Children.Add(new FontIcon
        {
            Glyph = entry.Glyph,
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = entry.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        border.Child = stack;

        // Clique simples seleciona (estilo Windows Explorer)
        border.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            {
                SelectIndex(index);
            }
        };

        // Duplo clique executa o atalho
        border.DoubleTapped += async (_, _) =>
        {
            await LaunchService.LaunchAsync(_repository, entry);
        };

        // Drag-and-drop para mover entre grupos
        border.DragStarting += (s, e) =>
        {
            e.Data.RequestedOperation = DataPackageOperation.Move;
            e.Data.SetData(DraggedItemFormat, entry.Id);
            e.Data.SetData(SourceGroupFormat, _groupId);
        };

        _tileBorders.Add(border);
        return border;
    }

    private MenuFlyout BuildItemContextFlyout(MenuEntryViewModel entry)
    {
        var flyout = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = "Abrir / Executar", FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        openItem.Click += async (_, _) => await LaunchService.LaunchAsync(_repository, entry);
        flyout.Items.Add(openItem);

        if (!string.IsNullOrWhiteSpace(entry.Path) && (System.IO.File.Exists(entry.Path) || System.IO.Directory.Exists(entry.Path)))
        {
            var openLocationItem = new MenuFlyoutItem { Text = "Abrir local do arquivo" };
            openLocationItem.Click += (_, _) =>
            {
                try
                {
                    var dir = System.IO.Directory.Exists(entry.Path) ? entry.Path : System.IO.Path.GetDirectoryName(entry.Path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{entry.Path}\"", UseShellExecute = true });
                    }
                }
                catch { }
            };
            flyout.Items.Add(openLocationItem);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var renameItem = new MenuFlyoutItem { Text = "Renomear" };
        renameItem.Click += async (_, _) => await PromptRenameAsync(entry);
        flyout.Items.Add(renameItem);

        var deleteItem = new MenuFlyoutItem { Text = "Remover deste grupo" };
        deleteItem.Click += async (_, _) =>
        {
            await _repository.DeleteAsync(entry.Id);
            await ReloadAsync();
        };
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private async Task PromptRenameAsync(MenuEntryViewModel entry)
    {
        var input = new TextBox { Text = entry.Name, Width = 260 };
        var dialog = new ContentDialog
        {
            Title = "Renomear atalho",
            Content = input,
            PrimaryButtonText = "Salvar",
            CloseButtonText = "Cancelar",
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            var raw = await _repository.GetByIdAsync(entry.Id);
            if (raw != null)
            {
                raw.Name = input.Text.Trim();
                await _repository.UpdateAsync(raw);
                await ReloadAsync();
            }
        }
    }

    // ---- Navegação de Teclado (Estilo Windows Explorer) ----

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= _tileBorders.Count)
        {
            _selectedIndex = -1;
            UpdateSelectionVisuals();
            return;
        }

        _selectedIndex = index;
        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        for (var i = 0; i < _tileBorders.Count; i++)
        {
            var isSel = i == _selectedIndex;
            _tileBorders[i].Background = isSel
                ? new SolidColorBrush(Color.FromArgb(50, 0, 120, 212))
                : new SolidColorBrush(Colors.Transparent);
            _tileBorders[i].BorderBrush = isSel
                ? new SolidColorBrush(Color.FromArgb(180, 0, 120, 212))
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isCtrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

        if (isCtrl && e.Key == VirtualKey.F)
        {
            OpenSearch();
            e.Handled = true;
            return;
        }

        if (_currentItems.Count == 0)
        {
            return;
        }

        var count = _currentItems.Count;
        var cols = Math.Max(1, Math.Min(count, 4));

        switch (e.Key)
        {
            case VirtualKey.Right:
                SelectIndex(_selectedIndex < 0 ? 0 : Math.Min(count - 1, _selectedIndex + 1));
                e.Handled = true;
                break;
            case VirtualKey.Left:
                SelectIndex(_selectedIndex < 0 ? 0 : Math.Max(0, _selectedIndex - 1));
                e.Handled = true;
                break;
            case VirtualKey.Down:
                SelectIndex(_selectedIndex < 0 ? 0 : Math.Min(count - 1, _selectedIndex + cols));
                e.Handled = true;
                break;
            case VirtualKey.Up:
                SelectIndex(_selectedIndex < 0 ? 0 : Math.Max(0, _selectedIndex - cols));
                e.Handled = true;
                break;
            case VirtualKey.Home:
                SelectIndex(0);
                e.Handled = true;
                break;
            case VirtualKey.End:
                SelectIndex(count - 1);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _currentItems.Count)
                {
                    await LaunchService.LaunchAsync(_repository, _currentItems[_selectedIndex]);
                    e.Handled = true;
                }
                break;
            case VirtualKey.Escape:
                SelectIndex(-1);
                e.Handled = true;
                break;
            default:
                // Type-ahead: salto direto digitando letra inicial
                var keyChar = (char)e.Key;
                if (char.IsLetterOrDigit(keyChar))
                {
                    var matchIdx = _currentItems.FindIndex(it => it.Name.StartsWith(keyChar.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (matchIdx >= 0)
                    {
                        SelectIndex(matchIdx);
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    // ---- Busca CTRL+F com Lista Suspensa Inteligente ----

    private void OpenSearch()
    {
        SearchBarRow.Visibility = Visibility.Visible;
        SearchBox.Text = string.Empty;
        SearchBox.Focus(FocusState.Programmatic);
        _ = AutoArrangeAndResizeAsync();
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        OpenSearch();
        args.Handled = true;
    }

    private void SearchMenu_Click(object sender, RoutedEventArgs e) => OpenSearch();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                sender.ItemsSource = null;
            }
            else
            {
                // Filtra pelo início do nome OU por qualquer parte do nome (substring case-insensitive)
                sender.ItemsSource = _currentItems
                    .Where(it => it.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }

    private async void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is MenuEntryViewModel chosen)
        {
            SearchBarRow.Visibility = Visibility.Collapsed;
            await LaunchService.LaunchAsync(_repository, chosen);
        }
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is MenuEntryViewModel chosen)
        {
            SearchBarRow.Visibility = Visibility.Collapsed;
            await LaunchService.LaunchAsync(_repository, chosen);
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var match = _currentItems.FirstOrDefault(it => it.Name.Contains(args.QueryText, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SearchBarRow.Visibility = Visibility.Collapsed;
                await LaunchService.LaunchAsync(_repository, match);
            }
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            SearchBarRow.Visibility = Visibility.Collapsed;
            RootGrid.Focus(FocusState.Programmatic);
            _ = AutoArrangeAndResizeAsync();
            e.Handled = true;
        }
    }

    // ---- Drag and Drop entre Grupos e do Windows Explorer ----

    private void IconCanvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(DraggedItemFormat) || e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        }
    }

    private async void IconCanvas_Drop(object sender, DragEventArgs e)
    {
        // 1. Movido de outro grupo do QuickStacks
        if (e.DataView.Contains(DraggedItemFormat))
        {
            var itemId = (string)await e.DataView.GetDataAsync(DraggedItemFormat);
            var sourceGroupId = e.DataView.Contains(SourceGroupFormat)
                ? (string)await e.DataView.GetDataAsync(SourceGroupFormat)
                : null;

            if (sourceGroupId != null && sourceGroupId != _groupId)
            {
                var item = await _repository.GetByIdAsync(itemId);
                if (item != null)
                {
                    // Transfere a posse do atalho para este grupo
                    item.ParentId = _groupId;
                    await _repository.UpdateAsync(item);

                    // Notifica o grupo de origem para se reorganizar
                    var sourceWindow = _openGroups.FirstOrDefault(g => g._groupId == sourceGroupId);
                    if (sourceWindow != null)
                    {
                        _ = sourceWindow.ReloadAsync();
                    }

                    // Reorganiza este grupo imediatamente
                    await ReloadAsync();
                }
            }
            return;
        }

        // 2. Solto de outra aplicação (Explorer / Desktop)
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var count = (await _repository.GetChildrenAsync(_groupId)).Count;

            foreach (var storageItem in items)
            {
                var name = storageItem.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? System.IO.Path.GetFileNameWithoutExtension(storageItem.Name)
                    : storageItem.Name;
                var shortcut = MenuItem.CreateShortcut(name, _groupId, MenuItemType.Shortcut, storageItem.Path, count++);
                await _repository.AddAsync(shortcut);
            }

            // Auto-organização e redimensionamento imediatos
            await ReloadAsync();
        }
    }

    // ---- Modo App Folder ----

    private void ApplyDisplayModeChrome()
    {
        var isAppFolder = _placement.DisplayMode == DesktopGroupDisplayMode.AppFolder;
        HeaderBar.Visibility = isAppFolder ? Visibility.Collapsed : Visibility.Visible;
        SearchBarRow.Visibility = Visibility.Collapsed;
        IconCanvas.Visibility = isAppFolder ? Visibility.Collapsed : Visibility.Visible;
        AppFolderRoot.Visibility = isAppFolder ? Visibility.Visible : Visibility.Collapsed;
        ToggleDisplayModeItem.Text = isAppFolder ? "Alternar para Painel" : "Alternar para App Folder";
    }

    private async void ToggleDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        var newMode = _placement.DisplayMode == DesktopGroupDisplayMode.AppFolder
            ? DesktopGroupDisplayMode.Panel
            : DesktopGroupDisplayMode.AppFolder;

        _placement = _placement with { DisplayMode = newMode };
        await _repository.SetDesktopGroupPlacementAsync(_placement);

        ApplyDisplayModeChrome();
        await ReloadAsync();
    }

    private void BuildAppFolderTile(IReadOnlyList<MenuItem> children)
    {
        AppFolderRoot.Children.Clear();

        var stack = new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };

        var plateSize = 76.0;
        var plate = new Border
        {
            Width = plateSize,
            Height = plateSize,
            CornerRadius = new CornerRadius(plateSize * 0.26),
            Padding = new Thickness(plateSize * 0.1),
            Background = new SolidColorBrush(Colors.Black) { Opacity = 0.25 },
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
            var entry = new MenuEntryViewModel(child);
            var cell = new FontIcon
            {
                Glyph = entry.Glyph,
                FontSize = plateSize * 0.16,
                Margin = new Thickness(1.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
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
                Foreground = new SolidColorBrush(Colors.White),
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
                Background = new SolidColorBrush(Color.FromArgb(255, 0x00, 0x78, 0xD4)), // Azul Windows Accent
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
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        AppFolderRoot.Children.Add(stack);
    }

    private void AppFolderRoot_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _sheetPopup ??= new PopupWindow(_repository);
        _ = _sheetPopup.NavigateToFolderAsync(_groupId, _groupName);
        _sheetPopup.Closed += (_, _) => _sheetPopup = null;
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        _sheetPopup.ActivateCentered(displayArea);
    }

    // ---- Menu de Contexto da Superfície e Comandos ----

    private async void AddShortcut_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Nome do atalho" };
        var targetBox = new TextBox { PlaceholderText = @"C:\caminho\app.exe ou shell:::{...}" };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(targetBox);

        var dialog = new ContentDialog
        {
            Title = "Novo atalho",
            Content = panel,
            PrimaryButtonText = "Adicionar",
            CloseButtonText = "Cancelar",
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(targetBox.Text))
        {
            var target = targetBox.Text.Trim();
            var name = string.IsNullOrWhiteSpace(nameBox.Text) ? System.IO.Path.GetFileNameWithoutExtension(target) : nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = target;

            var siblingCount = (await _repository.GetChildrenAsync(_groupId)).Count;
            var shortcut = MenuItem.CreateShortcut(name, _groupId, MenuItemType.Shortcut, target, siblingCount);
            await _repository.AddAsync(shortcut);
            await ReloadAsync();
        }
    }

    private async void SortByName_Click(object sender, RoutedEventArgs e)
    {
        var items = (await _repository.GetChildrenAsync(_groupId)).OrderBy(i => i.Name).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
            await _repository.UpdateAsync(items[i]);
        }
        await ReloadAsync();
    }

    private async void SortByType_Click(object sender, RoutedEventArgs e)
    {
        var items = (await _repository.GetChildrenAsync(_groupId)).OrderBy(i => i.Type).ThenBy(i => i.Name).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
            await _repository.UpdateAsync(items[i]);
        }
        await ReloadAsync();
    }

    private async void SortByRecent_Click(object sender, RoutedEventArgs e)
    {
        var items = (await _repository.GetChildrenAsync(_groupId)).OrderByDescending(i => i.LaunchCount).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].SortOrder = i;
            await _repository.UpdateAsync(items[i]);
        }
        await ReloadAsync();
    }

    private void EditStructure_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var editor = new EditorWindow(app.MenuRepository, app.ExportService, app.LnkImportService);
        editor.Activate();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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

        _placement = _placement with { X = sender.Position.X, Y = sender.Position.Y };
        _ = _repository.SetDesktopGroupPlacementAsync(_placement);
    }
}
