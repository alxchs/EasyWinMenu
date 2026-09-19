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

    public static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickStacks");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "app.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public App()
    {
        InitializeComponent();

        UnhandledException += (s, e) =>
        {
            Log($"[FATAL] App.UnhandledException: {e.Message}\n{e.Exception}");
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"[FATAL] AppDomain.CurrentDomain.UnhandledException: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log($"[FATAL] TaskScheduler.UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };
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

    public IShellContextMenuService ShellContextMenuService { get; private set; } = null!;

    public IUpdateService UpdateService { get; private set; } = null!;

    public SettingsStore Settings { get; private set; } = null!;

    public static IIconCacheService? IconCache => Current is App app ? app.IconCacheService : null;

    public static IShellContextMenuService? ShellContextMenu => Current is App app ? app.ShellContextMenuService : null;

    public static IUpdateService? Updates => Current is App app ? app.UpdateService : null;

    private static HelpWindow? _helpWindow;

    public static void OpenHelp()
    {
        if (_helpWindow is null)
        {
            _helpWindow = new HelpWindow();
            _helpWindow.Closed += (_, _) => _helpWindow = null;
        }

        _helpWindow.Activate();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched disparado.");
        try
        {
            var desktopAction = DesktopContextMenuRegistration.ParseAction(Environment.GetCommandLineArgs());
            var command = desktopAction ?? "open";

            _singleInstance = new SingleInstanceCoordinator();
            if (!_singleInstance.IsFirstInstance)
            {
                Log("Instância secundária detectada. Encaminhando comando e saindo.");
                SingleInstanceCoordinator.TrySendToRunningInstance(command);
                Exit();
                return;
            }

            MenuRepository = new SqliteMenuRepository();
            IconCacheService = new IconCacheService();
            ShellContextMenuService = new ShellContextMenuService();
            UpdateService = new UpdateService();
            ExportService = new ConfigExportService(MenuRepository);
            LnkImportService = new LnkImportService(new LnkResolver(), MenuRepository);
            Settings = new SettingsStore();
            ThemeService.Initialize(Settings);
            FeatureTier.Initialize(Settings);
            LocalizationService.SetLanguage(Settings.Get(SettingsStore.LanguageKey) ?? LocalizationService.DetectLanguage());
            _ = SeedData.EnsureSeededAsync(MenuRepository);

            _trayIconWindow = new TrayIconWindow();
            _trayIconWindow.Activate();
            _trayIconWindow.AppWindow.Hide();

            Log("TrayIconWindow ativada e oculta. Abrindo desktop groups se modo Full...");
            _trayIconWindow.OpenDesktopGroupsIfFull();

            if (desktopAction is not null)
            {
                try
                {
                    _trayIconWindow.HandleExternalCommand(desktopAction);
                }
                catch (Exception ex)
                {
                    Log($"Erro ao executar ação externa '{desktopAction}': {ex}");
                }
            }

            _singleInstance.StartListening(command => _trayIconWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _trayIconWindow.HandleExternalCommand(command);
                }
                catch (Exception ex)
                {
                    Log($"Erro ao processar comando recebido '{command}': {ex}");
                }
            }));

            Log("Inicialização concluída com sucesso.");
        }
        catch (Exception ex)
        {
            Log($"[FATAL] Exceção em OnLaunched: {ex}");
            throw;
        }
    }
}
