using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// The closed-folder look of a group: a rounded plate holding a 3x3 mosaic of the
/// first few icons, an optional count badge, and the group name underneath.
/// </summary>
internal static class AppFolderTile
{
    public const double PlateSize = 92;
    private const int MosaicColumns = 3;
    private const int MosaicCapacity = MosaicColumns * MosaicColumns;

    public static FrameworkElement Build(MenuCategory category, MenuTheme theme, IconCacheService iconCache)
    {
        var plate = new Border
        {
            Width = PlateSize,
            Height = PlateSize,
            CornerRadius = new CornerRadius(PlateSize * 0.26),
            Padding = new Thickness(PlateSize * 0.11),
            Background = BuildPlateBackground(category, theme),
            Child = BuildMosaic(category, theme, iconCache)
        };

        plate.Effect = DesktopGroupWindow.BuildGroupShadow(theme);

        var plateLayer = new Grid { Width = PlateSize, Height = PlateSize };
        plateLayer.Children.Add(plate);

        var count = GroupEntries.Count(category);
        if (category.ShowBadge && count > 0)
        {
            plateLayer.Children.Add(BuildBadge(count, theme));
        }

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent
        };
        stack.Children.Add(plateLayer);
        stack.Children.Add(BuildCaption(category, theme));

        return stack;
    }

    private static Brush BuildPlateBackground(MenuCategory category, MenuTheme theme)
    {
        var top = (Color)ColorConverter.ConvertFromString(theme.BackgroundColor)!;
        var bottom = Darken(top, 0.75);
        top.A = (byte)Math.Clamp(top.A * theme.Opacity * category.AreaOpacity, 0, 255);
        bottom.A = top.A;

        return new LinearGradientBrush(top, bottom, 90);
    }

    private static Color Darken(Color color, double factor)
    {
        return Color.FromArgb(
            color.A,
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor));
    }

    private static UIElement BuildMosaic(MenuCategory category, MenuTheme theme, IconCacheService iconCache)
    {
        var mosaic = new UniformGrid { Columns = MosaicColumns, Rows = MosaicColumns };

        foreach (var entry in GroupEntries.Enumerate(category).Take(MosaicCapacity))
        {
            mosaic.Children.Add(BuildMosaicCell(entry, theme, iconCache));
        }

        return mosaic;
    }

    private static UIElement BuildMosaicCell(object entry, MenuTheme theme, IconCacheService iconCache)
    {
        var icon = GroupEntries.IconOf(entry, iconCache);
        if (icon is not null)
        {
            return new Image
            {
                Source = icon,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1.5)
            };
        }

        // Subfolders and unresolved targets fall back to a glyph so the mosaic keeps its rhythm.
        return new TextBlock
        {
            Text = entry is MenuCategory ? "" : "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = PlateSize * 0.16,
            Foreground = ThemeBrushes.CreateBrush(theme.TextColor, 0.75),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static UIElement BuildBadge(int count, MenuTheme theme)
    {
        var text = new TextBlock
        {
            Text = count > 99 ? "99+" : count.ToString(),
            Foreground = Brushes.White,
            FontFamily = new FontFamily(theme.ItemFontFamily),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return new Border
        {
            MinWidth = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(6, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, -7, 0),
            Child = text
        };
    }

    private static UIElement BuildCaption(MenuCategory category, MenuTheme theme)
    {
        return new TextBlock
        {
            Text = category.Name,
            Foreground = ThemeBrushes.CreateBrush(theme.TextColor, 1.0),
            FontFamily = new FontFamily(theme.TitleFontFamily),
            FontSize = theme.ItemFontSize,
            FontWeight = theme.TitleBold ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = PlateSize + 24,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Opacity = 0.9,
                Color = Colors.Black
            }
        };
    }
}
