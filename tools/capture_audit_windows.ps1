# ==============================================================================
# Script de Comprovação Visual - EasyWinMenu
# Captura as janelas ativas de grupos e atalhos na Área de Trabalho
# ==============================================================================

$repoRoot = "C:\desenv\utils\EasyWinMenu"
$appExe = Join-Path $repoRoot "src\EasyWinMenu.App\bin\Release\net10.0-windows\EasyWinMenu.App.exe"
$screenshotDir = Join-Path $repoRoot "tools\screenshots"
$artifactDir = "C:\Users\alxch\.gemini\antigravity\brain\3b4eee52-4015-4dd6-808e-716488bce559"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

# Finaliza instâncias anteriores
Get-Process -Name "EasyWinMenu.App" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

# Inicia EasyWinMenu
$proc = Start-Process -FilePath $appExe -PassThru
Start-Sleep -Seconds 3

# Captura desktop inteiro
try {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)

    $outPath1 = Join-Path $screenshotDir "audit_desktop_verified.png"
    $outPath2 = Join-Path $artifactDir "audit_desktop_verified.png"
    $bmp.Save($outPath1, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Save($outPath2, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose()
    $bmp.Dispose()
    Write-Host "Screenshot salvo com sucesso em $outPath1 e $outPath2" -ForegroundColor Green
} catch {
    Write-Host "Falha na captura: $_" -ForegroundColor Yellow
}

# Mantém processo rodando ou encerra conforme teste
try {
    $proc.Kill()
} catch {}

