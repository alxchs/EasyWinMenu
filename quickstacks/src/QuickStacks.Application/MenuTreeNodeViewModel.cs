using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>Um no do editor (RF14): mesma logica de <see cref="MenuEntryViewModel"/>, mas com filhos carregados para a TreeView inteira.</summary>
public sealed partial class MenuTreeNodeViewModel : ObservableObject
{
    public MenuTreeNodeViewModel(MenuItem item)
    {
        Item = item;
    }

    public MenuItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    public bool IsFolder => Item.IsFolder;

    public string Glyph => Item.Type switch
    {
        MenuItemType.Folder => "",
        MenuItemType.Url => "",
        MenuItemType.Shortcut => "",
        MenuItemType.Executable => "",
        _ => "",
    };

    public ObservableCollection<MenuTreeNodeViewModel> Children { get; } = [];

    /// <summary>Monta a arvore inteira a partir de uma lista plana (IMenuRepository.GetAllAsync).</summary>
    public static ObservableCollection<MenuTreeNodeViewModel> BuildTree(IReadOnlyList<MenuItem> allItems)
    {
        var byId = allItems.ToDictionary(i => i.Id, i => new MenuTreeNodeViewModel(i));
        var roots = new ObservableCollection<MenuTreeNodeViewModel>();

        foreach (var item in allItems.OrderBy(i => i.SortOrder))
        {
            var node = byId[item.Id];
            if (item.ParentId is not null && byId.TryGetValue(item.ParentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }
}
