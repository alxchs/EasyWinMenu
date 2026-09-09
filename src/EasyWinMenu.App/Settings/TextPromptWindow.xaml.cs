using System.Windows;

namespace EasyWinMenu.App.Settings;

public partial class TextPromptWindow : Window
{
    public string Value => ValueBox.Text.Trim();

    public TextPromptWindow(string prompt, string initialValue)
    {
        InitializeComponent();

        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        ValueBox.SelectAll();
        Loaded += (_, _) => ValueBox.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            return;
        }

        DialogResult = true;
    }
}
