Add-Type -AssemblyName System.Drawing

$code = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;

public static class WinCap {
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    public static System.Collections.Generic.List<IntPtr> GetWindows(uint pid) {
        var list = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            if (IsWindowVisible(hWnd)) {
                uint wPid;
                GetWindowThreadProcessId(hWnd, out wPid);
                if (wPid == pid) list.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static bool Capture(IntPtr hWnd, string outputPath) {
        RECT rect;
        GetWindowRect(hWnd, out rect);
        int w = Math.Max(1, rect.Width);
        int h = Math.Max(1, rect.Height);
        IntPtr hdcSrc = GetWindowDC(hWnd);
        if (hdcSrc == IntPtr.Zero) return false;
        IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
        IntPtr hBmp = CreateCompatibleBitmap(hdcSrc, w, h);
        IntPtr hOld = SelectObject(hdcDest, hBmp);
        if (!PrintWindow(hWnd, hdcDest, 2)) {
            BitBlt(hdcDest, 0, 0, w, h, hdcSrc, 0, 0, 0x00CC0020);
        }
        using (var bmp = Image.FromHbitmap(hBmp)) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            bmp.Save(outputPath, ImageFormat.Png);
        }
        SelectObject(hdcDest, hOld);
        DeleteObject(hBmp);
        DeleteDC(hdcDest);
        ReleaseDC(hWnd, hdcSrc);
        return true;
    }
}
"@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing

$p = Get-Process -Name EasyWinMenu.App -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) {
    $appExe = "C:\desenv\utils\EasyWinMenu\src\EasyWinMenu.App\bin\Release\net10.0-windows\EasyWinMenu.App.exe"
    Write-Host "Iniciando $appExe..."
    $p = Start-Process -FilePath $appExe -PassThru
    Start-Sleep -Seconds 3
}

if ($p) {
    $hwnds = [WinCap]::GetWindows($p.Id)
    Write-Host "Process PID: $($p.Id), Windows found: $($hwnds.Count)"
    $idx = 0
    foreach ($h in $hwnds) {
        $out = "C:\Users\alxch\.gemini\antigravity\brain\3b4eee52-4015-4dd6-808e-716488bce559\desktop_group_$idx.png"
        if ([WinCap]::Capture($h, $out)) {
            Write-Host "Captured window $idx to $out"
        }
        $idx++
    }
} else {
    Write-Host "Process not running."
}
