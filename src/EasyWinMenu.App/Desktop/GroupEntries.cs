using System.Windows.Media;
using EasyWinMenu.App.Services;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// A group shows two kinds of entry side by side: its subfolders and its pinned items.
/// These helpers keep that pairing in one place so the tile, the badge and the overlay
/// always agree on what a group contains and in which order.
/// </summary>
internal static class GroupEntries
{
    public static IEnumerable<object> Enumerate(MenuCategory category)
    {
        foreach (var folder in category.Categories)
        {
            yield return folder;
        }

        foreach (var item in category.Items.Where(i => i.IsDesktopPinned))
        {
            yield return item;
        }
    }

    public static int Count(MenuCategory category)
    {
        return category.Categories.Count + category.Items.Count(i => i.IsDesktopPinned);
    }

    public static string NameOf(object entry) => entry switch
    {
        LaunchItem item => item.Name,
        MenuCategory folder => folder.Name,
        _ => string.Empty
    };

    /// <summary>Null for subfolders, which are drawn as a glyph instead of an extracted icon.</summary>
    public static ImageSource? IconOf(object entry, IconCacheService iconCache) => entry switch
    {
        LaunchItem item => iconCache.GetImageSource(item.IconOverridePath ?? item.Target),
        _ => null
    };
}
