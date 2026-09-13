using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// Answers one question: which window actually owns the pixels of the empty desktop on
// this Windows build? Written in C# rather than PowerShell because PowerShell's P/Invoke
// marshalling of a null lpWindowName silently returns no match, which produced two wrong
// conclusions earlier in this investigation.

internal static class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine($"GetShellWindow()        = {Describe(GetShellWindow())}");
        Console.WriteLine($"GetDesktopWindow()      = {Describe(GetDesktopWindow())}");
        Console.WriteLine($"FindWindow(Progman)     = {Describe(FindWindow("Progman", null))}");
        Console.WriteLine();

        var screenWidth = GetSystemMetrics(0);
        var screenHeight = GetSystemMetrics(1);
        Console.WriteLine($"tela = {screenWidth}x{screenHeight}");
        Console.WriteLine();

        Console.WriteLine("=== janelas de nivel superior, do topo para o fundo (visiveis, area >= 25% da tela) ===");
        var index = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsCloaked(hwnd))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area < (long)screenWidth * screenHeight / 4)
            {
                return true;
            }

            Console.WriteLine($"[{index,2}] {Describe(hwnd)}");
            index++;
            return true;
        }, IntPtr.Zero);

        Console.WriteLine();
        Console.WriteLine("=== quem esta sob pontos da area de trabalho ===");
        foreach (var (x, y) in new[] { (5, 400), (900, 700), (1500, 900), (960, 540) })
        {
            var hit = WindowFromPoint(new NativePoint { X = x, Y = y });
            Console.WriteLine($"({x},{y}) -> {Describe(hit)}");
            foreach (var ancestor in Ancestors(hit))
            {
                Console.WriteLine($"        ^ {Describe(ancestor)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== quem contem SHELLDLL_DefView ===");
        EnumWindows((hwnd, _) =>
        {
            FindDefViewUnder(hwnd, hwnd, 0);
            return true;
        }, IntPtr.Zero);

        if (args.Contains("--paint"))
        {
            Console.WriteLine();
            Console.WriteLine("=== sonda de pintura ===");
            var progman = FindWindow("Progman", null);
            var target = progman;
            if (args.Contains("--defview"))
            {
                target = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }

            Console.WriteLine($"pai escolhido: {Describe(target)}");
            PaintTest.Run(target, 12);
        }

        if (args.Contains("--tree"))
        {
            Console.WriteLine();
            Console.WriteLine("=== arvore do Progman ===");
            DumpTree(FindWindow("Progman", null), 0);
        }
    }

    private static void FindDefViewUnder(IntPtr root, IntPtr current, int depth)
    {
        if (depth > 4)
        {
            return;
        }

        EnumChildWindows(current, (child, _) =>
        {
            if (ClassOf(child) == "SHELLDLL_DefView")
            {
                Console.WriteLine($"  defview {Describe(child)}");
                Console.WriteLine($"     pai imediato {Describe(GetParent(child))}");
                Console.WriteLine($"     raiz         {Describe(root)}");
            }

            return true;
        }, IntPtr.Zero);
    }

    private static void DumpTree(IntPtr parent, int depth)
    {
        if (parent == IntPtr.Zero || depth > 3)
        {
            return;
        }

        EnumChildWindows(parent, (child, _) =>
        {
            if (GetParent(child) != parent)
            {
                return true;
            }

            Console.WriteLine($"{new string(' ', (depth + 1) * 2)}{Describe(child)}");
            DumpTree(child, depth + 1);
            return true;
        }, IntPtr.Zero);
    }

    private static IEnumerable<IntPtr> Ancestors(IntPtr hwnd)
    {
        var current = GetParent(hwnd);
        var guard = 0;
        while (current != IntPtr.Zero && guard++ < 10)
        {
            yield return current;
            current = GetParent(current);
        }
    }

    private static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "0 (nenhuma)";
        }

        GetWindowRect(hwnd, out var rect);
        GetWindowThreadProcessId(hwnd, out var pid);

        var process = "?";
        try
        {
            process = Process.GetProcessById((int)pid).ProcessName;
        }
        catch (ArgumentException)
        {
        }

        var title = TitleOf(hwnd);
        var titlePart = string.IsNullOrEmpty(title) ? string.Empty : $" \"{title}\"";

        return $"{hwnd.ToInt64()} {ClassOf(hwnd)}{titlePart} [{process}] "
             + $"rect={rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top} "
             + $"vis={IsWindowVisible(hwnd)} cloaked={IsCloaked(hwnd)} "
             + $"style=0x{GetWindowLong(hwnd, -16):X8} ex=0x{GetWindowLong(hwnd, -20):X8}";
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        // A window can be visible and still not be drawn: DWM cloaks virtual-desktop and
        // suspended-app windows, and IsWindowVisible knows nothing about it.
        return DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string TitleOf(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
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

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder buffer, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
