using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brushes = System.Windows.Media.Brushes;
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
        Left = initialScreenDip.X;
        Top = initialScreenDip.Y;
        _physicalTopLeft = initialPhysical;

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
    /// <param name="initialScreenDip">Where the ghost starts, in screen DIP coordinates.</param>
    /// <param name="initialPhysical">Where the ghost starts, in physical screen coordinates.</param>
    /// <param name="dpiScaleX">Display scale X for high-DPI snapshot sharpness.</param>
    /// <param name="dpiScaleY">Display scale Y for high-DPI snapshot sharpness.</param>
    /// <param name="onShown">Called once the ghost is visible - used to dim the source tile so it doesn't look duplicated.</param>
    public static DragGhostWindow Show(
        FrameworkElement tile,
        double visualWidth,
        double visualHeight,
        Point initialScreenDip,
        POINT initialPhysical,
        double dpiScaleX,
        double dpiScaleY,
        Action onShown)
    {
        var ghost = new DragGhostWindow(tile, visualWidth, visualHeight, initialScreenDip, initialPhysical, dpiScaleX, dpiScaleY);
        ghost.Show();
        onShown();
        return ghost;
    }
}
