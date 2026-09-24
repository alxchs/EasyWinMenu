using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace EasyWinMenu.App.Desktop;

/// <summary>
/// A borderless, click-through, always-on-top window that shows a live snapshot of a tile
/// being dragged, positioned in physical screen coordinates via Win32 SetWindowPos so it can
/// travel across monitors and window borders with 1:1 cursor tracking, zero drift, and no DPI jumping.
/// </summary>
internal sealed class DragGhostWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    private IntPtr _hwnd;
    private POINT _physicalTopLeft;

    private DragGhostWindow(
        FrameworkElement tile,
        double visualWidth,
        double visualHeight,
        Point initialScreenDip,
        POINT initialPhysical,
        double dpiScaleX,
        double dpiScaleY,
        int itemCount)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        ShowActivated = false;

        var extraWidth = itemCount > 1 ? 16.0 : 0.0;
        var extraHeight = itemCount > 1 ? 16.0 : 0.0;
        Width = Math.Max(1, visualWidth + extraWidth);
        Height = Math.Max(1, visualHeight + extraHeight);
        Left = initialScreenDip.X;
        Top = initialScreenDip.Y;
        _physicalTopLeft = initialPhysical;

        var snapshot = CaptureVisual(tile, dpiScaleX, dpiScaleY);
        FrameworkElement primaryVisual = snapshot is not null
            ? new System.Windows.Controls.Image
            {
                Source = snapshot,
                Width = Math.Max(1, visualWidth),
                Height = Math.Max(1, visualHeight),
                Stretch = Stretch.Uniform,
                Opacity = 0.92,
                IsHitTestVisible = false
            }
            : new Rectangle
            {
                Width = Math.Max(1, visualWidth),
                Height = Math.Max(1, visualHeight),
                Fill = new VisualBrush(tile)
                {
                    Stretch = Stretch.Uniform,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                },
                Opacity = 0.92,
                IsHitTestVisible = false
            };

        if (itemCount <= 1)
        {
            Content = primaryVisual;
        }
        else
        {
            var grid = new Grid
            {
                Width = Width,
                Height = Height,
                IsHitTestVisible = false
            };

            var stackCard = new Border
            {
                Width = Math.Max(1, visualWidth - 4),
                Height = Math.Max(1, visualHeight - 4),
                Background = new SolidColorBrush(Color.FromArgb(0x55, 0x36, 0x41, 0x53)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x64, 0x74, 0x8B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10, 10, 0, 0),
                Opacity = 0.70,
                IsHitTestVisible = false
            };
            grid.Children.Add(stackCard);

            primaryVisual.HorizontalAlignment = HorizontalAlignment.Left;
            primaryVisual.VerticalAlignment = VerticalAlignment.Top;
            primaryVisual.Margin = new Thickness(0, 0, 0, 0);
            grid.Children.Add(primaryVisual);

            // Badge com contador (estilo discreto, sem vermelho):
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x22, 0x30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = itemCount.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                }
            };
            grid.Children.Add(badge);

            Content = grid;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd != IntPtr.Zero)
        {
            var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED);

            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);

            // Position at initial physical point immediately
            SetWindowPos(_hwnd, HWND_TOPMOST, _physicalTopLeft.X, _physicalTopLeft.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            handled = true;
            return (IntPtr)HTTRANSPARENT;
        }

        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }

    public void MovePhysical(int physicalX, int physicalY)
    {
        _physicalTopLeft = new POINT { X = physicalX, Y = physicalY };
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, physicalX, physicalY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        else
        {
            Left = physicalX;
            Top = physicalY;
        }
    }

    public POINT CurrentPhysicalTopLeft => _physicalTopLeft;

    public Point CurrentTopLeft => new(Left, Top);

    private static ImageSource? CaptureVisual(FrameworkElement element, double dpiScaleX, double dpiScaleY)
    {
        try
        {
            var w = element.ActualWidth > 0 ? element.ActualWidth : 80;
            var h = element.ActualHeight > 0 ? element.ActualHeight : 80;
            var pxW = (int)Math.Max(1, Math.Ceiling(w * dpiScaleX));
            var pxH = (int)Math.Max(1, Math.Ceiling(h * dpiScaleY));

            // Renderizar via DrawingVisual + VisualBrush com Viewbox absoluto
            // para ignorar completamente offsets do Canvas/painel pai (ex: Canvas.Left, Canvas.Top)
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new VisualBrush(element)
                {
                    Stretch = Stretch.Uniform,
                    Viewbox = new Rect(0, 0, w, h),
                    ViewboxUnits = BrushMappingMode.Absolute
                };
                dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96 * dpiScaleX, 96 * dpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
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
    /// <param name="initialScreenDip">Where the ghost starts, in screen DIP coordinates.</param>
    /// <param name="initialPhysical">Where the ghost starts, in physical screen coordinates.</param>
    /// <param name="dpiScaleX">Display scale X for high-DPI snapshot sharpness.</param>
    /// <param name="dpiScaleY">Display scale Y for high-DPI snapshot sharpness.</param>
    /// <param name="itemCount">Total number of items being dragged simultaneously.</param>
    /// <param name="onShown">Called once the ghost is visible - used to dim the source tile(s).</param>
    public static DragGhostWindow Show(
        FrameworkElement tile,
        double visualWidth,
        double visualHeight,
        Point initialScreenDip,
        POINT initialPhysical,
        double dpiScaleX,
        double dpiScaleY,
        int itemCount,
        Action onShown)
    {
        var ghost = new DragGhostWindow(tile, visualWidth, visualHeight, initialScreenDip, initialPhysical, dpiScaleX, dpiScaleY, itemCount);
        ghost.Show();
        if (ghost._hwnd != IntPtr.Zero)
        {
            SetWindowPos(ghost._hwnd, HWND_TOPMOST, initialPhysical.X, initialPhysical.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        onShown();
        return ghost;
    }
}
