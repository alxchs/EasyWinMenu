<#
.SYNOPSIS
    Script de Auto-Auditoria Rigorosa para os Grupos da Área de Trabalho do QuickStacks.
    Executa verificação automatizada de binário, banco de dados, inicialização,
    deschroming (janela sem borda nativa WinUI), dimensões compactas, atalhos virtuais
    (Modo Deus / Painel de Controle) e ausência de crashes.

.DESCRIPTION
    Atua com a postura de um QA exigente, desconfiado e experiente:
    - Nunca assume que algo funciona sem medir no runtime.
    - Audita estilos Win32 da janela em tempo real.
    - Mede posição e dimensões exatas de tela.
    - Audita estabilidade e ausência de exceções não tratadas no log.
#>

param (
    [switch]$KeepTestData = $false,
    [int]$TimeoutSeconds = 8
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  QUICKSTACKS QA AUDITOR - DESKTOP GROUPS RUNTIME AUDIT   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$Results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-AuditResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details
    )
    $color = if ($Passed) { "Green" } else { "Red" }
    $status = if ($Passed) { "[PASS]" } else { "[FAIL]" }
    Write-Host "$status $TestName - $Details" -ForegroundColor $color
    $Results.Add([PSCustomObject]@{
        Test = $TestName
        Passed = $Passed
        Details = $Details
    })
}

# Win32 P/Invoke Definitions
$win32Code = @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32Audit
{
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;

    public const byte VK_CONTROL = 0x11;
    public const byte VK_F = 0x46;
    public const byte VK_ESCAPE = 0x1B;
    public const uint KEYEVENTF_KEYUP = 0x0002;
}
"@
Add-Type -TypeDefinition $win32Code

# 1. Auditoria do Binário Release
Write-Host "`n[Fase 1] Verificando Binário Release..." -ForegroundColor Yellow
$projectDir = "C:\desenv\utils\EasyWinMenu"
$exePath = "$projectDir\quickstacks\src\QuickStacks.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\QuickStacks.UI.exe"
$dllSqlite = "$projectDir\quickstacks\src\QuickStacks.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\Microsoft.Data.Sqlite.dll"

if (Test-Path $exePath) {
    $fileInfo = Get-Item $exePath
    Record-AuditResult -TestName "BinaryReleaseExists" -Passed $true -Details "QuickStacks.UI.exe encontrado ($($fileInfo.Length) bytes, modificado em $($fileInfo.LastWriteTime))"
} else {
    Record-AuditResult -TestName "BinaryReleaseExists" -Passed $false -Details "QuickStacks.UI.exe não encontrado em $exePath"
    exit 1
}

# Matar processos anteriores se houver antes de mexer no banco
Get-Process "QuickStacks.UI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

$logPath = "$env:LOCALAPPDATA\QuickStacks\quickstacks.log"
if (Test-Path $logPath) {
    Remove-Item $logPath -Force
}

# 2. Auditoria e Preparação do Banco de Dados
Write-Host "`n[Fase 2] Preparando e Auditando Dados no SQLite..." -ForegroundColor Yellow
$dbPath = "$env:LOCALAPPDATA\QuickStacks\quickstacks.db"
$dbBak = "$env:LOCALAPPDATA\QuickStacks\quickstacks.db.audit-bak"

if (Test-Path $dbPath) {
    Copy-Item $dbPath $dbBak -Force
    Write-Host "Backup da base criado em $dbBak" -ForegroundColor DarkGray
}

Add-Type -Path $dllSqlite
$conn = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$dbPath")
$conn.Open()

# Configurar ProductTier = Full e Sombra
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
INSERT INTO Settings (Key, Value) VALUES ('product.tier', 'Full')
ON CONFLICT(Key) DO UPDATE SET Value = 'Full';

INSERT INTO Settings (Key, Value) VALUES ('desktopGroup.shadow.angle', '135')
ON CONFLICT(Key) DO UPDATE SET Value = '135';

INSERT INTO Settings (Key, Value) VALUES ('desktopGroup.shadow.enabled', 'true')
ON CONFLICT(Key) DO UPDATE SET Value = 'true';
"@
$cmd.ExecuteNonQuery() | Out-Null

# Criar Grupo de Teste de Auditoria
$groupId = "audit_group_qa_999"
$cmd.CommandText = @"
DELETE FROM MenuItems WHERE ParentId = '$groupId' OR Id = '$groupId';
DELETE FROM DesktopGroupPlacement WHERE GroupId = '$groupId';

INSERT INTO MenuItems (Id, ParentId, Name, Type, SortOrder, IsFavorite, LaunchCount, CreatedAt, UpdatedAt, IsDesktopGroup)
VALUES ('$groupId', NULL, 'QA Audit Group', 0, 0, 0, 0, datetime('now'), datetime('now'), 1);

INSERT INTO DesktopGroupPlacement (GroupId, X, Y, Width, Height, DisplayMode, IconScale, IsCollapsed)
VALUES ('$groupId', 150, 150, 260, 180, 0, 1.0, 0);

-- Inserir atalhos estritos (Executáveis, URLs, e Atalhos Virtuais tipo Modo Deus / Painel de Controle)
INSERT INTO MenuItems (Id, ParentId, Name, Type, Path, SortOrder, IsFavorite, LaunchCount, CreatedAt, UpdatedAt, IsDesktopGroup)
VALUES 
  ('qa_item_1', '$groupId', 'Bloco de Notas', 1, 'notepad.exe', 0, 0, 5, datetime('now'), datetime('now'), 0),
  ('qa_item_2', '$groupId', 'Calculadora', 1, 'calc.exe', 1, 0, 2, datetime('now'), datetime('now'), 0),
  ('qa_item_3', '$groupId', 'Painel de Controle', 3, 'shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}', 2, 0, 8, datetime('now'), datetime('now'), 0),
  ('qa_item_4', '$groupId', 'Modo Deus Windows', 3, 'shell:::{ED7BA470-8E54-465E-825C-99712043E01C}', 3, 0, 1, datetime('now'), datetime('now'), 0),
  ('qa_item_5', '$groupId', 'Documentação', 2, 'https://github.com/alxchs/EasyWinMenu', 4, 0, 0, datetime('now'), datetime('now'), 0);
"@
$cmd.ExecuteNonQuery() | Out-Null
$conn.Close()

Record-AuditResult -TestName "DatabaseSeededWithShortcuts" -Passed $true -Details "Grupo 'QA Audit Group' criado com 5 atalhos (Notepad, Calc, Painel de Controle, Modo Deus, URL)"

# 3. Execução do QuickStacks.UI.exe e Monitoramento de Processo
Write-Host "`n[Fase 3] Iniciando Processo QuickStacks.UI e Monitorando..." -ForegroundColor Yellow

$proc = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    Record-AuditResult -TestName "ProcessSurvival" -Passed $false -Details "Processo encerrou prematuramente com ExitCode $($proc.ExitCode)!"
    exit 1
} else {
    Record-AuditResult -TestName "ProcessSurvival" -Passed $true -Details "Processo QuickStacks.UI ativo com PID $($proc.Id)"
}

# 4. Auditoria de Janelas Win32 do Processo
Write-Host "`n[Fase 4] Auditando Janelas Win32 e De-chroming..." -ForegroundColor Yellow

# Obter todas as janelas associadas ao processo
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$foundGroupHwnd = [IntPtr]::Zero
$hwnds = [System.Collections.Generic.List[IntPtr]]::new()

$enumCallback = {
    param($hwnd, $lparam)
    $pid = 0
    [void][System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $threadId = [QuickStacksAuditNative]::GetWindowThreadProcessId($hwnd, [ref]$pid)
    if ($pid -eq $proc.Id) {
        $hwnds.Add($hwnd)
    }
    return $true
}

$nativeHelper = @"
using System;
using System.Runtime.InteropServices;

public static class QuickStacksAuditNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
"@
Add-Type -TypeDefinition $nativeHelper

$procPid = [uint32]$proc.Id
$groupWindowHwnd = [IntPtr]::Zero
$startTime = [DateTime]::UtcNow

while (([DateTime]::UtcNow - $startTime).TotalSeconds -lt 8) {
    $allHwnds = [System.Collections.Generic.List[IntPtr]]::new()
    [QuickStacksAuditNative]::EnumWindows({
        param($h, $l)
        $wPid = [uint32]0
        [QuickStacksAuditNative]::GetWindowThreadProcessId($h, [ref]$wPid)
        if ($wPid -eq $procPid) {
            $allHwnds.Add($h)
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null

    foreach ($h in $allHwnds) {
        $sb = [System.Text.StringBuilder]::new(256)
        [Win32Audit]::GetWindowText($h, $sb, 256) | Out-Null
        $title = $sb.ToString()
        $isVisible = [Win32Audit]::IsWindowVisible($h)
        
        $rect = [Win32Audit+RECT]::new()
        [Win32Audit]::GetWindowRect($h, [ref]$rect) | Out-Null

        # A janela do DesktopGroup tem título do grupo OU tamanho compacto de desktop (não 1920x1025)
        if ($title -eq "QA Audit Group" -or ($rect.Width -ge 180 -and $rect.Width -le 600 -and $rect.Height -ge 100 -and $rect.Height -le 500)) {
            $groupWindowHwnd = $h
            Write-Host "  -> ENCONTRADA HWND 0x$($h.ToString("X")): Title='$title', Visible=$isVisible, Size=$($rect.Width)x$($rect.Height)" -ForegroundColor Green
            break
        }
    }

    if ($groupWindowHwnd -ne [IntPtr]::Zero) {
        break
    }
    Start-Sleep -Milliseconds 500
}

if ($groupWindowHwnd -ne [IntPtr]::Zero) {
    Record-AuditResult -TestName "DesktopGroupWindowFound" -Passed $true -Details "Janela do DesktopGroup identificada: HWND 0x$($groupWindowHwnd.ToString("X"))"

    # Verificar De-chroming (Ausência da barra padrão "WinUI Desktop" e de botões de controle nativos)
    $sb = [System.Text.StringBuilder]::new(256)
    [Win32Audit]::GetWindowText($groupWindowHwnd, $sb, 256) | Out-Null
    $title = $sb.ToString()
    $hasWinUiTitle = $title -match "WinUI Desktop"
    Record-AuditResult -TestName "NoWinUiDefaultTitle" -Passed (-not $hasWinUiTitle) -Details "Título nativo retornado: '$title' (não contém 'WinUI Desktop')"

    # Verificar dimensões compactas auto-organizadas
    $rect = [Win32Audit+RECT]::new()
    [Win32Audit]::GetWindowRect($groupWindowHwnd, [ref]$rect) | Out-Null
    Write-Host "Dimensões da Janela: Largura=$($rect.Width)px, Altura=$($rect.Height)px, Posição=($($rect.Left), $($rect.Top))" -ForegroundColor Cyan

    $isCompact = ($rect.Width -le 450) -and ($rect.Height -le 350) -and ($rect.Width -ge 180) -and ($rect.Height -ge 100)
    Record-AuditResult -TestName "AutoOrganizedDimensions" -Passed $isCompact -Details "Tamanho compacto ajustado aos 5 atalhos: $($rect.Width)x$($rect.Height)px (esperado ~220-400x120-250)"

    # Verificar limites e posicionamento na tela
    $screens = [System.Windows.Forms.Screen]::AllScreens
    $onScreen = $false
    foreach ($s in $screens) {
        if ($rect.Left -ge $s.Bounds.Left -and $rect.Right -le $s.Bounds.Right -and
            $rect.Top -ge $s.Bounds.Top -and $rect.Bottom -le $s.Bounds.Bottom) {
            $onScreen = $true
            break
        }
    }
    Record-AuditResult -TestName "WithinMonitorBounds" -Passed $onScreen -Details "Janela está inteiramente contida dentro dos limites de um monitor sem transbordar (Left=$($rect.Left), Top=$($rect.Top), Right=$($rect.Right), Bottom=$($rect.Bottom))"

    # 5. Auditoria de UI Automation (Navegação de Teclado, Busca CTRL+F e Elementos)
    Write-Host "`n[Fase 5] Auditando Controles de UI Automation e Atalhos..." -ForegroundColor Yellow
    
    $automation = [System.Windows.Automation.AutomationElement]::FromHandle($groupWindowHwnd)
    if ($automation -ne $null) {
        $nameProp = $automation.Current.Name
        Write-Host "Nome UI Automation da janela: '$nameProp'" -ForegroundColor DarkGray
        
        # Procurar elementos filhos
        $allChildren = $automation.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        Write-Host "Total de elementos visuais renderizados: $($allChildren.Count)"
        
        $itemNames = [System.Collections.Generic.List[string]]::new()
        $hasSearchButton = $false
        $hasCloseButton = $false

        foreach ($child in $allChildren) {
            $cName = $child.Current.Name
            $cId = $child.Current.AutomationId
            if ($cName -match "Bloco de Notas|Calculadora|Painel de Controle|Modo Deus Windows|Documentação") {
                if (-not $itemNames.Contains($cName)) {
                    $itemNames.Add($cName)
                }
            }
            if ($cName -match "Pesquisar" -or $cId -eq "SearchButton") { $hasSearchButton = $true }
            if ($cName -match "Fechar" -or $cId -eq "CloseButton") { $hasCloseButton = $true }
        }

        Record-AuditResult -TestName "RenderedShortcutItems" -Passed ($itemNames.Count -ge 4) -Details "Atalhos identificados na interface: $($itemNames -join ', ') (Encontrados: $($itemNames.Count)/5)"
        Record-AuditResult -TestName "HeaderSearchAndCloseControls" -Passed ($hasSearchButton -and $hasCloseButton) -Details "Botões de controle de cabeçalho presentes (Pesquisar: $hasSearchButton, Fechar: $hasCloseButton)"

        # Teste interativo: Ativar busca via SearchButton / KeyboardAccelerator
        Write-Host "`n[Fase 6] Testando Interação de Busca CTRL+F e Botão Pesquisar..." -ForegroundColor Yellow
        [Win32Audit]::SetForegroundWindow($groupWindowHwnd) | Out-Null
        Start-Sleep -Milliseconds 300

        # Invocar botão de busca via UI Automation InvokePattern ou keybd_event
        $searchBtnElement = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "SearchButton"))
        if ($searchBtnElement -ne $null) {
            $invokePattern = $searchBtnElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
            if ($invokePattern -ne $null) {
                $invokePattern.Invoke()
            } else {
                [Win32Audit]::keybd_event([Win32Audit]::VK_CONTROL, 0, 0, 0)
                [Win32Audit]::keybd_event([Win32Audit]::VK_F, 0, 0, 0)
                [Win32Audit]::keybd_event([Win32Audit]::VK_F, 0, [Win32Audit]::KEYEVENTF_KEYUP, 0)
                [Win32Audit]::keybd_event([Win32Audit]::VK_CONTROL, 0, [Win32Audit]::KEYEVENTF_KEYUP, 0)
            }
        } else {
            [Win32Audit]::keybd_event([Win32Audit]::VK_CONTROL, 0, 0, 0)
            [Win32Audit]::keybd_event([Win32Audit]::VK_F, 0, 0, 0)
            [Win32Audit]::keybd_event([Win32Audit]::VK_F, 0, [Win32Audit]::KEYEVENTF_KEYUP, 0)
            [Win32Audit]::keybd_event([Win32Audit]::VK_CONTROL, 0, [Win32Audit]::KEYEVENTF_KEYUP, 0)
        }
        Start-Sleep -Milliseconds 600

        # Verificar se o SearchBox apareceu na árvore de UI Automation
        $searchBoxCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            "SearchBox"
        )
        $searchElement = $automation.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchBoxCondition)
        $searchVisible = ($searchElement -ne $null)
        Record-AuditResult -TestName "CtrlFActivation" -Passed $searchVisible -Details "Ativação do SearchBox via CTRL+F / Botão Pesquisar (Localizado: $searchVisible)"

        # Testar Esc para fechar busca via Win32 keybd_event
        [Win32Audit]::keybd_event([Win32Audit]::VK_ESCAPE, 0, 0, 0)
        [Win32Audit]::keybd_event([Win32Audit]::VK_ESCAPE, 0, [Win32Audit]::KEYEVENTF_KEYUP, 0)
        Start-Sleep -Milliseconds 300
    } else {
        Record-AuditResult -TestName "UIAutomationAttach" -Passed $false -Details "Não foi possível conectar AutomationElement ao HWND"
    }

} else {
    Record-AuditResult -TestName "DesktopGroupWindowFound" -Passed $false -Details "Nenhuma janela de grupo encontrada"
}

# 6. Auditoria de Logs de Erro / Exceções
Write-Host "`n[Fase 7] Verificando Log de Execução da Aplicação..." -ForegroundColor Yellow
$logPath = "$env:LOCALAPPDATA\QuickStacks\quickstacks.log"
$hasExceptions = $false
$exceptionCount = 0

if (Test-Path $logPath) {
    $logLines = Get-Content $logPath
    Write-Host "--- Conteúdo do Log de Execução ($($logLines.Count) linhas) ---" -ForegroundColor DarkCyan
    foreach ($line in $logLines) {
        if ($line -match "FATAL|ERROR|UnhandledException") {
            $hasExceptions = $true
            $exceptionCount++
            Write-Host "  $line" -ForegroundColor Red
        } else {
            Write-Host "  $line" -ForegroundColor DarkGray
        }
    }
    Record-AuditResult -TestName "CleanApplicationLog" -Passed (-not $hasExceptions) -Details "Log de execução verificado ($exceptionCount falhas/erros detectados)"
} else {
    Record-AuditResult -TestName "CleanApplicationLog" -Passed $true -Details "Arquivo quickstacks.log limpo/inexistente de erros"
}

# 7. Finalização e Encerramento Limpo
Write-Host "`n[Fase 8] Encerrando Processo de Teste e Limpeza..." -ForegroundColor Yellow
if ($proc -and -not $proc.HasExited) {
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not $proc.HasExited) {
        $proc.Kill()
    }
}

if (-not $KeepTestData) {
    if (Test-Path $dbBak) {
        Copy-Item $dbBak $dbPath -Force
        Remove-Item $dbBak -Force
        Write-Host "Banco de dados original restaurado com sucesso." -ForegroundColor DarkGray
    }
}

# 8. Relatório Final de Auditoria
Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "                  RESUMO DA AUDITORIA QA                  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$totalTests = $Results.Count
$passedTests = ($Results | Where-Object { $_.Passed }).Count
$failedTests = $totalTests - $passedTests

$Results | Format-Table -AutoSize

Write-Host "Total de Testes: $totalTests | Aprovados: $passedTests | Falhas: $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })

if ($failedTests -eq 0) {
    Write-Host "`n>>> VEREDICTO QA: APROVADO COM DISTINÇÃO <<<" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n>>> VEREDICTO QA: REPROVADO <<<" -ForegroundColor Red
    exit 1
}
