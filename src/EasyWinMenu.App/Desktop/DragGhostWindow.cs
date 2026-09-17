using System.Windows;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// A borderless, click-through, always-on-top window that shows a live snapshot of a tile
/// being dragged, positioned in screen coordinates so it can travel across the edges of the
/// source group's own window - something a tile moved only within its own Canvas can never
/// do, since nothing renders outside a WPF window's own bounds.
///
/// Built as a real <see cref="VisualBrush"/> of the original tile rather than a cloned visual
/// tree (an element can only ever live in one visual parent at a time in WPF), so the ghost
/// always mirrors whatever the tile currently looks like without any extra bookkeeping.
/// </summary>
internal sealed class DragGhostWindow : Window
{
    private DragGhostWindow(FrameworkElement tile, Point screenTopLeft)
    {
        var width = Math.Max(1, tile.ActualWidth);
        var height = Math.Max(1, tile.ActualHeight);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        ShowActivated = false;
        Width = width;
        Height = height;
        Left = screenTopLeft.X;
        Top = screenTopLeft.Y;
        Content = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = new VisualBrush(tile) { Stretch = Stretch.None },
            Opacity = 0.85,
        };
    }

    public Point CurrentTopLeft => new(Left, Top);

    /// <param name="tile">The tile to mirror. Its current appearance is captured live via a VisualBrush.</param>
    /// <param name="screenTopLeft">Where the ghost starts, in screen coordinates - normally the tile's own current screen position.</param>
    /// <param name="onShown">Called once the ghost is visible - used to dim the source tile so it doesn't look duplicated.</param>
    public static DragGhostWindow Show(FrameworkElement tile, Point screenTopLeft, Action onShown)
    {
        var ghost = new DragGhostWindow(tile, screenTopLeft);
        ghost.Show();
        onShown();
        return ghost;
    }
}
