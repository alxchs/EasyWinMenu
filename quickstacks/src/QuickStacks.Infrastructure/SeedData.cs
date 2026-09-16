using QuickStacks.Domain;

namespace QuickStacks.Infrastructure;

/// <summary>
/// Popula o banco com uma estrutura de exemplo real (nao lorem) na primeira execucao, so
/// para a Fase 1 ser visualmente testavel antes de existir um editor. Fases seguintes
/// substituem isto por um fluxo real de importacao/criacao.
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(IMenuRepository repository, CancellationToken ct = default)
    {
        var root = await repository.GetChildrenAsync(null, ct);
        if (root.Count > 0)
        {
            return;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var utilitarios = MenuItem.CreateFolder("Utilitários", null, 0);
        await repository.AddAsync(utilitarios, ct);

        await repository.AddAsync(
            MenuItem.CreateShortcut("Bloco de Notas", utilitarios.Id, MenuItemType.Executable, Path.Combine(windows, "notepad.exe"), 0),
            ct);
        await repository.AddAsync(
            MenuItem.CreateShortcut("Calculadora", utilitarios.Id, MenuItemType.Executable, "calc.exe", 1),
            ct);

        var ferramentasWeb = MenuItem.CreateFolder("Ferramentas Web", utilitarios.Id, 2);
        await repository.AddAsync(ferramentasWeb, ct);
        await repository.AddAsync(
            MenuItem.CreateShortcut("Anthropic", ferramentasWeb.Id, MenuItemType.Url, "https://www.anthropic.com", 0),
            ct);

        var documentosPessoais = MenuItem.CreateFolder("Documentos", null, 1);
        await repository.AddAsync(documentosPessoais, ct);
        // MenuItemType.Executable, nao .Folder: aqui o alvo e' uma pasta REAL do Windows a
        // ser aberta no Explorer (ShellExecute aceita caminho de pasta). MenuItemType.Folder
        // e' reservado para uma pasta/grupo do proprio QuickStacks (um container na arvore).
        await repository.AddAsync(
            MenuItem.CreateShortcut("Meus Documentos", documentosPessoais.Id, MenuItemType.Executable, documents, 0),
            ct);
    }
}
