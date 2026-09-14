# Publishes a self-contained, single-file win-x64 build of the app and feeds
# it to Inno Setup to produce a real installer (Start Menu shortcut, optional
# desktop icon, an Add/Remove Programs entry, an uninstaller). This is what
# `mkfile package` runs for this project — see EasyWinMenu.App.csproj's
# sibling tools\ folder, the same convention the Flutter side of mkfile
# already uses for tools\build_apk.py.
#
# Self-contained on purpose: the installer needs to work on a machine that
# may not have the .NET 10 runtime installed, since the whole point is
# downloading and running it outside this network.

$ErrorActionPreference = 'Stop'

$appDir = Split-Path $PSScriptRoot -Parent
$repoRoot = (Resolve-Path (Join-Path $appDir '..\..')).ProviderPath
$csproj = Join-Path $appDir 'EasyWinMenu.App.csproj'
$versionFile = Join-Path $repoRoot 'VERSION'
$issFile = Join-Path $PSScriptRoot 'EasyWinMenu.iss'
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $versionFile)) {
    Write-Error "VERSION nao encontrado em '$versionFile'."
    exit 1
}
$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()

if (-not (Test-Path -LiteralPath $iscc)) {
    Write-Error "Inno Setup Compiler nao encontrado em '$iscc'. Instale com: winget install --id JRSoftware.InnoSetup"
    exit 1
}

Write-Host "[build_installer] publicando self-contained win-x64 $version..." -ForegroundColor Cyan
& dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "[build_installer] compilando instalador $version..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" $issFile
exit $LASTEXITCODE
