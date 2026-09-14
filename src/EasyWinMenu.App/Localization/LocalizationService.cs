using System.Globalization;
using System.IO;
using System.Text.Json;

namespace EasyWinMenu.App.Localization;

public static class LocalizationService
{
    public static readonly (string Code, string DisplayName)[] SupportedLanguages =
    [
        ("en", "English"),
        ("pt", "Português"),
        ("es", "Español"),
        ("de", "Deutsch"),
        ("it", "Italiano"),
        ("ru", "Русский"),
        ("pl", "Polski"),
        ("ja", "日本語")
    ];

    private static Dictionary<string, string>? _englishStrings;
    private static Dictionary<string, string>? _strings;

    public static string CurrentLanguage { get; private set; } = "en";

    private static Dictionary<string, string> EnglishStrings => _englishStrings ??= LoadEmbedded("en");

    public static void SetLanguage(string languageCode)
    {
        var code = SupportedLanguages.Any(l => l.Code == languageCode) ? languageCode : "en";
        _strings = code == "en" ? EnglishStrings : LoadEmbedded(code);
        CurrentLanguage = code;
    }

    public static string Get(string key)
    {
        var strings = _strings ?? EnglishStrings;
        if (strings.TryGetValue(key, out var value))
        {
            return value;
        }

        return EnglishStrings.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }

    /// <summary>
    /// Looks a string up in a specific language regardless of which one is currently
    /// active — for text that has to follow Windows' own display language rather than
    /// the app's configured one, such as the labels this app writes into the real
    /// Windows shell context menu (read by Explorer, not by this app, so the app's own
    /// language choice would be the wrong thing to bake into them).
    /// </summary>
    public static string GetForLanguage(string languageCode, string key)
    {
        var code = SupportedLanguages.Any(l => l.Code == languageCode) ? languageCode : "en";
        var strings = code == "en" ? EnglishStrings : LoadEmbedded(code);
        if (strings.TryGetValue(key, out var value))
        {
            return value;
        }

        return EnglishStrings.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string DetectLanguage()
    {
        var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return SupportedLanguages.Any(l => l.Code == twoLetter) ? twoLetter : "en";
    }

    private static Dictionary<string, string> LoadEmbedded(string code)
    {
        var uri = new Uri($"Localization/Strings.{code}.json", UriKind.Relative);
        var resourceInfo = System.Windows.Application.GetResourceStream(uri);
        if (resourceInfo is null)
        {
            return new Dictionary<string, string>();
        }

        using var stream = resourceInfo.Stream;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }
}
