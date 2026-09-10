using System.Windows;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Style = System.Windows.Style;

namespace EasyWinMenu.App.Theming;

public class ModernWindow : Window
{
    public ModernWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Style = (Style)Application.Current.Resources["ModernWindowStyle"];
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_TitleBar") is UIElement titleBar)
        {
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            };
        }

        if (GetTemplateChild("PART_MinimizeButton") is Button minimizeButton)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        if (GetTemplateChild("PART_CloseButton") is Button closeButton)
        {
            closeButton.Click += (_, _) => Close();
        }
    }
}
