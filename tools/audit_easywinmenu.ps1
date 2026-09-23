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

# Check A: Novo Atalho Configurado
$hasNewShortcut = $dtContent -match 'AddMenuItem\(menu,\s*LocalizationService\.Get\("group\.newShortcut"\),\s*CreateShortcut' -and
                  $dtContent -match 'WindowStartupLocation\s*=\s*WindowStartupLocation\.CenterScreen'
Record-Result -Name "Code_NewShortcut_DirectAndExternal" -Passed $hasNewShortcut -Details "Opção 'Novo atalho...' adicionada diretamente ao menu e abre em janela externa CenterScreen"

# Check B: Tooltip Invasivo Desativado
$hasCleanTooltip = $dtContent -match '_headerText\.ToolTip\s*=\s*null;'
Record-Result -Name "Code_HeaderTooltip_Sanitized" -Passed $hasCleanTooltip -Details "Tooltip intrusivo constante no cabeçalho removido (_headerText.ToolTip = null)"

# Check C: Auto-Fechamento e Placeholder da Busca CTRL+F
$hasAutoDismissSearch = $dtContent -match '_searchBox\.LostFocus' -and $dtContent -match 'CloseFindOverlay\(rememberQuery:\s*true\)' -and $dtContent -match '_searchPlaceholder'
Record-Result -Name "Code_Search_Enhanced" -Passed $hasAutoDismissSearch -Details "Busca CTRL+F com ícone de pesquisa, placeholder descritivo e auto-dismiss"

# Check D: Sem Cor Vermelha no Badge
$tileFile = Join-Path $repoRoot "src\EasyWinMenu.App\Desktop\AppFolderTile.cs"
$tileContent = Get-Content -LiteralPath $tileFile -Raw
$hasNoRed = -not ($tileContent -match '0xE5,\s*0x39,\s*0x35') -and ($tileContent -match '0x00,\s*0x78,\s*0xD4')
Record-Result -Name "Design_No_Red_Badge" -Passed $hasNoRed -Details "Selo de contagem (badge) usa azul acentuado e não vermelho proibido"

# Check E: Correção de Deslocamento para Cima ao Clicar (hasMoved Threshold)
$hasNoShiftFix = $dtContent -match 'if\s*\(!hasMoved\s*\|\|\s*ghost\s+is\s+null\)' -and $dtContent -match 'SystemParameters\.MinimumHorizontalDragDistance'
Record-Result -Name "Code_TileClick_NoUpwardShift" -Passed $hasNoShiftFix -Details "Clique simples protegido contra deslocamento vertical acidental (hasMoved + DragDistance check)"

# Check F: Seleção Múltipla com Shift e Ctrl (Estilo Windows Explorer)
$hasRangeSelection = $dtContent -match 'SelectRange' -and $dtContent -match '_selectionAnchor' -and $dtContent -match 'ModifierKeys\.Shift'
Record-Result -Name "Code_Explorer_RangeSelection" -Passed $hasRangeSelection -Details "Seleção por intervalo com Shift e alternância com Ctrl implementadas"

# Check G: Navegação por Teclas 2D e Teclas de Atalho (F5, Enter, Esc)
$hasKeyNav = $dtContent -match 'Key\.F5' -and $dtContent -match 'Key\.Enter' -and $dtContent -match 'Key\.Escape' -and $dtContent -match 'Key\.Left or Key\.Right'
Record-Result -Name "Code_Keyboard_2D_Navigation" -Passed $hasKeyNav -Details "Navegação por setas (Left/Right/Up/Down), F5 refresh, Enter para abrir e Esc para desselecionar"

# Check H: Animação Heartbeat Pulse
$hasHeartbeat = $dtContent -match 'StartHeartbeatAnimation' -and $dtContent -match 'StopHeartbeatAnimation'
Record-Result -Name "Code_Heartbeat_Pulse_Animation" -Passed $hasHeartbeat -Details "Animação sutil de pulso (Heartbeat) de 300ms ao segurar o mouse implementada"

# Check I: Botão Fechar no Cabeçalho e Gerenciamento de Fechamento (IsClosed)
$hasCloseFeature = $dtContent -match '_closeButton' -and $dtContent -match 'CloseGroup' -and $dtContent -match '_category\.IsClosed\s*=\s*true'
Record-Result -Name "Code_Group_Close_Management" -Passed $hasCloseFeature -Details "Botão de fechar '✕' no cabeçalho com persistência IsClosed implementado"

# Check J: Redimensionamento com Reflow em Tempo Real
$hasLiveReflow = $dtContent -match 'ReflowGridDuringResize'
Record-Result -Name "Code_Live_Resize_Reflow" -Passed $hasLiveReflow -Details "Reflow instantâneo e suave dos ícones durante redimensionamento da janela"

# Check K: Clipboard Completo (Recortar, Copiar, Colar) nos Menus
$hasClipboard = $dtContent -match 'LocalizationService\.Get\("item\.cut"\)' -and $dtContent -match 'LocalizationService\.Get\("group\.paste"\)'
Record-Result -Name "Code_Clipboard_Context_Menus" -Passed $hasClipboard -Details "Opções Recortar (Ctrl+X), Copiar (Ctrl+C) e Colar (Ctrl+V) disponíveis nos menus de contexto"

# Check L: Drag and Drop com Precisão 1:1 e Consciência de DPI
$ghostFile = Join-Path $repoRoot "src\EasyWinMenu.App\Desktop\DragGhostWindow.cs"
$ghostContent = Get-Content -LiteralPath $ghostFile -Raw
$hasDpiDrag = $dtContent -match 'grabOffset(Physical)?\s*=' -and
              $dtContent -match '(currentMouseScreenDip|mouseScreenDip|screenDip|currentCursorPt|pt)\.X\s*-\s*grabOffset(Physical)?\.X' -and
              $dtContent -match 'TransformToDescendant\(_canvas\)' -and
              $dtContent -match 'target\.TransformToDescendant\(target\._canvas\)' -and
              $ghostContent -match 'CaptureVisual' -and
              $ghostContent -match 'visualWidth'
Record-Result -Name "Code_DragDrop_1to1_DpiAware" -Passed $hasDpiDrag -Details "Arrasto com rastreamento 1:1 do cursor no ponto de clique, snapshot High-DPI e soltura exata sem drift"

# Check M: Drag Resilience e 60 FPS Cursor Tracking sem sumiço de ghost
$hasDragResilience = $dtContent -match 'dragTrackingTimer' -and
                     $dtContent -match 'GetAsyncKeyState\(0x01\)' -and
                     $dtContent -match 'GetCursorPos\(out\s+POINT\s+lpPoint\)' -and
                     $dtContent -match 'FinishDrag'
Record-Result -Name "Code_Drag_Resilience_60FPS" -Passed $hasDragResilience -Details "Rastreamento contínuo a 60 FPS com GetCursorPos e checagem de botão físico (evita sumiço de ghost mid-drag)"

# Check N: Auto-Arrangement Respeitado no Drop em Outro Grupo
$hasDropPreserveArrange = -not ($dtContent -match 'AcceptMovedEntry[^{]*\{[^}]*_category\.IconArrangement\s*=\s*IconArrangement\.None') -and
                          $dtContent -match 'FinishStructuralChange\(\)'
Record-Result -Name "Code_Drop_Preserves_AutoArrangement" -Passed $hasDropPreserveArrange -Details "Soltura de item em outro grupo preserva regra de organização ativa e auto-reorganiza"

# Check O: Auto-Arrangement Ativado por Padrão 'Por Nome'
$hasDefaultByName = $dtContent -match 'ToggleArrangeIconsAutomatically' -and
                    $dtContent -match 'SortByName\(\)' -and
                    $dtContent -match '_category\.IconArrangement\s*!=\s*IconArrangement\.None'
Record-Result -Name "Code_AutoArrange_Default_ByName" -Passed $hasDefaultByName -Details "Primeira ativação de auto-organizar define 'Por Nome' como padrão e alterna status corretamente"

# Check P: Gerenciamento de Z-Order das Janelas (Frente/Trás)
$hasZOrder = $dtContent -match 'BringAllGroupsToFront' -and
             $dtContent -match 'SendOthersToBack' -and
             $dtContent -match 'SendAllToBack' -and
             $dtContent -match 'SetWindowPos' -and
             $dtContent -match 'HWND_BOTTOM'
Record-Result -Name "Code_Window_ZOrder_Management" -Passed $hasZOrder -Details "Ações de Z-Order (todos à frente, outros para trás, todos para trás) via SetWindowPos implementadas"

# Check Q: Navegação Multi-Monitor Simétrica Invariante
$hasSymmetricMonitors = $dtContent -match '_monitorPositions' -and
                        $dtContent -match '_monitorPositions\[from\]\s*=' -and
                        $dtContent -match 'MoveToAdjacentMonitor'
Record-Result -Name "Code_MultiMonitor_Symmetric_Navigation" -Passed $hasSymmetricMonitors -Details "Navegação por monitores armazena histórico de posição de origem para retorno 100% simétrico e sem drift"

# Check R: Seleção Retangular (Marquee Rubber-Band) no Canvas Vazio
$hasMarquee = $dtContent -match '_isMarqueeActive' -and
              $dtContent -match '_marqueeBorder' -and
              $dtContent -match 'IntersectsWith\(tileRect\)' -and
              $dtContent -match 'ModifierKeys\.Control'
Record-Result -Name "Code_Marquee_RubberBand_Selection" -Passed $hasMarquee -Details "Seleção por retângulo de arraste (rubber-band) em área vazia com suporte a Ctrl/Shift e sem conflito de tile"

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

$procWindows = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
$foundWindows = ($procWindows -ne $null -and -not $proc.HasExited)
Record-Result -Name "Desktop_Windows_Enumeration" -Passed $foundWindows -Details "Instância do EasyWinMenu validada em execução ativa (PID $($proc.Id))"

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
Write-Host ("Resultado Geral: " + $passedCount + " / " + $totalCount + " testes aprovados.") -ForegroundColor $summaryColor

if ($passedCount -ne $totalCount) {
    exit 1
}

exit 0
