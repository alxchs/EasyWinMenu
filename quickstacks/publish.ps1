# publish.ps1 - gera a distribuicao "xcopy" do QuickStacks: uma pasta autocontida, sem
# instalador, sem precisar de administrador (RNF06) - o app roda direto do .exe.
#
# Isto e' o caminho de distribuicao VERIFICADO nesta maquina (Fase 7). O empacotamento MSIX
# (mais "corporativo": Store, atualizacao automatica, assinatura) fica documentado na secao
# 6.9 do doc tecnico como um proximo passo que precisa de uma maquina com Visual Studio - ver
# a secao 4 do mesmo documento para o motivo (ferramenta MRT/PRI ausente aqui).
#
# Uso:
#   .\publish.ps1                    publica em .\publish\win-x64
#   .\publish.ps1 -OutputDir C:\dist  publica em outra pasta

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot 'publish\win-x64')
)

$ErrorActionPreference = 'Stop'
$uiProject = Join-Path $PSScriptRoot 'src\QuickStacks.UI\QuickStacks.UI.csproj'

Write-Host "Publicando QuickStacks (win-x64, autocontido) em: $OutputDir" -ForegroundColor Cyan

dotnet publish $uiProject `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falhou (codigo $LASTEXITCODE)."
}

Write-Host ''
Write-Host "Pronto. Para distribuir: copie a pasta '$OutputDir' inteira para a outra maquina" -ForegroundColor Green
Write-Host "e rode QuickStacks.UI.exe - nao precisa instalar nada nem ser administrador."
