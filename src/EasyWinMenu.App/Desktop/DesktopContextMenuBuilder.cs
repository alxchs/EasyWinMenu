using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Theming;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using FontFamily = System.Windows.Media.FontFamily;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Style = System.Windows.Style;

namespace EasyWinMenu.App.Desktop;

internal static class DesktopContextMenuBuilder
{
    public static ContextMenu Build(
        MenuTheme theme,
        bool hasOpenGroups,
        Action newDesktopGroup,
        Action toggleCollapseAll,
        Action gatherAll,
        Action openSettings)
    {
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.Resources["EasyWinMenuContextMenuStyle"]
        };
        ApplyPanelAppearance(menu, theme);
        ApplyOpenAnimation(menu, theme);

        AddMenuItem(menu, theme, LocalizationService.Get("tray.newDesktopGroup"), newDesktopGroup, true, "IconAdd");
        AddMenuItem(menu, theme, LocalizationService.Get("desktop.toggleCollapseAll"), toggleCollapseAll, hasOpenGroups, "IconChevronUp");
        AddMenuItem(menu, theme, LocalizationService.Get("desktop.gatherAll"), gatherAll, hasOpenGroups, "IconGather");

        menu.Items.Add(new Separator { Background = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0), Height = 1, Margin = new System.Windows.Thickness(6, 4, 6, 4) });
        AddMenuItem(menu, theme, LocalizationService.Get("tray.settings"), openSettings, true, "IconSettings");

        return menu;
    }

    private static void AddMenuItem(ContextMenu menu, MenuTheme theme, string header, Action handler, bool enabled, string? iconKey = null)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            Icon = iconKey is null
                ? new System.Windows.Controls.Border { Width = theme.IconSize, Height = theme.IconSize }
                : AppIcons.Create(iconKey, ThemeBrushes.CreateBrush(theme.TextColor, 0.85), theme.IconSize)
        };
        ApplyMenuItemAppearance(item, theme);
        item.Click += (_, _) => handler();
        menu.Items.Add(item);
    }

    private static void ApplyOpenAnimation(ContextMenu menu, MenuTheme theme)
    {
        menu.Opacity = 0;
        menu.Loaded += (_, _) =>
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(theme.AnimationDurationMs));
            menu.BeginAnimation(System.Windows.UIElement.OpacityProperty, animation);
        };
    }

    private static void ApplyPanelAppearance(ContextMenu menu, MenuTheme theme)
    {
        menu.Background = ThemeBrushes.CreateBrush(theme.BackgroundColor, theme.Opacity);
        menu.BorderBrush = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0);
        menu.BorderThickness = new System.Windows.Thickness(1);
        MenuThemeProperties.SetPanelCornerRadius(menu, new System.Windows.CornerRadius(theme.CornerRadius));

        if (theme.ShowShadow)
        {
            menu.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.35,
                Color = Colors.Black
            };
        }
    }

    private static void ApplyMenuItemAppearance(MenuItem item, MenuTheme theme)
    {
        item.Style = (Style)Application.Current.Resources["EasyWinMenuMenuItemStyle"];
        item.Foreground = ThemeBrushes.CreateBrush(theme.TextColor, 1.0);
        item.FontFamily = new FontFamily(theme.ItemFontFamily);
        item.FontSize = theme.ItemFontSize;
        item.Padding = new System.Windows.Thickness(theme.ItemPadding, theme.ItemPadding / 2, theme.ItemPadding, theme.ItemPadding / 2);
        item.Margin = new System.Windows.Thickness(2, theme.ItemSpacing / 2, 2, theme.ItemSpacing / 2);
        MenuThemeProperties.SetHighlightBrush(item, ThemeBrushes.CreateBrush(theme.HighlightColor, 1.0));
        MenuThemeProperties.SetHighlightCornerRadius(item, new System.Windows.CornerRadius(4));
        MenuThemeProperties.SetPanelCornerRadius(item, new System.Windows.CornerRadius(theme.CornerRadius));
    }
}
