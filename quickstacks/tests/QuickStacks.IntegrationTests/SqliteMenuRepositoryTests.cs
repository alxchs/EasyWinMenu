using QuickStacks.Domain;
using QuickStacks.Infrastructure;
using Xunit;

namespace QuickStacks.IntegrationTests;

public class SqliteMenuRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteMenuRepository _repository;

    public SqliteMenuRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"quickstacks-tests-{Guid.NewGuid():N}.db");
        _repository = new SqliteMenuRepository($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task AddAndGetChildren_ReturnsItemsOrderedBySortOrder()
    {
        var folder = MenuItem.CreateFolder("Desenvolvimento", null, 0);
        await _repository.AddAsync(folder);

        var second = MenuItem.CreateShortcut("Git", folder.Id, MenuItemType.Executable, "git.exe", 1);
        var first = MenuItem.CreateShortcut("Visual Studio", folder.Id, MenuItemType.Executable, "devenv.exe", 0);
        await _repository.AddAsync(second);
        await _repository.AddAsync(first);

        var children = await _repository.GetChildrenAsync(folder.Id);

        Assert.Equal(2, children.Count);
        Assert.Equal("Visual Studio", children[0].Name);
        Assert.Equal("Git", children[1].Name);
    }

    [Fact]
    public async Task MoveAsync_MovesItemToNewParent()
    {
        var origem = MenuItem.CreateFolder("Origem", null, 0);
        var destino = MenuItem.CreateFolder("Destino", null, 1);
        var item = MenuItem.CreateShortcut("Notepad", origem.Id, MenuItemType.Executable, "notepad.exe", 0);
        await _repository.AddAsync(origem);
        await _repository.AddAsync(destino);
        await _repository.AddAsync(item);

        await _repository.MoveAsync(item.Id, destino.Id);

        Assert.Empty(await _repository.GetChildrenAsync(origem.Id));
        var movido = Assert.Single(await _repository.GetChildrenAsync(destino.Id));
        Assert.Equal(item.Id, movido.Id);
    }

    [Fact]
    public async Task MoveAsync_IntoOwnDescendant_ThrowsAndDoesNotMove()
    {
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateFolder("Filho", pai.Id, 0);
        await _repository.AddAsync(pai);
        await _repository.AddAsync(filho);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.MoveAsync(pai.Id, filho.Id));

        // 'Pai' continua na raiz - o move nao deve ter tido efeito parcial.
        var raiz = await _repository.GetChildrenAsync(null);
        Assert.Contains(raiz, i => i.Id == pai.Id);
    }

    [Fact]
    public async Task MoveAsync_IntoSelf_Throws()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.MoveAsync(pasta.Id, pasta.Id));
    }

    [Fact]
    public async Task DeleteAsync_OnFolder_CascadesToChildren()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        var filho = MenuItem.CreateShortcut("Notepad", pasta.Id, MenuItemType.Executable, "notepad.exe", 0);
        await _repository.AddAsync(pasta);
        await _repository.AddAsync(filho);

        await _repository.DeleteAsync(pasta.Id);

        Assert.Null(await _repository.GetByIdAsync(pasta.Id));
        Assert.Null(await _repository.GetByIdAsync(filho.Id));
    }

    [Fact]
    public async Task ReorderChildrenAsync_UpdatesSortOrder()
    {
        var a = MenuItem.CreateFolder("A", null, 0);
        var b = MenuItem.CreateFolder("B", null, 1);
        await _repository.AddAsync(a);
        await _repository.AddAsync(b);

        await _repository.ReorderChildrenAsync(null, [b.Id, a.Id]);

        var raiz = await _repository.GetChildrenAsync(null);
        Assert.Equal("B", raiz[0].Name);
        Assert.Equal("A", raiz[1].Name);
    }

    [Fact]
    public async Task ReplaceAllAsync_RebuildsTreeEvenWithChildBeforeParentInList()
    {
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateShortcut("Filho", pai.Id, MenuItemType.Executable, "calc.exe", 0);

        // Proposital: filho antes do pai na lista, para provar que o import nao depende
        // da ordem (FK fica desligada so durante a carga - ver SqliteMenuRepository.ReplaceAllAsync).
        await _repository.ReplaceAllAsync([filho, pai]);

        var raiz = await _repository.GetChildrenAsync(null);
        var doPai = await _repository.GetChildrenAsync(pai.Id);
        Assert.Single(raiz);
        Assert.Equal("Pai", raiz[0].Name);
        Assert.Single(doPai);
        Assert.Equal("Filho", doPai[0].Name);
    }

    [Fact]
    public async Task ReplaceAllAsync_ThenDelete_StillCascades()
    {
        // Garante que a FK volta a ficar ligada depois do import (nao fica OFF para sempre).
        var pai = MenuItem.CreateFolder("Pai", null, 0);
        var filho = MenuItem.CreateShortcut("Filho", pai.Id, MenuItemType.Executable, "calc.exe", 0);
        await _repository.ReplaceAllAsync([pai, filho]);

        await _repository.DeleteAsync(pai.Id);

        Assert.Null(await _repository.GetByIdAsync(filho.Id));
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitiveAndGlobal_NotScopedToOneLevel()
    {
        var pasta = MenuItem.CreateFolder("Ferramentas Web", null, 0);
        var netoDentroDaPasta = MenuItem.CreateShortcut("Anthropic Console", pasta.Id, MenuItemType.Url, "https://console.anthropic.com", 0);
        var outroNaRaiz = MenuItem.CreateShortcut("Calculadora", null, MenuItemType.Executable, "calc.exe", 1);
        await _repository.AddAsync(pasta);
        await _repository.AddAsync(netoDentroDaPasta);
        await _repository.AddAsync(outroNaRaiz);

        var resultado = await _repository.SearchAsync("anthropic");

        var encontrado = Assert.Single(resultado);
        Assert.Equal(netoDentroDaPasta.Id, encontrado.Id);
    }

    [Fact]
    public async Task SearchAsync_EscapesLikeWildcards()
    {
        var item = MenuItem.CreateShortcut("100% Pronto", null, MenuItemType.Executable, "notepad.exe", 0);
        var outro = MenuItem.CreateShortcut("100X Pronto", null, MenuItemType.Executable, "notepad.exe", 1);
        await _repository.AddAsync(item);
        await _repository.AddAsync(outro);

        // Sem escapar o '%', esta busca combinaria com "100X Pronto" tambem (LIKE trata '%'
        // como coringa) - com o escape, so o item que tem o '%' literal deve aparecer.
        var resultado = await _repository.SearchAsync("100%");

        var encontrado = Assert.Single(resultado);
        Assert.Equal(item.Id, encontrado.Id);
    }

    [Fact]
    public async Task GetFavoritesAsync_ReturnsOnlyFlaggedItems()
    {
        var favorito = MenuItem.CreateShortcut("Favorito", null, MenuItemType.Executable, "a.exe", 0);
        var comum = MenuItem.CreateShortcut("Comum", null, MenuItemType.Executable, "b.exe", 1);
        await _repository.AddAsync(favorito);
        await _repository.AddAsync(comum);

        await _repository.SetFavoriteAsync(favorito.Id, true);

        var favoritos = await _repository.GetFavoritesAsync();
        var encontrado = Assert.Single(favoritos);
        Assert.Equal(favorito.Id, encontrado.Id);
    }

    [Fact]
    public async Task GetRecentAsync_OrdersByLastUsedDescending()
    {
        var antigo = MenuItem.CreateShortcut("Antigo", null, MenuItemType.Executable, "a.exe", 0);
        var recente = MenuItem.CreateShortcut("Recente", null, MenuItemType.Executable, "b.exe", 1);
        await _repository.AddAsync(antigo);
        await _repository.AddAsync(recente);

        await _repository.RegisterLaunchAsync(antigo.Id);
        await _repository.RegisterLaunchAsync(recente.Id);

        var recentes = await _repository.GetRecentAsync(10);
        Assert.Equal(recente.Id, recentes[0].Id);
        Assert.Equal(antigo.Id, recentes[1].Id);
    }

    [Fact]
    public async Task GetMostUsedAsync_OrdersByLaunchCountDescending()
    {
        var poucoUsado = MenuItem.CreateShortcut("Pouco usado", null, MenuItemType.Executable, "a.exe", 0);
        var muitoUsado = MenuItem.CreateShortcut("Muito usado", null, MenuItemType.Executable, "b.exe", 1);
        await _repository.AddAsync(poucoUsado);
        await _repository.AddAsync(muitoUsado);

        await _repository.RegisterLaunchAsync(poucoUsado.Id);
        await _repository.RegisterLaunchAsync(muitoUsado.Id);
        await _repository.RegisterLaunchAsync(muitoUsado.Id);
        await _repository.RegisterLaunchAsync(muitoUsado.Id);

        var maisUsados = await _repository.GetMostUsedAsync(10);
        Assert.Equal(muitoUsado.Id, maisUsados[0].Id);
        Assert.Equal(3, maisUsados[0].LaunchCount);
    }

    [Fact]
    public async Task FolderBackgroundColor_DefaultsToNull_ThenCanBeSetAndCleared()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        Assert.Null(await _repository.GetFolderBackgroundColorAsync(pasta.Id));

        await _repository.SetFolderBackgroundColorAsync(pasta.Id, "#224466");
        Assert.Equal("#224466", await _repository.GetFolderBackgroundColorAsync(pasta.Id));

        await _repository.SetFolderBackgroundColorAsync(pasta.Id, "#112233");
        Assert.Equal("#112233", await _repository.GetFolderBackgroundColorAsync(pasta.Id));

        await _repository.SetFolderBackgroundColorAsync(pasta.Id, null);
        Assert.Null(await _repository.GetFolderBackgroundColorAsync(pasta.Id));
    }

    [Fact]
    public async Task DeleteAsync_OnFolder_AlsoCascadesFolderAppearance()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);
        await _repository.SetFolderBackgroundColorAsync(pasta.Id, "#224466");

        await _repository.DeleteAsync(pasta.Id);

        // Nao deve sobrar uma linha orfa em FolderAppearance referenciando uma pasta que nao existe mais.
        Assert.Null(await _repository.GetFolderBackgroundColorAsync(pasta.Id));
    }

    [Fact]
    public async Task SetFolderBackgroundColorAsync_RejectsMalformedHex()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SetFolderBackgroundColorAsync(pasta.Id, "nao-e-uma-cor"));
    }

    [Fact]
    public async Task GetFolderThemeAsync_WithNoOverride_ReturnsEmpty()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        Assert.Equal(FolderTheme.Empty, await _repository.GetFolderThemeAsync(pasta.Id));
    }

    [Fact]
    public async Task SetFolderThemeAsync_RoundTripsEveryField()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);

        var tema = new FolderTheme(
            BackgroundColorHex: "#101010",
            BorderColorHex: "#202020",
            TextColorHex: "#EFEFEF",
            HighlightColorHex: "#3D7EB8FF",
            CornerRadius: 6,
            ShadowBlurRadius: 12,
            ShadowDepth: 2,
            ShadowDirection: 315,
            ShadowOpacity: 0.35,
            ItemSpacing: 2,
            ItemPadding: 8,
            IconSize: 18,
            TitleFontFamily: "Segoe UI",
            TitleFontSize: 13,
            TitleBold: true,
            ItemFontFamily: "Segoe UI",
            ItemFontSize: 13,
            AnimationDurationMs: 120);

        await _repository.SetFolderThemeAsync(pasta.Id, tema);

        Assert.Equal(tema, await _repository.GetFolderThemeAsync(pasta.Id));
        // A coluna de cor de fundo simples (Fase 4) continua funcionando junto com o tema completo.
        Assert.Equal("#101010", await _repository.GetFolderBackgroundColorAsync(pasta.Id));
    }

    [Fact]
    public async Task SetFolderThemeAsync_ThenClearingBackgroundColor_KeepsRestOfTheme()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);
        await _repository.SetFolderThemeAsync(pasta.Id, FolderTheme.Empty with { BackgroundColorHex = "#101010", BorderColorHex = "#202020" });

        await _repository.SetFolderBackgroundColorAsync(pasta.Id, null);

        var tema = await _repository.GetFolderThemeAsync(pasta.Id);
        Assert.Null(tema.BackgroundColorHex);
        Assert.Equal("#202020", tema.BorderColorHex);
    }

    [Fact]
    public async Task SetFolderThemeAsync_AllFieldsEmpty_DeletesTheRow()
    {
        var pasta = MenuItem.CreateFolder("Pasta", null, 0);
        await _repository.AddAsync(pasta);
        await _repository.SetFolderThemeAsync(pasta.Id, FolderTheme.Empty with { BorderColorHex = "#202020" });

        await _repository.SetFolderThemeAsync(pasta.Id, FolderTheme.Empty);

        Assert.Equal(FolderTheme.Empty, await _repository.GetFolderThemeAsync(pasta.Id));
    }

    [Fact]
    public async Task SetIsDesktopGroupAsync_True_CreatesDefaultPlacement()
    {
        var pasta = MenuItem.CreateFolder("Grupo", null, 0);
        await _repository.AddAsync(pasta);

        await _repository.SetIsDesktopGroupAsync(pasta.Id, true);

        var atualizada = await _repository.GetByIdAsync(pasta.Id);
        Assert.True(atualizada!.IsDesktopGroup);
        var placement = await _repository.GetDesktopGroupPlacementAsync(pasta.Id);
        Assert.NotNull(placement);
        Assert.Equal(DesktopGroupDisplayMode.Panel, placement!.DisplayMode);
    }

    [Fact]
    public async Task SetIsDesktopGroupAsync_TwiceTrue_DoesNotResetExistingPlacement()
    {
        var pasta = MenuItem.CreateFolder("Grupo", null, 0);
        await _repository.AddAsync(pasta);
        await _repository.SetIsDesktopGroupAsync(pasta.Id, true);
        var original = await _repository.GetDesktopGroupPlacementAsync(pasta.Id);
        await _repository.SetDesktopGroupPlacementAsync(original! with { X = 500, Y = 500 });

        await _repository.SetIsDesktopGroupAsync(pasta.Id, true);

        var placement = await _repository.GetDesktopGroupPlacementAsync(pasta.Id);
        Assert.Equal(500, placement!.X);
    }

    [Fact]
    public async Task GetDesktopGroupsAsync_ReturnsOnlyRootFoldersFlagged()
    {
        var grupo = MenuItem.CreateFolder("Grupo", null, 0);
        var comum = MenuItem.CreateFolder("Comum", null, 1);
        await _repository.AddAsync(grupo);
        await _repository.AddAsync(comum);
        await _repository.SetIsDesktopGroupAsync(grupo.Id, true);

        var grupos = await _repository.GetDesktopGroupsAsync();

        var encontrado = Assert.Single(grupos);
        Assert.Equal(grupo.Id, encontrado.Id);
    }

    [Fact]
    public async Task DeleteAsync_OnDesktopGroup_CascadesPlacementAndIconPositions()
    {
        var grupo = MenuItem.CreateFolder("Grupo", null, 0);
        await _repository.AddAsync(grupo);
        await _repository.SetIsDesktopGroupAsync(grupo.Id, true);
        var item = MenuItem.CreateShortcut("Calculadora", grupo.Id, MenuItemType.Executable, "calc.exe", 0);
        await _repository.AddAsync(item);
        await _repository.SetDesktopIconPositionAsync(new DesktopIconPosition(item.Id, 30, 40));

        await _repository.DeleteAsync(grupo.Id);

        Assert.Null(await _repository.GetDesktopGroupPlacementAsync(grupo.Id));
        Assert.Empty(await _repository.GetDesktopIconPositionsAsync(grupo.Id));
    }

    [Fact]
    public async Task DesktopIconPosition_RoundTrips()
    {
        var grupo = MenuItem.CreateFolder("Grupo", null, 0);
        await _repository.AddAsync(grupo);
        var item = MenuItem.CreateShortcut("Calculadora", grupo.Id, MenuItemType.Executable, "calc.exe", 0);
        await _repository.AddAsync(item);

        await _repository.SetDesktopIconPositionAsync(new DesktopIconPosition(item.Id, 30, 40));
        await _repository.SetDesktopIconPositionAsync(new DesktopIconPosition(item.Id, 55, 60));

        var posicoes = await _repository.GetDesktopIconPositionsAsync(grupo.Id);
        var posicao = Assert.Single(posicoes.Values);
        Assert.Equal(55, posicao.X);
        Assert.Equal(60, posicao.Y);
    }
}
