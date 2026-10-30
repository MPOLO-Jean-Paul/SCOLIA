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

Write-Host "==> Documentation du paquet"
Copy-Item (Join-Path $racine 'scripts\windows\LIRE_MOI.txt') $paquet

# Livraison SCOLIA Phone : deux plateformes distinctes et aucun faux IPA.
# Android : APK. iPhone : IPA signe uniquement si fourni par la compilation Apple.
$phone = Join-Path $paquet 'Phone'
$android = Join-Path $phone 'Android'
$iphone = Join-Path $phone 'iPhone'
New-Item -ItemType Directory -Force $android, $iphone | Out-Null
Copy-Item (Join-Path $racine 'scripts\windows\LIRE_MOI_TELEPHONE.txt') (Join-Path $android 'LIRE_MOI_ANDROID.txt')
Copy-Item (Join-Path $racine 'scripts\windows\LIRE_MOI_IPHONE.txt') (Join-Path $iphone 'LIRE_MOI_IPHONE.txt')
$apk = Get-ChildItem (Join-Path $Sortie 'android') -Filter 'SCOLIA_Pro_*_Android.apk' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($apk) {
  Write-Host '==> SCOLIA Phone Android (APK)'
  Copy-Item $apk.FullName (Join-Path $android 'SCOLIA-Android.apk')
}
# Le code iPhone et les ressources partagées restent disponibles dans le kit,
# sans faire croire qu'une compilation de simulateur équivaut à un IPA signé.
$sourceIos = Join-Path $Sortie 'sources/SCOLIA_Pro_Sources.zip'
if (Test-Path $sourceIos) {
  Copy-Item $sourceIos (Join-Path $iphone 'SCOLIA_Phone_iPhone_Sources.zip')
}
# Un IPA fourni manuellement n'est pas admis comme livrable par défaut :
# seule une archive explicitement nommée iOS_Signed peut être jointe.
$ipa = Get-ChildItem (Join-Path $Sortie 'ios') -Filter 'SCOLIA_Phone_*_iOS_Signed.ipa' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($ipa) {
  Write-Host '==> SCOLIA Phone iPhone (IPA fourni par le pipeline Apple)'
  Copy-Item $ipa.FullName (Join-Path $iphone 'SCOLIA-iPhone.ipa')
}
else {
  Write-Host '==> SCOLIA Phone iPhone : source disponible, IPA signe non fourni'
}
Get-ChildItem $paquet -Filter *.pdb | Remove-Item

$archive = Join-Path $Sortie "SCOLIA_Pro_${version}_Windows_x64.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path $paquet -DestinationPath $archive -CompressionLevel Optimal
(Get-FileHash $archive -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path $archive -Leaf) | Set-Content "$archive.sha256" -Encoding ascii
Write-Host "Paquet : $archive"
