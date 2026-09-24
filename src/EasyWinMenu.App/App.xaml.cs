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

public partial class App : Application, IDesktopGroupCommands
{
    private ConfigurationStore? _configurationStore;
    private LauncherConfiguration? _configuration;
    private IconCacheService? _iconCache;
    private NotifyIcon? _trayIcon;
    private Window? _menuHost;
    private GlobalHotkeyService? _hotkeyService;
    private DesktopOrganizerService? _desktopOrganizer;
    private SettingsWindow? _settingsWindow;
    private SingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsFirstInstance)
        {
            // This launch exists only to carry a desktop context-menu verb (or a stray
            // double-click) to the instance that is already running.
            var forwardedAction = DesktopContextMenuRegistration.ParseAction(e.Args);
            if (forwardedAction is not null)
            {
                SingleInstanceCoordinator.TrySendToRunningInstance(forwardedAction);
            }

            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        _configurationStore = new ConfigurationStore(ApplicationPaths.ConfigFilePath);
        _configuration = _configurationStore.Load();
        if (DesktopOrganizerService.EnsureIds(_configuration))
        {
            _configurationStore.Save(_configuration);
        }

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
        _hotkeyService.RestoreGroupsRequested += () => _desktopOrganizer?.RestoreMinimizedGroups();
        _hotkeyService.Apply(_configuration.Behavior);

        _desktopOrganizer = new DesktopOrganizerService(_iconCache);
        RefreshDesktopGroups();
        _desktopOrganizer.StartWatchingDisplays();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
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

        DesktopContextMenuRegistration.Register();
        _singleInstance.StartListening(action => Dispatcher.Invoke(() => HandleDesktopAction(action)));

        // This very launch can itself carry a verb — the desktop context menu when the
        // app was closed relaunches it directly rather than going through the pipe.
        var startupAction = DesktopContextMenuRegistration.ParseAction(e.Args);
        if (startupAction is not null)
        {
            HandleDesktopAction(startupAction);
        }
    }

    private void HandleDesktopAction(string action)
    {
        if (action.StartsWith(DesktopContextMenuRegistration.FocusGroupActionPrefix, StringComparison.Ordinal))
        {
            FocusGroup(action[DesktopContextMenuRegistration.FocusGroupActionPrefix.Length..]);
            return;
        }

        switch (action)
        {
            case DesktopContextMenuRegistration.NewGroupAction:
                CreateDesktopGroup();
                break;
            case DesktopContextMenuRegistration.OpenAllGroupsAction:
                _desktopOrganizer?.OpenAllGroups(_configuration!);
                SaveAndReloadGroups();
                break;
            case DesktopContextMenuRegistration.CloseAllGroupsAction:
                _desktopOrganizer?.CloseAllGroups(_configuration!);
                SaveAndReloadGroups();
                break;
            case DesktopContextMenuRegistration.ToggleCollapseAllAction:
                _desktopOrganizer?.ToggleCollapseAll();
                break;
            case DesktopContextMenuRegistration.AllAppFolderAction:
                SetAllGroupsDisplayMode(DesktopGroupDisplayMode.AppFolder);
                break;
            case DesktopContextMenuRegistration.AllPanelAction:
                SetAllGroupsDisplayMode(DesktopGroupDisplayMode.Panel);
                break;
            case DesktopContextMenuRegistration.OpenSettingsAction:
                OpenSettingsWindow();
                break;
        }
    }

    private void SetAllGroupsDisplayMode(DesktopGroupDisplayMode mode)
    {
        foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
        {
            group.DisplayMode = mode;
        }

        SaveAndReloadGroups();
    }

    /// <summary>
    /// The app icon at the size the tray asks for. Picking the frame by the system's
    /// small-icon size, rather than letting it scale the largest one, is what keeps the
    /// tile gaps crisp on a high-DPI tray.
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/EasyWin.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
    }

    private void OnDesktopGroupLayoutChanged(MenuCategory category)
    {
        _configurationStore!.Save(_configuration!);
    }

    private void RefreshDesktopGroups()
    {
        _desktopOrganizer!.Refresh(
            _configuration!,
            OnDesktopGroupLayoutChanged,
            OnDesktopGroupDeleteRequested,
            this);
    }

    /// <summary>Writes the configuration out and repaints whatever is already on screen.</summary>
    private void SaveAndReloadGroups()
    {
        _configurationStore!.Save(_configuration!);
        RefreshDesktopGroups();
        _desktopOrganizer!.ReloadVisuals(_configuration!);
    }

    private void FocusGroup(string groupId)
    {
        var group = DesktopOrganizerService.FindGroupById(_configuration!, groupId);
        if (group is null)
        {
            _trayIcon?.ShowBalloonTip(3000, LocalizationService.Get("common.appName"), LocalizationService.Get("group.notFound"), ToolTipIcon.Warning);
            return;
        }

        if (group.IsClosed)
        {
            group.IsClosed = false;
            SaveAndReloadGroups();
        }

        _desktopOrganizer!.FocusOpenGroup(group);
    }

    void IDesktopGroupCommands.CreateTaskbarShortcut(MenuCategory source)
    {
        if (DesktopOrganizerService.EnsureIds(_configuration!))
        {
            _configurationStore!.Save(_configuration!);
        }

        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var shortcut = TaskbarShortcutService.CreateGroupShortcut(source, exePath);
        TaskbarShortcutService.RevealInExplorer(shortcut);
        _trayIcon?.ShowBalloonTip(5000, LocalizationService.Get("common.appName"), LocalizationService.Get("group.taskbarShortcutHint"), ToolTipIcon.Info);
    }

    void IDesktopGroupCommands.Duplicate(MenuCategory source)
    {
        var parent = DesktopOrganizerService.FindParentList(_configuration!, source) ?? _configuration!.Categories;

        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = LocalizationService.Format("group.copySuffix", source.Name);

        // Offset so the copy is visibly a second group rather than hiding the original.
        copy.DesktopX = source.DesktopX + 28;
        copy.DesktopY = source.DesktopY + 28;

        parent.Add(copy);
        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.ApplyVisualToAllGroups(MenuCategory source)
    {
        foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
        {
            if (!ReferenceEquals(group, source))
            {
                group.CopyVisualFrom(source);
            }
        }

        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.SetAsDefaultVisual(MenuCategory source)
    {
        // Becomes what groups without an override show, and what new ones start from.
        _configuration!.Theme = (source.ThemeOverride ?? _configuration.Theme).Clone();
        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.ApplyWallpaper(MenuCategory? target)
    {
        var wallpaper = WallpaperService.TryGetCurrentWallpaper();
        if (wallpaper is null)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get("group.wallpaperUnavailable"),
                LocalizationService.Get("common.appName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (target is not null)
        {
            target.DesktopBackgroundImagePath = wallpaper;
        }
        else
        {
            foreach (var group in DesktopOrganizerService.AllDesktopGroups(_configuration!))
            {
                group.DesktopBackgroundImagePath = wallpaper;
            }
        }

        SaveAndReloadGroups();
    }

    void IDesktopGroupCommands.OpenSettings()
    {
        OpenSettingsWindow();
    }

    private void OnDesktopGroupDeleteRequested(MenuCategory category)
    {
        DesktopOrganizerService.RemoveCategory(_configuration!, category);
        _configurationStore!.Save(_configuration!);
        RefreshDesktopGroups();
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
        RefreshDesktopGroups();
    }

    private void ShowTrayMenu()
    {
        var screenPoint = System.Windows.Forms.Cursor.Position;

        _menuHost!.Left = screenPoint.X;
        _menuHost.Top = screenPoint.Y;

        var menu = TrayMenuBuilder.Build(
            _configuration!,
            StartupRegistration.IsEnabled(),
            _desktopOrganizer!.HasOpenGroups,
            OpenSettingsWindow,
            CreateDesktopGroup,
            _desktopOrganizer.ToggleCollapseAll,
            _desktopOrganizer.GatherAll,
            () =>
            {
                _desktopOrganizer.OpenAllGroups(_configuration!);
                SaveAndReloadGroups();
            },
            () =>
            {
                _desktopOrganizer.CloseAllGroups(_configuration!);
                SaveAndReloadGroups();
            },
            group =>
            {
                _desktopOrganizer.ToggleGroup(group);
                SaveAndReloadGroups();
            },
            StartupRegistration.SetEnabled,
            Shutdown);

        var cursorPosition = ToDeviceIndependentPoint(screenPoint);

        menu.PlacementTarget = _menuHost;
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = cursorPosition.X;
        menu.VerticalOffset = cursorPosition.Y;
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
        RefreshDesktopGroups();
        _desktopOrganizer!.ReloadVisuals(_configuration!);
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
        _desktopOrganizer?.StopWatchingDisplays();
        _desktopOrganizer?.CloseAll();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
