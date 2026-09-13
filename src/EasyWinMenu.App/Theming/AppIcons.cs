using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;

namespace EasyWinMenu.App.Theming;

/// <summary>
/// Renders the line icons from Icons.xaml. They are authored on a 24x24 grid, so each
/// one is laid on a canvas of exactly that size before being scaled: without the fixed
/// canvas a short glyph would be blown up to the same bounds as a tall one and the set
/// would lose its common weight.
/// </summary>
internal static class AppIcons
{
    private const double DesignGrid = 24;
    private const double StrokeThickness = 1.7;

    public static FrameworkElement? Create(string key, Brush stroke, double size = 16)
    {
        if (Application.Current?.TryFindResource(key) is not Geometry geometry)
        {
            return null;
        }

        var path = new Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            Fill = null
        };

        var canvas = new Canvas
        {
            Width = DesignGrid,
            Height = DesignGrid,
            Background = Brushes.Transparent
        };
        canvas.Children.Add(path);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            SnapsToDevicePixels = true
        };
    }
}
