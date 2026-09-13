using System.Text.Json.Serialization;

namespace EasyWinMenu.Core.Models;

public sealed class MenuCategory : IMenuContainer
{
    public required string Name { get; set; }
    public List<MenuCategory> Categories { get; init; } = [];
    public List<LaunchItem> Items { get; init; } = [];
    public MenuTheme? ThemeOverride { get; set; }
    public bool IsDesktopGroup { get; set; }
    public double DesktopX { get; set; } = 40;
    public double DesktopY { get; set; } = 40;
    public double DesktopWidth { get; set; } = 220;
    public double DesktopHeight { get; set; } = 180;
    public double DesktopIconScale { get; set; } = 1.0;
    public string? DesktopBackgroundImagePath { get; set; }
    public double? IconX { get; set; }
    public double? IconY { get; set; }
    public bool IsCollapsed { get; set; }
    /// <summary>0 is fully see-through, 1 fully painted. Replaces the old on/off flag.</summary>
    public double AreaOpacity { get; set; } = 1.0;
    public double TitleOpacity { get; set; } = 1.0;

    /// <summary>
    /// Reads the on/off transparency written before the sliders existed. The getter is
    /// null so the retired key is never written back into the file.
    /// </summary>
    [JsonPropertyName("AreaTransparent")]
    public bool? LegacyAreaTransparent
    {
        get => null;
        set { if (value == true) { AreaOpacity = 0; } }
    }

    [JsonPropertyName("TitleTransparent")]
    public bool? LegacyTitleTransparent
    {
        get => null;
        set { if (value == true) { TitleOpacity = 0; } }
    }
    public DesktopGroupDisplayMode DisplayMode { get; set; } = DesktopGroupDisplayMode.Panel;

    /// <summary>
    /// Where the panel sat before switching to the folder look. The folder tile is a
    /// different size, so without remembering this the panel would come back resized
    /// and moved to wherever the tile happened to be.
    /// </summary>
    public double? PanelX { get; set; }
    public double? PanelY { get; set; }
    public bool ShowBadge { get; set; } = true;

    /// <summary>
    /// Takes on another group's appearance — theme, opacities, backdrop and badge —
    /// while leaving its own contents, position and size alone.
    /// </summary>
    public void CopyVisualFrom(MenuCategory source)
    {
        ThemeOverride = source.ThemeOverride?.Clone();
        AreaOpacity = source.AreaOpacity;
        TitleOpacity = source.TitleOpacity;
        DesktopBackgroundImagePath = source.DesktopBackgroundImagePath;
        ShowBadge = source.ShowBadge;
        DesktopIconScale = source.DesktopIconScale;
    }

    public MenuCategory Clone()
    {
        return new MenuCategory
        {
            Name = Name,
            Items = [.. Items.Select(item => item.Clone())],
            Categories = [.. Categories.Select(category => category.Clone())],
            ThemeOverride = ThemeOverride?.Clone(),
            IsDesktopGroup = IsDesktopGroup,
            DesktopX = DesktopX,
            DesktopY = DesktopY,
            DesktopWidth = DesktopWidth,
            DesktopHeight = DesktopHeight,
            DesktopIconScale = DesktopIconScale,
            DesktopBackgroundImagePath = DesktopBackgroundImagePath,
            IconX = IconX,
            IconY = IconY,
            IsCollapsed = IsCollapsed,
            AreaOpacity = AreaOpacity,
            TitleOpacity = TitleOpacity,
            DisplayMode = DisplayMode,
            PanelX = PanelX,
            PanelY = PanelY,
            ShowBadge = ShowBadge
        };
    }
}
