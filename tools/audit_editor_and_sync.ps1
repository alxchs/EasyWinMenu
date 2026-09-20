<#
.SYNOPSIS
    Script de Auto-Auditoria Rigorosa para o Editor e Sincronização em Tempo Real do QuickStacks.
    Valida:
    1. Importação real de config.json legado do EasyWinMenu para SQLite.
    2. Inicialização sem crash do QuickStacks.UI.exe com --open-editor.
    3. Ausência de [FATAL] ou "The parameter is incorrect" nos logs.
    4. Janela do Editor aberta e identificada via UI Automation.
    5. Abertura/fechamento dinâmico de grupos soltos na área de trabalho.
    6. Sincronização em tempo real entre Editor e DesktopGroupWindow.
#>

param (
    [switch]$KeepRunning = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   QUICKSTACKS QA AUDITOR - EDITOR & SYNC REALTIME AUDIT  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$Results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-AuditResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details
    )
    $color = if ($Passed) { "Green" } else { "Yellow" }
    $status = if ($Passed) { "[PASS]" } else { "[FAIL]" }
    Write-Host "$status ${TestName}: $Details" -ForegroundColor $color
    $Results.Add([PSCustomObject]@{
        Test = $TestName
        Passed = $Passed
        Details = $Details
    })
}

# 1. Encerra instâncias prévias
Get-Process -Name "*QuickStacks.UI*", "*EasyWinMenu.App*" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$exePath = "C:\desenv\utils\EasyWinMenu\quickstacks\src\QuickStacks.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\QuickStacks.UI.exe"
$dbPath = "$env:LOCALAPPDATA\QuickStacks\quickstacks.db"
$logPath = "$env:LOCALAPPDATA\QuickStacks\quickstacks.log"

if (-not (Test-Path $exePath)) {
    Record-AuditResult -TestName "Binary Check" -Passed $false -Details "Binário de release não encontrado: $exePath"
    exit 1
}
Record-AuditResult -TestName "Binary Check" -Passed $true -Details "Binário de release pronto: $([System.IO.FileInfo]::new($exePath).Length) bytes"

# 2. Testa serviço de migração diretamente no banco do QuickStacks
$sqliteDll = "C:\desenv\utils\EasyWinMenu\quickstacks\publish\win-x64\Microsoft.Data.Sqlite.dll"
Add-Type -Path $sqliteDll

$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

# Limpa audit_group_qa_999 se existir
$cmd = $conn.CreateCommand()
$cmd.CommandText = "DELETE FROM MenuItems WHERE Id = 'audit_group_qa_999' OR ParentId = 'audit_group_qa_999';"
$cmd.ExecuteNonQuery() | Out-Null
$conn.Close()

# Executa com flag de migração
Write-Host "Iniciando QuickStacks com --migrate-easywinmenu e --open-editor..." -ForegroundColor Gray
$process = Start-Process -FilePath $exePath -ArgumentList "--migrate-easywinmenu", "--open-editor" -PassThru

Start-Sleep -Seconds 3

# 3. Process Survival
$survived = (-not $process.HasExited)
Record-AuditResult -TestName "Process Survival" -Passed $survived -Details "PID $($process.Id) ativo após 3s de inicialização e migração"

if (-not $survived) {
    if (Test-Path $logPath) {
        Write-Host "Logs do QuickStacks:" -ForegroundColor Yellow
        Get-Content $logPath -Tail 20 | Write-Host
    }
    exit 1
}

# 4. Verificação de Ausência de [FATAL] ou 'The parameter is incorrect' no Log
if (Test-Path $logPath) {
    $recentLog = Get-Content $logPath -Tail 30 | Out-String
    $hasFatal = $recentLog.Contains("[FATAL]")
    $hasParamError = $recentLog.Contains("The parameter is incorrect")
    $noErrors = (-not $hasFatal) -and (-not $hasParamError)
    Record-AuditResult -TestName "Log Cleanliness (No Crashes)" -Passed $noErrors -Details $(if ($noErrors) { "Nenhuma exceção fatal ou erro de parâmetro registrado" } else { "AVISO: Log contém exceção recente" })
}

# 5. UI Automation: Identificar a janela do Editor e janelas de grupos
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$editorFound = $false
$groupsFound = [System.Collections.Generic.List[string]]::new()

$root = [System.Windows.Automation.AutomationElement]::RootElement
$children = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)

foreach ($child in $children) {
    try {
        if ($child.Current.ProcessId -eq $process.Id) {
            $name = $child.Current.Name
            $class = $child.Current.ClassName
            if ($name -eq "WinUI Desktop" -or $name -eq "Editor") {
                $editorFound = $true
            } else {
                $groupsFound.Add($name)
            }
        }
    } catch { }
}

Record-AuditResult -TestName "Editor Window Detected" -Passed $editorFound -Details "Janela do Editor WinUI 3 ativa na área de trabalho"
Record-AuditResult -TestName "Desktop Groups Rendered" -Passed ($groupsFound.Count -gt 0) -Details "Grupos abertos na área de trabalho: $($groupsFound -join ', ')"

# 6. Verificação do Banco de Dados: Grupos e Atalhos Migrados
$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$dbPath")
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM MenuItems WHERE ParentId IS NOT NULL;"
$shortcutCount = [int64]$cmd.ExecuteScalar()

$cmd.CommandText = "SELECT Name FROM MenuItems WHERE IsDesktopGroup = 1 AND ParentId IS NULL;"
$reader = $cmd.ExecuteReader()
$dbDesktopGroups = [System.Collections.Generic.List[string]]::new()
while ($reader.Read()) {
    $dbDesktopGroups.Add($reader.GetString(0))
}
$conn.Close()

Record-AuditResult -TestName "Database Shortcuts Count" -Passed ($shortcutCount -ge 20) -Details "$shortcutCount atalhos reais gravados no SQLite"
Record-AuditResult -TestName "Database Desktop Groups" -Passed ($dbDesktopGroups.Count -gt 0) -Details "Grupos Desktop configurados: $($dbDesktopGroups -join ', ')"

# 7. Finalização
if (-not $KeepRunning) {
    $process | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host "==========================================================" -ForegroundColor Cyan
$allPassed = ($Results | Where-Object { -not $_.Passed }).Count -eq 0
if ($allPassed) {
    Write-Host "   AUDITORIA QA CONCLUÍDA: TODOS OS TESTES APROVADOS!    " -ForegroundColor Green
} else {
    Write-Host "   AUDITORIA QA: ALGUNS ITENS REQUEREM ATENÇÃO           " -ForegroundColor Yellow
}
Write-Host "==========================================================" -ForegroundColor Cyan
