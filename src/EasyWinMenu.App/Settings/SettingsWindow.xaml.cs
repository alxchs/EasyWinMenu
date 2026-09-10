using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyWinMenu.App.Theming;
using EasyWinMenu.Core.Configuration;
using EasyWinMenu.Core.Models;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace EasyWinMenu.App.Settings;

public partial class SettingsWindow : ModernWindow
{
    private readonly LauncherConfiguration _target;
    private readonly LauncherConfiguration _workingConfiguration;

    private Point? _dragStartPoint;
    private TreeViewItem? _dragCandidate;

    public event EventHandler? ConfigurationSaved;

    public SettingsWindow(LauncherConfiguration configuration)
    {
        InitializeComponent();

        _target = configuration;
        _workingConfiguration = configuration.Clone();

        RebuildTree();
        LoadBehavior();
    }

    private void LoadBehavior()
    {
        AppThemeBox.ItemsSource = new[]
        {
            new { Mode = AppThemeMode.System, Label = "Seguir o Windows" },
            new { Mode = AppThemeMode.Light, Label = "Claro" },
            new { Mode = AppThemeMode.Dark, Label = "Escuro" }
        };
        AppThemeBox.SelectedValue = _workingConfiguration.Behavior.AppTheme;

        ClickModeBox.ItemsSource = new[]
        {
            new { Mode = TrayClickMode.SingleClick, Label = "Clique simples" },
            new { Mode = TrayClickMode.DoubleClick, Label = "Duplo clique" }
        };
        ClickModeBox.SelectedValue = _workingConfiguration.Behavior.ClickMode;

        HotkeyKeyBox.ItemsSource = new[] { "Space", "Tab" }
            .Concat(Enumerable.Range('A', 26).Select(code => ((char)code).ToString()))
            .ToList();
        HotkeyKeyBox.SelectedItem = _workingConfiguration.Behavior.GlobalHotkeyKey;

        var modifiers = _workingConfiguration.Behavior.GlobalHotkeyModifiers;
        HotkeyEnabledBox.IsChecked = _workingConfiguration.Behavior.GlobalHotkeyEnabled;
        HotkeyCtrlBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Control);
        HotkeyAltBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Alt);
        HotkeyShiftBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Shift);
        HotkeyWinBox.IsChecked = modifiers.HasFlag(HotkeyModifiers.Windows);
    }

    private void SaveBehavior()
    {
        var behavior = _workingConfiguration.Behavior;
        behavior.AppTheme = AppThemeBox.SelectedValue as AppThemeMode? ?? AppThemeMode.System;
        behavior.ClickMode = ClickModeBox.SelectedValue as TrayClickMode? ?? TrayClickMode.SingleClick;
        behavior.GlobalHotkeyEnabled = HotkeyEnabledBox.IsChecked == true;
        behavior.GlobalHotkeyKey = HotkeyKeyBox.SelectedItem as string ?? "Space";

        var modifiers = HotkeyModifiers.None;
        if (HotkeyCtrlBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (HotkeyAltBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (HotkeyShiftBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (HotkeyWinBox.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        behavior.GlobalHotkeyModifiers = modifiers;
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
        var confirmed = MessageBox.Show(
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

    private void OnEditThemeClick(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedNode();
        new ThemeEditWindow(_workingConfiguration, selected?.Category) { Owner = this }.ShowDialog();
    }

    private void OnTreeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(MenuTree);
        _dragCandidate = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _dragCandidate is null)
        {
            return;
        }

        var offset = e.GetPosition(MenuTree) - _dragStartPoint.Value;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var draggedNode = _dragCandidate;
        _dragStartPoint = null;
        _dragCandidate = null;

        DragDrop.DoDragDrop(draggedNode, draggedNode, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TreeViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TreeViewItem)) is not TreeViewItem draggedNode || draggedNode.Tag is not NodeTag draggedTag)
        {
            return;
        }

        var targetNode = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
        if (targetNode == draggedNode)
        {
            return;
        }

        var targetTag = targetNode?.Tag as NodeTag;

        IMenuContainer destination;
        LaunchItem? insertBeforeItem = null;

        if (targetTag?.Category is not null)
        {
            destination = targetTag.Category;
        }
        else if (targetTag?.Item is not null)
        {
            destination = targetTag.Parent;
            insertBeforeItem = targetTag.Item;
        }
        else
        {
            destination = _workingConfiguration;
        }

        if (draggedTag.Category is not null && IsCategoryOrDescendant(draggedTag.Category, destination))
        {
            return;
        }

        MoveNode(draggedTag, destination, insertBeforeItem);
        RebuildTree();
    }

    private static void MoveNode(NodeTag dragged, IMenuContainer destination, LaunchItem? insertBeforeItem)
    {
        if (dragged.Item is not null)
        {
            dragged.Parent.Items.Remove(dragged.Item);

            var index = insertBeforeItem is not null ? destination.Items.IndexOf(insertBeforeItem) : -1;
            if (index < 0)
            {
                destination.Items.Add(dragged.Item);
            }
            else
            {
                destination.Items.Insert(index, dragged.Item);
            }

            return;
        }

        if (dragged.Category is not null)
        {
            dragged.Parent.Categories.Remove(dragged.Category);
            destination.Categories.Add(dragged.Category);
        }
    }

    private static bool IsCategoryOrDescendant(MenuCategory category, IMenuContainer container)
    {
        if (ReferenceEquals(category, container))
        {
            return true;
        }

        foreach (var child in category.Categories)
        {
            if (IsCategoryOrDescendant(child, container))
            {
                return true;
            }
        }

        return false;
    }

    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? source)
    {
        while (source is not null and not TreeViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as TreeViewItem;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Arquivo JSON (*.json)|*.json",
            FileName = "easywinmenu-config.json"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SaveBehavior();

        new ConfigurationStore(dialog.FileName).Save(_workingConfiguration);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Arquivo JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!new ConfigurationStore(dialog.FileName).TryLoad(out var imported))
        {
            MessageBox.Show(
                this,
                "Não foi possível importar o arquivo selecionado.",
                "EasyWinMenu",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _workingConfiguration.ReplaceContentsWith(imported);
        RebuildTree();
        LoadBehavior();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveBehavior();
        _target.ReplaceContentsWith(_workingConfiguration);
        ConfigurationSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed record NodeTag(IMenuContainer Parent, LaunchItem? Item, MenuCategory? Category);
}
