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

    /// <summary>Nivel na arvore (0 = raiz), usado para indentar na lista achatada do editor.</summary>
    public int Depth { get; set; }

    public bool HasChildren => Children.Count > 0;

    /// <summary>Espaco no lugar da seta, para os nomes de quem nao tem filhos alinharem com os que tem.</summary>
    public bool HasNoChildren => !HasChildren;

    /// <summary>Espaco reservado para a seta de expandir, mesmo em quem nao tem filhos, para os nomes alinharem.</summary>
    public double Indent => Depth * 20;

    [ObservableProperty]
    private bool _isExpanded;

    public string ExpanderGlyph => IsExpanded ? "" : "";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpanderGlyph));

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

        foreach (var root in roots)
        {
            AssignDepth(root, 0);
        }

        return roots;
    }

    private static void AssignDepth(MenuTreeNodeViewModel node, int depth)
    {
        node.Depth = depth;
        foreach (var child in node.Children)
        {
            AssignDepth(child, depth + 1);
        }
    }
}
