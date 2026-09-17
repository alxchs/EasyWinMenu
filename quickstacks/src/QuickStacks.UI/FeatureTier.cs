using QuickStacks.Infrastructure;

namespace QuickStacks.UI;

/// <summary>
/// Lite (o QuickStacks como sempre foi) vs Full (tudo do EasyWinMenu, incluindo grupos soltos
/// na área de trabalho - roteiro nas Fases 8-20). Guardado em Settings ("product.tier"),
/// trocável em runtime pelo submenu "Modo" na bandeja - mesmo padrão estático de
/// ThemeService/LocalizationService, e pela mesma razão documentada lá: assinar o evento e
/// reagir, em vez de reconstruir a UI inteira a cada troca.
/// </summary>
public static class FeatureTier
{
    public const string LiteValue = "Lite";
    public const string FullValue = "Full";

    public static bool IsFull => Current == FullValue;

    public static string Current { get; private set; } = LiteValue;

    public static event Action? TierChanged;

    public static void Initialize(SettingsStore settings)
    {
        var raw = settings.Get(SettingsStore.ProductTierKey);
        Current = raw == FullValue ? FullValue : LiteValue;
    }

    public static void SetTier(SettingsStore settings, string tier)
    {
        Current = tier == FullValue ? FullValue : LiteValue;
        settings.Set(SettingsStore.ProductTierKey, Current);
        TierChanged?.Invoke();
    }
}
