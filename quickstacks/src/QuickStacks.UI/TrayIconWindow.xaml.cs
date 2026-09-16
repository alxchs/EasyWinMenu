using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace QuickStacks.UI;

/// <summary>
/// Janela invisivel que so existe para hospedar o <c>TaskbarIcon</c> (H.NotifyIcon nao tem
/// como funcionar sem uma Window WinUI3 viva por baixo). RF01 do spec: um unico icone na
/// Taskbar, clique abre o popup diretamente - nao ha janela "principal" tradicional.
/// </summary>
public sealed partial class TrayIconWindow : Window
{
    private PopupWindow? _popup;
    private EditorWindow? _editor;

    public TrayIconWindow()
    {
        ShowPopupCommand = new RelayCommand(ShowPopup);
        OpenEditorCommand = new RelayCommand(OpenEditor);
        ExitCommand = new RelayCommand(() => Microsoft.UI.Xaml.Application.Current.Exit());

        InitializeComponent();

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "quickstacks-placeholder.ico");
        if (System.IO.File.Exists(iconPath))
        {
            TrayIcon.Icon = new System.Drawing.Icon(iconPath);
        }

        // O item marcado no submenu Tema precisa refletir o que ja' foi carregado de
        // Settings em App.OnLaunched - RadioMenuFlyoutItem.IsChecked nao tem como fazer isso
        // via x:Bind aqui pelo mesmo motivo do PopupWindow (Window nao e' FrameworkElement).
        (ThemeService.CurrentTheme switch
        {
            ElementTheme.Light => ThemeLightItem,
            ElementTheme.Dark => ThemeDarkItem,
            _ => ThemeSystemItem,
        }).IsChecked = true;
    }

    public IRelayCommand ShowPopupCommand { get; }

    public IRelayCommand OpenEditorCommand { get; }

    public IRelayCommand ExitCommand { get; }

    private void ShowPopup()
    {
        // Uma unica instancia do popup, reaberta/focada em vez de duplicada a cada clique.
        if (_popup is null)
        {
            var app = (App)Microsoft.UI.Xaml.Application.Current;
            _popup = new PopupWindow(app.MenuRepository);
            _popup.Closed += (_, _) => _popup = null;
        }

        _popup.ActivateNearCursor();
    }

    private void OpenEditor()
    {
        if (_editor is null)
        {
            var app = (App)Microsoft.UI.Xaml.Application.Current;
            _editor = new EditorWindow(app.MenuRepository, app.ExportService);
            _editor.Closed += (_, _) => _editor = null;
        }

        _editor.Activate();
    }

    private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetTheme(ElementTheme.Light);

    private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetTheme(ElementTheme.Dark);

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => SetTheme(ElementTheme.Default);

    private void SetTheme(ElementTheme theme)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        ThemeService.SetTheme(app.Settings, theme);
    }
}
