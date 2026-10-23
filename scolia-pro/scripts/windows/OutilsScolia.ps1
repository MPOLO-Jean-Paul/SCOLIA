# SCOLIA Pro : outils d'exploitation Windows (service, réseau local).
# Appelé par les fichiers .cmd du dossier. Demande l'élévation si nécessaire.
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('InstallerService', 'DesinstallerService', 'ActiverReseau', 'DesactiverReseau')]
    [string]$Action
)
$ErrorActionPreference = 'Stop'
$dossier = $PSScriptRoot
$exe = Join-Path $dossier 'SCOLIA.exe'
$config = Join-Path $dossier 'appsettings.json'
$nomService = 'SCOLIA'
$compteService = "NT SERVICE\$nomService"
$nomRegle = 'SCOLIA Pro (réseau local)'

$identite = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identite.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Action $Action"
    return
}

function Lire-Config { Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json }
function Enregistrer-Config($valeur) {
    $json = $valeur | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($config, $json, (New-Object Text.UTF8Encoding $false))
}
function Port { $c = Lire-Config; if ($c.Scolia.Reseau.Port) { [int]$c.Scolia.Reseau.Port } else { 5080 } }
function Dossier-Donnees {
    $c = Lire-Config
    $d = [string]$c.Scolia.Stockage.DataDirectory
    if ([string]::IsNullOrWhiteSpace($d)) { return (Join-Path $dossier 'donnees') }
    return [Environment]::ExpandEnvironmentVariables($d)
}
function Service { Get-Service -Name $nomService -ErrorAction SilentlyContinue }
function Appliquer {
    if (Service) { Restart-Service -Name $nomService; Write-Host 'Service SCOLIA redémarré : le réglage est appliqué.' -ForegroundColor Green }
    else { Write-Host 'Quittez SCOLIA depuis son icône de notification puis relancez-le pour appliquer le réglage.' -ForegroundColor Yellow }
}
function Adresses {
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { "  http://$($_.IPAddress):$(Port)" }
}

try {
    switch ($Action) {
        'InstallerService' {
            if (Service) { Write-Host 'Le service SCOLIA est déjà installé.' -ForegroundColor Yellow; break }
            $ouverts = Get-Process -Name SCOLIA -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
            if ($ouverts) { throw 'SCOLIA est ouvert sur ce poste. Quittez-le depuis son icône de notification, puis relancez cette installation.' }
            $donnees = Dossier-Donnees
            New-Item -ItemType Directory -Force -Path $donnees | Out-Null
            New-Service -Name $nomService -BinaryPathName "`"$exe`"" -DisplayName 'SCOLIA Pro' -StartupType Automatic `
                -Description 'Serveur de gestion scolaire SCOLIA Pro (secrétariat, caisse, scolarité).' | Out-Null
            # Compte virtuel dédié : le service n'a que les droits nécessaires (moindre privilège).
            & sc.exe config $nomService obj= $compteService | Out-Null
            & icacls.exe $dossier /grant "${compteService}:(OI)(CI)RX" /Q | Out-Null
            & icacls.exe $donnees /grant "${compteService}:(OI)(CI)M" /Q | Out-Null
            # Redémarrage automatique en cas d'arrêt inattendu.
            & sc.exe failure $nomService reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
            Start-Service -Name $nomService
            Write-Host "Service SCOLIA installé et démarré (démarrage automatique avec Windows)." -ForegroundColor Green
            Write-Host "Adresse sur ce poste : http://localhost:$(Port)"
        }
        'DesinstallerService' {
            if (-not (Service)) { Write-Host "Le service SCOLIA n'est pas installé." -ForegroundColor Yellow; break }
            Stop-Service -Name $nomService -ErrorAction SilentlyContinue
            & sc.exe delete $nomService | Out-Null
            Write-Host 'Service SCOLIA supprimé. Les données sont conservées.' -ForegroundColor Green
        }
        'ActiverReseau' {
            $c = Lire-Config
            $c.Scolia.Reseau.AccesReseauLocal = $true
            Enregistrer-Config $c
            Get-NetFirewallRule -DisplayName $nomRegle -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            New-NetFirewallRule -DisplayName $nomRegle -Direction Inbound -Protocol TCP -LocalPort (Port) -Action Allow -Profile Domain, Private | Out-Null
            Write-Host 'Accès depuis le réseau local activé (réseaux privés et de domaine uniquement).' -ForegroundColor Green
            Write-Host 'Les autres postes ouvrent SCOLIA dans leur navigateur avec :'
            Adresses
            Appliquer
        }
        'DesactiverReseau' {
            $c = Lire-Config
            $c.Scolia.Reseau.AccesReseauLocal = $false
            Enregistrer-Config $c
            Get-NetFirewallRule -DisplayName $nomRegle -ErrorAction SilentlyContinue | Remove-NetFirewallRule
            Write-Host 'Accès depuis le réseau local désactivé : SCOLIA ne répond plus que sur ce poste.' -ForegroundColor Green
            Appliquer
        }
    }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
}
Read-Host 'Appuyez sur Entrée pour fermer'
