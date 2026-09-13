using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.App.Theming;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;
using Border = System.Windows.Controls.Border;
using Brush = System.Windows.Media.Brush;
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
using ContentControl = System.Windows.Controls.ContentControl;
using Grid = System.Windows.Controls.Grid;
using MessageBox = System.Windows.MessageBox;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using Separator = System.Windows.Controls.Separator;
using Slider = System.Windows.Controls.Slider;
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
    private const double MinCanvasPadding = 8;
    private const double CanvasPaddingRatio = 0.05;
    private const double FolderTapTolerance = 5;

    private readonly MenuCategory _category;
    private MenuTheme _theme;
    private readonly IconCacheService _iconCache;
    private readonly Action<LaunchItem> _onExecute;
    private readonly Action<MenuCategory> _onLayoutChanged;
    private readonly Action<MenuCategory> _onDeleteRequested;
    private readonly IDesktopGroupCommands? _commands;
    private readonly ScaleTransform _zoomTransform;
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _openSubfolders = new();
    private readonly Dictionary<object, FrameworkElement> _tilesByEntry = new();
    private readonly HashSet<object> _selectedEntries = new();

    private Canvas _canvas = null!;
    private Border _border = null!;
    private Grid _root = null!;
    private ContentControl _folderHost = null!;
    private GroupOverlayWindow? _overlay;
    private Border _header = null!;
    private Border _resizeGrip = null!;
    private TextBlock _headerText = null!;
    private Border _collapseGlyph = null!;
    private Border _menuButton = null!;

    private bool _resizingGroup;
    private bool _draggingFolderTile;
    private System.Windows.Point _folderDragOrigin;
    private System.Windows.Point _resizeStart;
    private double _startWidth;
    private double _startHeight;

    public DesktopGroupWindow(
        MenuCategory category,
        MenuTheme theme,
        IconCacheService iconCache,
        Action<LaunchItem> onExecute,
        Action<MenuCategory> onLayoutChanged,
        Action<MenuCategory> onDeleteRequested,
        IDesktopGroupCommands? commands = null)
    {
        _commands = commands;
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

        Content = BuildRoot();
        SetCollapsed(_category.IsCollapsed);

        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
        Drop += OnDrop;
        Closed += (_, _) => _overlay?.Close();
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

    /// <summary>
    /// Both looks are built up front and swapped by visibility, so the panel canvas,
    /// header and grip stay alive (and keep working) while the folder look is showing.
    /// </summary>
    private FrameworkElement BuildRoot()
    {
        _folderHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 10, 10, 6),
            Visibility = Visibility.Collapsed
        };
        _folderHost.MouseLeftButtonDown += OnFolderTileMouseDown;
        _folderHost.MouseMove += OnFolderTileMouseMove;
        _folderHost.MouseLeftButtonUp += OnFolderTileMouseUp;

        _root = new Grid { Background = Brushes.Transparent };
        _root.Children.Add(BuildContent());
        _root.Children.Add(_folderHost);
        return _root;
    }

    private bool IsAppFolder => _category.DisplayMode == DesktopGroupDisplayMode.AppFolder;

    /// <summary>
    /// Icons keep at least a twentieth of the group clear on every side, so nothing
    /// sits flush against the title bar or the border however the group is resized.
    /// </summary>
    private double PaddingX => Math.Max(MinCanvasPadding, _category.DesktopWidth * CanvasPaddingRatio);

    private double PaddingY => Math.Max(MinCanvasPadding, _category.DesktopHeight * CanvasPaddingRatio);

    private void ApplyDisplayMode()
    {
        if (IsAppFolder)
        {
            // A ContextMenu cannot be shared between two owners, so the tile gets its own.
            _folderHost.ContextMenu = BuildHeaderContextMenu();
            RefreshFolderTile();
            _border.Visibility = Visibility.Collapsed;
            _folderHost.Visibility = Visibility.Visible;
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }

        _folderHost.Visibility = Visibility.Collapsed;
        _border.Visibility = Visibility.Visible;

        // SizeToContent has to be relaxed before Width and Height are assigned:
        // while it is still automatic the assignment is discarded, which is how the
        // panel used to come back from the folder look wearing the tile's size.
        SizeToContent = _category.IsCollapsed ? SizeToContent.Height : SizeToContent.Manual;
        Width = _category.DesktopWidth;

        if (!_category.IsCollapsed)
        {
            Height = _category.DesktopHeight;
        }
    }

    private void RefreshFolderTile()
    {
        if (!IsAppFolder)
        {
            return;
        }

        _folderHost.Content = AppFolderTile.Build(_category, _theme, _iconCache);
    }

    private void ToggleDisplayMode()
    {
        if (!IsAppFolder)
        {
            // Remember where the panel stood so the tile takes its top-left corner.
            _category.PanelX = Left;
            _category.PanelY = Top;
        }

        _category.DisplayMode = IsAppFolder ? DesktopGroupDisplayMode.Panel : DesktopGroupDisplayMode.AppFolder;
        _header.ContextMenu = BuildHeaderContextMenu();
        ApplyDisplayMode();
        _onLayoutChanged(_category);
    }

    private void ToggleBadge()
    {
        _category.ShowBadge = !_category.ShowBadge;
        _header.ContextMenu = BuildHeaderContextMenu();
        ApplyDisplayMode();
        _onLayoutChanged(_category);
    }

    private void OnFolderTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingFolderTile = true;
        _folderDragOrigin = PointToScreen(e.GetPosition(this));
        _folderHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnFolderTileMouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingFolderTile)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        Left += current.X - _folderDragOrigin.X;
        Top += current.Y - _folderDragOrigin.Y;
        _folderDragOrigin = PointToScreen(e.GetPosition(this));
    }

    private void OnFolderTileMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingFolderTile)
        {
            return;
        }

        _draggingFolderTile = false;
        _folderHost.ReleaseMouseCapture();

        // A press that barely moved is a tap, not a drag: snap back and open the sheet.
        if (Math.Abs(Left - _category.DesktopX) < FolderTapTolerance
            && Math.Abs(Top - _category.DesktopY) < FolderTapTolerance)
        {
            Left = _category.DesktopX;
            Top = _category.DesktopY;

            // Let this click finish before another window takes focus, otherwise the
            // captured mouse-up drags activation back here and the sheet closes at once.
            Dispatcher.BeginInvoke(OpenOverlay, DispatcherPriority.Input);
            return;
        }

        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _onLayoutChanged(_category);
    }

    private void OpenOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.Activate();
            return;
        }

        _overlay = new GroupOverlayWindow(_category, _theme, _iconCache, _onExecute, this);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        _overlay.Activate();
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

        _collapseGlyph = BuildHeaderButton(ToggleCollapse);
        DockPanel.SetDock(_collapseGlyph, Dock.Right);

        _menuButton = BuildHeaderButton(OpenHeaderMenu);
        _menuButton.Child = AppIcons.Create("IconMore", ThemeBrushes.CreateBrush(_theme.TextColor, 1.0), _theme.TitleFontSize + 3);
        _menuButton.ToolTip = LocalizationService.Get("group.menu");
        DockPanel.SetDock(_menuButton, Dock.Right);

        var headerPanel = new DockPanel();
        headerPanel.Children.Add(_menuButton);
        headerPanel.Children.Add(_collapseGlyph);
        headerPanel.Children.Add(_headerText);

        _header = new Border
        {
            Padding = new Thickness(8, 4, 4, 4),
            Child = headerPanel,
            ContextMenu = BuildHeaderContextMenu()
        };
        _header.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
        DockPanel.SetDock(_header, Dock.Top);
        panel.Children.Add(_header);

        _resizeGrip = new Border
        {
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.SizeNWSE,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M11 3L3 11 M11 7L7 11 M11 11L10.5 11.5"),
                Stroke = ThemeBrushes.CreateBrush(_theme.TextColor, 0.5),
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
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

        _border.Effect = BuildGroupShadow(_theme);

        return _border;
    }

    /// <summary>A flat square target in the title bar that does not start a window drag.</summary>
    private Border BuildHeaderButton(Action onClick)
    {
        var button = new Border
        {
            Width = _theme.TitleFontSize + 14,
            Height = _theme.TitleFontSize + 14,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        var hover = ThemeBrushes.CreateBrush(_theme.TextColor, 0.14);
        button.MouseEnter += (_, _) => button.Background = hover;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };

        return button;
    }

    /// <summary>
    /// Hands the icon over to Explorer's own menu, third-party entries included. It is
    /// opened from the dispatcher so our themed menu has closed first: the shell menu
    /// runs its own message loop and needs the foreground to itself.
    /// </summary>
    private void ShowStandardMenu(LaunchItem item)
    {
        if (!ShellCommands.TryResolveTarget(item, out var path))
        {
            return;
        }

        var point = System.Windows.Forms.Cursor.Position;
        var extendedVerbs = Keyboard.Modifiers == ModifierKeys.Shift;

        Dispatcher.BeginInvoke(
            () => ShellContextMenu.TryShow(this, path, point, extendedVerbs),
            DispatcherPriority.ApplicationIdle);
    }

    private void OpenHeaderMenu()
    {
        // Rebuilt per click so checkmarks and enabled states reflect the current group.
        var menu = BuildHeaderContextMenu();
        menu.PlacementTarget = _menuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>The configured shadow, or null when it is switched off.</summary>
    internal static DropShadowEffect? BuildGroupShadow(MenuTheme theme)
    {
        if (!theme.ShowShadow)
        {
            return null;
        }

        return new DropShadowEffect
        {
            BlurRadius = theme.ShadowBlurRadius,
            ShadowDepth = theme.ShadowDepth,
            Direction = theme.ShadowDirection,
            Opacity = theme.ShadowOpacity,
            Color = Colors.Black
        };
    }

    private void ApplyBackground()
    {
        if (_category.AreaOpacity <= 0)
        {
            _border.Background = Brushes.Transparent;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath) && File.Exists(_category.DesktopBackgroundImagePath))
        {
            _border.Background = new ImageBrush(new BitmapImage(new Uri(_category.DesktopBackgroundImagePath)))
            {
                Stretch = Stretch.UniformToFill,
                Opacity = _category.AreaOpacity
            };
            return;
        }

        // The group slider rides on top of the theme's own opacity rather than replacing it.
        _border.Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, _theme.Opacity * _category.AreaOpacity);
    }

    private void ApplyHeaderBackground()
    {
        _header.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, _category.TitleOpacity);
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
        return RelativeLuminance(background) > 0.55 ? "#000000" : "#FFFFFF";
    }

    private static double RelativeLuminance(Color color)
    {
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
    }

    /// <summary>
    /// Label colour is derived, never taken from the theme: the text sits on whatever
    /// the group is painted with, and a fixed colour disappears as soon as that changes.
    /// A background image can be anything, so those labels get white text and lean on
    /// the shadow underneath them instead.
    /// </summary>
    private Brush TileTextBrush()
    {
        if (!string.IsNullOrWhiteSpace(_category.DesktopBackgroundImagePath))
        {
            return Brushes.White;
        }

        var background = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;

        // A see-through group shows the wallpaper, which is usually the darker bet.
        var effective = _theme.Opacity * _category.AreaOpacity;
        if (effective < 0.5)
        {
            return Brushes.White;
        }

        return ThemeBrushes.CreateBrush(ComputeContrastingTextColor(background), 1.0);
    }

    /// <summary>Keeps a label readable over a background image or a translucent group.</summary>
    private static DropShadowEffect TileTextShadow()
    {
        return new DropShadowEffect
        {
            BlurRadius = 4,
            ShadowDepth = 1,
            Direction = 315,
            Opacity = 0.85,
            Color = Colors.Black
        };
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

        if (IsAppFolder)
        {
            AddMenuItem(menu, LocalizationService.Get("group.openFolder"), OpenOverlay, "IconOpenExternal");
        }
        else
        {
            AddMenuItem(
                menu,
                _category.IsCollapsed ? LocalizationService.Get("group.expand") : LocalizationService.Get("group.collapse"),
                ToggleCollapse,
                _category.IsCollapsed ? "IconChevronDown" : "IconChevronUp");
        }

        AddMenuItem(menu, LocalizationService.Get("group.rename"), OnRenameClick, "IconRename");

        menu.Items.Add(BuildSeparator());
        AddMenuItem(
            menu,
            LocalizationService.Get(IsAppFolder ? "group.stylePanel" : "group.styleAppFolder"),
            ToggleDisplayMode,
            IsAppFolder ? "IconStylePanel" : "IconStyleAppFolder");
        AddCheckItem(menu, LocalizationService.Get("group.showBadge"), _category.ShowBadge, ToggleBadge, "IconBadge");

        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("group.backgroundColor"), OnChangeColorClick, "IconColor");
        AddMenuItem(menu, LocalizationService.Get("group.backgroundImage"), OnChangeBackgroundImageClick, "IconImage");
        AddMenuItem(menu, LocalizationService.Get("group.removeBackgroundImage"), OnClearBackgroundImageClick);

        var opacityMenu = CreateMenuItem(LocalizationService.Get("group.opacity"), "IconOpacity");
        AddOpacitySlider(opacityMenu, LocalizationService.Get("group.areaOpacity"), _category.AreaOpacity, SetAreaOpacity);
        AddOpacitySlider(opacityMenu, LocalizationService.Get("group.titleOpacity"), _category.TitleOpacity, SetTitleOpacity);
        menu.Items.Add(opacityMenu);

        if (_commands is not null)
        {
            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("group.duplicate"), () => _commands.Duplicate(_category), "IconDuplicate");
            AddMenuItem(menu, LocalizationService.Get("group.wallpaperAsBackground"), () => _commands.ApplyWallpaper(_category), "IconImage");

            var shareMenu = CreateMenuItem(LocalizationService.Get("group.shareVisual"), "IconStylePanel");
            AddMenuItem(shareMenu, LocalizationService.Get("group.applyVisualToAll"), () => _commands.ApplyVisualToAllGroups(_category));
            AddMenuItem(shareMenu, LocalizationService.Get("group.setAsDefaultVisual"), () => _commands.SetAsDefaultVisual(_category));
            AddMenuItem(shareMenu, LocalizationService.Get("group.wallpaperForAll"), () => _commands.ApplyWallpaper(null));
            menu.Items.Add(shareMenu);
        }

        menu.Items.Add(BuildSeparator());

        var isEmpty = GroupEntries.Count(_category) == 0;
        var removeItem = CreateMenuItem(LocalizationService.Get("group.remove"), "IconDelete");
        removeItem.IsEnabled = isEmpty;
        removeItem.Click += (_, _) => OnDeleteGroupClick();
        if (!isEmpty)
        {
            removeItem.ToolTip = LocalizationService.Get("group.removeOnlyEmpty");
        }

        menu.Items.Add(removeItem);

        return menu;
    }

    private ContextMenu BuildCanvasContextMenu()
    {
        var menu = CreateContextMenuShell();

        var newMenu = CreateMenuItem(LocalizationService.Get("group.newMenu"), "IconAdd");
        AddMenuItem(newMenu, LocalizationService.Get("group.newFolder"), CreateSubfolder, "IconFolder");
        AddMenuItem(newMenu, LocalizationService.Get("group.newTextFile"), CreateTextFile, "IconFile");
        menu.Items.Add(newMenu);
        menu.Items.Add(BuildSeparator());

        AddMenuItem(menu, LocalizationService.Get("group.arrangeIcons"), ArrangeIconsAutomatically, "IconGrid");

        var sortMenu = CreateMenuItem(LocalizationService.Get("group.sortBy"), "IconSort");
        AddMenuItem(sortMenu, LocalizationService.Get("group.sortByName"), SortByName);
        AddMenuItem(sortMenu, LocalizationService.Get("group.sortByType"), SortByType);
        menu.Items.Add(sortMenu);

        var sizeMenu = CreateMenuItem(LocalizationService.Get("group.iconSize"), "IconIconSize");
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeSmall"), IsIconScale(0.75), () => SetIconScale(0.75));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeMedium"), IsIconScale(1.0), () => SetIconScale(1.0));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeLarge"), IsIconScale(1.5), () => SetIconScale(1.5));
        menu.Items.Add(sizeMenu);

        return menu;
    }

    private ContextMenu BuildItemTileContextMenu(LaunchItem item)
    {
        var menu = CreateContextMenuShell();
        AddMenuItem(menu, LocalizationService.Get("item.open"), () => _onExecute(item), "IconOpenExternal");

        if (ShellCommands.HasFileTarget(item))
        {
            AddMenuItem(menu, LocalizationService.Get("item.runAsAdmin"), () => ShellCommands.RunAsAdministrator(item), "IconShield");
            AddMenuItem(menu, LocalizationService.Get("item.openFileLocation"), () => ShellCommands.RevealInExplorer(item), "IconFolderOpen");
            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("item.copyPath"), () => ShellCommands.CopyPath(item), "IconCopy");
        }

        AddMenuItem(menu, LocalizationService.Get("item.rename"), () => RenameItem(item), "IconRename");
        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("item.removeFromGroup"), () => RemoveItem(item), "IconDelete");

        if (ShellCommands.HasFileTarget(item))
        {
            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("item.properties"), () => ShellCommands.ShowProperties(item), "IconProperties");
            AddMenuItem(menu, LocalizationService.Get("item.standardMenu"), () => ShowStandardMenu(item), "IconShellMenu");
        }

        return menu;
    }

    private ContextMenu BuildFolderTileContextMenu(MenuCategory folder)
    {
        var menu = CreateContextMenuShell();
        AddMenuItem(menu, LocalizationService.Get("item.open"), () => OpenSubfolder(folder), "IconFolder");
        AddMenuItem(menu, LocalizationService.Get("item.rename"), () => RenameFolder(folder), "IconRename");
        menu.Items.Add(BuildSeparator());

        var isEmpty = GroupEntries.Count(folder) == 0;
        var removeItem = CreateMenuItem(LocalizationService.Get("item.removeFromGroup"), "IconDelete");
        removeItem.IsEnabled = isEmpty;
        removeItem.Click += (_, _) => RemoveFolder(folder);
        if (!isEmpty)
        {
            removeItem.ToolTip = LocalizationService.Get("group.removeOnlyEmpty");
        }

        menu.Items.Add(removeItem);
        return menu;
    }

    private MenuItem CreateMenuItem(string header, string? iconKey = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Style = (Style)Application.Current.Resources["EasyWinMenuMenuItemStyle"],
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            Padding = new Thickness(_theme.ItemPadding, _theme.ItemPadding / 2, _theme.ItemPadding, _theme.ItemPadding / 2),
            Icon = BuildMenuIcon(iconKey),

            // Read by the submenu popup in the template, not by the row itself.
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, _theme.Opacity),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0)
        };
        MenuThemeProperties.SetHighlightBrush(item, ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0));
        MenuThemeProperties.SetHighlightCornerRadius(item, new CornerRadius(4));
        MenuThemeProperties.SetPanelCornerRadius(item, new CornerRadius(_theme.CornerRadius));
        return item;
    }

    /// <summary>
    /// Every row reserves the icon column even when it has no icon, so headers stay on
    /// one vertical line instead of stepping in and out as icons come and go.
    /// </summary>
    private FrameworkElement BuildMenuIcon(string? iconKey)
    {
        var size = _theme.ItemFontSize + 3;
        if (iconKey is null)
        {
            return new Border { Width = size, Height = size };
        }

        return AppIcons.Create(iconKey, ThemeBrushes.CreateBrush(_theme.TextColor, 0.85), size)
            ?? new Border { Width = size, Height = size };
    }

    private Separator BuildSeparator()
    {
        return new Separator
        {
            Background = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4)
        };
    }

    private bool IsIconScale(double scale) => Math.Abs(_category.DesktopIconScale - scale) < 0.01;

    private void AddCheckItem(ItemsControl parent, string header, bool isChecked, Action handler, string? iconKey = null)
    {
        // A tick in the icon column is how Windows shows a setting that is on; when it
        // is off the row keeps its own icon, or an empty column if it has none.
        AddMenuItem(parent, header, handler, isChecked ? "IconCheck" : iconKey);
    }

    /// <summary>
    /// A live slider inside the menu. The value is applied while dragging so the group
    /// updates under the pointer, but only written to the config once the drag ends.
    /// </summary>
    private void AddOpacitySlider(ItemsControl parent, string label, double value, Action<double> apply)
    {
        var caption = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize - 1
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = value,
            Width = 168,
            SmallChange = 0.05,
            LargeChange = 0.1,
            Margin = new Thickness(0, 6, 0, 0)
        };

        void UpdateCaption() => caption.Text = $"{label}   {slider.Value * 100:0}%";

        UpdateCaption();
        slider.ValueChanged += (_, _) =>
        {
            UpdateCaption();
            apply(slider.Value);
        };
        slider.LostMouseCapture += (_, _) => _onLayoutChanged(_category);
        slider.KeyUp += (_, _) => _onLayoutChanged(_category);

        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 2, 0, 4) };
        panel.Children.Add(caption);
        panel.Children.Add(slider);

        var item = CreateMenuItem(string.Empty);
        item.Header = panel;
        item.StaysOpenOnClick = true;
        MenuThemeProperties.SetHighlightBrush(item, Brushes.Transparent);
        parent.Items.Add(item);
    }

    private void AddMenuItem(ItemsControl parent, string header, Action handler, string? iconKey = null)
    {
        var item = CreateMenuItem(header, iconKey);
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
        RefreshFolderTile();
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

    private void SetAreaOpacity(double value)
    {
        _category.AreaOpacity = value;
        ApplyBackground();
        _header.ContextMenu = BuildHeaderContextMenu();
        _onLayoutChanged(_category);
    }

    private void SetTitleOpacity(double value)
    {
        _category.TitleOpacity = value;
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

        UpdateCollapseGlyph();
        ApplyDisplayMode();
    }

    private void UpdateCollapseGlyph()
    {
        _collapseGlyph.Child = AppIcons.Create(
            _category.IsCollapsed ? "IconChevronDown" : "IconChevronUp",
            ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            _theme.TitleFontSize + 3);
        _collapseGlyph.ToolTip = LocalizationService.Get(_category.IsCollapsed ? "group.expand" : "group.collapse");
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

    /// <summary>Rebuilds the whole window after its category was rewritten from outside.</summary>
    public void ReloadVisuals(MenuTheme theme)
    {
        _theme = theme;
        Content = BuildRoot();
        SetCollapsed(_category.IsCollapsed);
    }

    private void ApplyThemeChange()
    {
        ApplyBackground();
        ApplyHeaderBackground();
        _headerText.Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0);
        UpdateCollapseGlyph();
        PopulateTiles();
        ApplyDisplayMode();
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
            AddFolderTile(
                folder,
                folder.IconX ?? PaddingX + ((index % 3) * TileSize),
                folder.IconY ?? PaddingY + ((index / 3) * TileSize));
            index++;
        }

        foreach (var item in _category.Items.Where(i => i.IsDesktopPinned))
        {
            AddTile(
                item,
                item.DesktopIconX ?? PaddingX + ((index % 3) * TileSize),
                item.DesktopIconY ?? PaddingY + ((index / 3) * TileSize));
            index++;
        }

        RefreshFolderTile();
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
        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / TileSize));
        var index = 0;
        foreach (var entry in orderedEntries)
        {
            var x = PaddingX + ((index % columns) * TileSize);
            var y = PaddingY + ((index / columns) * TileSize);
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

            var x = ClampToCanvas(Canvas.GetLeft(tile), tile.ActualWidth, _canvas.ActualWidth, PaddingX);
            var y = ClampToCanvas(Canvas.GetTop(tile), tile.ActualHeight, _canvas.ActualHeight, PaddingY);
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            onMoved(x, y);
        };
    }

    private static double ClampToCanvas(double value, double tileSize, double canvasSize, double padding)
    {
        // A canvas that has not been measured yet cannot bound anything; leave the value be.
        if (double.IsNaN(value) || canvasSize <= 0)
        {
            return double.IsNaN(value) ? padding : value;
        }

        var max = Math.Max(padding, canvasSize - padding - tileSize);
        return Math.Clamp(value, padding, max);
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

        // The Delete key answers to the same rule as the menu: a folder that still
        // holds something is left alone rather than quietly taken with the selection.
        var removable = _selectedEntries
            .Where(entry => entry is not MenuCategory folder || GroupEntries.Count(folder) == 0)
            .ToList();

        if (removable.Count == 0)
        {
            MessageBox.Show(
                this,
                LocalizationService.Get("group.removeOnlyEmpty"),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var names = string.Join(", ", removable.Select(GetEntryName));
        var prompt = LocalizationService.Format("item.removeConfirm", names);
        if (removable.Count < _selectedEntries.Count)
        {
            prompt += Environment.NewLine + Environment.NewLine + LocalizationService.Get("group.removeOnlyEmpty");
        }

        var confirmed = MessageBox.Show(
            this,
            prompt,
            LocalizationService.Get("common.appName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var entry in removable)
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
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
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
            Foreground = TileTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = folder.Name,
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
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

        // Shown only while the button is held: hovering the title is not a move.
        _header.Cursor = Cursors.SizeAll;
        try
        {
            DragMove();
        }
        finally
        {
            _header.Cursor = Cursors.Arrow;
        }

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
