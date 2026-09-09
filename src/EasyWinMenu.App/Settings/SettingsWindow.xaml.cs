using System.Windows;
using System.Windows.Controls;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly LauncherConfiguration _target;
    private readonly LauncherConfiguration _workingConfiguration;

    public event EventHandler? ConfigurationSaved;

    public SettingsWindow(LauncherConfiguration configuration)
    {
        InitializeComponent();

        _target = configuration;
        _workingConfiguration = configuration.Clone();

        RebuildTree();
    }

    private void RebuildTree()
    {
        MenuTree.Items.Clear();
        PopulateContainer(MenuTree.Items, _workingConfiguration);
    }

    private static void PopulateContainer(ItemCollection collection, IMenuContainer container)
    {
        foreach (var item in container.Items)
        {
            collection.Add(new TreeViewItem
            {
                Header = item.Name,
                Tag = new NodeTag(container, item, null)
            });
        }

        foreach (var category in container.Categories)
        {
            var categoryNode = new TreeViewItem
            {
                Header = category.Name,
                Tag = new NodeTag(container, null, category),
                IsExpanded = true
            };

            PopulateContainer(categoryNode.Items, category);
            collection.Add(categoryNode);
        }
    }

    private NodeTag? GetSelectedNode()
    {
        return (MenuTree.SelectedItem as TreeViewItem)?.Tag as NodeTag;
    }

    private IMenuContainer GetTargetContainer()
    {
        var selected = GetSelectedNode();
        if (selected is null)
        {
            return _workingConfiguration;
        }

        return selected.Category ?? selected.Parent;
    }

    private void OnAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Nome da categoria:", string.Empty) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        GetTargetContainer().Categories.Add(new MenuCategory { Name = prompt.Value });
        RebuildTree();
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        var newItem = new LaunchItem
        {
            Name = string.Empty,
            Type = LaunchItemType.Application,
            Target = string.Empty
        };

        var editor = new LaunchItemEditWindow(newItem) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        GetTargetContainer().Items.Add(newItem);
        RebuildTree();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedNode();
        if (selected is null)
        {
            return;
        }

        if (selected.Item is not null)
        {
            new LaunchItemEditWindow(selected.Item) { Owner = this }.ShowDialog();
            RebuildTree();
            return;
        }

        if (selected.Category is null)
        {
            return;
        }

        var prompt = new TextPromptWindow("Nome da categoria:", selected.Category.Name) { Owner = this };
        if (prompt.ShowDialog() == true)
        {
            selected.Category.Name = prompt.Value;
        }

        RebuildTree();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedNode();
        if (selected is null)
        {
            return;
        }

        var name = selected.Item?.Name ?? selected.Category?.Name;
        var confirmed = System.Windows.MessageBox.Show(
            this,
            $"Excluir \"{name}\"?",
            "EasyWinMenu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        if (selected.Item is not null)
        {
            selected.Parent.Items.Remove(selected.Item);
        }
        else if (selected.Category is not null)
        {
            selected.Parent.Categories.Remove(selected.Category);
        }

        RebuildTree();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _target.ReplaceContentsWith(_workingConfiguration);
        ConfigurationSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed record NodeTag(IMenuContainer Parent, LaunchItem? Item, MenuCategory? Category);
}
