using CommunityToolkit.Mvvm.ComponentModel;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>Wrapper de exibicao em cima de <see cref="MenuItem"/> para bind na UI (Fase 17: suporte a icones reais em PNG).</summary>
public sealed partial class MenuEntryViewModel : ObservableObject
{
    public MenuEntryViewModel(MenuItem item, IIconCacheService? iconCache = null)
    {
        Item = item;
        IconPath = !item.IsFolder ? iconCache?.GetIconPath(item.Path) : null;
    }

    public MenuItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    public bool IsFolder => Item.IsFolder;

    public MenuItemType Type => Item.Type;

    public string? Path => Item.Path;

    public bool IsFavorite => Item.IsFavorite;

    public string? IconPath { get; }

    public bool HasCustomIcon => IconPath is not null;

    public bool HasNoCustomIcon => IconPath is null;

    /// <summary>Codepoint Segoe Fluent Icons - fallback quando nao ha icone extraido.</summary>
    public string Glyph => Type switch
    {
        MenuItemType.Folder => "",
        MenuItemType.Url => "",
        MenuItemType.Shortcut => "",
        MenuItemType.Executable => "",
        _ => "",
    };
}
