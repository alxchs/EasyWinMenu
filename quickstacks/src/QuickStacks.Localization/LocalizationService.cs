using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace QuickStacks.Localization;

/// <summary>
/// pt-BR/en-US/es-ES/de-DE (RF15/secao "Internacionalizacao" do spec original), trocavel em
/// runtime sem reiniciar. JSON puro embutido como recurso do assembly - nao ".resw"/MRT (ver
/// docs/QuickStacks_Especificacao_Tecnica.md secao 4: a maquina de dev nao tem a ferramenta
/// de PRI que .resw exigiria). Mesmo padrao de fallback para ingles que o EasyWinMenu ja usa,
/// so' que aqui o fallback e' para o codigo nativo do documento, pt-BR.
/// </summary>
public static class LocalizationService
{
    public const string DefaultLanguage = "pt-BR";

    public static readonly IReadOnlyList<(string Code, string DisplayName)> SupportedLanguages =
    [
        ("pt-BR", "Português (Brasil)"),
        ("en-US", "English (US)"),
        ("es-ES", "Español (España)"),
        ("de-DE", "Deutsch (Deutschland)"),
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = [];

    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>Disparado apos <see cref="SetLanguage"/> - a UI deve reconstruir os textos que ja' desenhou.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(string languageCode)
    {
        CurrentLanguage = SupportedLanguages.Any(l => l.Code == languageCode) ? languageCode : DefaultLanguage;
        LanguageChanged?.Invoke();
    }

    /// <summary>Le <see cref="CultureInfo.CurrentUICulture"/> e devolve o idioma suportado mais proximo, ou <see cref="DefaultLanguage"/>.</summary>
    public static string DetectLanguage()
    {
        var name = CultureInfo.CurrentUICulture.Name; // ex: "en-US", "pt-BR", "fr-FR"
        if (SupportedLanguages.Any(l => l.Code == name))
        {
            return name;
        }

        var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var match = SupportedLanguages.FirstOrDefault(l => l.Code.StartsWith(twoLetter, StringComparison.OrdinalIgnoreCase));
        return match.Code ?? DefaultLanguage;
    }

    public static string Get(string key) => GetForLanguage(CurrentLanguage, key);

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    public static string GetForLanguage(string languageCode, string key)
    {
        var table = LoadTable(languageCode);
        if (table.TryGetValue(key, out var value))
        {
            return value;
        }

        var fallback = LoadTable(DefaultLanguage);
        return fallback.TryGetValue(key, out var fallbackValue) ? fallbackValue : key;
    }

    /// <summary>Exposto so' para os testes conferirem paridade de chaves entre idiomas sem passar pelo fallback do Get().</summary>
    internal static IReadOnlyDictionary<string, string> LoadTableForTests(string languageCode) => LoadTable(languageCode);

    private static Dictionary<string, string> LoadTable(string languageCode)
    {
        if (Cache.TryGetValue(languageCode, out var cached))
        {
            return cached;
        }

        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = $"QuickStacks.Localization.Strings.{languageCode}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);

        var table = stream is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];

        Cache[languageCode] = table;
        return table;
    }
}
