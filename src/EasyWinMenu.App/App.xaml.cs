using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using EasyWinMenu.App.Desktop;
using EasyWinMenu.App.Localization;
using EasyWinMenu.App.Menu;
using EasyWinMenu.App.Services;
using EasyWinMenu.App.Settings;
using EasyWinMenu.App.Theming;
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
    private Window? _menuHost;
    private GlobalHotkeyService? _hotkeyService;
    private DesktopOrganizerService? _desktopOrganizer;
    private DesktopContextMenuHookService? _desktopContextMenuHook;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        _configuration = _configurationStore.Load();
        _iconCache = new IconCacheService(ApplicationPaths.IconCacheDirectory);

        AppThemeService.Apply(_configuration.Behavior.AppTheme);
        LocalizationService.SetLanguage(
            string.IsNullOrEmpty(_configuration.Behavior.Language)
                ? LocalizationService.DetectLanguage()
                : _configuration.Behavior.Language);

        _menuHost = new Window
        {
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        _menuHost.Show();

        _hotkeyService = new GlobalHotkeyService(_menuHost);
        _hotkeyService.HotkeyPressed += ShowTrayMenu;
        _hotkeyService.Apply(_configuration.Behavior);

        _desktopOrganizer = new DesktopOrganizerService(_iconCache);
        _desktopOrganizer.Refresh(_configuration, OnDesktopGroupLayoutChanged, OnDesktopGroupDeleteRequested);

        _desktopContextMenuHook = new DesktopContextMenuHookService(OnDesktopRightClick);
        _desktopContextMenuHook.Start();

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = LocalizationService.Get("common.appName")
        };

        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                ShowTrayMenu();
                return;
            }

            if (args.Button == MouseButtons.Left && _configuration.Behavior.ClickMode == TrayClickMode.SingleClick)
            {
                ShowTrayMenu();
            }
        };

        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left && _configuration.Behavior.ClickMode == TrayClickMode.DoubleClick)
            {
                ShowTrayMenu();
            }
        };
    }

    private void OnDesktopGroupLayoutChanged(MenuCategory category)
    {
        _configurationStore!.Save(_configuration!);
    }

    private void OnDesktopGroupDeleteRequested(MenuCategory category)
    {
        DesktopOrganizerService.RemoveCategory(_configuration!, category);
        _configurationStore!.Save(_configuration!);
        _desktopOrganizer!.Refresh(_configuration!, OnDesktopGroupLayoutChanged, OnDesktopGroupDeleteRequested);
    }

    private void CreateDesktopGroup()
    {
        var prompt = new TextPromptWindow(LocalizationService.Get("group.namePrompt"), string.Empty);
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        _configuration!.Categories.Add(new MenuCategory { Name = prompt.Value, IsDesktopGroup = true });
        _configurationStore!.Save(_configuration);
        _desktopOrganizer!.Refresh(_configuration, OnDesktopGroupLayoutChanged, OnDesktopGroupDeleteRequested);
    }

    private void ShowTrayMenu()
    {
        var screenPoint = System.Windows.Forms.Cursor.Position;

        _menuHost!.Left = screenPoint.X;
        _menuHost.Top = screenPoint.Y;

        var menu = TrayMenuBuilder.Build(
            _configuration!,
            _iconCache!,
            StartupRegistration.IsEnabled(),
            OpenSettingsWindow,
            CreateDesktopGroup,
            StartupRegistration.SetEnabled,
            Shutdown);

        var cursorPosition = ToDeviceIndependentPoint(screenPoint);

        menu.PlacementTarget = _menuHost;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursorPosition.X;
        menu.VerticalOffset = cursorPosition.Y;
        menu.IsOpen = true;
    }

    private void OnDesktopRightClick(System.Drawing.Point screenPoint)
    {
        _menuHost!.Left = screenPoint.X;
        _menuHost.Top = screenPoint.Y;

        var menu = DesktopContextMenuBuilder.Build(
            _configuration!.Theme,
            _desktopOrganizer!.HasOpenGroups,
            CreateDesktopGroup,
            _desktopOrganizer.ToggleCollapseAll,
            _desktopOrganizer.GatherAll,
            OpenSettingsWindow);

        var devicePoint = ToDeviceIndependentPoint(screenPoint);

        menu.PlacementTarget = _menuHost;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = devicePoint.X;
        menu.VerticalOffset = devicePoint.Y;
        menu.IsOpen = true;
    }

    private System.Windows.Point ToDeviceIndependentPoint(System.Drawing.Point screenPoint)
    {
        var transform = PresentationSource.FromVisual(_menuHost!)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new System.Windows.Point(screenPoint.X, screenPoint.Y));
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
        _hotkeyService!.Apply(_configuration!.Behavior);
        _desktopOrganizer!.Refresh(_configuration!, OnDesktopGroupLayoutChanged, OnDesktopGroupDeleteRequested);
        AppThemeService.Apply(_configuration!.Behavior.AppTheme);
        LocalizationService.SetLanguage(
            string.IsNullOrEmpty(_configuration!.Behavior.Language)
                ? LocalizationService.DetectLanguage()
                : _configuration.Behavior.Language);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _iconCache?.Dispose();
        _hotkeyService?.Dispose();
        _desktopContextMenuHook?.Dispose();
        _desktopOrganizer?.CloseAll();
        base.OnExit(e);
    }
}
