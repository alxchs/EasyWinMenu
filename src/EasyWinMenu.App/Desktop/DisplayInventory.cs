using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace EasyWinMenu.App.Desktop;

/// <summary>The monitors connected right now, in the coordinates WPF positions windows with.</summary>
internal static class DisplayInventory
{
    /// <summary>
    /// Work areas of every monitor, taskbars excluded, in device-independent pixels.
    /// The conversion goes through <paramref name="reference"/> because Screen reports
    /// device pixels and a window's Left and Top are not.
    /// </summary>
    public static IReadOnlyList<Rect> WorkAreas(Visual reference)
    {
        var transform = PresentationSource.FromVisual(reference)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        return Screen.AllScreens
            .Select(screen =>
            {
                var area = screen.WorkingArea;
                var topLeft = transform.Transform(new Point(area.Left, area.Top));
                var bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
                return new Rect(topLeft, bottomRight);
            })
            .ToList();
    }
}
