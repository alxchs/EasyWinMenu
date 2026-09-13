using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// Actions a group can ask for that reach beyond itself — creating a sibling, rewriting
/// the other groups, or changing what every future group inherits. A group window owns
/// only its own category, so these are handed in from whoever owns the configuration.
/// Subfolder windows get none of this: they are not desktop groups.
/// </summary>
internal interface IDesktopGroupCommands
{
    void Duplicate(MenuCategory source);

    /// <summary>Copies the source group's look onto every other desktop group.</summary>
    void ApplyVisualToAllGroups(MenuCategory source);

    /// <summary>Makes the source group's look the theme new groups start from.</summary>
    void SetAsDefaultVisual(MenuCategory source);

    /// <summary>Uses the Windows wallpaper as the background of one group, or of all of them.</summary>
    void ApplyWallpaper(MenuCategory? target);
}
