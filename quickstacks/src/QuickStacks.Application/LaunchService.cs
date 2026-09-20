using System.Diagnostics;
using QuickStacks.Domain;

namespace QuickStacks.Application;

/// <summary>Executa um item folha (RF03/RF04/RF05/RF06 - exe, arquivo, pasta ou URL).</summary>
public static class LaunchService
{
    public static async Task LaunchAsync(IMenuRepository repository, MenuEntryViewModel entry, CancellationToken ct = default)
    {
        if (entry.IsFolder || entry.Path is null)
        {
            return;
        }

        var item = await repository.GetByIdAsync(entry.Id, ct);
        if (item is null)
        {
            return;
        }

        try
        {
            var path = item.Path?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            ProcessStartInfo psi;

            if (path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                // Atalhos virtuais do Windows (ex: Modo Deus, Painel de Controle)
                psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = path,
                    UseShellExecute = true,
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = item.Arguments ?? string.Empty,
                    WorkingDirectory = item.WorkingDirectory ?? string.Empty,
                    UseShellExecute = true,
                };
            }

            Process.Start(psi);
            await repository.RegisterLaunchAsync(item.Id, ct);
        }
        catch
        {
            // Ignora falha de execução externa sem derrubar a aplicação
        }
    }
}
