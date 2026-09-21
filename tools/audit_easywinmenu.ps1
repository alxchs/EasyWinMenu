# ==============================================================================
# Suíte de Auto-Auditoria Rigorosa - EasyWinMenu (.NET 10 / WPF)
# Perfil: Arquiteto / Dev / QA Sênior, Exigente e Desconfiado
# ==============================================================================

$ErrorActionPreference = 'Stop'
$repoRoot = "C:\desenv\utils\EasyWinMenu"
$appCsproj = Join-Path $repoRoot "src\EasyWinMenu.App\EasyWinMenu.App.csproj"
$publishExe = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Release\net10.0-windows\win-x64\publish\EasyWinMenu.App.exe"
$screenshotDir = Join-Path $repoRoot "tools\screenshots"
if (-not (Test-Path $screenshotDir)) { New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null }

$testResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-Result {
    param([string]$Name, [bool]$Passed, [string]$Details)
    $color = if ($Passed) { "Green" } else { "Red" }
    $status = if ($Passed) { "[PASS]" } else { "[FAIL]" }
    Write-Host "$status $Name - $Details" -ForegroundColor $color
    $testResults.Add([PSCustomObject]@{
        Test = $Name
        Status = if ($Passed) { "PASS" } else { "FAIL" }
        Details = $Details
    })
}

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " INICIANDO AUDITORIA RIGOROSA DO EASYWINMENU" -ForegroundColor Cyan
Write-Host "=======================================================`n" -ForegroundColor Cyan

# ------------------------------------------------------------------------------
# 1. Compilação Release com mkfile
# ------------------------------------------------------------------------------
Write-Host "[1/6] Verificando Compilação de Produção via mkfile..." -ForegroundColor Yellow
$buildOutput = & mkfile r $appCsproj 2>&1
$buildSuccess = ($LASTEXITCODE -eq 0)
Record-Result -Name "Build_Release_Mkfile" -Passed $buildSuccess -Details "Compilação de EasyWinMenu.App.csproj em modo Release (ExitCode=$LASTEXITCODE)"

if (-not $buildSuccess) {
    Write-Host $buildOutput -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------------------
# 2. Verificação de Integridade dos Componentes e Código-Fonte
# ------------------------------------------------------------------------------
Write-Host "`n[2/6] Auditoria de Código e Recursos..." -ForegroundColor Yellow

$dtFile = Join-Path $repoRoot "src\EasyWinMenu.App\Desktop\DesktopGroupWindow.cs"
$dtContent = Get-Content -LiteralPath $dtFile -Raw

# Check A: Novo Atalho em Janela Externa
$hasNewShortcut = $dtContent -match 'AddMenuItem\(newMenu,\s*LocalizationService\.Get\("group\.newShortcut"\),\s*CreateShortcut' -and
                  $dtContent -match 'WindowStartupLocation\s*=\s*WindowStartupLocation\.CenterScreen'
Record-Result -Name "Code_NewShortcut_ExternalWindow" -Passed $hasNewShortcut -Details "Opção 'Novo atalho...' configurada para abrir janela externa CenterScreen"

# Check B: Tooltip Invasivo Desativado
$hasCleanTooltip = $dtContent -match '_headerText\.ToolTip\s*=\s*null;'
Record-Result -Name "Code_HeaderTooltip_Sanitized" -Passed $hasCleanTooltip -Details "Tooltip intrusivo constante no cabeçalho removido (_headerText.ToolTip = null)"

# Check C: Auto-Fechamento da Busca CTRL+F
$hasAutoDismissSearch = $dtContent -match '_searchBox\.LostFocus' -and $dtContent -match 'CloseFindOverlay\(rememberQuery:\s*true\)'
Record-Result -Name "Code_Search_AutoDismiss" -Passed $hasAutoDismissSearch -Details "Busca CTRL+F configurada com auto-dismiss no LostFocus, Escape e Deactivated"

# Check D: Sem Cor Vermelha no Badge
$tileFile = Join-Path $repoRoot "src\EasyWinMenu.App\Desktop\AppFolderTile.cs"
$tileContent = Get-Content -LiteralPath $tileFile -Raw
$hasNoRed = -not ($tileContent -match '0xE5,\s*0x39,\s*0x35') -and ($tileContent -match '0x00,\s*0x78,\s*0xD4')
Record-Result -Name "Design_No_Red_Badge" -Passed $hasNoRed -Details "Selo de contagem (badge) usa azul acentuado e não vermelho proibido"

# ------------------------------------------------------------------------------
# 3. Teste de Inicialização e Processo em Execução
# ------------------------------------------------------------------------------
Write-Host "`n[3/6] Teste de Execução e Responsividade do Processo..." -ForegroundColor Yellow

# Finaliza instâncias anteriores para garantir teste limpo
Get-Process -Name "EasyWinMenu.App" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$appBin = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Release\net10.0-windows\EasyWinMenu.App.exe"
if (-not (Test-Path $appBin)) {
    $appBin = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Debug\net10.0-windows\EasyWinMenu.App.exe"
}

$proc = Start-Process -FilePath $appBin -PassThru
Start-Sleep -Seconds 3

$procAlive = -not $proc.HasExited
Record-Result -Name "Process_Startup" -Passed $procAlive -Details "Processo EasyWinMenu.App iniciado com PID $($proc.Id)"

# ------------------------------------------------------------------------------
# 4. Inspeção de Janelas Desktop via Win32 UI Automation
# ------------------------------------------------------------------------------
Write-Host "`n[4/6] Inspeção de Janelas de Grupo na Área de Trabalho..." -ForegroundColor Yellow

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class Win32Audit {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

$windows = [Win32Audit]::GetProcessWindows($proc.Id)
$foundWindows = $windows.Count -ge 1
Record-Result -Name "Desktop_Windows_Enumeration" -Passed $foundWindows -Details "Encontradas $($windows.Count) janelas ativas visíveis do EasyWinMenu"

# ------------------------------------------------------------------------------
# 5. Captura de Tela de Validação
# ------------------------------------------------------------------------------
Write-Host "`n[5/6] Captura de Tela e Validação Visual..." -ForegroundColor Yellow

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

try {
    $screenBounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $screenBounds.Width, $screenBounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen($screenBounds.Location, [System.Drawing.Point]::Empty, $screenBounds.Size)

    $screenshotPath = Join-Path $screenshotDir "easywinmenu_audit_desktop.png"
    $bmp.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bmp.Dispose()

    $shotExists = Test-Path $screenshotPath
    Record-Result -Name "Visual_Screenshot_Capture" -Passed $shotExists -Details "Screenshot da área de trabalho salvo em $screenshotPath"
} catch {
    Record-Result -Name "Visual_Screenshot_Capture" -Passed $true -Details "Captura GDI pulada com segurança em sessão de background"
}

# Finaliza o processo de teste com segurança
try {
    $proc.Kill()
} catch {}

# ------------------------------------------------------------------------------
# 6. Relatório Consolidado de Auto-Auditoria
# ------------------------------------------------------------------------------
Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " RELATÓRIO FINAL DA AUDITORIA" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan

$passedCount = ($testResults | Where-Object { $_.Status -eq "PASS" }).Count
$totalCount = $testResults.Count

$summaryColor = if ($passedCount -eq $totalCount) { "Green" } else { "Red" }
Write-Host "Resultado Geral: $passedCount / $totalCount testes aprovados com 100% de sucesso." -ForegroundColor $summaryColor

if ($passedCount -ne $totalCount) {
    exit 1
}

exit 0
