using EasyWinMenu.App.Services;
using System.Windows;
using System.Windows.Threading;
using EasyWinMenu.Core.Models;
using Microsoft.Win32;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopOrganizerService(IconCacheService iconCache)
{
    private static readonly TimeSpan DisplaySettleDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PlacementHold = TimeSpan.FromSeconds(3);

    private readonly Dictionary<MenuCategory, DesktopGroupWindow> _windows = new();
    private IReadOnlyList<Rect>? _lastWorkAreas;
    private DispatcherTimer? _displaySettleTimer;

    public void StartWatchingDisplays()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void StopWatchingDisplays()
    {
        // SystemEvents is static: a handler left attached outlives this service.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _displaySettleTimer?.Stop();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Raised on a system thread; the windows belong to the UI thread.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _windows.Values)
            {
                window.HoldPlacement(PlacementHold);
            }

            // Several notifications arrive for one plug or unplug, and Windows moves its
            // own windows in the middle of them; act once, after it has finished.
            if (_displaySettleTimer is null)
            {
                _displaySettleTimer = new DispatcherTimer { Interval = DisplaySettleDelay };
                _displaySettleTimer.Tick += (_, _) =>
                {
                    _displaySettleTimer.Stop();
                    EnsureGroupsReachable();
                };
            }

            _displaySettleTimer.Stop();
            _displaySettleTimer.Start();
        });
    }

    /// <summary>
    /// Makes every group reachable on the monitors connected now. A group whose home is on
    /// screen goes home; one stranded on a monitor that went away is carried to the
    /// nearest remaining monitor, keeping its relative place when the old layout is known.
    /// Rescued positions are never saved, so a group returns home when its monitor does.
    /// </summary>
    public void EnsureGroupsReachable()
    {
        var reference = _windows.Values.FirstOrDefault();
        if (reference is null)
        {
            return;
        }

        var areas = DisplayInventory.WorkAreas(reference);
        if (areas.Count == 0)
        {
            return;
        }

        var taken = new List<System.Windows.Point>();
        foreach (var window in _windows.Values)
        {
            var home = window.HomeRect;

            if (MonitorPlacement.IsReachable(home, areas))
            {
                if (Math.Abs(window.Left - home.X) > 0.5 || Math.Abs(window.Top - home.Y) > 0.5)
                {
                    window.PlaceWithoutSaving(home.X, home.Y);
                }

                continue;
            }

            var destination = areas[MonitorPlacement.IndexOfOwner(home, areas)];

            var position = _lastWorkAreas is { Count: > 0 } previous && MonitorPlacement.IsReachable(home, previous)
                ? MonitorPlacement.MapBetween(home, previous[MonitorPlacement.IndexOfOwner(home, previous)], destination)
                : MonitorPlacement.ClampInto(home.Location, home.Size, destination);

            position = MonitorPlacement.AvoidStacking(position, home.Size, destination, taken);
            window.PlaceWithoutSaving(position.X, position.Y);
        }

        // Only a layout that shows every home is worth remembering as the one to map from.
        if (_windows.Values.All(window => MonitorPlacement.IsReachable(window.HomeRect, areas)))
        {
            _lastWorkAreas = areas;
        }
    }

    /// <summary>Re-reads every open group's appearance from the configuration.</summary>
    /// <summary>Brings back every group Windows minimized and then failed to restore.</summary>
    public void RestoreMinimizedGroups()
    {
        foreach (var window in _windows.Values)
        {
            window.RestoreIfMinimized();
        }
    }

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
        var groups = FindDesktopGroups(configuration).Where(g => !g.IsClosed).ToList();

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

        EnsureGroupsReachable();
    }

    public void OpenAllGroups(LauncherConfiguration configuration)
    {
        foreach (var group in AllDesktopGroups(configuration))
        {
            group.IsClosed = false;
        }
    }

    public void CloseAllGroups(LauncherConfiguration configuration)
    {
        foreach (var group in AllDesktopGroups(configuration))
        {
            group.IsClosed = true;
        }
    }

    public void ToggleGroup(MenuCategory group)
    {
        group.IsClosed = !group.IsClosed;
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

    /// <summary>Gives every desktop group that lacks one a stable <see cref="MenuCategory.Id"/>. True when anything changed.</summary>
    public static bool EnsureIds(IMenuContainer container)
    {
        var changed = false;
        foreach (var group in AllDesktopGroups(container))
        {
            if (string.IsNullOrEmpty(group.Id))
            {
                group.Id = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }

        return changed;
    }

    public static MenuCategory? FindGroupById(IMenuContainer container, string id)
    {
        return AllDesktopGroups(container).FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Brings an open group's window to the front with a short pulse. False when it has no window (closed).</summary>
    public bool FocusOpenGroup(MenuCategory group)
    {
        if (!_windows.TryGetValue(group, out var window))
        {
            return false;
        }

        window.BringForwardAndPulse();
        return true;
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
