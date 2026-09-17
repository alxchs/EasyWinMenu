namespace QuickStacks.Domain;

public enum DesktopGroupDisplayMode
{
    Panel,
    AppFolder,
}

/// <summary>
/// Organizacao automatica "viva" (Fase 11) - None deixa cada icone onde o usuario arrastou
/// (DesktopIconPosition manda); os outros tres reaplicam a ordem sozinhos a cada mudanca de
/// conteudo/geometria, ignorando a posicao livre salva.
/// </summary>
public enum DesktopIconArrangement
{
    None,
    Grid,
    ByName,
    ByType,
}

/// <summary>
/// Geometria/estado da janela solta de um grupo (Fase 9, modo Full) - equivalente aos campos
/// DesktopX/Y/Width/Height/DisplayMode/DesktopIconScale/IsCollapsed do MenuCategory do
/// EasyWinMenu, numa tabela separada em vez de colunas soltas em MenuItems (só quem usa Full
/// tem uma linha aqui).
/// </summary>
public sealed record DesktopGroupPlacement(
    string GroupId,
    double X,
    double Y,
    double Width,
    double Height,
    DesktopGroupDisplayMode DisplayMode,
    double IconScale,
    bool IsCollapsed,
    DesktopIconArrangement Arrangement = DesktopIconArrangement.None)
{
    public const double DefaultWidth = 260;
    public const double DefaultHeight = 220;
    public const double DefaultIconScale = 1.0;

    public static DesktopGroupPlacement CreateDefault(string groupId, double x, double y) =>
        new(groupId, x, y, DefaultWidth, DefaultHeight, DesktopGroupDisplayMode.Panel, DefaultIconScale, false, DesktopIconArrangement.None);
}
