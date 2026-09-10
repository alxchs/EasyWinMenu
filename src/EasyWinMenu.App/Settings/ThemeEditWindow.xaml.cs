using System.Globalization;
using System.Windows;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Settings;

public partial class ThemeEditWindow : Window
{
    private readonly LauncherConfiguration _configuration;
    private readonly MenuCategory? _category;

    public ThemeEditWindow(LauncherConfiguration configuration, MenuCategory? category)
    {
        InitializeComponent();

        _configuration = configuration;
        _category = category;

        if (category is null)
        {
            UseOwnThemeBox.Visibility = Visibility.Collapsed;
            LoadTheme(configuration.Theme);
        }
        else
        {
            UseOwnThemeBox.IsChecked = category.ThemeOverride is not null;
            LoadTheme(category.ThemeOverride ?? configuration.Theme);
            UpdateFieldsEnabled();
        }
    }

    private void OnUseOwnThemeChanged(object sender, RoutedEventArgs e)
    {
        UpdateFieldsEnabled();
    }

    private void UpdateFieldsEnabled()
    {
        FieldsPanel.IsEnabled = _category is null || UseOwnThemeBox.IsChecked == true;
    }

    private void LoadTheme(MenuTheme theme)
    {
        BackgroundColorBox.Text = theme.BackgroundColor;
        OpacityBox.Text = theme.Opacity.ToString(CultureInfo.InvariantCulture);
        BorderColorBox.Text = theme.BorderColor;
        CornerRadiusBox.Text = theme.CornerRadius.ToString(CultureInfo.InvariantCulture);
        ShowShadowBox.IsChecked = theme.ShowShadow;
        ItemSpacingBox.Text = theme.ItemSpacing.ToString(CultureInfo.InvariantCulture);
        ItemPaddingBox.Text = theme.ItemPadding.ToString(CultureInfo.InvariantCulture);
        IconSizeBox.Text = theme.IconSize.ToString(CultureInfo.InvariantCulture);
        TextColorBox.Text = theme.TextColor;
        HighlightColorBox.Text = theme.HighlightColor;
        TitleFontFamilyBox.Text = theme.TitleFontFamily;
        TitleFontSizeBox.Text = theme.TitleFontSize.ToString(CultureInfo.InvariantCulture);
        TitleBoldBox.IsChecked = theme.TitleBold;
        ItemFontFamilyBox.Text = theme.ItemFontFamily;
        ItemFontSizeBox.Text = theme.ItemFontSize.ToString(CultureInfo.InvariantCulture);
        AnimationDurationBox.Text = theme.AnimationDurationMs.ToString(CultureInfo.InvariantCulture);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        LoadTheme(new MenuTheme());
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_category is not null && UseOwnThemeBox.IsChecked != true)
        {
            _category.ThemeOverride = null;
            DialogResult = true;
            return;
        }

        var theme = BuildTheme();

        if (_category is null)
        {
            _configuration.Theme = theme;
        }
        else
        {
            _category.ThemeOverride = theme;
        }

        DialogResult = true;
    }

    private MenuTheme BuildTheme()
    {
        return new MenuTheme
        {
            BackgroundColor = BackgroundColorBox.Text.Trim(),
            Opacity = ParseDouble(OpacityBox.Text, 0.97),
            BorderColor = BorderColorBox.Text.Trim(),
            CornerRadius = ParseDouble(CornerRadiusBox.Text, 6),
            ShowShadow = ShowShadowBox.IsChecked == true,
            ItemSpacing = ParseDouble(ItemSpacingBox.Text, 2),
            ItemPadding = ParseDouble(ItemPaddingBox.Text, 8),
            IconSize = ParseDouble(IconSizeBox.Text, 18),
            TextColor = TextColorBox.Text.Trim(),
            HighlightColor = HighlightColorBox.Text.Trim(),
            TitleFontFamily = TitleFontFamilyBox.Text.Trim(),
            TitleFontSize = ParseDouble(TitleFontSizeBox.Text, 13),
            TitleBold = TitleBoldBox.IsChecked == true,
            ItemFontFamily = ItemFontFamilyBox.Text.Trim(),
            ItemFontSize = ParseDouble(ItemFontSizeBox.Text, 13),
            AnimationDurationMs = (int)ParseDouble(AnimationDurationBox.Text, 120)
        };
    }

    private static double ParseDouble(string text, double fallback)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }
}
