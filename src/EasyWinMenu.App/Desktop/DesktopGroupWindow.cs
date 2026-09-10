using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Cursors = System.Windows.Input.Cursors;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using DragEventArgs = System.Windows.DragEventArgs;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using ItemsControl = System.Windows.Controls.ItemsControl;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Separator = System.Windows.Controls.Separator;
using StackPanel = System.Windows.Controls.StackPanel;
using Style = System.Windows.Style;
using TextAlignment = System.Windows.TextAlignment;
using TextBlock = System.Windows.Controls.TextBlock;
using TextWrapping = System.Windows.TextWrapping;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopGroupWindow : Window
{
    private const double MinIconScale = 0.5;
    private const double MaxIconScale = 3.0;
    private const double TileSize = 80;

    private readonly MenuCategory _category;
    private MenuTheme _theme;
    private readonly IconCacheService _iconCache;
    private readonly Action<LaunchItem> _onExecute;
    private readonly Action<MenuCategory> _onLayoutChanged;
    private readonly Action<MenuCategory> _onDeleteRequested;
    private readonly ScaleTransform _zoomTransform;
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _openSubfolders = new();
    private readonly Dictionary<object, FrameworkElement> _tilesByEntry = new();
    private readonly HashSet<object> _selectedEntries = new();

    private Canvas _canvas = null!;
    private Border _border = null!;
    private Border _header = null!;
    private Border _resizeGrip = null!;
    private TextBlock _headerText = null!;
    private TextBlock _collapseGlyph = null!;

    private bool _resizingGroup;
    private System.Windows.Point _resizeStart;
    private double _startWidth;
    private double _startHeight;

    public DesktopGroupWindow(
        MenuCategory category,
        MenuTheme theme,
        IconCacheService iconCache,
        Action<LaunchItem> onExecute,
        Action<MenuCategory> onLayoutChanged,
        Action<MenuCategory> onDeleteRequested)
    {
        _category = category;
        _theme = theme;
        _iconCache = iconCache;
        _onExecute = onExecute;
        _onLayoutChanged = onLayoutChanged;
        _onDeleteRequested = onDeleteRequested;
        _zoomTransform = new ScaleTransform(category.DesktopIconScale, category.DesktopIconScale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        Background = Brushes.Transparent;
        Left = category.DesktopX;
        Top = category.DesktopY;
        Width = category.DesktopWidth;
        Height = category.DesktopHeight;
        AllowDrop = true;

        Content = BuildContent();
        SetCollapsed(_category.IsCollapsed);

        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        Drop += OnDrop;
    }

    public bool IsCollapsed => _category.IsCollapsed;

    public void SetCollapsedExternally(bool collapsed)
    {
        SetCollapsed(collapsed);
        _onLayoutChanged(_category);
    }

    public void MoveToCenterKeepingSize(double centerX, double centerY, double cascadeOffset)
    {
        Left = centerX - (_category.DesktopWidth / 2) + cascadeOffset;
        Top = centerY - (_category.DesktopHeight / 2) + cascadeOffset;
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _onLayoutChanged(_category);
    }

    private FrameworkElement BuildContent()
    {
        var panel = new DockPanel();

        _headerText = new TextBlock
        {
            Text = _category.Name,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.TitleFontFamily),
            FontSize = _theme.TitleFontSize,
            FontWeight = _theme.TitleBold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _collapseGlyph = new TextBlock
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = _theme.TitleFontSize,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        _collapseGlyph.MouseLeftButtonDown += (_, e) =>
        {
            ToggleCollapse();
            e.Handled = true;
        };
        DockPanel.SetDock(_collapseGlyph, Dock.Right);

        var headerPanel = new DockPanel();
        headerPanel.Children.Add(_collapseGlyph);
        headerPanel.Children.Add(_headerText);

        _header = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.SizeAll,
            Child = headerPanel,
            ContextMenu = BuildHeaderContextMenu()
        };
        _header.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
        DockPanel.SetDock(_header, Dock.Top);
        panel.Children.Add(_header);

        _resizeGrip = new Border
        {
            Width = 14,
            Height = 14,
            Background = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.SizeNWSE
        };
        _resizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
        _resizeGrip.MouseMove += OnResizeGripMouseMove;
        _resizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;
        DockPanel.SetDock(_resizeGrip, Dock.Bottom);
        panel.Children.Add(_resizeGrip);

        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            RenderTransform = _zoomTransform,
            RenderTransformOrigin = new System.Windows.Point(0, 0),
            ContextMenu = BuildCanvasContextMenu()
        };
        _canvas.MouseLeftButtonDown += (_, _) => ClearSelection();

        PopulateTiles();

        panel.Children.Add(_canvas);

        _border = new Border
        {
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_theme.CornerRadius),
            ClipToBounds = true,
            Child = panel
        };
        ApplyBackground();
        ApplyHeaderBackground();
        UpdateCollapseGlyph();

        if (_theme.ShowShadow)
        {
            _border.Effect = new DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Colors.Black
            };
        }

        return _border;
    }

    private void ApplyBackground()
    {
        if (_category.AreaTransparent)
        {
            _border.Background = Brushes.Transparent;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath) && File.Exists(_category.DesktopBackgroundImagePath))
        {
            _border.Background = new ImageBrush(new BitmapImage(new Uri(_category.DesktopBackgroundImagePath)))
            {
                Stretch = Stretch.UniformToFill
            };
            return;
        }

        _border.Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, _theme.Opacity);
    }

    private void ApplyHeaderBackground()
    {
        _header.Background = _category.TitleTransparent
            ? Brushes.Transparent
            : ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0);
    }

    private void EnsureThemeOverride()
    {
        if (_category.ThemeOverride is null)
        {
            _category.ThemeOverride = _theme.Clone();
            _theme = _category.ThemeOverride;
        }
    }

    private static string ComputeContrastingTextColor(Color background)
    {
        var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
        return luminance > 0.55 ? "#000000" : "#FFFFFF";
    }

    private ContextMenu CreateContextMenuShell()
    {
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.Resources["EasyWinMenuContextMenuStyle"],
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, 1.0),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1)
        };
        MenuThemeProperties.SetPanelCornerRadius(menu, new CornerRadius(_theme.CornerRadius));
        return menu;
    }

    private ContextMenu BuildHeaderContextMenu()
    {
        var menu = CreateContextMenuShell();

        AddMenuItem(menu, LocalizationService.Get("group.rename"), OnRenameClick);
        AddMenuItem(menu, _category.IsCollapsed ? LocalizationService.Get("group.expand") : LocalizationService.Get("group.collapse"), ToggleCollapse);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, LocalizationService.Get("group.backgroundColor"), OnChangeColorClick);
        AddMenuItem(menu, LocalizationService.Get("group.backgroundImage"), OnChangeBackgroundImageClick);
        AddMenuItem(menu, LocalizationService.Get("group.removeBackgroundImage"), OnClearBackgroundImageClick);
        AddMenuItem(menu, (_category.AreaTransparent ? "✓ " : string.Empty) + LocalizationService.Get("group.areaTransparency"), ToggleAreaTransparency);
        AddMenuItem(menu, (_category.TitleTransparent ? "✓ " : string.Empty) + LocalizationService.Get("group.titleTransparency"), ToggleTitleTransparency);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, LocalizationService.Get("group.remove"), OnDeleteGroupClick);

        return menu;
    }

    private ContextMenu BuildCanvasContextMenu()
    {
        var menu = CreateContextMenuShell();

        var newMenu = CreateMenuItem(LocalizationService.Get("group.newMenu"));
        AddMenuItem(newMenu, LocalizationService.Get("group.newFolder"), CreateSubfolder);
        AddMenuItem(newMenu, LocalizationService.Get("group.newTextFile"), CreateTextFile);
        menu.Items.Add(newMenu);
        menu.Items.Add(new Separator());

        AddMenuItem(menu, LocalizationService.Get("group.arrangeIcons"), ArrangeIconsAutomatically);

        var sortMenu = CreateMenuItem(LocalizationService.Get("group.sortBy"));
        AddMenuItem(sortMenu, LocalizationService.Get("group.sortByName"), SortByName);
        AddMenuItem(sortMenu, LocalizationService.Get("group.sortByType"), SortByType);
        menu.Items.Add(sortMenu);

        var sizeMenu = CreateMenuItem(LocalizationService.Get("group.iconSize"));
        AddMenuItem(sizeMenu, LocalizationService.Get("group.iconSizeSmall"), () => SetIconScale(0.75));
        AddMenuItem(sizeMenu, LocalizationService.Get("group.iconSizeMedium"), () => SetIconScale(1.0));
        AddMenuItem(sizeMenu, LocalizationService.Get("group.iconSizeLarge"), () => SetIconScale(1.5));
        menu.Items.Add(sizeMenu);

        return menu;
    }

    private ContextMenu BuildItemTileContextMenu(LaunchItem item)
    {
        var menu = CreateContextMenuShell();
        AddMenuItem(menu, LocalizationService.Get("item.open"), () => _onExecute(item));
        AddMenuItem(menu, LocalizationService.Get("item.rename"), () => RenameItem(item));
        AddMenuItem(menu, LocalizationService.Get("item.removeFromGroup"), () => RemoveItem(item));
        return menu;
    }

    private ContextMenu BuildFolderTileContextMenu(MenuCategory folder)
    {
        var menu = CreateContextMenuShell();
        AddMenuItem(menu, LocalizationService.Get("item.open"), () => OpenSubfolder(folder));
        AddMenuItem(menu, LocalizationService.Get("item.rename"), () => RenameFolder(folder));
        AddMenuItem(menu, LocalizationService.Get("item.removeFromGroup"), () => RemoveFolder(folder));
        return menu;
    }

    private MenuItem CreateMenuItem(string header)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)Application.Current.Resources["EasyWinMenuMenuItemStyle"],
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            Padding = new Thickness(_theme.ItemPadding, _theme.ItemPadding / 2, _theme.ItemPadding, _theme.ItemPadding / 2)
        };
        MenuThemeProperties.SetHighlightBrush(item, ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0));
        MenuThemeProperties.SetHighlightCornerRadius(item, new CornerRadius(4));
        MenuThemeProperties.SetPanelCornerRadius(item, new CornerRadius(_theme.CornerRadius));
        return item;
    }

    private void AddMenuItem(ItemsControl parent, string header, Action handler)
    {
        var item = CreateMenuItem(header);
        item.Click += (_, _) => handler();
        parent.Items.Add(item);
    }

    private void CreateSubfolder()
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.folderNamePrompt"), string.Empty);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _category.Categories.Add(new MenuCategory { Name = prompt.Value });
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void CreateTextFile()
    {
        var directory = GetGroupFilesDirectory();
        Directory.CreateDirectory(directory);
        var fileName = GetAvailableFileName(directory, LocalizationService.Get("group.newTextFileName"), ".txt");
        var fullPath = Path.Combine(directory, fileName);
        File.WriteAllText(fullPath, string.Empty);

        var item = new LaunchItem
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            Type = LaunchItemType.File,
            Target = fullPath,
            IsDesktopPinned = true
        };

        _category.Items.Add(item);
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private string GetGroupFilesDirectory()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyWinMenu", "GroupFiles");
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Concat(_category.Name.Select(c => invalidChars.Contains(c) ? '_' : c));
        return Path.Combine(root, safeName);
    }

    private static string GetAvailableFileName(string directory, string baseName, string extension)
    {
        var candidate = baseName + extension;
        var counter = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{baseName} ({counter}){extension}";
            counter++;
        }

        return candidate;
    }

    private void RenameItem(LaunchItem item)
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("item.renamePrompt"), item.Name);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        item.Name = prompt.Value;
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void RemoveItem(LaunchItem item)
    {
        var confirmed = MessageBox.Show(
            this,
            LocalizationService.Format("item.removeConfirm", item.Name),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        _category.Items.Remove(item);
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void RenameFolder(MenuCategory folder)
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.folderNamePrompt"), folder.Name);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        folder.Name = prompt.Value;
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void RemoveFolder(MenuCategory folder)
    {
        var confirmed = MessageBox.Show(
            this,
            LocalizationService.Format("item.removeConfirm", folder.Name),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteFolder(folder);
    }

    private void DeleteFolder(MenuCategory folder)
    {
        if (_openSubfolders.TryGetValue(folder, out var openWindow))
        {
            openWindow.Close();
            _openSubfolders.Remove(folder);
        }

        _category.Categories.Remove(folder);
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void OpenSubfolder(MenuCategory folder)
    {
        if (_openSubfolders.TryGetValue(folder, out var existing))
        {
            existing.Activate();
            return;
        }

        var theme = folder.ThemeOverride ?? _theme;
        var window = new DesktopGroupWindow(
            folder,
            theme,
            _iconCache,
            _onExecute,
            _ => _onLayoutChanged(_category),
            DeleteFolder);

        window.Closed += (_, _) => _openSubfolders.Remove(folder);
        _openSubfolders[folder] = window;
        window.Show();
    }

    private void OnRenameClick()
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.namePrompt"), _category.Name);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _category.Name = prompt.Value;
        _headerText.Text = prompt.Value;
        _onLayoutChanged(_category);
    }

    private void OnChangeColorClick()
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        var current = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;
        dialog.Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        EnsureThemeOverride();
        var newColor = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
        _category.ThemeOverride!.BackgroundColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        _category.ThemeOverride.TextColor = ComputeContrastingTextColor(newColor);
        ApplyThemeChange();
    }

    private void ToggleAreaTransparency()
    {
        _category.AreaTransparent = !_category.AreaTransparent;
        ApplyBackground();
        _header.ContextMenu = BuildHeaderContextMenu();
        _onLayoutChanged(_category);
    }

    private void ToggleTitleTransparency()
    {
        _category.TitleTransparent = !_category.TitleTransparent;
        ApplyHeaderBackground();
        _header.ContextMenu = BuildHeaderContextMenu();
        _onLayoutChanged(_category);
    }

    private void ToggleCollapse()
    {
        SetCollapsed(!_category.IsCollapsed);
        _header.ContextMenu = BuildHeaderContextMenu();
        _onLayoutChanged(_category);
    }

    private void SetCollapsed(bool collapsed)
    {
        _category.IsCollapsed = collapsed;
        _canvas.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _resizeGrip.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        if (collapsed)
        {
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            Height = _category.DesktopHeight;
        }

        UpdateCollapseGlyph();
    }

    private void UpdateCollapseGlyph()
    {
        _collapseGlyph.Text = _category.IsCollapsed ? "" : "";
    }

    private void OnChangeBackgroundImageClick()
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationService.Get("group.imageFilter")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _category.DesktopBackgroundImagePath = dialog.FileName;
        ApplyBackground();
        _onLayoutChanged(_category);
    }

    private void OnClearBackgroundImageClick()
    {
        _category.DesktopBackgroundImagePath = null;
        ApplyBackground();
        _onLayoutChanged(_category);
    }

    private void OnDeleteGroupClick()
    {
        var confirmed = MessageBox.Show(
            this,
            LocalizationService.Format("group.deleteConfirm", _category.Name),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed == MessageBoxResult.Yes)
        {
            _onDeleteRequested(_category);
        }
    }

    private void ApplyThemeChange()
    {
        ApplyBackground();
        ApplyHeaderBackground();
        _headerText.Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0);
        _collapseGlyph.Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0);
        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        var dropPoint = e.GetPosition(_canvas);
        var offset = 0.0;

        foreach (var path in paths)
        {
            var item = new LaunchItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Type = InferType(path),
                Target = path,
                IsDesktopPinned = true,
                DesktopIconX = dropPoint.X + offset,
                DesktopIconY = dropPoint.Y + offset
            };

            _category.Items.Add(item);
            AddTile(item, item.DesktopIconX.Value, item.DesktopIconY.Value);
            offset += 16;
        }

        _onLayoutChanged(_category);
    }

    private static LaunchItemType InferType(string path)
    {
        if (Directory.Exists(path))
        {
            return LaunchItemType.Folder;
        }

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
            ? LaunchItemType.Application
            : LaunchItemType.File;
    }

    private void PopulateTiles()
    {
        _canvas.Children.Clear();
        _tilesByEntry.Clear();
        _selectedEntries.Clear();
        var index = 0;
        foreach (var folder in _category.Categories)
        {
            AddFolderTile(folder, folder.IconX ?? (index % 3) * TileSize, folder.IconY ?? (index / 3) * TileSize);
            index++;
        }

        foreach (var item in _category.Items.Where(i => i.IsDesktopPinned))
        {
            AddTile(item, item.DesktopIconX ?? (index % 3) * TileSize, item.DesktopIconY ?? (index / 3) * TileSize);
            index++;
        }
    }

    private void ArrangeIconsAutomatically()
    {
        IEnumerable<object> entries = _category.Categories
            .Cast<object>()
            .Concat(_category.Items.Where(i => i.IsDesktopPinned));
        ArrangeInGrid(entries);
    }

    private void SortByName()
    {
        var folders = _category.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        var items = _category.Items.Where(i => i.IsDesktopPinned).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
        ArrangeInGrid(folders.Cast<object>().Concat(items));
    }

    private void SortByType()
    {
        var folders = _category.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        var items = _category.Items.Where(i => i.IsDesktopPinned)
            .OrderBy(i => i.Type)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
        ArrangeInGrid(folders.Cast<object>().Concat(items));
    }

    private void ArrangeInGrid(IEnumerable<object> orderedEntries)
    {
        var columns = Math.Max(1, (int)(_category.DesktopWidth / TileSize));
        var index = 0;
        foreach (var entry in orderedEntries)
        {
            var x = (index % columns) * TileSize;
            var y = (index / columns) * TileSize;
            switch (entry)
            {
                case LaunchItem item:
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    break;
                case MenuCategory folder:
                    folder.IconX = x;
                    folder.IconY = y;
                    break;
            }

            index++;
        }

        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void SetIconScale(double scale)
    {
        _category.DesktopIconScale = scale;
        _zoomTransform.ScaleX = scale;
        _zoomTransform.ScaleY = scale;
        _onLayoutChanged(_category);
    }

    private void AttachTileBehavior(FrameworkElement tile, object entry, Action onOpen, Action<double, double> onMoved)
    {
        _tilesByEntry[entry] = tile;

        System.Windows.Point dragStart = default;
        System.Windows.Point tileStart = default;
        var dragging = false;

        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                onOpen();
                e.Handled = true;
                return;
            }

            SelectEntry(entry, Keyboard.Modifiers == ModifierKeys.Control);

            dragging = true;
            dragStart = e.GetPosition(_canvas);
            tileStart = new System.Windows.Point(Canvas.GetLeft(tile), Canvas.GetTop(tile));
            tile.CaptureMouse();
            e.Handled = true;
        };

        tile.MouseMove += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var current = e.GetPosition(_canvas);
            var delta = current - dragStart;
            Canvas.SetLeft(tile, tileStart.X + delta.X);
            Canvas.SetTop(tile, tileStart.Y + delta.Y);
        };

        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            tile.ReleaseMouseCapture();
            onMoved(Canvas.GetLeft(tile), Canvas.GetTop(tile));
        };
    }

    private void SelectEntry(object entry, bool additive)
    {
        if (additive)
        {
            if (!_selectedEntries.Remove(entry))
            {
                _selectedEntries.Add(entry);
            }
        }
        else
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(entry);
        }

        RefreshSelectionVisuals();
    }

    private void ClearSelection()
    {
        if (_selectedEntries.Count == 0)
        {
            return;
        }

        _selectedEntries.Clear();
        RefreshSelectionVisuals();
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var (entry, tile) in _tilesByEntry)
        {
            if (tile is Panel panel)
            {
                panel.Background = _selectedEntries.Contains(entry)
                    ? ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0)
                    : Brushes.Transparent;
            }
        }
    }

    private void RemoveSelectedEntries()
    {
        if (_selectedEntries.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", _selectedEntries.Select(GetEntryName));
        var confirmed = MessageBox.Show(
            this,
            LocalizationService.Format("item.removeConfirm", names),
            LocalizationService.Get("common.appName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var entry in _selectedEntries.ToList())
        {
            switch (entry)
            {
                case LaunchItem item:
                    _category.Items.Remove(item);
                    break;
                case MenuCategory folder:
                    DeleteFolder(folder);
                    break;
            }
        }

        PopulateTiles();
        _onLayoutChanged(_category);
    }

    private void RenameSelectedEntry()
    {
        switch (_selectedEntries.FirstOrDefault())
        {
            case LaunchItem item:
                RenameItem(item);
                break;
            case MenuCategory folder:
                RenameFolder(folder);
                break;
        }
    }

    private static string GetEntryName(object entry) => entry switch
    {
        LaunchItem item => item.Name,
        MenuCategory folder => folder.Name,
        _ => string.Empty
    };

    private void AddTile(LaunchItem item, double x, double y)
    {
        var tile = BuildTile(item);
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        _canvas.Children.Add(tile);
    }

    private FrameworkElement BuildTile(LaunchItem item)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = TileSize - 8,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ContextMenu = BuildItemTileContextMenu(item)
        };

        var icon = _iconCache.GetIcon(item.IconOverridePath ?? item.Target);
        if (icon is not null)
        {
            stack.Children.Add(new Image
            {
                Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                Width = _theme.IconSize * 1.6,
                Height = _theme.IconSize * 1.6,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = item.Name,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        AttachTileBehavior(
            stack,
            item,
            () => _onExecute(item),
            (x, y) =>
            {
                item.DesktopIconX = x;
                item.DesktopIconY = y;
                _onLayoutChanged(_category);
            });

        return stack;
    }

    private void AddFolderTile(MenuCategory folder, double x, double y)
    {
        var tile = BuildFolderTile(folder);
        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        _canvas.Children.Add(tile);
    }

    private FrameworkElement BuildFolderTile(MenuCategory folder)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = TileSize - 8,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ContextMenu = BuildFolderTileContextMenu(folder)
        };

        stack.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = _theme.IconSize * 1.6,
            Foreground = ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = folder.Name,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        AttachTileBehavior(
            stack,
            folder,
            () => OpenSubfolder(folder),
            (x, y) =>
            {
                folder.IconX = x;
                folder.IconY = y;
                _onLayoutChanged(_category);
            });

        return stack;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _onLayoutChanged(_category);
    }

    private void OnResizeGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _resizingGroup = true;
        _resizeStart = PointToScreen(e.GetPosition(this));
        _startWidth = Width;
        _startHeight = Height;
        ((UIElement)sender).CaptureMouse();
    }

    private void OnResizeGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizingGroup)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        Width = Math.Max(140, _startWidth + (current.X - _resizeStart.X));
        Height = Math.Max(120, _startHeight + (current.Y - _resizeStart.Y));
    }

    private void OnResizeGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizingGroup)
        {
            return;
        }

        _resizingGroup = false;
        ((UIElement)sender).ReleaseMouseCapture();
        _category.DesktopWidth = Width;
        _category.DesktopHeight = Height;
        _onLayoutChanged(_category);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        ApplyZoomDelta(e.Delta > 0 ? 0.1 : -0.1);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                ApplyZoomDelta(0.1);
                e.Handled = true;
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                ApplyZoomDelta(-0.1);
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            RemoveSelectedEntries();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && _selectedEntries.Count == 1)
        {
            RenameSelectedEntry();
            e.Handled = true;
        }
    }

    private void ApplyZoomDelta(double delta)
    {
        var newScale = Math.Clamp(_category.DesktopIconScale + delta, MinIconScale, MaxIconScale);
        if (Math.Abs(newScale - _category.DesktopIconScale) < 0.001)
        {
            return;
        }

        SetIconScale(newScale);
    }
}
