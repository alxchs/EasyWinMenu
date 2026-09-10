using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace EasyWinMenu.App.Menu;

internal static class ThemeBrushes
{
    public static Brush CreateBrush(string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return new SolidColorBrush(color);
    }
}
