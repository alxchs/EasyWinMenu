using System.Windows;
using Brush = System.Windows.Media.Brush;

namespace EasyWinMenu.App.Menu;

internal static class MenuThemeProperties
{
    public static readonly DependencyProperty HighlightBrushProperty = DependencyProperty.RegisterAttached(
        "HighlightBrush", typeof(Brush), typeof(MenuThemeProperties), new PropertyMetadata(System.Windows.Media.Brushes.Transparent));

    public static void SetHighlightBrush(DependencyObject element, Brush value) => element.SetValue(HighlightBrushProperty, value);
    public static Brush GetHighlightBrush(DependencyObject element) => (Brush)element.GetValue(HighlightBrushProperty);

    public static readonly DependencyProperty HighlightCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "HighlightCornerRadius", typeof(CornerRadius), typeof(MenuThemeProperties), new PropertyMetadata(new CornerRadius(0)));

    public static void SetHighlightCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(HighlightCornerRadiusProperty, value);
    public static CornerRadius GetHighlightCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(HighlightCornerRadiusProperty);

    public static readonly DependencyProperty PanelCornerRadiusProperty = DependencyProperty.RegisterAttached(
        "PanelCornerRadius", typeof(CornerRadius), typeof(MenuThemeProperties), new PropertyMetadata(new CornerRadius(0)));

    public static void SetPanelCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(PanelCornerRadiusProperty, value);
    public static CornerRadius GetPanelCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(PanelCornerRadiusProperty);
}
