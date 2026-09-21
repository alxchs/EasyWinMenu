Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$repoRoot = "C:\desenv\utils\EasyWinMenu"
$appExe = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Release\net10.0-windows\EasyWinMenu.App.exe"
if (-not (Test-Path $appExe)) {
    $appExe = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Release\net10.0-windows\win-x64\publish\EasyWinMenu.App.exe"
}

# Stop any running instances first
Get-Process -Name "EasyWinMenu.App" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Write-Host "Iniciando $appExe..."
$proc = Start-Process -FilePath $appExe -PassThru
Start-Sleep -Seconds 3

# Encontra janelas ativas do processo
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class Win32UI {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public static List<IntPtr> GetProcessWindows(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            if (IsWindowVisible(hWnd)) {
                GetWindowThreadProcessId(hWnd, out uint wPid);
                if (wPid == pid) {
                    list.Add(hWnd);
                }
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$wList = [Win32UI]::GetProcessWindows($proc.Id)
Write-Host "Janelas visiveis encontradas: $($wList.Count)"

if ($wList.Count -gt 0) {
    [Win32UI]::SetForegroundWindow($wList[0])
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait("{RIGHT}")
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait("{F5}")
    Start-Sleep -Milliseconds 300
}

# Captura de tela
$artifactPath = "C:\Users\alxch\.gemini\antigravity\brain\3b4eee52-4015-4dd6-808e-716488bce559\verified_groups_desktop.png"
try {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bmp.Save($artifactPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "Screenshot salvo com sucesso em: $artifactPath"
} catch {
    Write-Host "Nao foi possivel capturar screenshot GDI direto: $_"
}

Write-Host "Processo ativo e verificado: PID $($proc.Id)"
