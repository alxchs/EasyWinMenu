using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Services;
using EasyWinMenu.App.Theming;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using Image = System.Windows.Controls.Image;
using ItemCollection = System.Windows.Controls.ItemCollection;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using Style = System.Windows.Style;
using Border = System.Windows.Controls.Border;

namespace EasyWinMenu.App.Menu;

internal static class TrayMenuBuilder
{
    public static ContextMenu Build(
        LauncherConfiguration configuration,
        IconCacheService iconCache,
        bool startWithWindowsEnabled,
        bool hasOpenGroups,
        Action openSettings,
        Action createDesktopGroup,
        Action toggleCollapseAll,
        Action gatherAll,
        Action<bool> setStartWithWindows,
        Action exit)
    {
        var theme = configuration.Theme;
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.Resources["EasyWinMenuContextMenuStyle"]
        };
        ApplyPanelAppearance(menu, theme);
        ApplyOpenAnimation(menu, theme);

        AddContainerItems(menu.Items, configuration, theme, iconCache);

        if (configuration.Items.Count > 0 || configuration.Categories.Count > 0)
        {
            menu.Items.Add(BuildSeparator(theme));
        }

        var newGroupItem = new MenuItem { Header = LocalizationService.Get("tray.newDesktopGroup") };
        ApplyMenuItemAppearance(newGroupItem, theme, theme, "IconAdd");
        newGroupItem.Click += (_, _) => createDesktopGroup();
        menu.Items.Add(newGroupItem);

        var collapseAllItem = new MenuItem
        {
            Header = LocalizationService.Get("desktop.toggleCollapseAll"),
            IsEnabled = hasOpenGroups
        };
        ApplyMenuItemAppearance(collapseAllItem, theme, theme, "IconChevronUp");
        collapseAllItem.Click += (_, _) => toggleCollapseAll();
        menu.Items.Add(collapseAllItem);

        var gatherAllItem = new MenuItem
        {
            Header = LocalizationService.Get("desktop.gatherAll"),
            IsEnabled = hasOpenGroups
        };
        ApplyMenuItemAppearance(gatherAllItem, theme, theme, "IconGather");
        gatherAllItem.Click += (_, _) => gatherAll();
        menu.Items.Add(gatherAllItem);

        menu.Items.Add(BuildSeparator(theme));

        var startupItem = new MenuItem
        {
            Header = LocalizationService.Get("tray.startWithWindows"),
            IsChecked = startWithWindowsEnabled
        };
        ApplyMenuItemAppearance(startupItem, theme, theme, startWithWindowsEnabled ? "IconCheck" : "IconStartup");
        startupItem.Click += (_, _) => setStartWithWindows(!startWithWindowsEnabled);
        menu.Items.Add(startupItem);

        var settingsItem = new MenuItem { Header = LocalizationService.Get("tray.settings") };
        ApplyMenuItemAppearance(settingsItem, theme, theme, "IconSettings");
        settingsItem.Click += (_, _) => openSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(BuildSeparator(theme));

        var exitItem = new MenuItem { Header = LocalizationService.Get("tray.exit") };
        ApplyMenuItemAppearance(exitItem, theme, theme, "IconPower");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static void AddContainerItems(ItemCollection collection, IMenuContainer container, MenuTheme theme, IconCacheService iconCache)
    {
        foreach (var item in container.Items)
        {
            var menuItem = new MenuItem
            {
                Header = item.Name,
                Icon = BuildIcon(item, theme, iconCache)
            };
            ApplyMenuItemAppearance(menuItem, theme, theme);
            menuItem.Click += (_, _) => LaunchExecutor.Execute(item);
            collection.Add(menuItem);
        }

        foreach (var category in container.Categories)
        {
            var categoryTheme = category.ThemeOverride ?? theme;
            var categoryItem = new MenuItem { Header = category.Name };
            ApplyMenuItemAppearance(categoryItem, theme, categoryTheme);
            AddContainerItems(categoryItem.Items, category, categoryTheme, iconCache);
            collection.Add(categoryItem);
        }
    }

    private static void ApplyOpenAnimation(ContextMenu menu, MenuTheme theme)
    {
        menu.Opacity = 0;
        menu.Loaded += (_, _) =>
        {
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(theme.AnimationDurationMs));
            menu.BeginAnimation(UIElement.OpacityProperty, animation);
        };
    }

    private static void ApplyPanelAppearance(Control control, MenuTheme panelTheme)
    {
        control.Background = ThemeBrushes.CreateBrush(panelTheme.BackgroundColor, panelTheme.Opacity);
        control.BorderBrush = ThemeBrushes.CreateBrush(panelTheme.BorderColor, 1.0);
        control.BorderThickness = new Thickness(1);
        MenuThemeProperties.SetPanelCornerRadius(control, new CornerRadius(panelTheme.CornerRadius));

        if (panelTheme.ShowShadow)
        {
            control.Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.35,
                Color = Colors.Black
            };
        }
    }

    private static void ApplyMenuItemAppearance(MenuItem item, MenuTheme rowTheme, MenuTheme panelTheme, string? iconKey = null)
    {
        item.Icon ??= BuildCommandIcon(iconKey, rowTheme);
        item.Style = (Style)Application.Current.Resources["EasyWinMenuMenuItemStyle"];
        item.Foreground = ThemeBrushes.CreateBrush(rowTheme.TextColor, 1.0);
        item.FontFamily = new FontFamily(rowTheme.ItemFontFamily);
        item.FontSize = rowTheme.ItemFontSize;
        item.Padding = new Thickness(rowTheme.ItemPadding, rowTheme.ItemPadding / 2, rowTheme.ItemPadding, rowTheme.ItemPadding / 2);
        item.Margin = new Thickness(2, rowTheme.ItemSpacing / 2, 2, rowTheme.ItemSpacing / 2);
        MenuThemeProperties.SetHighlightBrush(item, ThemeBrushes.CreateBrush(rowTheme.HighlightColor, 1.0));
        MenuThemeProperties.SetHighlightCornerRadius(item, new CornerRadius(4));

        ApplyPanelAppearance(item, panelTheme);
    }

    /// <summary>
    /// A line icon for a command row, or an empty box of the same size when the row has
    /// none, so every header in the menu starts on the same vertical line.
    /// </summary>
    private static UIElement BuildCommandIcon(string? iconKey, MenuTheme theme)
    {
        var size = theme.IconSize;
        if (iconKey is null)
        {
            return new Border { Width = size, Height = size };
        }

        return AppIcons.Create(iconKey, ThemeBrushes.CreateBrush(theme.TextColor, 0.85), size)
            ?? new Border { Width = size, Height = size };
    }

    private static Separator BuildSeparator(MenuTheme theme)
    {
        return new Separator
        {
            Background = ThemeBrushes.CreateBrush(theme.BorderColor, 1.0),
            Height = 1,
            Margin = new Thickness(6, 4, 6, 4)
        };
    }

    private static UIElement BuildIcon(LaunchItem item, MenuTheme theme, IconCacheService iconCache)
    {
        var icon = iconCache.GetIcon(item.IconOverridePath ?? item.Target);
        if (icon is null)
        {
            return new Border { Width = theme.IconSize, Height = theme.IconSize };
        }

        return new Image
        {
            Source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
            Width = theme.IconSize,
            Height = theme.IconSize
        };
    }
}
