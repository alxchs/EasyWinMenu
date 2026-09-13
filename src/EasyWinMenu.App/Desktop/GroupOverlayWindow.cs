using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// The opened-folder view: a translucent sheet centred on the work area, sized from the
/// panel it belongs to. Subfolders are walked in place rather than in new windows, so
/// Escape always means "one level up, then close".
/// </summary>
internal sealed class GroupOverlayWindow : Window
{
    private const double TileWidth = 118;
    private const double TileHeight = 128;
    private const double MinSheetWidth = 360;
    private const double MinSheetHeight = 260;

    private readonly MenuCategory _category;
    private readonly MenuTheme _theme;
    private readonly IconCacheService _iconCache;
    private readonly Action<LaunchItem> _onExecute;
    private readonly List<MenuCategory> _trail;

    private Grid _root = null!;
    private TextBlock _title = null!;
    private WrapPanel _grid = null!;
    private ScaleTransform _contentScale = null!;
    private bool _closing;
    private bool _armed;

    public GroupOverlayWindow(
        MenuCategory category,
        MenuTheme theme,
        IconCacheService iconCache,
        Action<LaunchItem> onExecute,
        Window anchor)
    {
        _category = category;
        _theme = theme;
        _iconCache = iconCache;
        _onExecute = onExecute;
        _trail = [category];

        WindowStyle = WindowStyle.None;

        // Real translucency rather than a blurred backdrop: DWM's blur only samples the
        // wallpaper, so over any open window it collapses to black. A layered window keeps
        // whatever is behind the sheet visible on every Windows version.
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;

        Content = BuildContent();
        PlaceNear(anchor);

        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnBackdropClick;
        Loaded += (_, _) => PlayOpenAnimation();

        // Close-on-focus-loss stays disarmed until the sheet has finished opening.
        // The click that opens it can bounce activation back to the tile underneath,
        // and an armed handler would close the sheet before it is ever seen.
        Deactivated += OnDeactivated;
    }

    private MenuCategory Current => _trail[^1];

    private FrameworkElement BuildContent()
    {
        _title = new TextBlock
        {
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.TitleFontFamily),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.6, Color = Colors.Black }
        };

        _grid = new WrapPanel { Orientation = Orientation.Horizontal };

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Content = _grid
        };

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            MaxWidth = double.PositiveInfinity,
            Margin = new Thickness(28, 22, 28, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        column.Children.Add(_title);
        column.Children.Add(scroller);

        _contentScale = new ScaleTransform(0.94, 0.94);
        column.RenderTransform = _contentScale;
        column.RenderTransformOrigin = new Point(0.5, 0.4);

        var shell = new Border
        {
            Background = BuildSheetBackground(),
            BorderBrush = ThemeBrushes.CreateBrush(_theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Max(8, _theme.CornerRadius)),
            Effect = DesktopGroupWindow.BuildGroupShadow(_theme),
            Child = column
        };

        _root = new Grid { Background = Brushes.Transparent, Opacity = 0 };
        _root.Children.Add(shell);

        // The column centres itself, so the scroller has to be told how tall it may grow.
        _root.SizeChanged += (_, e) => scroller.MaxHeight = Math.Max(120, e.NewSize.Height - 120);

        Populate();
        return _root;
    }

    private void Populate()
    {
        _title.Text = Current.Name;
        _grid.Children.Clear();

        if (_trail.Count > 1)
        {
            _grid.Children.Add(BuildTile(
                LocalizationService.Get("overlay.back"),
                null,
                "",
                NavigateUp));
        }

        foreach (var entry in GroupEntries.Enumerate(Current))
        {
            switch (entry)
            {
                case MenuCategory folder:
                    _grid.Children.Add(BuildTile(folder.Name, null, "", () => NavigateInto(folder)));
                    break;
                case LaunchItem item:
                    _grid.Children.Add(BuildTile(
                        item.Name,
                        GroupEntries.IconOf(item, _iconCache),
                        "",
                        () =>
                        {
                            _onExecute(item);
                            BeginClose();
                        }));
                    break;
            }
        }

        if (_grid.Children.Count == 0)
        {
            _grid.Children.Add(new TextBlock
            {
                Text = LocalizationService.Get("overlay.empty"),
                Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 0.7),
                FontFamily = new FontFamily(_theme.ItemFontFamily),
                FontSize = _theme.ItemFontSize + 2
            });
        }
    }

    private FrameworkElement BuildTile(string caption, ImageSource? icon, string glyphFallback, Action onActivate)
    {
        var visual = icon is not null
            ? new Image
            {
                Source = icon,
                Width = 56,
                Height = 56,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            }
            : (FrameworkElement)new TextBlock
            {
                Text = glyphFallback,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 48,
                Foreground = ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

        var label = new TextBlock
        {
            Text = caption,
            Foreground = ThemeBrushes.CreateBrush(_theme.TextColor, 1.0),
            FontFamily = new FontFamily(_theme.ItemFontFamily),
            FontSize = _theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(visual);
        stack.Children.Add(label);

        var tile = new Border
        {
            Width = TileWidth,
            Height = TileHeight,
            Margin = new Thickness(4),
            Padding = new Thickness(8, 14, 8, 8),
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = stack
        };

        var hover = ThemeBrushes.CreateBrush(_theme.HighlightColor, 1.0);
        tile.MouseEnter += (_, _) => tile.Background = hover;
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            onActivate();
        };

        return tile;
    }

    private void NavigateInto(MenuCategory folder)
    {
        _trail.Add(folder);
        Populate();
        PlayNavigationAnimation();
    }

    private void NavigateUp()
    {
        if (_trail.Count <= 1)
        {
            return;
        }

        _trail.RemoveAt(_trail.Count - 1);
        Populate();
        PlayNavigationAnimation();
    }

    /// <summary>
    /// A phone opens a folder full screen; a desktop has other windows to keep in view.
    /// The sheet therefore grows to at most four times the panel's area — twice its
    /// width and twice its height — and is then held to 60% of the working width and
    /// 50% of its height. It is centred on the work area, which already excludes the
    /// taskbar.
    /// </summary>
    private void PlaceNear(Window anchor)
    {
        var handle = new WindowInteropHelper(anchor).Handle;
        var screen = handle == IntPtr.Zero
            ? System.Windows.Forms.Screen.PrimaryScreen!
            : System.Windows.Forms.Screen.FromHandle(handle);

        var transform = PresentationSource.FromVisual(anchor)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        var workWidth = bottomRight.X - topLeft.X;
        var workHeight = bottomRight.Y - topLeft.Y;

        var width = Math.Min(_category.DesktopWidth * 2, workWidth * 0.6);
        var height = Math.Min(_category.DesktopHeight * 2, workHeight * 0.5);

        Width = Math.Max(MinSheetWidth, width);
        Height = Math.Max(MinSheetHeight, height);
        Left = topLeft.X + ((workWidth - Width) / 2);
        Top = topLeft.Y + ((workHeight - Height) / 2);
    }

    /// <summary>Dark enough to carry the icons, sheer enough to keep the desktop readable behind.</summary>
    private Brush BuildSheetBackground()
    {
        var color = (Color)ColorConverter.ConvertFromString(_theme.BackgroundColor)!;
        color.A = 0xD2;
        return new SolidColorBrush(color);
    }

    private void PlayOpenAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(_theme.AnimationDurationMs, 1) * 1.8);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeIn = new DoubleAnimation(0, 1, duration);
        fadeIn.Completed += (_, _) => _armed = true;
        _root.BeginAnimation(OpacityProperty, fadeIn);
        _contentScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = ease });
        _contentScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = ease });
    }

    private void PlayNavigationAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(_theme.AnimationDurationMs, 1) * 1.2);
        _grid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_armed)
        {
            BeginClose();
            return;
        }

        // Still opening: take focus back instead of closing.
        Activate();
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        // Only a click on the sheet itself closes; tiles mark their own clicks handled.
        BeginClose();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        if (_trail.Count > 1)
        {
            NavigateUp();
            return;
        }

        BeginClose();
    }

    private void BeginClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;

        var duration = TimeSpan.FromMilliseconds(Math.Max(_theme.AnimationDurationMs, 1));
        var fade = new DoubleAnimation(_root.Opacity, 0, duration);
        fade.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, fade);
    }
}
