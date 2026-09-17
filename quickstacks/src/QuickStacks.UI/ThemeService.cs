using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuickStacks.Infrastructure;
using Windows.UI;

namespace QuickStacks.UI;

/// <summary>
/// Tema claro/escuro/seguir Windows (Fase 4), persistido em Settings ("theme.mode"). WinUI 3
/// nao tem um "Application.RequestedTheme" que se possa trocar em runtime numa app nao
/// empacotada - por isso cada janela se registra aqui e tem seu ElementTheme reaplicado a
/// mao quando o usuario troca de tema no menu da bandeja.
///
/// Tambem e' quem pinta o fundo de cada janela: <c>{ThemeResource LayerFillColorDefaultBrush}</c>
/// (o jeito "normal" do WinUI 3) falha com XamlParseException nesta maquina porque o App.xaml
/// nao tem os dicionarios padrao do Fluent mergeados - e mergea-los
/// (<c>XamlControlsResources</c>) trava/derruba o processo aqui tao cedo quanto o
/// RadioMenuFlyoutItem travava (mesmo tipo de falha nativa 0xC0000409, reproduzido isolado).
/// Em vez de depender de qualquer recurso de tema do framework, cada janela pinta o proprio
/// fundo com uma cor fixa escolhida aqui a partir de <see cref="FrameworkElement.ActualTheme"/>.
/// </summary>
public static class ThemeService
{
    private static readonly List<WeakReference<FrameworkElement>> TrackedRoots = [];

    public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public static void Initialize(SettingsStore settings)
    {
        var raw = settings.Get(SettingsStore.ThemeModeKey);
        CurrentTheme = Enum.TryParse<ElementTheme>(raw, out var parsed) ? parsed : ElementTheme.Default;
    }

    /// <summary>Chamar no construtor de cada Window, com o painel raiz do conteudo (precisa ter Background).</summary>
    public static void Register(Panel root)
    {
        root.RequestedTheme = CurrentTheme;
        ApplyBackground(root);
        root.ActualThemeChanged += (_, _) => ApplyBackground(root);
        TrackedRoots.Add(new WeakReference<FrameworkElement>(root));
    }

    public static void SetTheme(SettingsStore settings, ElementTheme theme)
    {
        CurrentTheme = theme;
        settings.Set(SettingsStore.ThemeModeKey, theme.ToString());

        foreach (var weak in TrackedRoots)
        {
            if (weak.TryGetTarget(out var root))
            {
                root.RequestedTheme = theme;
            }
        }
    }

    private static void ApplyBackground(Panel root) => root.Background = CreateDefaultBackgroundBrush(root.ActualTheme);

    /// <summary>Exposto para quem precisa voltar ao fundo padrao depois de ter trocado por uma cor customizada (ex.: PopupWindow ao sair de uma pasta com cor propria).</summary>
    public static SolidColorBrush CreateDefaultBackgroundBrush(ElementTheme actualTheme) => new(actualTheme == ElementTheme.Dark
        ? Color.FromArgb(255, 32, 32, 32)
        : Color.FromArgb(255, 243, 243, 243));
}
