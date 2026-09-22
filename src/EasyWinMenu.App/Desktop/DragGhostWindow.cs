using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// A borderless, click-through, always-on-top window that shows a live snapshot of a tile
/// being dragged, positioned in screen DIP coordinates so it can travel across the edges of the
/// source group's own window with 1:1 mouse tracking and DPI scaling awareness.
/// </summary>
internal sealed class DragGhostWindow : Window
{
    private DragGhostWindow(
        FrameworkElement tile,
        double visualWidth,
        double visualHeight,
        Point screenTopLeft,
        double dpiScaleX,
        double dpiScaleY)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        ShowActivated = false;
        Width = Math.Max(1, visualWidth);
        Height = Math.Max(1, visualHeight);
        Left = screenTopLeft.X;
        Top = screenTopLeft.Y;

        var snapshot = CaptureVisual(tile, dpiScaleX, dpiScaleY);
        if (snapshot is not null)
        {
            Content = new System.Windows.Controls.Image
            {
                Source = snapshot,
                Width = Math.Max(1, visualWidth),
                Height = Math.Max(1, visualHeight),
                Stretch = Stretch.Uniform,
                Opacity = 0.88,
                IsHitTestVisible = false
            };
        }
        else
        {
            Content = new Rectangle
            {
                Width = Math.Max(1, visualWidth),
                Height = Math.Max(1, visualHeight),
                Fill = new VisualBrush(tile)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                },
                Opacity = 0.88,
                IsHitTestVisible = false
            };
        }
    }

    public Point CurrentTopLeft => new(Left, Top);

    private static ImageSource? CaptureVisual(FrameworkElement element, double dpiScaleX, double dpiScaleY)
    {
        try
        {
            var width = (int)Math.Max(1, Math.Ceiling(element.ActualWidth * dpiScaleX));
            var height = (int)Math.Max(1, Math.Ceiling(element.ActualHeight * dpiScaleY));
            var rtb = new RenderTargetBitmap(width, height, 96 * dpiScaleX, 96 * dpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(element);
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    /// <param name="tile">The tile to mirror.</param>
    /// <param name="visualWidth">Visual width in DIPs matching on-screen scaled size.</param>
    /// <param name="visualHeight">Visual height in DIPs matching on-screen scaled size.</param>
    /// <param name="screenTopLeft">Where the ghost starts, in screen DIP coordinates.</param>
    /// <param name="dpiScaleX">Display scale X for high-DPI snapshot sharpness.</param>
    /// <param name="dpiScaleY">Display scale Y for high-DPI snapshot sharpness.</param>
    /// <param name="onShown">Called once the ghost is visible - used to dim the source tile so it doesn't look duplicated.</param>
    public static DragGhostWindow Show(
        FrameworkElement tile,
        double visualWidth,
        double visualHeight,
        Point screenTopLeft,
        double dpiScaleX,
        double dpiScaleY,
        Action onShown)
    {
        var ghost = new DragGhostWindow(tile, visualWidth, visualHeight, screenTopLeft, dpiScaleX, dpiScaleY);
        ghost.Show();
        onShown();
        return ghost;
    }
}
