using Microsoft.UI.Xaml;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using QuickStacks.Localization;

namespace QuickStacks.UI;

// Application aqui e' Microsoft.UI.Xaml.Application, mas o namespace QuickStacks.Application
// (a camada de ViewModels) e' visivel sem qualificar por ser irmao de QuickStacks.UI sob o
// mesmo namespace raiz QuickStacks - por isso o nome precisa ser totalmente qualificado.
public partial class App : Microsoft.UI.Xaml.Application
{
    private TrayIconWindow? _trayIconWindow;
    private SingleInstanceCoordinator? _singleInstance;

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

    public ILnkImportService LnkImportService { get; private set; } = null!;

    public IIconCacheService IconCacheService { get; private set; } = null!;

    public SettingsStore Settings { get; private set; } = null!;

    public static IIconCacheService? IconCache => Current is App app ? app.IconCacheService : null;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Fase 15: um verbo do menu de contexto real da area de trabalho relanca o exe com
        // "--desktop-action=<acao>" - se nao houver nenhum, o comando padrao e' so' "open"
        // (equivalente a clicar no icone da bandeja).
        var desktopAction = DesktopContextMenuRegistration.ParseAction(Environment.GetCommandLineArgs());
        var command = desktopAction ?? "open";

        // Fase 14: um segundo lancamento so' repassa o comando pra instancia ja' viva e sai -
        // nunca abre um segundo icone na bandeja/segundo conjunto de grupos.
        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstanceCoordinator.TrySendToRunningInstance(command);
            Exit();
            return;
        }

        MenuRepository = new SqliteMenuRepository();
        IconCacheService = new IconCacheService();
        ExportService = new ConfigExportService(MenuRepository);
        LnkImportService = new LnkImportService(new LnkResolver(), MenuRepository);
        Settings = new SettingsStore();
        ThemeService.Initialize(Settings);
        FeatureTier.Initialize(Settings);
        LocalizationService.SetLanguage(Settings.Get(SettingsStore.LanguageKey) ?? LocalizationService.DetectLanguage());
        _ = SeedData.EnsureSeededAsync(MenuRepository);

        // Nao existe uma "janela principal" visivel: o unico ponto de entrada e' o icone
        // da bandeja (RF01 - um unico icone na Taskbar). TrayIconWindow hospeda o
        // TaskbarIcon (H.NotifyIcon) e permanece oculta; ela e' quem abre o PopupWindow.
        _trayIconWindow = new TrayIconWindow();
        _trayIconWindow.Activate();
        _trayIconWindow.AppWindow.Hide();

        // So' depois desta janela ja' ter sido ativada - ver o comentario em
        // TrayIconWindow.OpenDesktopGroupsIfFull para o motivo (XamlParseException se uma
        // Window com {ThemeResource} for construida antes da primeira janela do processo
        // ser ativada - so' que aqui nao usamos mais ThemeResource nenhum, e' so' por cautela).
        _trayIconWindow.OpenDesktopGroupsIfFull();

        // Se este primeiro lancamento ja' veio com uma acao (o app estava fechado quando o
        // usuario clicou um verbo do menu de contexto real da area de trabalho), processa
        // agora - sem isso, so' segundos lancamentos (Fase 14) executariam alguma acao.
        if (desktopAction is not null)
        {
            try
            {
                _trayIconWindow.HandleExternalCommand(desktopAction);
            }
            catch
            {
            }
        }

        // Despachado pro DispatcherQueue (thread de UI certo) porque chega de uma thread do
        // listener do named pipe. O try/catch e' obrigatorio aqui, nao defensivo: uma excecao
        // gerenciada que escapa de um callback do DispatcherQueue derruba o processo inteiro
        // com uma falha nativa (STATUS_STOWED_EXCEPTION) em vez de ser capturavel normalmente
        // - reproduzido e documentado na secao 6.16 do doc tecnico.
        _singleInstance.StartListening(command => _trayIconWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _trayIconWindow.HandleExternalCommand(command);
            }
            catch
            {
            }
        }));
    }
}
