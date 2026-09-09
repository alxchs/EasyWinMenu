using System.Windows;
using EasyWinMenu.Core.Models;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace EasyWinMenu.App.Settings;

public partial class LaunchItemEditWindow : Window
{
    private readonly LaunchItem _item;

    public LaunchItemEditWindow(LaunchItem item)
    {
        InitializeComponent();

        _item = item;

        TypeBox.ItemsSource = Enum.GetValues<LaunchItemType>();
        NameBox.Text = item.Name;
        TypeBox.SelectedItem = item.Type;
        TargetBox.Text = item.Target;
        ArgumentsBox.Text = item.Arguments;
        WorkingDirectoryBox.Text = item.WorkingDirectory;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var selectedType = TypeBox.SelectedItem as LaunchItemType? ?? LaunchItemType.Application;

        if (selectedType == LaunchItemType.Folder)
        {
            var folderDialog = new OpenFolderDialog();
            if (folderDialog.ShowDialog(this) == true)
            {
                TargetBox.Text = folderDialog.FolderName;
            }

            return;
        }

        var fileDialog = new OpenFileDialog { Filter = "Todos os arquivos (*.*)|*.*" };
        if (fileDialog.ShowDialog(this) == true)
        {
            TargetBox.Text = fileDialog.FileName;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text) || TypeBox.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Nome, tipo e destino são obrigatórios.",
                "EasyWinMenu",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _item.Name = NameBox.Text.Trim();
        _item.Type = (LaunchItemType)TypeBox.SelectedItem;
        _item.Target = TargetBox.Text.Trim();
        _item.Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
        _item.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text) ? null : WorkingDirectoryBox.Text.Trim();

        DialogResult = true;
    }
}
