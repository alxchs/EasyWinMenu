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

        // UseShellExecute=true: mesmo caminho para .exe, arquivo, pasta e URL - o shell do
        // Windows decide o handler (o proprio Explorer, o navegador padrao, etc.).
        Process.Start(new ProcessStartInfo
        {
            FileName = item.Path,
            Arguments = item.Arguments ?? string.Empty,
            WorkingDirectory = item.WorkingDirectory ?? string.Empty,
            UseShellExecute = true,
        });

        await repository.RegisterLaunchAsync(item.Id, ct);
    }
}
