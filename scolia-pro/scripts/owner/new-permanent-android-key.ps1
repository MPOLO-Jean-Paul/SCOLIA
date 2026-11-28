# Initialise, sur l'ordinateur du proprietaire, la premiere cle permanente SCOLIA Console.
# IMPORTANT : executer le script localement, jamais dans GitHub Actions, et stocker la cle HORS du depot.
# Ne pas relancer pour une mise a jour : reutiliser le MEME fichier JKS prive.
[CmdletBinding()]
param(
  [string]$Destination = (Join-Path $HOME 'SCOLIA-PrivateSigning\owner-admin-release.jks'),
  [string]$Alias = 'scolia-admin-release'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Get-Command keytool -ErrorAction SilentlyContinue)) {
  throw 'Installer un JDK 17+ (incluant keytool), puis relancer cette procedure sur votre PC Windows.'
}
$parent = Split-Path -Parent $Destination
if (-not $parent) { throw 'Chemin de sortie invalide.' }
New-Item -ItemType Directory -Path $parent -Force | Out-Null
if (Test-Path -LiteralPath $Destination) {
  throw "Ce keystore existe deja : $Destination. Ne jamais le remplacer : il est necessaire pour les mises a jour."
}
Write-Host 'Generation de la cle permanente SCOLIA Console Android sur VOTRE PC.'
Write-Host 'Choisir des mots de passe forts et les conserver hors ligne. Aucune cle ne sera chargee sur GitHub automatiquement.'
& keytool -genkeypair -keystore $Destination -storetype JKS -alias $Alias -keyalg RSA -keysize 4096 -validity 10000 -dname 'CN=SCOLIA Console, OU=Software, O=JP-Services, C=CD' -J-Duser.language=en -J-Duser.country=US
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Destination)) {
  throw 'Generation du keystore interrompue ou echouee.'
}
Write-Host ''
Write-Host 'Lecture de l empreinte publique du certificat (mot de passe requis a nouveau).'
$info = & keytool -list -v -keystore $Destination -storetype JKS -alias $Alias -J-Duser.language=en -J-Duser.country=US
if ($LASTEXITCODE -ne 0) { throw 'Lecture du certificat impossible.' }
$match = [regex]::Match(($info -join "`n"), '(?im)^\s*SHA256:\s*([0-9A-Fa-f:]{64,95})\s*$')
if (-not $match.Success) { throw 'Empreinte publique SHA256 non trouvee ; verifier le certificat avec keytool.' }
$sha = ($match.Groups[1].Value -replace ':','').ToLowerInvariant()
if ($sha.Length -ne 64) { throw 'Empreinte publique SHA256 invalide.' }
Write-Host ''
Write-Host 'A CONSERVER :'
Write-Host "  Cle privee JKS : $Destination"
Write-Host "  Alias : $Alias"
Write-Host "  Empreinte publique (GitHub variable OWNER_ADMIN_CERT_SHA256) : $sha"
Write-Host ''
Write-Host 'Deux copies chiffrées hors ligne du JKS et de ses mots de passe sont indispensables.'
Write-Host 'Ne jamais envoyer le JKS ou les mots de passe dans ChatGPT, un email ou un depot public.'
Write-Host 'Pour l instruction sur les 4 secrets GitHub : voir le guide de publication dans ce depot.'
