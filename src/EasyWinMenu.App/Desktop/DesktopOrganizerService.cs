using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopOrganizerService(IconCacheService iconCache)
{
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _windows = new();

    /// <summary>Re-reads every open group's appearance from the configuration.</summary>
    public void ReloadVisuals(LauncherConfiguration configuration)
    {
        foreach (var (category, window) in _windows)
        {
            window.ReloadVisuals(category.ThemeOverride ?? configuration.Theme);
        }
    }

    public void Refresh(
        LauncherConfiguration configuration,
        Action<MenuCategory> onLayoutChanged,
        Action<MenuCategory> onDeleteRequested,
        IDesktopGroupCommands? commands = null)
    {
        var groups = FindDesktopGroups(configuration).ToList();

        foreach (var stale in _windows.Keys.Except(groups).ToList())
        {
            _windows[stale].Close();
            _windows.Remove(stale);
        }

        foreach (var category in groups)
        {
            if (_windows.ContainsKey(category))
            {
                continue;
            }

            var theme = category.ThemeOverride ?? configuration.Theme;
            var window = new DesktopGroupWindow(category, theme, iconCache, LaunchExecutor.Execute, onLayoutChanged, onDeleteRequested, commands);
            window.Show();
            _windows[category] = window;
        }
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values)
        {
            window.Close();
        }

        _windows.Clear();
    }

    public bool HasOpenGroups => _windows.Count > 0;

    public void ToggleCollapseAll()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        var shouldCollapse = _windows.Values.Any(window => !window.IsCollapsed);
        foreach (var window in _windows.Values)
        {
            window.SetCollapsedExternally(shouldCollapse);
        }
    }

    public void GatherAll()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        var workArea = System.Windows.SystemParameters.WorkArea;
        var centerX = workArea.Left + (workArea.Width / 2);
        var centerY = workArea.Top + (workArea.Height / 2);
        var offset = 0.0;

        foreach (var window in _windows.Values)
        {
            window.MoveToCenterKeepingSize(centerX, centerY, offset);
            offset += 24;
        }
    }

    /// <summary>The list <paramref name="target"/> lives in, so a copy can join it.</summary>
    public static List<MenuCategory>? FindParentList(IMenuContainer container, MenuCategory target)
    {
        if (container.Categories.Contains(target))
        {
            return container.Categories;
        }

        foreach (var category in container.Categories)
        {
            var found = FindParentList(category, target);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Every desktop group in the configuration, whatever its nesting.</summary>
    public static IEnumerable<MenuCategory> AllDesktopGroups(IMenuContainer container)
    {
        foreach (var category in container.Categories)
        {
            if (category.IsDesktopGroup)
            {
                yield return category;
            }

            foreach (var nested in AllDesktopGroups(category))
            {
                yield return nested;
            }
        }
    }

    public static bool RemoveCategory(IMenuContainer container, MenuCategory target)
    {
        if (container.Categories.Remove(target))
        {
            return true;
        }

        foreach (var category in container.Categories)
        {
            if (RemoveCategory(category, target))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MenuCategory> FindDesktopGroups(IMenuContainer container)
    {
        foreach (var category in container.Categories)
        {
            if (category.IsDesktopGroup)
            {
                yield return category;
            }
        }
    }
}
