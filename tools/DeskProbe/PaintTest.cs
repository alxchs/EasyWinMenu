using System.Runtime.InteropServices;

/// <summary>
/// The decisive experiment: create a plain Win32 child window of the desktop, painted a
/// solid colour, and see whether its pixels reach the screen. If they do, cross-process
/// child painting is fine and the earlier failure belongs to WPF specifically. If they
/// do not, no window can live on the desktop layer on this build.
/// </summary>
internal static class PaintTest
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const uint WmPaint = 0x000F;
    private const uint WmDestroy = 0x0002;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private static WndProc? _proc;

    public static void Run(IntPtr parent, int seconds)
    {
        _proc = WindowProcedure;

        var className = "EwmPaintProbe";
        var wndClass = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
            hbrBackground = IntPtr.Zero
        };

        var atom = RegisterClassEx(ref wndClass);
        Console.WriteLine($"RegisterClassEx -> {atom} (erro {Marshal.GetLastWin32Error()})");

        var hwnd = CreateWindowEx(
            0,
            className,
            "probe",
            WsChild | WsVisible,
            300,
            300,
            400,
            300,
            parent,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        Console.WriteLine($"CreateWindowEx -> {hwnd.ToInt64()} (erro {Marshal.GetLastWin32Error()})");
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Sit above the icon view, the same placement the real groups would need.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);

        var iconView = FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (iconView != IntPtr.Zero)
        {
            SetWindowPos(iconView, hwnd, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
            Console.WriteLine($"icon view {iconView.ToInt64()} empurrado para baixo da sonda");
        }

        Console.WriteLine($"sonda visivel por {seconds}s em 300,300 400x300 — capture a tela agora");

        var reported = false;
        var reportAt = DateTime.UtcNow.AddSeconds(2);

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (!reported && DateTime.UtcNow >= reportAt)
            {
                reported = true;
                var centre = new NativePoint { X = 500, Y = 450 };
                var hit = WindowFromPoint(centre);
                Console.WriteLine($"WindowFromPoint(500,450) -> {hit.ToInt64()} "
                                + $"(sonda={hwnd.ToInt64()}, acerta={hit == hwnd})");
                Console.Out.Flush();
            }

            Thread.Sleep(15);
        }

        DestroyWindow(hwnd);
        Console.WriteLine("sonda destruida");
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmPaint:
            {
                var dc = BeginPaint(hwnd, out var paint);
                var brush = CreateSolidBrush(0x0000FF); // vivid red, in BGR
                FillRect(dc, ref paint.rcPaint, brush);
                DeleteObject(brush);
                EndPaint(hwnd, ref paint);
                return IntPtr.Zero;
            }

            case WmDestroy:
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr hdc;
        public bool fErase;
        public NativeRect rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage msg);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
