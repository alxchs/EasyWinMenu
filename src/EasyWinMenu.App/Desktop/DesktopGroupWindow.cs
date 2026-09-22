using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Media.Animation;
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
using Rectangle = System.Windows.Shapes.Rectangle;
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
    private const double MinGroupWidth = 140;
    private const double MinGroupHeight = 120;
    private const double ResizeEdgeThickness = 6;
    private const double ResizeCornerSize = 14;
    private const double AppFolderDesktopScale = 1.4;

    private readonly MenuCategory _category;
    private MenuTheme _theme;
    private readonly IconCacheService _iconCache;
    private readonly Action<LaunchItem> _onExecute;
    private readonly Action<MenuCategory> _onLayoutChanged;
    private readonly Action<MenuCategory> _onDeleteRequested;
    private readonly IDesktopGroupCommands? _commands;
    private readonly ScaleTransform _zoomTransform;
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _openSubfolders = new();

    /// <summary>Every open desktop group, so one can find another under a drag's drop point.</summary>
    private static readonly List<DesktopGroupWindow> _allGroupWindows = new();
    private readonly Dictionary<object, FrameworkElement> _tilesByEntry = new();
    private readonly HashSet<object> _selectedEntries = new();
    private readonly Dictionary<int, System.Windows.Point> _monitorPositions = new();

    private System.Windows.Point _marqueeStart;
    private bool _isMarqueeActive;
    private Border? _marqueeBorder;
    private HashSet<object> _preMarqueeSelection = new();

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static readonly IntPtr HWND_TOP = new IntPtr(0);
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private Canvas _canvas = null!;
    private Border _border = null!;
    private Grid _root = null!;
    private ContentControl _folderHost = null!;
    private GroupOverlayWindow? _overlay;
    private Border _header = null!;
    private readonly List<Border> _resizeHandles = new();
    private TextBlock _headerText = null!;
    private Border _collapseGlyph = null!;
    private Border _closeButton = null!;
    private Border _menuButton = null!;
    private readonly Dictionary<object, TextBlock> _captionsByEntry = new();
    private readonly Dictionary<object, Action> _openActionsByEntry = new();
    private object? _selectionAnchor;

    private Border _searchBar = null!;
    private System.Windows.Controls.TextBox _searchBox = null!;
    private TextBlock? _searchPlaceholder;
    private TextBlock _searchCountText = null!;
    private System.Windows.Controls.Primitives.Popup _searchPopup = null!;
    private System.Windows.Controls.ListBox _searchResultsList = null!;
    private readonly List<object> _searchMatches = new();
    private string _lastSearchText = string.Empty;
    private string _typeAheadBuffer = string.Empty;
    private DateTime _typeAheadLastInput;
    private static readonly TimeSpan TypeAheadTimeout = TimeSpan.FromSeconds(1);

    private ResizeEdge _resizeEdge = ResizeEdge.None;
    private System.Windows.Point? _placedAt;
    private DateTime _holdPlacementUntil;
    private DispatcherTimer? _persistMoveTimer;
    private System.Windows.Point _resizeOrigin;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    private enum ResizeEdge
    {
        None,
        N,
        S,
        E,
        W,
        NE,
        NW,
        SE,
        SW
    }

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
        PreviewTextInput += OnPreviewTextInput;
        LocationChanged += OnLocationChanged;
        Deactivated += (_, _) => CloseFindOverlay(rememberQuery: true);
        Drop += OnDrop;
        _allGroupWindows.Add(this);
        Closed += (_, _) =>
        {
            _overlay?.Close();
            _pendingCuts.RemoveAll(p => ReferenceEquals(p.Owner, this));
            StopPendingCutWatcherIfIdle();
            _allGroupWindows.Remove(this);
        };
    }

    public bool IsCollapsed => _category.IsCollapsed;

    /// <summary>
    /// Brings this group back after Windows hid it, and does the same for any open
    /// subfolders. Measured directly rather than assumed: Show Desktop does not minimize
    /// a window with no taskbar button — WindowState stays Normal — it just brings the
    /// desktop's own window to the front of the z-order and leaves ours buried under it.
    /// Toggling Topmost is the standard way to force a window back to the front of its
    /// z-order band without stealing focus permanently. The WindowState check stays as
    /// a second line of defence, in case some other trigger does minimize it for real.
    /// </summary>
    internal void RestoreIfMinimized()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        Topmost = false;

        foreach (var subfolder in _openSubfolders.Values)
        {
            subfolder.RestoreIfMinimized();
        }
    }

    /// <summary>
    /// Where the group lives by choice, at its current size. It can differ from where
    /// the window is: a group rescued off a monitor that went away keeps this as its
    /// home, so it can walk back when the monitor returns.
    /// </summary>
    internal Rect HomeRect => new(
        _category.DesktopX,
        _category.DesktopY,
        ActualWidth > 0 ? ActualWidth : _category.DesktopWidth,
        ActualHeight > 0 ? ActualHeight : _category.DesktopHeight);

    /// <summary>Moves the window without recording the move as the group's new home.</summary>
    internal void PlaceWithoutSaving(double left, double top)
    {
        // Remembered rather than flagged: WPF reports the move after this method has
        // returned, so a "moving now" flag is already clear by the time anyone checks it.
        _placedAt = new System.Windows.Point(left, top);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Ignores moves for a while. Windows shuffles windows on its own while monitors come
    /// and go, and remembering those shuffles would overwrite the group's real home.
    /// </summary>
    internal void HoldPlacement(TimeSpan duration)
    {
        _holdPlacementUntil = DateTime.UtcNow + duration;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _holdPlacementUntil)
        {
            return;
        }

        if (_persistMoveTimer is null)
        {
            _persistMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _persistMoveTimer.Tick += (_, _) =>
            {
                _persistMoveTimer.Stop();
                PersistExternalMove();
            };
        }

        // Debounced: a drag reports dozens of positions and only the last one matters.
        _persistMoveTimer.Stop();
        _persistMoveTimer.Start();
    }

    /// <summary>
    /// Remembers a move this class did not make itself — Windows' own Win+Shift+arrow,
    /// for instance, when the shell handles the shortcut before the group ever sees it.
    /// </summary>
    private void PersistExternalMove()
    {
        if (DateTime.UtcNow < _holdPlacementUntil)
        {
            return;
        }

        // Still exactly where this class put it: that was a placement, not a user's move.
        if (_placedAt is { } placed && Math.Abs(Left - placed.X) < 0.5 && Math.Abs(Top - placed.Y) < 0.5)
        {
            return;
        }

        if (Math.Abs(Left - _category.DesktopX) < 0.5 && Math.Abs(Top - _category.DesktopY) < 0.5)
        {
            return;
        }

        _category.DesktopX = Left;
        _category.DesktopY = Top;
        _monitorPositions.Clear();
        _onLayoutChanged(_category);
    }

    /// <summary>
    /// Carries the group to the monitor beside the one it is on, keeping its relative
    /// place. With a single monitor there is nowhere to go and nothing happens.
    /// </summary>
    private void MoveToAdjacentMonitor(int direction)
    {
        var areas = DisplayInventory.WorkAreas(this);
        var current = new Rect(Left, Top, ActualWidth, ActualHeight);
        var from = MonitorPlacement.IndexOfOwner(current, areas);

        if (MonitorPlacement.AdjacentIndex(from, areas, direction) is not { } to)
        {
            return;
        }

        _monitorPositions[from] = new System.Windows.Point(Left, Top);

        System.Windows.Point position;
        if (_monitorPositions.TryGetValue(to, out var remembered)
            && MonitorPlacement.IsReachable(new Rect(remembered.X, remembered.Y, ActualWidth, ActualHeight), areas))
        {
            position = remembered;
        }
        else
        {
            position = MonitorPlacement.MapBetween(current, areas[from], areas[to]);
            _monitorPositions[to] = position;
        }

        PlaceWithoutSaving(position.X, position.Y);

        _category.DesktopX = position.X;
        _category.DesktopY = position.Y;
        _onLayoutChanged(_category);
    }

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
        _folderHost.PreviewMouseRightButtonDown += (_, _) => _folderHost.ContextMenu = BuildHeaderContextMenu();

        _root = new Grid { Background = Brushes.Transparent };
        _root.Children.Add(BuildContent());
        _root.Children.Add(_folderHost);
        _root.Children.Add(_searchPopup);
        BuildResizeHandles(_root);
        return _root;
    }

    /// <summary>
    /// Eight thin, invisible strips laid over the window's own edges and corners so the
    /// whole perimeter resizes, the way a normal titled window does — not just the one
    /// corner a single grip could reach. They sit on top (added last) so they win the
    /// hit test over the header or canvas in that last handful of pixels.
    /// </summary>
    private void BuildResizeHandles(Grid root)
    {
        _resizeHandles.Clear();

        AddResizeHandle(root, ResizeEdge.N, Cursors.SizeNS,
            HorizontalAlignment.Stretch, VerticalAlignment.Top,
            double.NaN, ResizeEdgeThickness,
            new Thickness(ResizeCornerSize, 0, ResizeCornerSize, 0));
        AddResizeHandle(root, ResizeEdge.S, Cursors.SizeNS,
            HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
            double.NaN, ResizeEdgeThickness,
            new Thickness(ResizeCornerSize, 0, ResizeCornerSize, 0));
        AddResizeHandle(root, ResizeEdge.W, Cursors.SizeWE,
            HorizontalAlignment.Left, VerticalAlignment.Stretch,
            ResizeEdgeThickness, double.NaN,
            new Thickness(0, ResizeCornerSize, 0, ResizeCornerSize));
        AddResizeHandle(root, ResizeEdge.E, Cursors.SizeWE,
            HorizontalAlignment.Right, VerticalAlignment.Stretch,
            ResizeEdgeThickness, double.NaN,
            new Thickness(0, ResizeCornerSize, 0, ResizeCornerSize));

        AddResizeHandle(root, ResizeEdge.NW, Cursors.SizeNWSE,
            HorizontalAlignment.Left, VerticalAlignment.Top,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.SE, Cursors.SizeNWSE,
            HorizontalAlignment.Right, VerticalAlignment.Bottom,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.NE, Cursors.SizeNESW,
            HorizontalAlignment.Right, VerticalAlignment.Top,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
        AddResizeHandle(root, ResizeEdge.SW, Cursors.SizeNESW,
            HorizontalAlignment.Left, VerticalAlignment.Bottom,
            ResizeCornerSize, ResizeCornerSize, new Thickness(0));
    }

    private void AddResizeHandle(
        Grid root,
        ResizeEdge edge,
        System.Windows.Input.Cursor cursor,
        HorizontalAlignment horizontalAlignment,
        VerticalAlignment verticalAlignment,
        double width,
        double height,
        Thickness margin)
    {
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = cursor,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = margin,
            // Matches the panel's own rounding so a corner handle's hit area follows the
            // curve instead of squaring it off — invisible either way, but it keeps the
            // cursor change lined up with where the eye reads the corner as starting.
            CornerRadius = new CornerRadius(_theme.CornerRadius)
        };

        if (!double.IsNaN(width))
        {
            handle.Width = width;
        }

        if (!double.IsNaN(height))
        {
            handle.Height = height;
        }

        handle.MouseLeftButtonDown += (sender, e) => OnResizeMouseDown(edge, (UIElement)sender, e);
        handle.MouseMove += OnResizeMouseMove;
        handle.MouseLeftButtonUp += OnResizeMouseUp;

        root.Children.Add(handle);
        _resizeHandles.Add(handle);
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
        UpdateHeaderTooltip();

        if (IsAppFolder)
        {
            // A ContextMenu cannot be shared between two owners, so the tile gets its own.
            _folderHost.ContextMenu = BuildHeaderContextMenu();
            RefreshFolderTile();
            _border.Visibility = Visibility.Collapsed;
            _folderHost.Visibility = Visibility.Visible;
            SetResizeHandlesVisible(false);
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }

        _folderHost.Visibility = Visibility.Collapsed;
        _border.Visibility = Visibility.Visible;
        SetResizeHandlesVisible(!_category.IsCollapsed);

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

        _folderHost.Content = AppFolderTile.Build(_category, _theme, _iconCache, AppFolderDesktopScale);
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

    /// <summary>
    /// Drives the whole press-drag-release gesture through WPF's own <see cref="Window.DragMove"/>
    /// — the same primitive the header already uses — instead of accumulating the move by
    /// hand from <c>PointToScreen</c> deltas. The hand-rolled version was the actual cause
    /// of the tile "disappearing": on a per-monitor-DPI-aware system, calling
    /// <c>PointToScreen</c> a second time in the same handler, right after the window's own
    /// position had just been changed by the first delta, could read back a value scaled by
    /// a different DPI factor than the first call — landing the window at a corrupted
    /// coordinate (observed once at exactly <c>Int16.MinValue</c>) with no monitor anywhere
    /// near it. <c>DragMove</c> hands the whole drag to Windows itself, immune to that.
    /// </summary>
    private void OnFolderTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        e.Handled = true;

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
        _monitorPositions.Clear();
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

        _closeButton = BuildHeaderButton(CloseGroup);
        _closeButton.Child = AppIcons.Create("IconClose", ThemeBrushes.CreateBrush(_theme.TextColor, 1.0), _theme.TitleFontSize);
        _closeButton.ToolTip = LocalizationService.Get("group.close");
        DockPanel.SetDock(_closeButton, Dock.Right);

        _collapseGlyph = BuildHeaderButton(ToggleCollapse);
        DockPanel.SetDock(_collapseGlyph, Dock.Right);

        _menuButton = BuildHeaderButton(OpenHeaderMenu);
        _menuButton.Child = AppIcons.Create("IconMore", ThemeBrushes.CreateBrush(_theme.TextColor, 1.0), _theme.TitleFontSize + 3);
        _menuButton.ToolTip = LocalizationService.Get("group.menu");
        DockPanel.SetDock(_menuButton, Dock.Right);

        var headerPanel = new DockPanel();
        headerPanel.Children.Add(_closeButton);
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
        // Rebuilt fresh on every right-click rather than trusted to whichever action
        // last happened to reassign it — checkmarks (arrangement, icon size, badge) need
        // to reflect the group's actual current state, not whatever it was the last time
        // something else touched the menu.
        _header.PreviewMouseRightButtonDown += (_, _) => _header.ContextMenu = BuildHeaderContextMenu();
        DockPanel.SetDock(_header, Dock.Top);
        panel.Children.Add(_header);

        _searchBar = BuildSearchBar();
        DockPanel.SetDock(_searchBar, Dock.Top);
        panel.Children.Add(_searchBar);

        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            RenderTransform = _zoomTransform,
            RenderTransformOrigin = new System.Windows.Point(0, 0),
            ContextMenu = BuildHeaderContextMenu()
        };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            CloseFindOverlay(rememberQuery: true);
            var isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (!isCtrl && !isShift)
            {
                ClearSelection();
                _preMarqueeSelection.Clear();
            }
            else
            {
                _preMarqueeSelection = new HashSet<object>(_selectedEntries);
            }

            _marqueeStart = e.GetPosition(_canvas);
            _isMarqueeActive = true;
            _canvas.CaptureMouse();

            if (_marqueeBorder is not null)
            {
                _canvas.Children.Remove(_marqueeBorder);
            }

            _marqueeBorder = new Border
            {
                BorderBrush = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.85),
                BorderThickness = new Thickness(1),
                Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.20),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_marqueeBorder, _marqueeStart.X);
            Canvas.SetTop(_marqueeBorder, _marqueeStart.Y);
            _marqueeBorder.Width = 0;
            _marqueeBorder.Height = 0;
            _canvas.Children.Add(_marqueeBorder);

            e.Handled = true;
        };

        _canvas.MouseMove += (_, e) =>
        {
            if (!_isMarqueeActive || _marqueeBorder is null)
            {
                return;
            }

            var current = e.GetPosition(_canvas);
            var left = Math.Min(_marqueeStart.X, current.X);
            var top = Math.Min(_marqueeStart.Y, current.Y);
            var width = Math.Abs(current.X - _marqueeStart.X);
            var height = Math.Abs(current.Y - _marqueeStart.Y);

            Canvas.SetLeft(_marqueeBorder, left);
            Canvas.SetTop(_marqueeBorder, top);
            _marqueeBorder.Width = width;
            _marqueeBorder.Height = height;

            var marqueeRect = new Rect(left, top, width, height);
            var isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            foreach (var (entry, tile) in _tilesByEntry)
            {
                var tileLeft = Canvas.GetLeft(tile);
                var tileTop = Canvas.GetTop(tile);
                var tileWidth = tile.ActualWidth > 0 ? tile.ActualWidth : TileSize - 8;
                var tileHeight = tile.ActualHeight > 0 ? tile.ActualHeight : TileSize;
                var tileRect = new Rect(tileLeft, tileTop, tileWidth, tileHeight);

                var intersects = marqueeRect.IntersectsWith(tileRect);

                if (isCtrl)
                {
                    var originallySelected = _preMarqueeSelection.Contains(entry);
                    if (intersects)
                    {
                        if (originallySelected)
                        {
                            _selectedEntries.Remove(entry);
                        }
                        else
                        {
                            _selectedEntries.Add(entry);
                        }
                    }
                    else
                    {
                        if (originallySelected)
                        {
                            _selectedEntries.Add(entry);
                        }
                        else
                        {
                            _selectedEntries.Remove(entry);
                        }
                    }
                }
                else
                {
                    var originallySelected = _preMarqueeSelection.Contains(entry);
                    if (intersects || originallySelected)
                    {
                        _selectedEntries.Add(entry);
                    }
                    else
                    {
                        _selectedEntries.Remove(entry);
                    }
                }
            }

            RefreshSelectionVisuals();
        };

        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_isMarqueeActive)
            {
                _isMarqueeActive = false;
                _canvas.ReleaseMouseCapture();
                if (_marqueeBorder is not null)
                {
                    _canvas.Children.Remove(_marqueeBorder);
                    _marqueeBorder = null;
                }
                e.Handled = true;
            }
        };

        _canvas.LostMouseCapture += (_, _) =>
        {
            if (_isMarqueeActive)
            {
                var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!isLButtonDown)
                {
                    _isMarqueeActive = false;
                    if (_marqueeBorder is not null)
                    {
                        _canvas.Children.Remove(_marqueeBorder);
                        _marqueeBorder = null;
                    }
                }
            }
        };

        _canvas.PreviewMouseRightButtonDown += (_, _) => _canvas.ContextMenu = BuildHeaderContextMenu();

        PopulateTiles();

        panel.Children.Add(_canvas);

        _border = new Border
        {
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

    private void CloseGroup()
    {
        _category.IsClosed = true;
        _onLayoutChanged(_category);
        Close();
    }

    /// <summary>
    /// A Ctrl+F search strip docked under the header, hidden until asked for. The match
    /// list itself lives in a <see cref="System.Windows.Controls.Primitives.Popup"/>
    /// anchored to this bar rather than in the DockPanel flow, so it floats over the
    /// icons instead of pushing them down.
    /// </summary>
    private Border BuildSearchBar()
    {
        var searchIcon = AppIcons.Create("IconSearch", ThemeBrushes.CreateBrush(_theme.TextColor, 0.65), _theme.ItemFontSize + 2);
        var iconWrap = new Border
        {
            Child = searchIcon,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        DockPanel.SetDock(iconWrap, Dock.Left);

        var closeSearch = new Border
        {
            Width = _theme.ItemFontSize + 4,
            Height = _theme.ItemFontSize + 4,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Child = AppIcons.Create("IconClose", ThemeBrushes.CreateBrush(_theme.TextColor, 0.65), _theme.ItemFontSize - 2),
            ToolTip = LocalizationService.Get("common.cancel")
        };
        closeSearch.MouseLeftButtonDown += (_, _) => CloseFindOverlay(rememberQuery: false);
        DockPanel.SetDock(closeSearch, Dock.Right);

        _searchCountText = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 0.65),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = Math.Max(10, _theme.ItemFontSize - 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(_searchCountText, Dock.Right);

        _searchPlaceholder = new TextBlock
        {
            Text = LocalizationService.Get("group.searchPlaceholder") + " (Esc)",
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 0.4),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _searchBox = new System.Windows.Controls.TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += (_, _) =>
        {
            if (_searchPlaceholder is not null)
            {
                _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateSearchMatches(_searchBox.Text);
        };
        _searchBox.LostFocus += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
            {
                if (!_searchBox.IsKeyboardFocusWithin && !_searchResultsList.IsKeyboardFocusWithin)
                {
                    CloseFindOverlay(rememberQuery: true);
                }
            });
        };

        var boxGrid = new Grid();
        boxGrid.Children.Add(_searchPlaceholder);
        boxGrid.Children.Add(_searchBox);

        var row = new DockPanel();
        row.Children.Add(iconWrap);
        row.Children.Add(closeSearch);
        row.Children.Add(_searchCountText);
        row.Children.Add(boxGrid);

        var bar = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            Visibility = Visibility.Collapsed,
            Child = row
        };
        ApplySearchBarAppearance(bar);

        _searchResultsList = new System.Windows.Controls.ListBox
        {
            MaxHeight = 240,
            Background = ThemeBrushes.CreateBrush(_theme.BackgroundColor, 0.98),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1)
        };

        _searchPopup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = bar,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true,
            Child = _searchResultsList
        };
        bar.SizeChanged += (_, _) => _searchPopup.Width = bar.ActualWidth;

        return bar;
    }

    private void ApplySearchBarAppearance(Border bar)
    {
        bar.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.18);
        bar.BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0);
        bar.BorderThickness = new Thickness(0, 0, 0, 1);
    }

    /// <summary>Only meaningful in the free-canvas panel look — the closed folder tile has no icons to search.</summary>
    private void OpenFindOverlay()
    {
        if (IsAppFolder)
        {
            return;
        }

        _searchBar.Visibility = Visibility.Visible;
        _searchBox.Text = _lastSearchText;
        if (_searchPlaceholder is not null)
        {
            _searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        _searchBox.SelectAll();
        _searchBox.Focus();
        Keyboard.Focus(_searchBox);
        UpdateSearchMatches(_searchBox.Text);
    }

    /// <summary>
    /// Remembers the query exactly at the moment the user picked a result or backed out
    /// with Esc — never at, say, a stray loss of focus — so the next Ctrl+F picks up
    /// where this one left off.
    /// </summary>
    private void CloseFindOverlay(bool rememberQuery)
    {
        if (_searchBar.Visibility != Visibility.Visible)
        {
            return;
        }

        if (rememberQuery)
        {
            _lastSearchText = _searchBox.Text;
        }

        _searchPopup.IsOpen = false;
        _searchBar.Visibility = Visibility.Collapsed;
        ClearAllHighlights();
        _searchMatches.Clear();
        _searchResultsList.Items.Clear();
        Keyboard.ClearFocus();
    }

    private void UpdateSearchMatches(string query)
    {
        ClearAllHighlights();
        _searchMatches.Clear();
        _searchResultsList.Items.Clear();

        if (string.IsNullOrEmpty(query))
        {
            _searchCountText.Text = string.Empty;
            _searchPopup.IsOpen = false;
            return;
        }

        foreach (var entry in GroupEntries.Enumerate(_category))
        {
            var name = GroupEntries.NameOf(entry);
            var matchIndex = name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
            {
                continue;
            }

            _searchMatches.Add(entry);

            if (_captionsByEntry.TryGetValue(entry, out var caption))
            {
                ApplyNameHighlight(caption, name, matchIndex, query.Length);
            }

            var resultText = new TextBlock
            {
                Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
                FontFamily = new FontFamily(_theme.ItemFontFamily),
                FontSize = _theme.ItemFontSize
            };
            ApplyNameHighlight(resultText, name, matchIndex, query.Length);

            var listItem = new System.Windows.Controls.ListBoxItem { Content = resultText, Tag = entry };
            listItem.PreviewMouseLeftButtonDown += (_, _) =>
            {
                _searchResultsList.SelectedItem = listItem;
                ActivateSelectedSearchResult();
            };
            _searchResultsList.Items.Add(listItem);
        }

        _searchCountText.Text = LocalizationService.Format("group.searchCount", _searchMatches.Count);

        if (_searchResultsList.Items.Count > 0)
        {
            _searchResultsList.SelectedIndex = 0;
        }

        _searchPopup.IsOpen = _searchResultsList.Items.Count > 0;
    }

    private void MoveSearchSelection(int delta)
    {
        if (_searchResultsList.Items.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(_searchResultsList.SelectedIndex + delta, 0, _searchResultsList.Items.Count - 1);
        _searchResultsList.SelectedIndex = next;
        _searchResultsList.ScrollIntoView(_searchResultsList.SelectedItem);
    }

    /// <summary>Enter on a result does exactly what double-clicking the icon itself would.</summary>
    private void ActivateSelectedSearchResult()
    {
        if (_searchResultsList.SelectedItem is not System.Windows.Controls.ListBoxItem { Tag: { } entry })
        {
            return;
        }

        CloseFindOverlay(rememberQuery: true);
        if (_openActionsByEntry.TryGetValue(entry, out var open))
        {
            open();
        }
    }

    private void ApplyNameHighlight(TextBlock textBlock, string name, int matchIndex, int matchLength)
    {
        textBlock.Inlines.Clear();
        if (matchIndex < 0)
        {
            textBlock.Inlines.Add(new Run(name));
            return;
        }

        if (matchIndex > 0)
        {
            textBlock.Inlines.Add(new Run(name[..matchIndex]));
        }

        textBlock.Inlines.Add(new Run(name.Substring(matchIndex, matchLength))
        {
            FontWeight = FontWeights.Bold,
            Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0)
        });

        var tailStart = matchIndex + matchLength;
        if (tailStart < name.Length)
        {
            textBlock.Inlines.Add(new Run(name[tailStart..]));
        }
    }

    private void ClearAllHighlights()
    {
        foreach (var (entry, textBlock) in _captionsByEntry)
        {
            textBlock.Text = GroupEntries.NameOf(entry);
        }
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
        UpdateHeaderTooltip();
        _border.BorderBrush = new SolidColorBrush(ComputeGroupBorderColor());

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

    /// <summary>
    /// Derived from the face colour rather than the theme's own border swatch, so the
    /// outline always reads against whatever the group is painted with: 10% lighter than
    /// the face, except when the face is white (or close enough that lightening it would
    /// go nowhere), where 10% darker is what actually shows.
    /// </summary>
    private Color ComputeGroupBorderColor()
    {
        var face = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;
        return RelativeLuminance(face) > 0.9 ? ShiftColor(face, -0.10) : ShiftColor(face, 0.10);
    }

    /// <summary>A positive factor moves each channel toward white, a negative one toward black.</summary>
    private static Color ShiftColor(Color color, double factor)
    {
        byte Shift(byte channel) => factor >= 0
            ? (byte)Math.Clamp(channel + ((255 - channel) * factor), 0, 255)
            : (byte)Math.Clamp(channel * (1 + factor), 0, 255);

        return Color.FromArgb(color.A, Shift(color.R), Shift(color.G), Shift(color.B));
    }

    private void ApplyHeaderBackground()
    {
        _header.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, _category.TitleOpacity);
    }

    /// <summary>
    /// A hover tooltip on the caption summarising the group's own settings — style,
    /// arrangement, icon size, opacity — so they are visible without opening the menu.
    /// Recomputed rather than tracked, since it is cheap and there is no single choke
    /// point every setting change already passes through.
    /// </summary>
    private void UpdateHeaderTooltip()
    {
        var lines = new List<string>
        {
            LocalizationService.Get(IsAppFolder ? "group.styleAppFolder" : "group.stylePanel")
        };

        var arrangementKey = _category.IconArrangement switch
        {
            IconArrangement.Grid => "group.arrangeIcons",
            IconArrangement.ByName => "group.sortByName",
            IconArrangement.ByType => "group.sortByType",
            _ => null
        };
        if (arrangementKey is not null)
        {
            lines.Add(LocalizationService.Get(arrangementKey));
        }

        var iconSizeKey = _category.DesktopIconScale switch
        {
            <= 0.8 => "group.iconSizeSmall",
            >= 1.4 => "group.iconSizeLarge",
            _ => "group.iconSizeMedium"
        };
        lines.Add($"{LocalizationService.Get("group.iconSize")}: {LocalizationService.Get(iconSizeKey)}");

        lines.Add($"{LocalizationService.Get("group.areaOpacity")}: {_category.AreaOpacity * 100:0}%");

        if (_category.ShowBadge)
        {
            lines.Add(LocalizationService.Get("group.showBadge"));
        }

        _headerText.ToolTip = null;
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

        AddMenuItem(menu, LocalizationService.Get("group.newShortcut"), CreateShortcut, "IconAdd");

        var canPaste = System.Windows.Clipboard.ContainsFileDropList() || _pendingCuts.Count > 0;
        var pasteItem = CreateMenuItem(LocalizationService.Get("group.paste") + "\tCtrl+V", "IconPaste");
        pasteItem.IsEnabled = canPaste;
        pasteItem.Click += (_, _) => PasteFromClipboard();
        menu.Items.Add(pasteItem);

        menu.Items.Add(BuildSeparator());

        AddCheckItem(menu, LocalizationService.Get("group.arrangeIcons"), _category.IconArrangement != IconArrangement.None, ToggleArrangeIconsAutomatically, "IconGrid");

        var sortMenu = CreateMenuItem(LocalizationService.Get("group.sortBy"), "IconSort");
        AddCheckItem(sortMenu, LocalizationService.Get("group.sortByName"), _category.IconArrangement == IconArrangement.ByName, SortByName);
        AddCheckItem(sortMenu, LocalizationService.Get("group.sortByType"), _category.IconArrangement == IconArrangement.ByType, SortByType);
        menu.Items.Add(sortMenu);

        var sizeMenu = CreateMenuItem(LocalizationService.Get("group.iconSize"), "IconIconSize");
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeSmall"), IsIconScale(0.75), () => SetIconScale(0.75));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeMedium"), IsIconScale(1.0), () => SetIconScale(1.0));
        AddCheckItem(sizeMenu, LocalizationService.Get("group.iconSizeLarge"), IsIconScale(1.5), () => SetIconScale(1.5));
        menu.Items.Add(sizeMenu);

        var orderMenu = CreateMenuItem(LocalizationService.Get("group.windowOrder"), "IconSort");
        AddMenuItem(orderMenu, LocalizationService.Get("group.bringAllToFront"), BringAllGroupsToFront);
        AddMenuItem(orderMenu, LocalizationService.Get("group.sendOthersToBack"), SendOthersToBack);
        AddMenuItem(orderMenu, LocalizationService.Get("group.sendAllToBack"), SendAllToBack);
        menu.Items.Add(orderMenu);

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

            menu.Items.Add(BuildSeparator());
            AddMenuItem(menu, LocalizationService.Get("tray.settings"), _commands.OpenSettings, "IconSettings");
        }

        menu.Items.Add(BuildSeparator());

        AddMenuItem(menu, LocalizationService.Get("group.close"), CloseGroup, "IconClose");

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
        AddMenuItem(menu, LocalizationService.Get("item.cut") + "\tCtrl+X", () => CopySelectedToClipboard(cut: true), "IconCut");
        AddMenuItem(menu, LocalizationService.Get("item.copy") + "\tCtrl+C", () => CopySelectedToClipboard(cut: false), "IconCopy");
        menu.Items.Add(BuildSeparator());
        AddMenuItem(menu, LocalizationService.Get("item.removeFromGroup"), () => RemoveEntryRespectingSelection(item, () => RemoveItem(item)), "IconDelete");

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
        AddMenuItem(menu, LocalizationService.Get("item.cut") + "\tCtrl+X", () => CopySelectedToClipboard(cut: true), "IconCut");
        AddMenuItem(menu, LocalizationService.Get("item.copy") + "\tCtrl+C", () => CopySelectedToClipboard(cut: false), "IconCopy");
        menu.Items.Add(BuildSeparator());

        var isEmpty = GroupEntries.Count(folder) == 0;
        var partOfMultiSelection = _selectedEntries.Count > 1 && _selectedEntries.Contains(folder);
        var removeItem = CreateMenuItem(LocalizationService.Get("item.removeFromGroup"), "IconDelete");
        removeItem.IsEnabled = isEmpty || partOfMultiSelection;
        removeItem.Click += (_, _) => RemoveEntryRespectingSelection(folder, () => RemoveFolder(folder));
        if (!isEmpty && !partOfMultiSelection)
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

    private void CreateShortcut()
    {
        var newItem = new LaunchItem
        {
            Name = string.Empty,
            Type = LaunchItemType.Application,
            Target = string.Empty,
            IsDesktopPinned = true
        };
        var editor = new Settings.LaunchItemEditWindow(newItem)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true
        };
        if (editor.ShowDialog() == true)
        {
            newItem.IsDesktopPinned = true;
            if (newItem.DesktopIconX is null || newItem.DesktopIconY is null)
            {
                var index = _category.Items.Count + _category.Categories.Count;
                var usableWidth = _category.DesktopWidth - (PaddingX * 2);
                var columns = Math.Max(1, (int)(usableWidth / (TileSize * _category.DesktopIconScale)));
                newItem.DesktopIconX = PaddingX + ((index % columns) * TileSize);
                newItem.DesktopIconY = PaddingY + ((index / columns) * TileSize);
            }

            _category.Items.Add(newItem);
            FinishStructuralChange();
            _onLayoutChanged(_category);
        }
    }

    private void CreateSubfolder()
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.folderNamePrompt"), string.Empty);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _category.Categories.Add(new MenuCategory { Name = prompt.Value });
        FinishStructuralChange();
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
        FinishStructuralChange();
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
        FinishStructuralChange();
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
        FinishStructuralChange();
    }

    private void RenameFolder(MenuCategory folder)
    {
        var prompt = new Settings.TextPromptWindow(LocalizationService.Get("group.folderNamePrompt"), folder.Name);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        folder.Name = prompt.Value;
        FinishStructuralChange();
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
        FinishStructuralChange();
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
        SetResizeHandlesVisible(!collapsed);

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
            offset += 16;
        }

        FinishStructuralChange();
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
        _captionsByEntry.Clear();
        _openActionsByEntry.Clear();
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

        foreach (var item in _category.Items)
        {
            item.IsDesktopPinned = true;
            AddTile(
                item,
                item.DesktopIconX ?? PaddingX + ((index % 3) * TileSize),
                item.DesktopIconY ?? PaddingY + ((index / 3) * TileSize));
            index++;
        }

        RefreshFolderTile();
        RefreshCutVisuals();
    }

    private void ToggleArrangeIconsAutomatically()
    {
        if (_category.IconArrangement != IconArrangement.None)
        {
            _category.IconArrangement = IconArrangement.None;
            _onLayoutChanged(_category);
            UpdateHeaderTooltip();
        }
        else
        {
            SortByName();
        }
    }

    private static void BringAllGroupsToFront()
    {
        foreach (var window in _allGroupWindows.ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    private void SendOthersToBack()
    {
        foreach (var window in _allGroupWindows.Where(w => !ReferenceEquals(w, this)).ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        var thisHwnd = new WindowInteropHelper(this).Handle;
        if (thisHwnd != IntPtr.Zero)
        {
            SetWindowPos(thisHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static void SendAllToBack()
    {
        foreach (var window in _allGroupWindows.ToList())
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    private void SortByName()
    {
        _category.IconArrangement = IconArrangement.ByName;
        ArrangeInGrid(NameOrder());
    }

    private void SortByType()
    {
        _category.IconArrangement = IconArrangement.ByType;
        ArrangeInGrid(TypeOrder());
    }

    private IEnumerable<object> GridOrder()
    {
        return _category.Categories
            .Cast<object>()
            .Concat(_category.Items);
    }

    private IEnumerable<object> NameOrder()
    {
        var folders = _category.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        var items = _category.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
        return folders.Cast<object>().Concat(items);
    }

    private IEnumerable<object> TypeOrder()
    {
        var folders = _category.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
        var items = _category.Items
            .OrderBy(i => i.Type)
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase);
        return folders.Cast<object>().Concat(items);
    }

    /// <summary>
    /// Keeps a live arrangement (grid / by name / by type) honest after the set of icons
    /// changes underneath it — a drop, a paste, a delete, a rename. Without this, picking
    /// "sort by name" would only ever reflect the moment it was clicked instead of
    /// following the group as it actually stands. Free placement (<see cref="IconArrangement.None"/>)
    /// just persists and repaints, same as before this existed.
    /// </summary>
    private void FinishStructuralChange()
    {
        UpdateHeaderTooltip();

        switch (_category.IconArrangement)
        {
            case IconArrangement.Grid:
                ArrangeInGrid(GridOrder());
                return;
            case IconArrangement.ByName:
                ArrangeInGrid(NameOrder());
                return;
            case IconArrangement.ByType:
                ArrangeInGrid(TypeOrder());
                return;
            default:
                PopulateTiles();
                _onLayoutChanged(_category);
                return;
        }
    }

    /// <summary>
    /// Lays entries out on a grid sized for the panel as it stands right now — its own
    /// width and the icons' own scale both feed the cell size, so a resize or a zoom
    /// change reflows the grid instead of leaving it arranged for a size that no longer
    /// applies.
    /// </summary>
    private void ArrangeInGrid(IEnumerable<object> orderedEntries)
    {
        // Positions are stored in the canvas's own pre-zoom coordinates — the render
        // transform (_zoomTransform) scales them again on screen. So only how many
        // columns fit needs the icon scale (a bigger icon needs more on-screen room,
        // meaning fewer of them per row); the spacing between those columns must stay
        // in plain, unscaled TileSize units, or the transform would apply the scale
        // twice and blow the grid past the panel's actual width.
        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / (TileSize * _category.DesktopIconScale)));
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
        FinishStructuralChange();
    }

    private void AttachTileBehavior(FrameworkElement tile, object entry, Action onOpen, Action<double, double> onMoved)
    {
        _tilesByEntry[entry] = tile;
        _openActionsByEntry[entry] = onOpen;
        AttachHoverEffect(tile, entry);

        // O tile so' existe dentro do Canvas desta janela - arrastar movendo Canvas.Left/Top
        // (como era antes) faz o icone sumir assim que ele passa da borda da PROPRIA janela,
        // porque nao ha nada visivel fora dos limites de uma janela WPF. Por isso o arrasto
        // agora move uma janela-fantasma de verdade (_DragGhostWindow), em coordenadas de
        // tela, que atravessa livremente os limites de qualquer janela - exatamente como o
        // Explorer mostra um icone "flutuando" durante o arraste.
        //
        // Para nao repetir o bug ja documentado (App Folder tile sumindo por causa de
        // PointToScreen chamado duas vezes por MouseMove, misturando o DPI de antes/depois do
        // Left/Top mudar), PointToScreen so' e' chamado sobre "this" - a janela de origem, que
        // nunca se move durante o arrasto - nunca sobre a propria janela-fantasma.
        System.Windows.Point dragStartMouseInWindow = default;
        System.Windows.Point initialTileScreenDip = default;
        System.Windows.Point grabOffset = default;
        double visualWidth = 0;
        double visualHeight = 0;
        var dragging = false;
        var hasMoved = false;
        DragGhostWindow? ghost = null;
        DispatcherTimer? heartbeatTimer = null;
        DispatcherTimer? dragTrackingTimer = null;

        void FinishDrag()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;

            dragTrackingTimer?.Stop();
            dragTrackingTimer = null;

            heartbeatTimer?.Stop();
            StopHeartbeatAnimation(tile);

            tile.ReleaseMouseCapture();

            if (!hasMoved || ghost is null)
            {
                ghost?.Close();
                ghost = null;
                tile.Opacity = 1.0;
                return;
            }

            var finalScreenTopLeft = ghost.CurrentTopLeft;
            ghost.Close();
            ghost = null;
            tile.Opacity = 1.0;

            var screenCenter = new System.Windows.Point(
                finalScreenTopLeft.X + visualWidth / 2,
                finalScreenTopLeft.Y + visualHeight / 2);

            var targetGroup = FindGroupWindowAt(screenCenter, this);
            if (targetGroup is not null)
            {
                MoveEntryToOtherGroup(entry, targetGroup, finalScreenTopLeft);
                return;
            }

            var targetInWindow = new System.Windows.Point(
                finalScreenTopLeft.X - Left,
                finalScreenTopLeft.Y - Top);

            System.Windows.Point localTopLeft;
            try
            {
                var transform = TransformToDescendant(_canvas);
                localTopLeft = transform.Transform(targetInWindow);
            }
            catch
            {
                var scale = _category.DesktopIconScale > 0 ? _category.DesktopIconScale : 1.0;
                localTopLeft = new System.Windows.Point(targetInWindow.X / scale, targetInWindow.Y / scale);
            }

            var x = ClampToCanvas(localTopLeft.X, tile.ActualWidth, _canvas.ActualWidth, PaddingX);
            var y = ClampToCanvas(localTopLeft.Y, tile.ActualHeight, _canvas.ActualHeight, PaddingY);
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            onMoved(x, y);
        }

        void UpdateDrag(System.Windows.Point mouseScreenDip)
        {
            if (!dragging)
            {
                return;
            }

            if (!hasMoved)
            {
                var deltaX = mouseScreenDip.X - (Left + dragStartMouseInWindow.X);
                var deltaY = mouseScreenDip.Y - (Top + dragStartMouseInWindow.Y);

                if (Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                hasMoved = true;
                heartbeatTimer?.Stop();
                StopHeartbeatAnimation(tile);

                var dpi = VisualTreeHelper.GetDpi(this);
                ghost ??= DragGhostWindow.Show(
                    tile,
                    visualWidth,
                    visualHeight,
                    initialTileScreenDip,
                    dpi.DpiScaleX,
                    dpi.DpiScaleY,
                    () => tile.Opacity = SelectedTileOpacityWhileDragging);

                dragTrackingTimer?.Stop();
                dragTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(15)
                };
                dragTrackingTimer.Tick += (_, _) =>
                {
                    if (!dragging)
                    {
                        dragTrackingTimer.Stop();
                        return;
                    }

                    var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                    if (!isLButtonDown)
                    {
                        FinishDrag();
                        return;
                    }

                    if (GetCursorPos(out var pt))
                    {
                        var dpiScale = VisualTreeHelper.GetDpi(this);
                        var screenDip = new System.Windows.Point(pt.X / dpiScale.DpiScaleX, pt.Y / dpiScale.DpiScaleY);
                        if (ghost is not null)
                        {
                            ghost.Left = screenDip.X - grabOffset.X;
                            ghost.Top = screenDip.Y - grabOffset.Y;
                        }
                    }
                };
                dragTrackingTimer.Start();
            }

            if (ghost is not null)
            {
                ghost.Left = mouseScreenDip.X - grabOffset.X;
                ghost.Top = mouseScreenDip.Y - grabOffset.Y;
            }
        }

        tile.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                onOpen();
                e.Handled = true;
                return;
            }

            HandleTileSelection(entry);

            dragging = true;
            hasMoved = false;

            // Visual geometry in window DIP space:
            var tileTransform = tile.TransformToAncestor(this);
            var tileVisualOrigin = tileTransform.Transform(new System.Windows.Point(0, 0));
            var tileVisualBottomRight = tileTransform.Transform(new System.Windows.Point(
                tile.ActualWidth > 0 ? tile.ActualWidth : TileSize - 8,
                tile.ActualHeight > 0 ? tile.ActualHeight : TileSize));
            visualWidth = Math.Max(1, Math.Abs(tileVisualBottomRight.X - tileVisualOrigin.X));
            visualHeight = Math.Max(1, Math.Abs(tileVisualBottomRight.Y - tileVisualOrigin.Y));

            var mouseInWindow = e.GetPosition(this);
            dragStartMouseInWindow = mouseInWindow;
            grabOffset = new System.Windows.Point(
                mouseInWindow.X - tileVisualOrigin.X,
                mouseInWindow.Y - tileVisualOrigin.Y);

            initialTileScreenDip = new System.Windows.Point(
                Left + tileVisualOrigin.X,
                Top + tileVisualOrigin.Y);

            tile.CaptureMouse();

            heartbeatTimer?.Stop();
            heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            heartbeatTimer.Tick += (_, _) =>
            {
                heartbeatTimer.Stop();
                if (dragging && !hasMoved)
                {
                    StartHeartbeatAnimation(tile);
                }
            };
            heartbeatTimer.Start();

            e.Handled = true;
        };

        tile.MouseMove += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var currentMouseInWindow = e.GetPosition(this);
            var mouseScreenDip = new System.Windows.Point(
                Left + currentMouseInWindow.X,
                Top + currentMouseInWindow.Y);

            UpdateDrag(mouseScreenDip);
        };

        tile.MouseLeftButtonUp += (_, e) =>
        {
            FinishDrag();
        };

        tile.LostMouseCapture += (_, _) =>
        {
            if (dragging)
            {
                var isLButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (!isLButtonDown)
                {
                    FinishDrag();
                }
            }
        };
    }

    /// <summary>Opacidade do tile de origem enquanto a janela-fantasma o representa em tela - o mesmo "ícone esmaecido" que o Explorer usa durante um arrasto.</summary>
    private const double SelectedTileOpacityWhileDragging = 0.35;

    /// <summary>Whichever other open group's window occupies this screen point, if any.</summary>
    private static DesktopGroupWindow? FindGroupWindowAt(System.Windows.Point screenPoint, DesktopGroupWindow excluding)
    {
        foreach (var window in _allGroupWindows)
        {
            if (ReferenceEquals(window, excluding))
            {
                continue;
            }

            var rect = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
            if (rect.Contains(screenPoint))
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>
    /// Drops a tile dragged past this group's own edge onto whichever group it landed on.
    /// The entry actually moves — removed from this category's own list and added to the
    /// target's — rather than just visually relocating, which is what previously left a
    /// dragged subfolder nowhere at all, or (depending on what handled the drop) folded
    /// into the wrong list and mistaken for a standalone desktop group instead of a
    /// subfolder that belongs to the target.
    /// </summary>
    private void MoveEntryToOtherGroup(object entry, DesktopGroupWindow target, System.Windows.Point tileTopLeftScreen)
    {
        switch (entry)
        {
            case LaunchItem item:
                _category.Items.Remove(item);
                break;
            case MenuCategory folder:
                _category.Categories.Remove(folder);
                if (_openSubfolders.TryGetValue(folder, out var openWindow))
                {
                    openWindow.Close();
                    _openSubfolders.Remove(folder);
                }

                break;
            default:
                return;
        }

        FinishStructuralChange();

        var pointInTargetWindow = new System.Windows.Point(
            tileTopLeftScreen.X - target.Left,
            tileTopLeftScreen.Y - target.Top);

        System.Windows.Point dropPoint;
        try
        {
            var transform = target.TransformToDescendant(target._canvas);
            dropPoint = transform.Transform(pointInTargetWindow);
        }
        catch
        {
            var scale = target._category.DesktopIconScale > 0 ? target._category.DesktopIconScale : 1.0;
            dropPoint = new System.Windows.Point(pointInTargetWindow.X / scale, pointInTargetWindow.Y / scale);
        }

        target.AcceptMovedEntry(entry, dropPoint);
    }

    /// <summary>The other half of <see cref="MoveEntryToOtherGroup"/> — always adds to this
    /// group's own <see cref="MenuCategory.Categories"/>/<see cref="MenuCategory.Items"/>,
    /// never to the top-level configuration, so a moved subfolder stays a subfolder.</summary>
    private void AcceptMovedEntry(object entry, System.Windows.Point canvasPoint)
    {
        var x = Math.Max(PaddingX, canvasPoint.X);
        var y = Math.Max(PaddingY, canvasPoint.Y);

        switch (entry)
        {
            case LaunchItem item:
                item.DesktopIconX = x;
                item.DesktopIconY = y;
                _category.Items.Add(item);
                break;
            case MenuCategory folder:
                folder.IconX = x;
                folder.IconY = y;
                _category.Categories.Add(folder);
                break;
            default:
                return;
        }

        FinishStructuralChange();
        RestoreIfMinimized();
    }

    /// <summary>
    /// The lift a link or card gets on a web page: a soft tint plus a slight scale-up,
    /// eased in and out instead of snapping, so an icon visibly answers the mouse the
    /// instant it arrives. Left alone while the tile is selected — the selection
    /// highlight already says more than a hover tint could.
    /// </summary>
    private void AttachHoverEffect(FrameworkElement tile, object entry)
    {
        tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        tile.RenderTransform = scale;

        tile.MouseEnter += (_, _) =>
        {
            AnimateScale(scale, 1.08);
            if (tile is Panel panel && !_selectedEntries.Contains(entry))
            {
                panel.Background = ThemeBrushes.CreateBrush(_theme.HighlightColor, 0.45);
            }
        };

        tile.MouseLeave += (_, _) =>
        {
            AnimateScale(scale, 1.0);
            if (tile is Panel panel && !_selectedEntries.Contains(entry))
            {
                panel.Background = Brushes.Transparent;
            }
        };
    }

    private static void AnimateScale(ScaleTransform transform, double to)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
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

    private static void StartHeartbeatAnimation(FrameworkElement tile)
    {
        if (tile.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            tile.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            tile.RenderTransform = scale;
        }

        var anim = new DoubleAnimation(1.0, 1.07, TimeSpan.FromMilliseconds(220))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static void StopHeartbeatAnimation(FrameworkElement tile)
    {
        if (tile.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.0;
            scale.ScaleY = 1.0;
        }
    }

    private void HandleTileSelection(object entry)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!_selectedEntries.Remove(entry))
            {
                _selectedEntries.Add(entry);
            }
            _selectionAnchor = entry;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SelectRange(_selectionAnchor ?? entry, entry);
        }
        else
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(entry);
            _selectionAnchor = entry;
        }

        RefreshSelectionVisuals();
    }

    private void SelectRange(object fromEntry, object toEntry)
    {
        var all = GroupEntries.Enumerate(_category).ToList();
        var i1 = all.IndexOf(fromEntry);
        var i2 = all.IndexOf(toEntry);
        if (i1 < 0) i1 = 0;
        if (i2 < 0) i2 = all.Count - 1;

        var start = Math.Min(i1, i2);
        var end = Math.Max(i1, i2);

        _selectedEntries.Clear();
        for (var i = start; i <= end; i++)
        {
            _selectedEntries.Add(all[i]);
        }

        RefreshSelectionVisuals();
    }

    private void SelectEntry(object entry, bool additive)
    {
        if (additive)
        {
            if (!_selectedEntries.Remove(entry))
            {
                _selectedEntries.Add(entry);
            }
            _selectionAnchor = entry;
        }
        else
        {
            _selectedEntries.Clear();
            _selectedEntries.Add(entry);
            _selectionAnchor = entry;
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

    /// <summary>
    /// A right-click's own "Remove" always used to act on just the tile under the mouse,
    /// even with several tiles highlighted — the same click in Explorer acts on the
    /// whole selection instead. Route through <see cref="RemoveSelectedEntries"/> when the
    /// clicked entry is part of a multi-selection; otherwise this one tile is the whole
    /// story, so its own single-item removal (with its own confirmation wording) applies.
    /// </summary>
    private void RemoveEntryRespectingSelection(object entry, Action removeSingle)
    {
        if (_selectedEntries.Count > 1 && _selectedEntries.Contains(entry))
        {
            RemoveSelectedEntries();
            return;
        }

        removeSingle();
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
                    _category.Categories.Remove(folder);
                    if (_openSubfolders.TryGetValue(folder, out var openWindow))
                    {
                        openWindow.Close();
                        _openSubfolders.Remove(folder);
                    }

                    break;
            }
        }

        FinishStructuralChange();
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

        var caption = new TextBlock
        {
            Text = item.Name,
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
        };
        stack.Children.Add(caption);
        _captionsByEntry[item] = caption;

        AttachTileBehavior(
            stack,
            item,
            () => _onExecute(item),
            (x, y) =>
            {
                if (_category.IconArrangement != IconArrangement.None)
                {
                    FinishStructuralChange();
                }
                else
                {
                    _category.IconArrangement = IconArrangement.None;
                    item.DesktopIconX = x;
                    item.DesktopIconY = y;
                    _onLayoutChanged(_category);
                }
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
        // The "Remove" item's enabled state depends on the selection at the moment of
        // the click, not whenever the tile last happened to be rebuilt — rebuild the
        // menu fresh right before it opens rather than let that state go stale.
        stack.PreviewMouseRightButtonDown += (_, _) => stack.ContextMenu = BuildFolderTileContextMenu(folder);

        stack.Children.Add(new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = _theme.IconSize * 1.6,
            Foreground = TileTextBrush(),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var caption = new TextBlock
        {
            Text = folder.Name,
            Foreground = TileTextBrush(),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TileTextShadow()
        };
        stack.Children.Add(caption);
        _captionsByEntry[folder] = caption;

        AttachTileBehavior(
            stack,
            folder,
            () => OpenSubfolder(folder),
            (x, y) =>
            {
                if (_category.IconArrangement != IconArrangement.None)
                {
                    FinishStructuralChange();
                }
                else
                {
                    _category.IconArrangement = IconArrangement.None;
                    folder.IconX = x;
                    folder.IconY = y;
                    _onLayoutChanged(_category);
                }
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
        _monitorPositions.Clear();
        _onLayoutChanged(_category);
    }

    private void SetResizeHandlesVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var handle in _resizeHandles)
        {
            handle.Visibility = visibility;
        }
    }

    private void OnResizeMouseDown(ResizeEdge edge, UIElement source, MouseButtonEventArgs e)
    {
        _resizeEdge = edge;
        _resizeOrigin = PointToScreen(e.GetPosition(this));
        _resizeStartLeft = Left;
        _resizeStartTop = Top;
        _resizeStartWidth = Width;
        _resizeStartHeight = Height;
        source.CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var deltaX = current.X - _resizeOrigin.X;
        var deltaY = current.Y - _resizeOrigin.Y;

        var growsFromLeft = _resizeEdge is ResizeEdge.W or ResizeEdge.NW or ResizeEdge.SW;
        var growsFromTop = _resizeEdge is ResizeEdge.N or ResizeEdge.NE or ResizeEdge.NW;

        var newWidth = _resizeStartWidth;
        if (_resizeEdge is ResizeEdge.E or ResizeEdge.NE or ResizeEdge.SE)
        {
            newWidth = _resizeStartWidth + deltaX;
        }
        else if (growsFromLeft)
        {
            newWidth = _resizeStartWidth - deltaX;
        }

        var newHeight = _resizeStartHeight;
        if (_resizeEdge is ResizeEdge.S or ResizeEdge.SE or ResizeEdge.SW)
        {
            newHeight = _resizeStartHeight + deltaY;
        }
        else if (growsFromTop)
        {
            newHeight = _resizeStartHeight - deltaY;
        }

        newWidth = Math.Max(MinGroupWidth, newWidth);
        newHeight = Math.Max(MinGroupHeight, newHeight);

        // The opposite edge is the anchor: it must not move while this one is dragged.
        var newLeft = growsFromLeft ? _resizeStartLeft + _resizeStartWidth - newWidth : _resizeStartLeft;
        var newTop = growsFromTop ? _resizeStartTop + _resizeStartHeight - newHeight : _resizeStartTop;

        Width = newWidth;
        Height = newHeight;
        Left = newLeft;
        Top = newTop;
        _category.DesktopWidth = newWidth;
        _category.DesktopHeight = newHeight;
        _category.DesktopX = newLeft;
        _category.DesktopY = newTop;

        if (_category.IconArrangement != IconArrangement.None)
        {
            ReflowGridDuringResize();
        }
    }

    private void ReflowGridDuringResize()
    {
        var usableWidth = _category.DesktopWidth - (PaddingX * 2);
        var columns = Math.Max(1, (int)(usableWidth / (TileSize * _category.DesktopIconScale)));
        var ordered = _category.IconArrangement switch
        {
            IconArrangement.ByName => NameOrder(),
            IconArrangement.ByType => TypeOrder(),
            _ => GridOrder()
        };

        var index = 0;
        foreach (var entry in ordered)
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

            if (_tilesByEntry.TryGetValue(entry, out var tile))
            {
                Canvas.SetLeft(tile, x);
                Canvas.SetTop(tile, y);
            }

            index++;
        }
    }

    private void OnResizeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeEdge == ResizeEdge.None)
        {
            return;
        }

        _resizeEdge = ResizeEdge.None;
        ((UIElement)sender).ReleaseMouseCapture();
        _category.DesktopWidth = Width;
        _category.DesktopHeight = Height;
        _category.DesktopX = Left;
        _category.DesktopY = Top;
        FinishStructuralChange();
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

    /// <summary>
    /// Explorer-style type-to-select: with nothing else capturing the keyboard, typing a
    /// name jumps the selection to the first icon whose name starts with it, accumulating
    /// characters within <see cref="TypeAheadTimeout"/> of each other and starting over on
    /// the next keystroke after a pause. This is separate from Ctrl+F — no box appears,
    /// nothing is highlighted inside the name, it is just "start typing, land on the icon".
    /// </summary>
    private void OnPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (_searchBar.Visibility == Visibility.Visible)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _typeAheadLastInput > TypeAheadTimeout)
        {
            _typeAheadBuffer = string.Empty;
        }

        _typeAheadLastInput = now;
        _typeAheadBuffer += e.Text;

        var entries = GroupEntries.Enumerate(_category).ToList();
        var match = entries.FirstOrDefault(entry =>
            GroupEntries.NameOf(entry).StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase));

        if (match is null && _typeAheadBuffer.Length > 1)
        {
            // No entry continues the accumulated buffer — start over from just this
            // keystroke instead, the same recovery Explorer's own type-ahead does.
            _typeAheadBuffer = e.Text;
            match = entries.FirstOrDefault(entry =>
                GroupEntries.NameOf(entry).StartsWith(_typeAheadBuffer, StringComparison.CurrentCultureIgnoreCase));
        }

        if (match is not null)
        {
            SelectEntry(match, additive: false);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // While the find bar is open it owns the keyboard outright: everything from
        // plain letters (typed into the search box further down the tunnel) to Escape
        // and the result list's own Up/Down/Enter is decided here first.
        if (_searchBar.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CloseFindOverlay(rememberQuery: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveSearchSelection(1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSearchSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                ActivateSelectedSearchResult();
                e.Handled = true;
            }

            return;
        }

        // The same shortcut Windows uses to send a window to another monitor.
        if (e.Key is Key.Left or Key.Right
            && Keyboard.Modifiers == (ModifierKeys.Windows | ModifierKeys.Shift))
        {
            MoveToAdjacentMonitor(e.Key == Key.Left ? -1 : +1);
            e.Handled = true;
            return;
        }

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
            else if (e.Key == Key.A)
            {
                SelectAllEntries();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                CopySelectedToClipboard(cut: false);
                e.Handled = true;
            }
            else if (e.Key == Key.X)
            {
                CopySelectedToClipboard(cut: true);
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                OpenFindOverlay();
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F5)
            {
                FinishStructuralChange();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_selectedEntries.Count > 0)
                {
                    foreach (var entry in _selectedEntries.ToList())
                    {
                        if (_openActionsByEntry.TryGetValue(entry, out var open))
                        {
                            open();
                        }
                    }

                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete)
            {
                RemoveSelectedEntries();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2 && _selectedEntries.Count == 1)
            {
                RenameSelectedEntry();
                e.Handled = true;
                return;
            }
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End
            && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            var all = GroupEntries.Enumerate(_category).ToList();
            if (all.Count > 0)
            {
                var usableWidth = _category.DesktopWidth - (PaddingX * 2);
                var cols = Math.Max(1, (int)(usableWidth / (TileSize * _category.DesktopIconScale)));

                var currentIdx = _selectionAnchor is not null ? all.IndexOf(_selectionAnchor) : -1;
                if (currentIdx < 0 && _selectedEntries.Count > 0)
                {
                    currentIdx = all.IndexOf(_selectedEntries.First());
                }

                int targetIdx;
                switch (e.Key)
                {
                    case Key.Left:
                        targetIdx = currentIdx <= 0 ? 0 : currentIdx - 1;
                        break;
                    case Key.Right:
                        targetIdx = currentIdx < 0 ? 0 : Math.Min(all.Count - 1, currentIdx + 1);
                        break;
                    case Key.Up:
                        targetIdx = currentIdx < 0 ? 0 : Math.Max(0, currentIdx - cols);
                        break;
                    case Key.Down:
                        targetIdx = currentIdx < 0 ? 0 : Math.Min(all.Count - 1, currentIdx + cols);
                        break;
                    case Key.Home:
                        targetIdx = 0;
                        break;
                    case Key.End:
                        targetIdx = all.Count - 1;
                        break;
                    default:
                        targetIdx = 0;
                        break;
                }

                var targetEntry = all[targetIdx];
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    SelectRange(_selectionAnchor ?? all[0], targetEntry);
                }
                else
                {
                    _selectedEntries.Clear();
                    _selectedEntries.Add(targetEntry);
                    _selectionAnchor = targetEntry;
                    RefreshSelectionVisuals();
                }

                e.Handled = true;
                return;
            }
        }
    }

    private void SelectAllEntries()
    {
        _selectedEntries.Clear();
        foreach (var entry in GroupEntries.Enumerate(_category))
        {
            _selectedEntries.Add(entry);
        }

        RefreshSelectionVisuals();
    }

    /// <summary>
    /// Puts the selected tiles' underlying files on the Windows clipboard as a real
    /// file drop, so Ctrl+V works here and in Explorer alike. Subfolders are skipped:
    /// they are this app's own grouping, not a real folder on disk, so there is nothing
    /// to hand the clipboard.
    /// </summary>
    private void CopySelectedToClipboard(bool cut)
    {
        var candidates = _selectedEntries
            .OfType<LaunchItem>()
            .Select(item => (item, path: ShellCommands.TryResolveTarget(item, out var resolved) ? resolved : null))
            .Where(x => x.path is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, candidates.Select(x => x.path!).ToArray());
        // The convention Explorer itself uses to tell a paste-as-copy from a paste-as-move.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));

        try
        {
            System.Windows.Clipboard.SetDataObject(data, true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process owns the clipboard right now; nothing to recover.
            return;
        }

        // Whatever was pending before belongs to a clipboard state that no longer
        // exists — this copy or cut just replaced it.
        ClearPendingCuts();

        if (!cut)
        {
            return;
        }

        foreach (var (item, path) in candidates)
        {
            _pendingCuts.Add(new PendingCut(this, item, path!));
        }

        RefreshCutVisuals();
        StartPendingCutWatcher();
    }

    /// <summary>Accepts files copied or cut anywhere in Windows the same way a drag-drop does.</summary>
    private void PasteFromClipboard()
    {
        if (!System.Windows.Clipboard.ContainsFileDropList())
        {
            return;
        }

        var paths = System.Windows.Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        var offset = 0.0;
        foreach (var path in paths)
        {
            var item = new LaunchItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Type = InferType(path),
                Target = path,
                IsDesktopPinned = true,
                DesktopIconX = PaddingX + offset,
                DesktopIconY = PaddingY + offset
            };

            _category.Items.Add(item);
            offset += 16;
        }

        // This paste is what finally consumes a pending Ctrl+X: only now does the
        // source group actually let go of it, whether the paste landed here or in
        // another group in this same app.
        FinalizePendingCuts(paths);
        FinishStructuralChange();
    }

    private sealed record PendingCut(DesktopGroupWindow Owner, LaunchItem Item, string Path);

    private static readonly List<PendingCut> _pendingCuts = new();
    private static DispatcherTimer? _pendingCutWatcher;

    /// <summary>Half-opacity, the same way Explorer dims an icon between Cut and Paste.</summary>
    private void RefreshCutVisuals()
    {
        foreach (var (entry, tile) in _tilesByEntry)
        {
            tile.Opacity = _pendingCuts.Any(p => ReferenceEquals(p.Owner, this) && ReferenceEquals(p.Item, entry))
                ? 0.5
                : 1.0;
        }
    }

    private static void ClearPendingCuts()
    {
        if (_pendingCuts.Count == 0)
        {
            return;
        }

        var owners = _pendingCuts.Select(p => p.Owner).Distinct().ToList();
        _pendingCuts.Clear();
        foreach (var owner in owners)
        {
            owner.RefreshCutVisuals();
        }

        StopPendingCutWatcherIfIdle();
    }

    private static void FinalizePendingCuts(IReadOnlyCollection<string> pastedPaths)
    {
        var matched = _pendingCuts
            .Where(p => pastedPaths.Contains(p.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        RemovePendingCuts(matched);
    }

    private static void RemovePendingCuts(List<PendingCut> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var group in pending.GroupBy(p => p.Owner))
        {
            foreach (var entry in group)
            {
                group.Key._category.Items.Remove(entry.Item);
                _pendingCuts.Remove(entry);
            }

            group.Key.FinishStructuralChange();
        }

        StopPendingCutWatcherIfIdle();
    }

    private static void StartPendingCutWatcher()
    {
        if (_pendingCutWatcher is not null)
        {
            return;
        }

        // Real Explorer never tells us a cut file was pasted elsewhere and physically
        // moved — the only outside signal available is that the file stops existing at
        // the path it was cut from. Polling is the only option; a couple of seconds of
        // lag before the icon disappears is an acceptable trade for not hooking the shell.
        _pendingCutWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pendingCutWatcher.Tick += (_, _) => CheckPendingCutsAgainstDisk();
        _pendingCutWatcher.Start();
    }

    private static void CheckPendingCutsAgainstDisk()
    {
        var gone = _pendingCuts
            .Where(p => !File.Exists(p.Path) && !Directory.Exists(p.Path))
            .ToList();

        RemovePendingCuts(gone);
    }

    private static void StopPendingCutWatcherIfIdle()
    {
        if (_pendingCuts.Count > 0 || _pendingCutWatcher is null)
        {
            return;
        }

        _pendingCutWatcher.Stop();
        _pendingCutWatcher = null;
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
