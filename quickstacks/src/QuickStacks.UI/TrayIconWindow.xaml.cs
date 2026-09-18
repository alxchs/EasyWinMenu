using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using QuickStacks.Localization;

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
    private readonly List<DesktopGroupWindow> _desktopGroupWindows = [];
    private GlobalHotkeyService? _hotkeyService;

    public TrayIconWindow()
    {
        ShowPopupCommand = new RelayCommand(ShowPopup);
        OpenEditorCommand = new RelayCommand(OpenEditor);
        OpenHelpCommand = new RelayCommand(App.OpenHelp);
        ExitCommand = new RelayCommand(() => Microsoft.UI.Xaml.Application.Current.Exit());

        InitializeComponent();

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "quickstacks.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "quickstacks-placeholder.ico");
        }
        if (System.IO.File.Exists(iconPath))
        {
            TrayIcon.Icon = new System.Drawing.Icon(iconPath);
        }

        // O item marcado nos submenus Tema/Idioma precisa refletir o que ja' foi carregado de
        // Settings em App.OnLaunched - setado a mao pelo mesmo motivo do PopupWindow (Window
        // nao e' FrameworkElement, entao nao da' pra fazer isso via x:Bind aqui).
        SetCheckedExclusive(ThemeService.CurrentTheme switch
        {
            ElementTheme.Light => ThemeLightItem,
            ElementTheme.Dark => ThemeDarkItem,
            _ => ThemeSystemItem,
        }, ThemeLightItem, ThemeDarkItem, ThemeSystemItem);

        SetCheckedExclusive(LocalizationService.CurrentLanguage switch
        {
            "en-US" => LanguageEnUsItem,
            "es-ES" => LanguageEsEsItem,
            "de-DE" => LanguageDeDeItem,
            _ => LanguagePtBrItem,
        }, LanguagePtBrItem, LanguageEnUsItem, LanguageEsEsItem, LanguageDeDeItem);

        SetCheckedExclusive(FeatureTier.IsFull ? TierFullItem : TierLiteItem, TierLiteItem, TierFullItem);

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        GlobalHotkeyItem.IsChecked = app.Settings.Get(SettingsStore.GlobalHotkeyEnabledKey) != "false";
        StartWithWindowsItem.IsChecked = StartupRegistration.IsEnabled();

        _hotkeyService = new GlobalHotkeyService(this);
        _hotkeyService.OpenPopupRequested += ShowPopup;
        _hotkeyService.RestoreGroupsRequested += RestoreDesktopGroups;
        ApplyHotkeyState();

        RefreshTexts();
        LocalizationService.LanguageChanged += RefreshTexts;
    }

    /// <summary>Fase 13: Ctrl+Alt+Q sempre disponivel enquanto o atalho estiver ligado; Win+Ctrl+Alt+D (restaurar grupos) so' faz sentido com o modo Full ligado.</summary>
    private void ApplyHotkeyState()
    {
        var enabled = GlobalHotkeyItem.IsChecked;
        _hotkeyService?.SetOpenPopupHotkeyEnabled(enabled);
        _hotkeyService?.SetRestoreGroupsHotkeyEnabled(enabled && FeatureTier.IsFull);
    }

    /// <summary>Reativa as janelas de grupo ja' abertas (traz de volta pra frente) ou reabre do zero se nenhuma estiver rastreada - equivalente ao atalho "restaurar grupos" do EasyWinMenu.</summary>
    private void RestoreDesktopGroups()
    {
        if (!FeatureTier.IsFull)
        {
            return;
        }

        if (_desktopGroupWindows.Count == 0)
        {
            _ = OpenAllDesktopGroupsAsync();
            return;
        }

        foreach (var window in _desktopGroupWindows)
        {
            window.Activate();
        }
    }

    /// <summary>Comando repassado por uma segunda instancia via SingleInstanceCoordinator (Fase 14) - "open" (bandeja/hotkey) ou um dos verbos do menu de contexto real da area de trabalho (Fase 15). Ja' chamado no thread de UI certo pelo App.OnLaunched (DispatcherQueue.TryEnqueue).</summary>
    public void HandleExternalCommand(string command)
    {
        switch (command)
        {
            case "open":
                ShowPopup();
                break;
            case DesktopContextMenuRegistration.NewGroupAction:
                _ = CreateNewDesktopGroupAsync();
                break;
            case DesktopContextMenuRegistration.AllAppFolderAction:
                _ = SetAllGroupsDisplayModeAsync(DesktopGroupDisplayMode.AppFolder);
                break;
            case DesktopContextMenuRegistration.AllPanelAction:
                _ = SetAllGroupsDisplayModeAsync(DesktopGroupDisplayMode.Panel);
                break;
            case DesktopContextMenuRegistration.OpenEditorAction:
                OpenEditor();
                break;
        }
    }

    /// <summary>Verbo "Novo grupo" do menu de contexto real da area de trabalho (Fase 15) - cria uma pasta raiz ja' marcada como grupo e reabre as janelas soltas pra mostra-la.</summary>
    private async Task CreateNewDesktopGroupAsync()
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var existing = await app.MenuRepository.GetDesktopGroupsAsync();
        var folder = MenuItem.CreateFolder(LocalizationService.Get("editor.defaultFolderName"), null, existing.Count);
        await app.MenuRepository.AddAsync(folder);
        await app.MenuRepository.SetIsDesktopGroupAsync(folder.Id, true);

        if (FeatureTier.IsFull)
        {
            await OpenAllDesktopGroupsAsync();
        }
    }

    /// <summary>Verbos "Todos -> App Folder"/"Todos -> Panel" do menu de contexto real da area de trabalho (Fase 15).</summary>
    private async Task SetAllGroupsDisplayModeAsync(DesktopGroupDisplayMode mode)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var groups = await app.MenuRepository.GetDesktopGroupsAsync();
        foreach (var group in groups)
        {
            var placement = await app.MenuRepository.GetDesktopGroupPlacementAsync(group.Id);
            if (placement is not null)
            {
                await app.MenuRepository.SetDesktopGroupPlacementAsync(placement with { DisplayMode = mode });
            }
        }

        if (FeatureTier.IsFull)
        {
            await OpenAllDesktopGroupsAsync();
        }
    }

    private void GlobalHotkey_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        app.Settings.Set(SettingsStore.GlobalHotkeyEnabledKey, GlobalHotkeyItem.IsChecked ? "true" : "false");
        ApplyHotkeyState();
    }

    private void StartWithWindows_Click(object sender, RoutedEventArgs e) => StartupRegistration.SetEnabled(StartWithWindowsItem.IsChecked);

    /// <summary>
    /// So' pode ser chamado depois desta janela ja' ter sido ativada (App.OnLaunched, apos
    /// Activate()) - construir uma outra Window (DesktopGroupWindow) de dentro do construtor
    /// desta, antes dela mesma ter sido ativada, falha com XamlParseException "Cannot find a
    /// Resource... LayerFillColorDefaultBrush": os dicionarios de tema Fluent padrao so' ficam
    /// disponiveis depois que a primeira janela do processo e' ativada.
    /// </summary>
    public void OpenDesktopGroupsIfFull()
    {
        if (FeatureTier.IsFull)
        {
            _ = OpenAllDesktopGroupsAsync();
            DesktopContextMenuRegistration.Register();
        }
    }

    public IRelayCommand ShowPopupCommand { get; }

    public IRelayCommand OpenEditorCommand { get; }

    public IRelayCommand OpenHelpCommand { get; }

    public IRelayCommand ExitCommand { get; }

    /// <summary>Marca so' <paramref name="selected"/> entre os itens de <paramref name="group"/> - RadioMenuFlyoutItem faria isso sozinho via GroupName, mas trava/crasha o processo nesta maquina (reproduzido isolado, ver secao 4 do doc tecnico), daqui em diante e' ToggleMenuFlyoutItem com exclusividade a mao.</summary>
    private static void SetCheckedExclusive(ToggleMenuFlyoutItem selected, params ToggleMenuFlyoutItem[] group)
    {
        foreach (var item in group)
        {
            item.IsChecked = ReferenceEquals(item, selected);
        }
    }

    private void RefreshTexts()
    {
        OpenItem.Text = LocalizationService.Get("tray.open");
        EditStructureItem.Text = LocalizationService.Get("tray.editStructure");
        ThemeSubItem.Text = LocalizationService.Get("tray.theme");
        ThemeLightItem.Text = LocalizationService.Get("tray.theme.light");
        ThemeDarkItem.Text = LocalizationService.Get("tray.theme.dark");
        ThemeSystemItem.Text = LocalizationService.Get("tray.theme.system");
        LanguageSubItem.Text = LocalizationService.Get("tray.language");
        TierSubItem.Text = LocalizationService.Get("tray.tier");
        TierLiteItem.Text = LocalizationService.Get("tray.tier.lite");
        TierFullItem.Text = LocalizationService.Get("tray.tier.full");
        GlobalHotkeyItem.Text = LocalizationService.Get("tray.globalHotkey");
        StartWithWindowsItem.Text = LocalizationService.Get("tray.startWithWindows");
        HelpItem.Text = LocalizationService.Get("tray.help");
        ExitItem.Text = LocalizationService.Get("tray.exit");
    }

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
            _editor = new EditorWindow(app.MenuRepository, app.ExportService, app.LnkImportService);
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

        SetCheckedExclusive(theme switch
        {
            ElementTheme.Light => ThemeLightItem,
            ElementTheme.Dark => ThemeDarkItem,
            _ => ThemeSystemItem,
        }, ThemeLightItem, ThemeDarkItem, ThemeSystemItem);
    }

    private void LanguagePtBr_Click(object sender, RoutedEventArgs e) => SetLanguage("pt-BR");

    private void LanguageEnUs_Click(object sender, RoutedEventArgs e) => SetLanguage("en-US");

    private void LanguageEsEs_Click(object sender, RoutedEventArgs e) => SetLanguage("es-ES");

    private void LanguageDeDe_Click(object sender, RoutedEventArgs e) => SetLanguage("de-DE");

    private void SetLanguage(string languageCode)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        app.Settings.Set(SettingsStore.LanguageKey, languageCode);
        LocalizationService.SetLanguage(languageCode);

        SetCheckedExclusive(languageCode switch
        {
            "en-US" => LanguageEnUsItem,
            "es-ES" => LanguageEsEsItem,
            "de-DE" => LanguageDeDeItem,
            _ => LanguagePtBrItem,
        }, LanguagePtBrItem, LanguageEnUsItem, LanguageEsEsItem, LanguageDeDeItem);
    }

    private void TierLite_Click(object sender, RoutedEventArgs e) => SetTier(FeatureTier.LiteValue);

    private void TierFull_Click(object sender, RoutedEventArgs e) => SetTier(FeatureTier.FullValue);

    private void SetTier(string tier)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        FeatureTier.SetTier(app.Settings, tier);

        SetCheckedExclusive(FeatureTier.IsFull ? TierFullItem : TierLiteItem, TierLiteItem, TierFullItem);
        ApplyHotkeyState();

        if (FeatureTier.IsFull)
        {
            _ = OpenAllDesktopGroupsAsync();
            DesktopContextMenuRegistration.Register();
        }
        else
        {
            CloseAllDesktopGroups();
            DesktopContextMenuRegistration.Unregister();
        }
    }

    /// <summary>
    /// Abre uma DesktopGroupWindow para cada pasta ja' marcada IsDesktopGroup=true - ligar o
    /// modo Full nao apaga nem recria nada, so' mostra as janelas soltas que ja' existiam.
    /// </summary>
    private async Task OpenAllDesktopGroupsAsync()
    {
        CloseAllDesktopGroups();

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var groups = await app.MenuRepository.GetDesktopGroupsAsync();
        foreach (var group in groups)
        {
            var window = new DesktopGroupWindow(app.MenuRepository, group);
            _desktopGroupWindows.Add(window);
            window.Activate();
        }
    }

    /// <summary>Desligar o modo Full so' fecha as janelas - os dados (IsDesktopGroup, posicoes, geometria) continuam no banco.</summary>
    private void CloseAllDesktopGroups()
    {
        foreach (var window in _desktopGroupWindows)
        {
            window.Close();
        }

        _desktopGroupWindows.Clear();
    }
}
