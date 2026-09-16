using CommunityToolkit.Mvvm.ComponentModel;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>Wrapper de exibicao em cima de <see cref="MenuItem"/> para bind na UI.</summary>
public sealed partial class MenuEntryViewModel : ObservableObject
{
    public MenuEntryViewModel(MenuItem item)
    {
        Item = item;
    }

    public MenuItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    public bool IsFolder => Item.IsFolder;

    public MenuItemType Type => Item.Type;

    public string? Path => Item.Path;

    public bool IsFavorite => Item.IsFavorite;

    /// <summary>Codepoint Segoe Fluent Icons - so cosmetico, sem efeito em build/testes.</summary>
    public string Glyph => Type switch
    {
        MenuItemType.Folder => "",
        MenuItemType.Url => "",
        MenuItemType.Shortcut => "",
        MenuItemType.Executable => "",
        _ => "",
    };
}
