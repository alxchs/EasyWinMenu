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

    public static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickStacks");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "quickstacks.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            Log($"[FATAL] App.UnhandledException: {e.Message}\n{e.Exception}");
            e.Handled = true; // Impede que exceções não tratadas do WinRT derrubem o processo
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"[FATAL] AppDomain.UnhandledException: {e.ExceptionObject}");
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

    public SettingsStore Settings { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched: Starting initialization...");
        MenuRepository = new SqliteMenuRepository();
        ExportService = new ConfigExportService(MenuRepository);
        LnkImportService = new LnkImportService(new LnkResolver(), MenuRepository);
        Settings = new SettingsStore();
        ThemeService.Initialize(Settings);
        FeatureTier.Initialize(Settings);
        Log($"OnLaunched: FeatureTier initialized: IsFull = {FeatureTier.IsFull}");
        LocalizationService.SetLanguage(Settings.Get(SettingsStore.LanguageKey) ?? LocalizationService.DetectLanguage());

        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Contains("--migrate-easywinmenu") && EasyWinMenuMigrationService.HasLegacyConfig())
        {
            Log("OnLaunched: Migrating from EasyWinMenu config.json...");
            var migrated = await EasyWinMenuMigrationService.MigrateAsync(MenuRepository);
            Log($"OnLaunched: Migrated {migrated} items from EasyWinMenu.");
        }
        else
        {
            _ = SeedData.EnsureSeededAsync(MenuRepository);
        }

        _trayIconWindow = new TrayIconWindow();
        _trayIconWindow.Activate();
        _trayIconWindow.AppWindow.Hide();
        Log("OnLaunched: TrayIconWindow activated and hidden.");

        _trayIconWindow.OpenDesktopGroupsIfFull();
        Log("OnLaunched: OpenDesktopGroupsIfFull called.");

        if (cmdArgs.Contains("--open-editor"))
        {
            _trayIconWindow.OpenEditorCommand.Execute(null);
        }
        else if (cmdArgs.Contains("--open-popup"))
        {
            _trayIconWindow.ShowPopupCommand.Execute(null);
        }
        Log("OnLaunched: Completed.");
    }
}
