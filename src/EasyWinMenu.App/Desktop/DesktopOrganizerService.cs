using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopOrganizerService(IconCacheService iconCache)
{
    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _windows = new();

    public void Refresh(LauncherConfiguration configuration, Action<MenuCategory> onLayoutChanged)
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
            var window = new DesktopGroupWindow(category, theme, iconCache, LaunchExecutor.Execute, onLayoutChanged);
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

    private static IEnumerable<MenuCategory> FindDesktopGroups(IMenuContainer container)
    {
        foreach (var category in container.Categories)
        {
            if (category.Items.Any(item => item.IsDesktopPinned))
            {
                yield return category;
            }

            foreach (var nested in FindDesktopGroups(category))
            {
                yield return nested;
            }
        }
    }
}
