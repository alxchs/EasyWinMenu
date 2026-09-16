using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickStacks.Application;
using QuickStacks.Domain;
using QuickStacks.Localization;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QuickStacks.UI;

/// <summary>Editor de estrutura (RF14): arvore com CRUD, mover por drag, exportar/importar.</summary>
public sealed partial class EditorWindow : Window
{
    private const string DraggedItemFormat = "QuickStacksItemId";

    public EditorWindow(IMenuRepository repository, IConfigExportService exportService)
    {
        InitializeComponent();

        ViewModel = new EditorViewModel(repository, exportService);
        ThemeService.Register(RootGrid);

        RefreshTexts();
        LocalizationService.LanguageChanged += RefreshTexts;
        Closed += (_, _) => LocalizationService.LanguageChanged -= RefreshTexts;

        _ = ViewModel.LoadAsync();
    }

    public EditorViewModel ViewModel { get; }

    private void RefreshTexts()
    {
        AddFolderButton.Label = LocalizationService.Get("editor.addFolder");
        AddItemButton.Label = LocalizationService.Get("editor.addItem");
        RenameButton.Label = LocalizationService.Get("editor.rename");
        EditButton.Label = LocalizationService.Get("editor.edit");
        DeleteButton.Label = LocalizationService.Get("editor.delete");
        ExportButton.Label = LocalizationService.Get("editor.export");
        ImportButton.Label = LocalizationService.Get("editor.import");
    }

    private void Tree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        ViewModel.SelectedNode = sender.SelectedItem as MenuTreeNodeViewModel;
    }

    // ---- CRUD ----

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = await PromptTextAsync(
            LocalizationService.Get("editor.newFolderDialogTitle"),
            LocalizationService.Get("editor.folderNameLabel"),
            LocalizationService.Get("editor.defaultFolderName"));
        if (!string.IsNullOrWhiteSpace(name))
        {
            await ViewModel.AddFolderAsync(name, ViewModel.SelectedNode);
        }
    }

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        var result = await PromptItemAsync(LocalizationService.Get("editor.newItemDialogTitle"), null);
        if (result is { } item)
        {
            await ViewModel.AddItemAsync(item.Name, item.Type, item.Path, item.Arguments, item.WorkingDirectory, ViewModel.SelectedNode);
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is not { } node)
        {
            return;
        }

        var name = await PromptTextAsync(LocalizationService.Get("editor.renameDialogTitle"), LocalizationService.Get("editor.newNameLabel"), node.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await ViewModel.RenameAsync(node, name);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is not { IsFolder: false } node)
        {
            return;
        }

        var result = await PromptItemAsync(LocalizationService.Get("editor.editItemDialogTitle"), node.Item);
        if (result is { } item)
        {
            await ViewModel.UpdateItemAsync(node, item.Name, item.Path, item.Arguments, item.WorkingDirectory);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNode is not { } node)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = LocalizationService.Get("editor.deleteDialogTitle"),
            Content = LocalizationService.Format(
                node.IsFolder ? "editor.deleteConfirmWithChildrenFormat" : "editor.deleteConfirmFormat",
                node.Name),
            PrimaryButtonText = LocalizationService.Get("editor.delete"),
            CloseButtonText = LocalizationService.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteAsync(node);
        }
    }

    // ---- Exportar / Importar (RF17-RF20) ----

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = "quickstacks-backup",
        };
        picker.FileTypeChoices.Add("Configuracao QuickStacks", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await ViewModel.ExportAsync(file.Path);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = LocalizationService.Get("editor.importDialogTitle"),
            Content = LocalizationService.Get("editor.importConfirmText"),
            PrimaryButtonText = LocalizationService.Get("editor.import"),
            CloseButtonText = LocalizationService.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ImportAsync(file.Path);
        }
    }

    // ---- Drag-and-drop (reparentar dentro da arvore) ----

    private void Tree_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        if (args.Items.Count == 0 || args.Items[0] is not MenuTreeNodeViewModel node)
        {
            return;
        }

        args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        args.Data.SetData(DraggedItemFormat, node.Id);
    }

    private void TreeItem_DragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MenuTreeNodeViewModel { IsFolder: true } } && e.DataView.Contains(DraggedItemFormat))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }
    }

    private async void TreeItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MenuTreeNodeViewModel { IsFolder: true } target })
        {
            return;
        }

        if (!e.DataView.Contains(DraggedItemFormat))
        {
            return;
        }

        var draggedId = (string)await e.DataView.GetDataAsync(DraggedItemFormat);
        await ViewModel.TryMoveAsync(draggedId, target.Id);
    }

    // ---- Dialogos simples (sem XAML proprio - a Fase 2 prioriza funcionar; refinar visual e' polimento futuro) ----

    private async Task<string?> PromptTextAsync(string title, string label, string initialValue)
    {
        var textBox = new TextBox { Header = label, Text = initialValue };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = LocalizationService.Get("common.ok"),
            CloseButtonText = LocalizationService.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? textBox.Text : null;
    }

    private async Task<(string Name, MenuItemType Type, string Path, string? Arguments, string? WorkingDirectory)?> PromptItemAsync(string title, MenuItem? existing)
    {
        var nameBox = new TextBox { Header = LocalizationService.Get("editor.field.name"), Text = existing?.Name ?? string.Empty };
        var typeCombo = new ComboBox
        {
            Header = LocalizationService.Get("editor.field.type"),
            ItemsSource = new[] { MenuItemType.Executable, MenuItemType.Shortcut, MenuItemType.Url },
            SelectedItem = existing?.Type ?? MenuItemType.Executable,
            IsEnabled = existing is null, // tipo nao muda depois de criado - so nome/caminho/argumentos
        };
        var pathBox = new TextBox { Header = LocalizationService.Get("editor.field.path"), Text = existing?.Path ?? string.Empty };
        var argsBox = new TextBox { Header = LocalizationService.Get("editor.field.arguments"), Text = existing?.Arguments ?? string.Empty };
        var workDirBox = new TextBox { Header = LocalizationService.Get("editor.field.workingDirectory"), Text = existing?.WorkingDirectory ?? string.Empty };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(nameBox);
        panel.Children.Add(typeCombo);
        panel.Children.Add(pathBox);
        panel.Children.Add(argsBox);
        panel.Children.Add(workDirBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = LocalizationService.Get("common.ok"),
            CloseButtonText = LocalizationService.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(pathBox.Text))
        {
            return null;
        }

        return (
            nameBox.Text,
            (MenuItemType)typeCombo.SelectedItem,
            pathBox.Text,
            string.IsNullOrWhiteSpace(argsBox.Text) ? null : argsBox.Text,
            string.IsNullOrWhiteSpace(workDirBox.Text) ? null : workDirBox.Text);
    }
}
