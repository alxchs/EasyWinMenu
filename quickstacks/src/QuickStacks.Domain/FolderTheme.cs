namespace QuickStacks.Domain;

/// <summary>
/// Tema completo por pasta (Fase 8, modo Full) - equivalente ao MenuTheme do EasyWinMenu.
/// Todo campo é opcional: null significa "usa o tema global/padrão do sistema", igual ao
/// ThemeOverride do EasyWinMenu (que só existe de verdade quando o usuário liga "usar tema
/// próprio" para aquela pasta). Fica junto de FolderAppearance (mesma tabela desde a Fase 4)
/// em vez de nascer uma tabela nova - a cor de fundo simples da Fase 4 é só o primeiro campo
/// deste mesmo conjunto.
/// </summary>
public sealed record FolderTheme(
    string? BackgroundColorHex,
    string? BorderColorHex,
    string? TextColorHex,
    string? HighlightColorHex,
    double? CornerRadius,
    double? ShadowBlurRadius,
    double? ShadowDepth,
    double? ShadowDirection,
    double? ShadowOpacity,
    double? ItemSpacing,
    double? ItemPadding,
    double? IconSize,
    string? TitleFontFamily,
    double? TitleFontSize,
    bool? TitleBold,
    string? ItemFontFamily,
    double? ItemFontSize,
    double? AnimationDurationMs)
{
    public static FolderTheme Empty { get; } = new(
        null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null);
}
