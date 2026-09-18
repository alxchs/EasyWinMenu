# Script de empacotamento e publicacao do QuickStacks
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$rootDir = Split-Path $PSScriptRoot -Parent

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $rootDir "quickstacks\VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile).Trim()
    } else {
        $Version = "1.1.0"
    }
}

Write-Host "=== Compilando QuickStacks Release v$Version (win-x64 Self-Contained) ===" -ForegroundColor Cyan

$projectPath = Join-Path $rootDir "quickstacks\src\QuickStacks.UI\QuickStacks.UI.csproj"
$publishDir = Join-Path $rootDir "quickstacks\publish\win-x64"

& dotnet publish $projectPath `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao compilar QuickStacks.UI"
    exit $LASTEXITCODE
}

$exePath = Join-Path $publishDir "QuickStacks.UI.exe"
if (Test-Path $exePath) {
    Write-Host "Executavel Release gerado com sucesso em:" -ForegroundColor Green
    Write-Host "  $exePath" -ForegroundColor Yellow
}

# Tenta compilar o instalador Inno Setup se ISCC.exe estiver disponivel
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$iscc = $null
foreach ($c in $isccCandidates) {
    if (Test-Path $c) {
        $iscc = $c
        break
    }
}
if (-not $iscc) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if ($iscc) {
    Write-Host "Compilando instalador Inno Setup..." -ForegroundColor Cyan
    $issFile = Join-Path $rootDir "tools\QuickStacks.iss"
    & $iscc $issFile "/DMyAppVersion=$Version"
    Write-Host "Instalador gerado em dist/" -ForegroundColor Green
} else {
    Write-Host "ISCC.exe (Inno Setup) nao localizado localmente - executavel portavel disponivel na pasta publish/win-x64." -ForegroundColor Gray
}
