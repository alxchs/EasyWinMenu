using System.ComponentModel;
using System.Diagnostics;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>
/// Executa e interage com itens folha (Fase 18 - paridade total com EasyWinMenu):
/// - Modos Normal, Minimizada, Maximizada e Administrador (runas)
/// - Expansão de variáveis de ambiente e resolução via %PATH%
/// - Envoltório de comandos via cmd.exe /c
/// - Ações de Shell (Revelar no Explorer, resolver caminho real)
/// - Resiliência a cancelamento de UAC e arquivos inexistentes.
/// </summary>
public static class LaunchService
{
    public static async Task LaunchAsync(IMenuRepository repository, MenuEntryViewModel entry, CancellationToken ct = default)
    {
        await LaunchWithModeAsync(repository, entry, null, ct);
    }

    public static async Task LaunchWithModeAsync(
        IMenuRepository repository,
        MenuEntryViewModel entry,
        ExecutionMode? modeOverride,
        CancellationToken ct = default)
    {
        if (entry.IsFolder || entry.Path is null)
        {
            return;
        }

        var item = await repository.GetByIdAsync(entry.Id, ct) ?? entry.Item;
        var plan = LaunchPlanner.BuildPlan(item, modeOverride: modeOverride);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                Arguments = plan.Arguments,
                UseShellExecute = plan.UseShellExecute,
                WindowStyle = plan.WindowStyle,
            };

            if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory))
            {
                startInfo.WorkingDirectory = plan.WorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(plan.Verb))
            {
                startInfo.Verb = plan.Verb;
            }

            Process.Start(startInfo);
            await repository.RegisterLaunchAsync(item.Id, ct);
        }
        catch (Win32Exception)
        {
            // Erro 1223 = ERROR_CANCELLED (usuário recusou prompt do UAC) ou arquivo não encontrado.
        }
        catch (FileNotFoundException)
        {
        }
        catch
        {
            // Proteção contra falhas catastróficas ao disparar processos
        }
    }

    public static async Task RunAsAdministratorAsync(IMenuRepository repository, MenuEntryViewModel entry, CancellationToken ct = default)
    {
        await LaunchWithModeAsync(repository, entry, ExecutionMode.Administrator, ct);
    }

    /// <summary>
    /// Devolve o caminho físico real resolvido caso o item aponte para um arquivo ou pasta no disco.
    /// </summary>
    public static string? TryResolvePhysicalPath(MenuEntryViewModel entry)
    {
        if (entry.IsFolder || entry.Type == MenuItemType.Url || entry.Type == MenuItemType.Command || entry.Path is null)
        {
            return null;
        }

        var expanded = LaunchPlanner.Expand(entry.Path);
        if (File.Exists(expanded) || Directory.Exists(expanded))
        {
            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        var resolved = LaunchPlanner.ResolveExecutablePath(expanded);
        if (File.Exists(resolved) || Directory.Exists(resolved))
        {
            try
            {
                return Path.GetFullPath(resolved);
            }
            catch
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>
    /// Abre o Explorador do Windows selecionando o arquivo físico correspondente.
    /// </summary>
    public static void RevealInExplorer(MenuEntryViewModel entry)
    {
        var path = TryResolvePhysicalPath(entry) ?? LaunchPlanner.Expand(entry.Path);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var arguments = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
