namespace QuickStacks.Domain;

/// <summary>
/// Resolve um arquivo .lnk para o alvo/argumentos/diretorio/icone reais que ele aponta
/// (RF08). O EasyWinMenu nunca fez isso - so classificava ".lnk" pela extensao e guardava o
/// proprio arquivo .lnk como Target, sem nunca abri-lo.
/// </summary>
public interface ILnkResolver
{
    LnkShortcutInfo Resolve(string lnkFilePath);
}
