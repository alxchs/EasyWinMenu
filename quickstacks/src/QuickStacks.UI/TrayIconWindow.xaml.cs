using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        RefreshTexts();
        LocalizationService.LanguageChanged += RefreshTexts;
    }

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
        }
    }

    public IRelayCommand ShowPopupCommand { get; }

    public IRelayCommand OpenEditorCommand { get; }

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

        if (FeatureTier.IsFull)
        {
            _ = OpenAllDesktopGroupsAsync();
        }
        else
        {
            CloseAllDesktopGroups();
        }
    }

    /// <summary>
    /// Abre uma DesktopGroupWindow para cada pasta ja' marcada IsDesktopGroup=true - ligar o
    /// modo Full nao apaga nem recria nada, so' mostra as janelas soltas que ja' existiam.
    /// </summary>
    private async Task OpenAllDesktopGroupsAsync()
    {
        App.Log("OpenAllDesktopGroupsAsync: Starting...");
        CloseAllDesktopGroups();

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var groups = await app.MenuRepository.GetDesktopGroupsAsync();
        App.Log($"OpenAllDesktopGroupsAsync: Retrieved {groups.Count} desktop groups from repository.");
        foreach (var group in groups)
        {
            try
            {
                App.Log($"OpenAllDesktopGroupsAsync: Instantiating DesktopGroupWindow for '{group.Name}' ({group.Id})...");
                var window = new DesktopGroupWindow(app.MenuRepository, group);
                _desktopGroupWindows.Add(window);
                window.Activate();
                App.Log($"OpenAllDesktopGroupsAsync: DesktopGroupWindow activated for '{group.Name}'.");
            }
            catch (Exception ex)
            {
                App.Log($"OpenAllDesktopGroupsAsync: ERROR for group '{group.Name}': {ex}");
            }
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
