using System.Windows;
using System.Windows.Forms;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Configuration;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;

namespace EasyWinMenu.App;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private IconCacheService? _iconCache;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        var configuration = configurationStore.Load();

        _iconCache = new IconCacheService(ApplicationPaths.IconCacheDirectory);
        var menu = BuildTrayMenu(configuration, _iconCache);

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "EasyWinMenu",
            ContextMenuStrip = menu
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                menu.Show(Cursor.Position);
            }
        };
    }

    private static ContextMenuStrip BuildTrayMenu(LauncherConfiguration configuration, IconCacheService iconCache)
    {
        var menu = new ContextMenuStrip();

        AddItems(menu.Items, configuration.Items, iconCache);
        AddCategories(menu.Items, configuration.Categories, iconCache);

        if (configuration.Items.Count > 0 || configuration.Categories.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Sair", null, (_, _) => Current.Shutdown());

        return menu;
    }

    private static void AddItems(ToolStripItemCollection collection, IEnumerable<LaunchItem> items, IconCacheService iconCache)
    {
        foreach (var item in items)
        {
            var icon = iconCache.GetIcon(item.Target)?.ToBitmap();
            collection.Add(new ToolStripMenuItem(item.Name, icon, (_, _) => LaunchExecutor.Execute(item)));
        }
    }

    private static void AddCategories(ToolStripItemCollection collection, IEnumerable<MenuCategory> categories, IconCacheService iconCache)
    {
        foreach (var category in categories)
        {
            var categoryMenuItem = new ToolStripMenuItem(category.Name);
            AddItems(categoryMenuItem.DropDownItems, category.Items, iconCache);
            AddCategories(categoryMenuItem.DropDownItems, category.Categories, iconCache);
            collection.Add(categoryMenuItem);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _iconCache?.Dispose();
        base.OnExit(e);
    }
}
