# Produit le paquet Windows autonome de SCOLIA Pro (aucun prérequis sur le poste).
# Usage : powershell -ExecutionPolicy Bypass -File scripts\publier-windows.ps1
param([string]$Sortie = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$ErrorActionPreference = 'Stop'
$racine = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $racine 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$paquet = Join-Path $Sortie 'SCOLIA'

Write-Host '==> Interface web'
Push-Location (Join-Path $racine 'web')
try { npm ci --no-audit --no-fund; if ($LASTEXITCODE) { throw 'npm ci' }; npm run build; if ($LASTEXITCODE) { throw 'npm run build' } } finally { Pop-Location }

Write-Host "==> Exécutable Windows $version"
if (Test-Path $paquet) { Remove-Item $paquet -Recurse -Force }
dotnet publish (Join-Path $racine 'src\Scolia.Api') -c Release -f net10.0-windows -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true `
  -p:DebugType=None -p:DebugSymbols=false -p:IsTransformWebConfigDisabled=true `
  -o $paquet
if ($LASTEXITCODE) { throw 'dotnet publish' }

Write-Host '==> Client natif (fenêtre Avalonia, sans navigateur)'
dotnet publish (Join-Path $racine 'src\Scolia.Bureau') -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o $paquet
if ($LASTEXITCODE) { throw 'dotnet publish client natif' }

Write-Host "==> Outils d'exploitation"
Copy-Item (Join-Path $racine 'scripts\windows\*') $paquet
Get-ChildItem $paquet -Filter *.pdb | Remove-Item

$archive = Join-Path $Sortie "SCOLIA_Pro_${version}_Windows_x64.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path $paquet -DestinationPath $archive -CompressionLevel Optimal
(Get-FileHash $archive -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path $archive -Leaf) | Set-Content "$archive.sha256" -Encoding ascii
Write-Host "Paquet : $archive"
