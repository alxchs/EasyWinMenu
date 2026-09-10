using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextAlignment = System.Windows.TextAlignment;
using TextBlock = System.Windows.Controls.TextBlock;
using TextWrapping = System.Windows.TextWrapping;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WrapPanel = System.Windows.Controls.WrapPanel;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopGroupWindow : Window
{
    private readonly MenuCategory _category;
    private readonly Action<MenuCategory> _onLayoutChanged;

    private bool _resizing;
    private System.Windows.Point _resizeStart;
    private double _startWidth;
    private double _startHeight;

    public DesktopGroupWindow(
        MenuCategory category,
        MenuTheme theme,
        IconCacheService iconCache,
        Action<LaunchItem> onExecute,
        Action<MenuCategory> onLayoutChanged)
    {
        _category = category;
        _onLayoutChanged = onLayoutChanged;

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

        Content = BuildContent(category, theme, iconCache, onExecute);
    }

    private FrameworkElement BuildContent(MenuCategory category, MenuTheme theme, IconCacheService iconCache, Action<LaunchItem> onExecute)
    {
        var panel = new DockPanel();

        var header = new Border
        {
            Background = ThemeBrushes.CreateBrush(theme.HighlightColor, 1.0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.SizeAll,
            Child = new TextBlock
            {
                Text = category.Name,
                Foreground = ThemeBrushes.CreateBrush(theme.TextColor, 1.0),
                FontFamily = new FontFamily(theme.TitleFontFamily),
                FontSize = theme.TitleFontSize,
                FontWeight = theme.TitleBold ? FontWeights.Bold : FontWeights.Normal
            }
        };
        header.MouseLeftButtonDown += OnHeaderMouseLeftButtonDown;
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var resizeGrip = new Border
        {
            Width = 14,
            Height = 14,
            Background = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.SizeNWSE
        };
        resizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
        resizeGrip.MouseMove += OnResizeGripMouseMove;
        resizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;
        DockPanel.SetDock(resizeGrip, Dock.Bottom);
        panel.Children.Add(resizeGrip);

        var itemsPanel = new WrapPanel { Margin = new Thickness(8) };
        foreach (var item in category.Items.Where(i => i.IsDesktopPinned))
        {
            itemsPanel.Children.Add(BuildTile(item, theme, iconCache, onExecute));
        }

        panel.Children.Add(itemsPanel);

        var border = new Border
        {
            Background = ThemeBrushes.CreateBrush(theme.BackgroundColor, theme.Opacity),
            BorderBrush = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(theme.CornerRadius),
            Child = panel
        };

        if (theme.ShowShadow)
        {
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Colors.Black
            };
        }

        return border;
    }

    private static FrameworkElement BuildTile(LaunchItem item, MenuTheme theme, IconCacheService iconCache, Action<LaunchItem> onExecute)
    {
        var stack = new System.Windows.Controls.StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = 72,
            Margin = new Thickness(4),
            Cursor = Cursors.Hand
        };

        var icon = iconCache.GetIcon(item.IconOverridePath ?? item.Target);
        if (icon is not null)
        {
            stack.Children.Add(new Image
            {
                Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                Width = theme.IconSize * 1.6,
                Height = theme.IconSize * 1.6,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = item.Name,
            Foreground = ThemeBrushes.CreateBrush(theme.TextColor, 1.0),
            FontFamily = new FontFamily(theme.ItemFontFamily),
            FontSize = theme.ItemFontSize,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                onExecute(item);
            }
        };

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
        _resizing = true;
        _resizeStart = PointToScreen(e.GetPosition(this));
        _startWidth = Width;
        _startHeight = Height;
        ((UIElement)sender).CaptureMouse();
    }

    private void OnResizeGripMouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        Width = Math.Max(140, _startWidth + (current.X - _resizeStart.X));
        Height = Math.Max(120, _startHeight + (current.Y - _resizeStart.Y));
    }

    private void OnResizeGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing)
        {
            return;
        }

        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        _category.DesktopWidth = Width;
        _category.DesktopHeight = Height;
        _onLayoutChanged(_category);
    }
}
