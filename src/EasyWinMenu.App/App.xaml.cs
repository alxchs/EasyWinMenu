using System.Windows;
using System.Windows.Forms;
using EasyWinMenu.App.Services;
using EasyWinMenu.App.Settings;
using EasyWinMenu.Core.Configuration;
using EasyWinMenu.Core.Models;
using Application = System.Windows.Application;

namespace EasyWinMenu.App;

public partial class App : Application
{
    private ConfigurationStore? _configurationStore;
    private LauncherConfiguration? _configuration;
    private IconCacheService? _iconCache;
    private NotifyIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        _configuration = _configurationStore.Load();
        _iconCache = new IconCacheService(ApplicationPaths.IconCacheDirectory);

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "EasyWinMenu",
            ContextMenuStrip = BuildTrayMenu(_configuration, _iconCache)
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                _trayIcon.ContextMenuStrip!.Show(Cursor.Position);
            }
        };
    }

    private ContextMenuStrip BuildTrayMenu(LauncherConfiguration configuration, IconCacheService iconCache)
    {
        var menu = new ContextMenuStrip();

        AddContainer(menu.Items, configuration, iconCache);

        if (configuration.Items.Count > 0 || configuration.Categories.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Configurações...", null, (_, _) => OpenSettingsWindow());

        var startWithWindowsItem = new ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled()
        };
        startWithWindowsItem.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(startWithWindowsItem.Checked);
        menu.Items.Add(startWithWindowsItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Current.Shutdown());

        return menu;
    }

    private static void AddContainer(ToolStripItemCollection collection, IMenuContainer container, IconCacheService iconCache)
    {
        foreach (var item in container.Items)
        {
            var icon = iconCache.GetIcon(item.IconOverridePath ?? item.Target)?.ToBitmap();
            collection.Add(new ToolStripMenuItem(item.Name, icon, (_, _) => LaunchExecutor.Execute(item)));
        }

        foreach (var category in container.Categories)
        {
            var categoryMenuItem = new ToolStripMenuItem(category.Name);
            AddContainer(categoryMenuItem.DropDownItems, category, iconCache);
            collection.Add(categoryMenuItem);
        }
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_configuration!);
        _settingsWindow.ConfigurationSaved += OnConfigurationSaved;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow.ConfigurationSaved -= OnConfigurationSaved;
            _settingsWindow = null;
        };
        _settingsWindow.Show();
    }

    private void OnConfigurationSaved(object? sender, EventArgs e)
    {
        _configurationStore!.Save(_configuration!);

        var previousMenu = _trayIcon!.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = BuildTrayMenu(_configuration!, _iconCache!);
        previousMenu?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _iconCache?.Dispose();
        base.OnExit(e);
    }
}
