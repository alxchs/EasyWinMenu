using Microsoft.UI.Xaml;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;

namespace QuickStacks.UI;

// Application aqui e' Microsoft.UI.Xaml.Application, mas o namespace QuickStacks.Application
// (a camada de ViewModels) e' visivel sem qualificar por ser irmao de QuickStacks.UI sob o
// mesmo namespace raiz QuickStacks - por isso o nome precisa ser totalmente qualificado.
public partial class App : Microsoft.UI.Xaml.Application
{
    private TrayIconWindow? _trayIconWindow;

    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Repositorio unico do processo. O popup e' recriado a cada abertura (nao ha janela
    /// "principal" tradicional - o unico ponto de entrada visivel e' o icone da bandeja),
    /// mas todos compartilham a mesma conexao logica ao banco.
    /// </summary>
    public IMenuRepository MenuRepository { get; private set; } = null!;

    public IConfigExportService ExportService { get; private set; } = null!;

    public SettingsStore Settings { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MenuRepository = new SqliteMenuRepository();
        ExportService = new ConfigExportService(MenuRepository);
        Settings = new SettingsStore();
        ThemeService.Initialize(Settings);
        _ = SeedData.EnsureSeededAsync(MenuRepository);

        // Nao existe uma "janela principal" visivel: o unico ponto de entrada e' o icone
        // da bandeja (RF01 - um unico icone na Taskbar). TrayIconWindow hospeda o
        // TaskbarIcon (H.NotifyIcon) e permanece oculta; ela e' quem abre o PopupWindow.
        _trayIconWindow = new TrayIconWindow();
        _trayIconWindow.Activate();
        _trayIconWindow.AppWindow.Hide();
    }
}
