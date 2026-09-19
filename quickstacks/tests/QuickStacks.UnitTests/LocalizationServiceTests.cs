using QuickStacks.Localization;
using Xunit;

namespace QuickStacks.UnitTests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    public void EveryLanguage_HasEveryKeyThatDefaultLanguageHas(string languageCode)
    {
        // Compara os dicionarios crus (via LoadTableForTests), nao o Get() com fallback - o
        // fallback pt-BR do Get() esconderia exatamente o tipo de lacuna que este teste
        // existe para pegar (uma chave que ficou faltando so' num dos 4 arquivos).
        var defaultKeys = LocalizationService.LoadTableForTests(LocalizationService.DefaultLanguage).Keys;
        var languageKeys = LocalizationService.LoadTableForTests(languageCode).Keys;

        var faltando = defaultKeys.Except(languageKeys).ToList();
        Assert.True(faltando.Count == 0, $"'{languageCode}' esta sem as chaves: {string.Join(", ", faltando)}");
    }

    [Fact]
    public void Get_UnknownKey_ReturnsTheKeyItself_NeverThrows()
    {
        Assert.Equal("chave.que.nao.existe", LocalizationService.GetForLanguage("en-US", "chave.que.nao.existe"));
    }

    [Theory]
    [InlineData("en-US", "tray.open", "Open")]
    [InlineData("pt-BR", "tray.open", "Abrir")]
    public void GetForLanguage_KnownKey_ReturnsRealTranslation_NotTheKeyItself(string languageCode, string key, string expected)
    {
        Assert.Equal(expected, LocalizationService.GetForLanguage(languageCode, key));
    }

    [Fact]
    public void GetForLanguage_UnsupportedLanguage_FallsBackToDefault()
    {
        var esperado = LocalizationService.GetForLanguage(LocalizationService.DefaultLanguage, "tray.open");
        Assert.Equal(esperado, LocalizationService.GetForLanguage("fr-FR", "tray.open"));
    }

    [Fact]
    public void DetectLanguage_NeverReturnsUnsupportedCode()
    {
        var detected = LocalizationService.DetectLanguage();
        Assert.Contains(LocalizationService.SupportedLanguages, l => l.Code == detected);
    }

    [Fact]
    public void SetLanguage_RaisesLanguageChanged_AndSetLanguage_RejectsUnsupportedCode()
    {
        var raised = 0;
        void Handler() => raised++;

        LocalizationService.LanguageChanged += Handler;
        try
        {
            LocalizationService.SetLanguage("en-US");
            Assert.Equal("en-US", LocalizationService.CurrentLanguage);
            Assert.Equal(1, raised);

            LocalizationService.SetLanguage("fr-FR"); // nao suportado - cai para o padrao
            Assert.Equal(LocalizationService.DefaultLanguage, LocalizationService.CurrentLanguage);
        }
        finally
        {
            LocalizationService.LanguageChanged -= Handler;
            LocalizationService.SetLanguage(LocalizationService.DefaultLanguage); // nao vazar estado entre testes
        }
    }
}
