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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        var configuration = configurationStore.Load();

        var menu = BuildTrayMenu(configuration);

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

    private static ContextMenuStrip BuildTrayMenu(LauncherConfiguration configuration)
    {
        var menu = new ContextMenuStrip();

        AddItems(menu.Items, configuration.Items);
        AddCategories(menu.Items, configuration.Categories);

        if (configuration.Items.Count > 0 || configuration.Categories.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Sair", null, (_, _) => Current.Shutdown());

        return menu;
    }

    private static void AddItems(ToolStripItemCollection collection, IEnumerable<LaunchItem> items)
    {
        foreach (var item in items)
        {
            collection.Add(item.Name, null, (_, _) => LaunchExecutor.Execute(item));
        }
    }

    private static void AddCategories(ToolStripItemCollection collection, IEnumerable<MenuCategory> categories)
    {
        foreach (var category in categories)
        {
            var categoryMenuItem = new ToolStripMenuItem(category.Name);
            AddItems(categoryMenuItem.DropDownItems, category.Items);
            AddCategories(categoryMenuItem.DropDownItems, category.Categories);
            collection.Add(categoryMenuItem);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
