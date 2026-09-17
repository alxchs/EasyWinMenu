using Microsoft.Win32;
using QuickStacks.Localization;

namespace QuickStacks.UI;

/// <summary>
/// Registra um submenu em cascata no proprio menu de contexto real da area de trabalho do
/// Windows (Fase 15, porta direta do DesktopContextMenuRegistration.cs do EasyWinMenu) - o
/// truque classico de verbo em <c>DesktopBackground\Shell</c>, sem nenhuma DLL de shell
/// extension nem hospedagem COM. Cada verbo relanca este exe com um argumento
/// "--desktop-action=" - o SingleInstanceCoordinator (Fase 14) garante que isso ou abre o app
/// (se estava fechado) ou repassa a acao pra instancia ja' rodando, funcionando tanto com o
/// icone da bandeja aberto quanto fechado.
/// </summary>
internal static class DesktopContextMenuRegistration
{
    private const string RootKeyPath = @"Software\Classes\DesktopBackground\Shell\QuickStacks";
    private const string ArgumentPrefix = "--desktop-action=";

    public const string NewGroupAction = "new-group";
    public const string AllAppFolderAction = "all-appfolder";
    public const string AllPanelAction = "all-panel";
    public const string OpenEditorAction = "open-editor";

    /// <summary>
    /// Idempotente - seguro de chamar a cada inicializacao. Os rotulos sempre seguem o idioma
    /// do proprio Windows, nunca o idioma configurado no app: este menu e' lido pelo Explorer
    /// mesmo com o app fechado, entao a preferencia interna de idioma do app nao tem influencia
    /// aqui - igual a qualquer outro item ja presente nesse menu (Atualizar, Novo, Configuracoes
    /// de tela...), que seguem o Windows, nao uma preferencia por app.
    /// </summary>
    public static void Register()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        var lang = LocalizationService.DetectLanguage();
        string L(string key) => LocalizationService.GetForLanguage(lang, key);

        using var root = Registry.CurrentUser.CreateSubKey(RootKeyPath);
        root.SetValue("MUIVerb", L("desktop.contextMenuRoot"));
        root.SetValue("Icon", $"\"{executablePath}\",0");
        // Um valor SubCommands vazio e' o que diz ao Explorer que este verbo e' um submenu
        // cujos itens moram na propria subchave "shell", em vez de um comando unico.
        root.SetValue("SubCommands", string.Empty);

        using var shell = root.CreateSubKey("shell");
        WriteVerb(shell, "01NewGroup", L("desktop.newGroup"), executablePath, NewGroupAction);
        WriteVerb(shell, "02AllAppFolder", L("desktop.allAppFolder"), executablePath, AllAppFolderAction);
        WriteVerb(shell, "03AllPanel", L("desktop.allPanel"), executablePath, AllPanelAction);
        WriteVerb(shell, "04OpenEditor", L("tray.editStructure"), executablePath, OpenEditorAction);
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(RootKeyPath, throwOnMissingSubKey: false);

    /// <summary>A acao nomeada por um argumento de linha de comando, ou nulo quando nao ha nenhuma.</summary>
    public static string? ParseAction(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
            {
                return arg[ArgumentPrefix.Length..];
            }
        }

        return null;
    }

    private static void WriteVerb(RegistryKey shellKey, string verbKeyName, string label, string executablePath, string action)
    {
        using var verb = shellKey.CreateSubKey(verbKeyName);
        verb.SetValue(string.Empty, label);

        using var command = verb.CreateSubKey("command");
        command.SetValue(string.Empty, $"\"{executablePath}\" {ArgumentPrefix}{action}");
    }
}
