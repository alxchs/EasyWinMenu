using System.Runtime.InteropServices;
using System.Text;

namespace EasyWinMenu.App.Desktop;

internal sealed class DesktopContextMenuHookService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmRbuttondown = 0x0204;
    private const uint GaRoot = 2;
    private const int LvmHittest = 0x1012;

    private readonly LowLevelMouseProc _proc;
    private readonly Action<System.Drawing.Point> _onDesktopRightClick;
    private IntPtr _hookId = IntPtr.Zero;

    public DesktopContextMenuHookService(Action<System.Drawing.Point> onDesktopRightClick)
    {
        _onDesktopRightClick = onDesktopRightClick;
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        _hookId = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(currentModule.ModuleName), 0);
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WmRbuttondown)
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            if (IsDesktopEmptySpace(data.pt))
            {
                var point = new System.Drawing.Point(data.pt.X, data.pt.Y);
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _onDesktopRightClick(point));
                return 1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsDesktopEmptySpace(Point screenPoint)
    {
        var hwnd = WindowFromPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (GetWindowClassName(hwnd) != "SysListView32")
        {
            return false;
        }

        var root = GetAncestor(hwnd, GaRoot);
        var rootClassName = GetWindowClassName(root);
        if (rootClassName != "Progman" && rootClassName != "WorkerW")
        {
            return false;
        }

        var clientPoint = screenPoint;
        ScreenToClient(hwnd, ref clientPoint);

        var hitTest = new LvHitTestInfo { pt = clientPoint };
        SendMessage(hwnd, LvmHittest, IntPtr.Zero, ref hitTest);
        return hitTest.iItem == -1;
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LvHitTestInfo
    {
        public Point pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LvHitTestInfo lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
