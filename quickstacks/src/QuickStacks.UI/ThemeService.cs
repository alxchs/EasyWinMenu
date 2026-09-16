using Microsoft.UI.Xaml;
using QuickStacks.Infrastructure;

namespace QuickStacks.UI;

/// <summary>
/// Tema claro/escuro/seguir Windows (Fase 4), persistido em Settings ("theme.mode"). WinUI 3
/// nao tem um "Application.RequestedTheme" que se possa trocar em runtime numa app nao
/// empacotada - por isso cada janela se registra aqui e tem seu ElementTheme reaplicado a
/// mao quando o usuario troca de tema no menu da bandeja.
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

    /// <summary>Chamar no construtor de cada Window, com o elemento raiz do conteudo (precisa ser um FrameworkElement).</summary>
    public static void Register(FrameworkElement root)
    {
        root.RequestedTheme = CurrentTheme;
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
}
