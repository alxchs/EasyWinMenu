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
using Canvas = System.Windows.Controls.Canvas;
using Cursors = System.Windows.Input.Cursors;
using Dock = System.Windows.Controls.Dock;
using DockPanel = System.Windows.Controls.DockPanel;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using StackPanel = System.Windows.Controls.StackPanel;
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
    private readonly Action<MenuCategory> _onLayoutChanged;
    private readonly ScaleTransform _zoomTransform;

    private bool _resizingGroup;
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

        Content = BuildContent(category, theme, iconCache, onExecute);

        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += OnPreviewKeyDown;
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

        var canvas = new Canvas
        {
            Background = Brushes.Transparent,
            RenderTransform = _zoomTransform,
            RenderTransformOrigin = new System.Windows.Point(0, 0)
        };

        var index = 0;
        foreach (var item in category.Items.Where(i => i.IsDesktopPinned))
        {
            var tile = BuildTile(item, theme, iconCache, onExecute, canvas);
            var x = item.DesktopIconX ?? (index % 3) * TileSize;
            var y = item.DesktopIconY ?? (index / 3) * TileSize;
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
            index++;
        }

        panel.Children.Add(canvas);

        var border = new Border
        {
            Background = ThemeBrushes.CreateBrush(theme.BackgroundColor, theme.Opacity),
            BorderBrush = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(theme.CornerRadius),
            ClipToBounds = true,
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

    private FrameworkElement BuildTile(LaunchItem item, MenuTheme theme, IconCacheService iconCache, Action<LaunchItem> onExecute, Canvas canvas)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = TileSize - 8,
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

        System.Windows.Point dragStart = default;
        System.Windows.Point tileStart = default;
        var dragging = false;

        stack.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                onExecute(item);
                return;
            }

            dragging = true;
            dragStart = e.GetPosition(canvas);
            tileStart = new System.Windows.Point(Canvas.GetLeft(stack), Canvas.GetTop(stack));
            stack.CaptureMouse();
        };

        stack.MouseMove += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            var current = e.GetPosition(canvas);
            var delta = current - dragStart;
            Canvas.SetLeft(stack, tileStart.X + delta.X);
            Canvas.SetTop(stack, tileStart.Y + delta.Y);
        };

        stack.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            stack.ReleaseMouseCapture();
            item.DesktopIconX = Canvas.GetLeft(stack);
            item.DesktopIconY = Canvas.GetTop(stack);
            _onLayoutChanged(_category);
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
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

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
    }

    private void ApplyZoomDelta(double delta)
    {
        var newScale = Math.Clamp(_category.DesktopIconScale + delta, MinIconScale, MaxIconScale);
        if (Math.Abs(newScale - _category.DesktopIconScale) < 0.001)
        {
            return;
        }

        _category.DesktopIconScale = newScale;
        _zoomTransform.ScaleX = newScale;
        _zoomTransform.ScaleY = newScale;
        _onLayoutChanged(_category);
    }
}
