using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyWinMenu.Probe;

/// <summary>
/// The Win32 surface the probe needs. Written in C# on purpose: PowerShell's marshalling of
/// <c>$null</c> into a P/Invoke <c>string</c> parameter has already produced two wrong
/// conclusions in this project (see docs/OVERVIEW.md, "Nota sobre PowerShell"), so every
/// window diagnostic here goes through real interop instead.
/// </summary>
internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    public const byte VK_ESCAPE = 0x1B;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public static string ClassOf(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string TitleOf(IntPtr hWnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static List<IntPtr> VisibleWindowsOf(uint processId)
    {
        var found = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner == processId && IsWindowVisible(hWnd))
            {
                found.Add(hWnd);
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static void RightClick(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(220);
        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
    }

    public static void PressEscape()
    {
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// Captures the window's own pixels. <c>PW_RENDERFULLCONTENT</c> is what makes this work for
    /// the group windows, which are <c>AllowsTransparency</c> (layered) — a plain BitBlt of the
    /// screen would capture whatever sits behind them instead.
    /// </summary>
    public static bool TryCapture(IntPtr hWnd, string pngPath)
    {
        if (!GetWindowRect(hWnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        using var bitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                {
                    return false;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        bitmap.Save(pngPath, ImageFormat.Png);
        return true;
    }
}
