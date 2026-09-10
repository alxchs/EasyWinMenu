namespace EasyWinMenu.Core.Models;

public sealed class MenuTheme
{
    public string BackgroundColor { get; set; } = "#1E1E1E";
    public double Opacity { get; set; } = 0.97;
    public string BorderColor { get; set; } = "#3C3C3C";
    public double CornerRadius { get; set; } = 6;
    public bool ShowShadow { get; set; } = true;
    public double ItemSpacing { get; set; } = 2;
    public double ItemPadding { get; set; } = 8;
    public double IconSize { get; set; } = 18;
    public string TextColor { get; set; } = "#FFFFFF";
    public string HighlightColor { get; set; } = "#3D7EB8FF";
    public string TitleFontFamily { get; set; } = "Segoe UI";
    public double TitleFontSize { get; set; } = 13;
    public bool TitleBold { get; set; } = true;
    public string ItemFontFamily { get; set; } = "Segoe UI";
    public double ItemFontSize { get; set; } = 13;
    public int AnimationDurationMs { get; set; } = 120;

    public MenuTheme Clone()
    {
        return new MenuTheme
        {
            BackgroundColor = BackgroundColor,
            Opacity = Opacity,
            BorderColor = BorderColor,
            CornerRadius = CornerRadius,
            ShowShadow = ShowShadow,
            ItemSpacing = ItemSpacing,
            ItemPadding = ItemPadding,
            IconSize = IconSize,
            TextColor = TextColor,
            HighlightColor = HighlightColor,
            TitleFontFamily = TitleFontFamily,
            TitleFontSize = TitleFontSize,
            TitleBold = TitleBold,
            ItemFontFamily = ItemFontFamily,
            ItemFontSize = ItemFontSize,
            AnimationDurationMs = AnimationDurationMs
        };
    }
}
