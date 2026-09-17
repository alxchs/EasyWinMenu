using System.Runtime.InteropServices;
using QuickStacks.Domain;

namespace QuickStacks.UI;

/// <summary>
/// Enumera as areas de trabalho dos monitores conectados (Fase 12) via P/Invoke classico
/// (EnumDisplayMonitors/GetMonitorInfo) em vez da API WinRT
/// <c>Microsoft.UI.Windowing.DisplayArea.FindAll()</c> - esta ultima lanca
/// <c>InvalidCastException: No such interface supported</c> ao enumerar a lista retornada
/// nesta maquina (build canary do Windows), o mesmo tipo de defeito especifico deste
/// ambiente ja documentado para RadioMenuFlyoutItem e XamlControlsResources (secao 6.10/6.12
/// do doc tecnico). EnumDisplayMonitors e' a API Win32 usada pelo proprio EasyWinMenu
/// (DisplayInventory.cs) e nao depende de nenhum tipo WinRT.
/// </summary>
internal static class DisplayInventory
{
    public static IReadOnlyList<MonitorRect> GetWorkAreas()
    {
        var results = new List<MonitorRect>();

        bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
        {
            var info = new MonitorInfo();
            info.cbSize = Marshal.SizeOf<MonitorInfo>();
            if (GetMonitorInfo(hMonitor, ref info))
            {
                var work = info.rcWork;
                results.Add(new MonitorRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top));
            }

            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return results;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);
}
