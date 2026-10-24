<#
.SYNOPSIS
  Vérifie le paquet SCOLIA Pro sur un poste Windows : fichiers, démarrage, santé, connexion,
  lectures de l'API, monnaie, sauvegarde et fenêtre du client natif.

.DESCRIPTION
  À lancer depuis le dossier du paquet (là où se trouvent SCOLIA.exe et SCOLIA-Bureau.exe).
  Par défaut, le script ne modifie aucune donnée : il lit seulement.
  Le paramètre -Ecriture crée une sauvegarde de test ; à n'utiliser que sur une installation de test.

.PARAMETER Dossier
  Dossier du paquet. Par défaut, le dossier de ce script.

.PARAMETER Port
  Port du serveur. Par défaut, la valeur de appsettings.json (5080).

.PARAMETER Utilisateur
  Compte de test. Par défaut : admin.

.PARAMETER Client
  Lance aussi SCOLIA-Bureau.exe, vérifie sa fenêtre puis la ferme.

.PARAMETER Ecriture
  Crée une sauvegarde de test et vérifie qu'elle apparaît dans la liste.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\VERIFIER_SCOLIA.ps1 -Client
#>
[CmdletBinding()]
param(
    [string]$Dossier = $PSScriptRoot,
    [int]$Port = 0,
    [string]$Utilisateur = 'admin',
    [switch]$Client,
    [switch]$Ecriture
)

$ErrorActionPreference = 'Stop'
$resultats = New-Object System.Collections.ArrayList
$avertissements = 0
$ko = 0

function Noter {
    param([string]$Controle, [bool]$Ok, [string]$Detail = '', [switch]$Avertissement)
    if ($Ok) { $statut = 'OK' }
    elseif ($Avertissement) { $statut = 'ATTENTION'; $script:avertissements++ }
    else { $statut = 'ECHEC'; $script:ko++ }
    [void]$script:resultats.Add([pscustomobject]@{ Controle = $Controle; Resultat = $statut; Detail = $Detail })
    $couleur = 'Red'
    if ($Ok) { $couleur = 'Green' } elseif ($Avertissement) { $couleur = 'Yellow' }
    Write-Host ("{0,-9} {1} {2}" -f $statut, $Controle, $Detail) -ForegroundColor $couleur
}

function Lire-Json([string]$Chemin) {
    return (Get-Content -Path $Chemin -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Attendre-Serveur([int]$PortServeur, [int]$DelaiSecondes) {
    $fin = (Get-Date).AddSeconds($DelaiSecondes)
    while ((Get-Date) -lt $fin) {
        try {
            $sante = Invoke-RestMethod -Uri "http://localhost:$PortServeur/api/sante" -TimeoutSec 2
            return $sante
        } catch {
            Start-Sleep -Milliseconds 300
        }
    }
    return $null
}

$exe = Join-Path $Dossier 'SCOLIA.exe'
$exeClient = Join-Path $Dossier 'SCOLIA-Bureau.exe'
$config = Join-Path $Dossier 'appsettings.json'
$donnees = Join-Path $Dossier 'donnees'
$rapport = Join-Path $Dossier ("verification-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

Write-Host ''
Write-Host 'SCOLIA Pro : vérification du paquet' -ForegroundColor Cyan
Write-Host ("Dossier : {0}" -f $Dossier)
Write-Host ''

# 1. Fichiers du paquet
Noter 'SCOLIA.exe présent' (Test-Path $exe) $exe
Noter 'SCOLIA-Bureau.exe présent' (Test-Path $exeClient) $exeClient
Noter 'appsettings.json présent' (Test-Path $config) $config
Noter 'Aide au poste présente (LIRE_MOI.txt)' (Test-Path (Join-Path $Dossier 'LIRE_MOI.txt'))
if (Test-Path $exe) {
    $taille = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Noter 'Taille de SCOLIA.exe raisonnable' ($taille -gt 20) ("{0} Mo" -f $taille) -Avertissement
}

# 2. Port et réglages
if (-not (Test-Path $config)) { throw "Fichier de réglages introuvable : $config" }
$reglages = Lire-Json $config
if ($Port -eq 0) {
    $Port = [int]$reglages.Scolia.Reseau.Port
    if ($Port -eq 0) { $Port = 5080 }
}
Noter 'Port de réglage lisible' ($Port -gt 0) ("port {0}" -f $Port)

# 3. Démarrage du serveur (ou service déjà actif)
$deja = Attendre-Serveur $Port 2
$processusServeur = $null
$demarre = $false
if ($deja) {
    Noter 'Serveur déjà actif (service ou instance ouverte)' $true ("port {0}" -f $Port)
} else {
    if (-not (Test-Path $exe)) { throw 'SCOLIA.exe introuvable : impossible de démarrer le serveur.' }
    $env:Scolia__Reseau__Port = "$Port"
    $env:Scolia__OuvrirNavigateur = 'false'
    $env:Scolia__Icone = 'false'
    $chrono = [Diagnostics.Stopwatch]::StartNew()
    $processusServeur = Start-Process -FilePath $exe -WorkingDirectory $Dossier -WindowStyle Hidden -PassThru
    $demarre = $true
    Remove-Item Env:\Scolia__Reseau__Port, Env:\Scolia__OuvrirNavigateur, Env:\Scolia__Icone -ErrorAction SilentlyContinue
    $deja = Attendre-Serveur $Port 60
    $chrono.Stop()
    $secondes = [math]::Round($chrono.Elapsed.TotalSeconds, 1)
    Noter 'Serveur démarré' ([bool]$deja) ("{0} s" -f $secondes)
    Noter 'Démarrage en moins de 10 secondes' ($secondes -lt 10) ("{0} s" -f $secondes) -Avertissement
}

if (-not $deja) {
    Noter 'Réponse du serveur sur /api/sante' $false 'aucune réponse'
} else {
    Noter 'Santé du serveur' ($deja.statut -eq 'ok') ("version {0}" -f $deja.version)
    $session = $null
    $sessionVariable = $null
    try {
        $secret = Read-Host ("Mot de passe du compte {0}" -f $Utilisateur) -AsSecureString
        $mdp = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret))
        $corps = @{ userName = $Utilisateur; password = $mdp } | ConvertTo-Json
        $mdp = $null
        $reponse = Invoke-RestMethod -Method Post -Uri "http://localhost:$Port/api/session" -Headers @{ 'X-Scolia' = '1' } `
            -ContentType 'application/json' -Body $corps -SessionVariable sessionVariable
        $session = $sessionVariable
        Noter 'Connexion' ($null -ne $reponse) ("{0}, {1}" -f $reponse.displayName, $reponse.schoolName)
        Noter 'Monnaie affichée : FC' ($reponse.currencySymbol -eq 'FC') ("symbole {0}, code {1}" -f $reponse.currencySymbol, $reponse.currencyCode) -Avertissement
    } catch {
        Noter 'Connexion' $false $_.Exception.Message
    }

    if ($session) {
        $lectures = @(
            @{ Nom = 'Tableau de bord'; Chemin = 'api/rapports/tableau-de-bord' },
            @{ Nom = 'Liste des apprenants'; Chemin = 'api/apprenants?taille=5' },
            @{ Nom = 'Journal des reçus'; Chemin = 'api/paiements?taille=5' },
            @{ Nom = 'Recouvrement'; Chemin = 'api/rapports/debiteurs' },
            @{ Nom = 'Caisse'; Chemin = 'api/caisse/situation' },
            @{ Nom = 'Journal d audit'; Chemin = 'api/audit?taille=5' },
            @{ Nom = 'Vérification du journal'; Chemin = 'api/audit/verification' },
            @{ Nom = 'Intégrité de la base'; Chemin = 'api/sauvegardes/integrite' },
            @{ Nom = 'Recherche globale'; Chemin = 'api/recherche?q=ACC' }
        )
        foreach ($lecture in $lectures) {
            try {
                $null = Invoke-RestMethod -Uri ("http://localhost:$Port/" + $lecture.Chemin) -WebSession $session -TimeoutSec 20
                Noter $lecture.Nom $true
            } catch {
                Noter $lecture.Nom $false $_.Exception.Message
            }
        }

        try {
            $integrite = Invoke-RestMethod -Uri "http://localhost:$Port/api/sauvegardes/integrite" -WebSession $session
            Noter 'Base de données intacte' ([bool]$integrite.intacte) ''
        } catch { Noter 'Base de données intacte' $false $_.Exception.Message }

        if ($Ecriture) {
            try {
                $avant = @(Invoke-RestMethod -Uri "http://localhost:$Port/api/sauvegardes" -WebSession $session).Count
                $null = Invoke-RestMethod -Method Post -Uri "http://localhost:$Port/api/sauvegardes" -WebSession $session -Headers @{ 'X-Scolia' = '1' } -ContentType 'application/json' -Body '{}'
                $apres = @(Invoke-RestMethod -Uri "http://localhost:$Port/api/sauvegardes" -WebSession $session).Count
                Noter 'Sauvegarde immédiate créée' ($apres -gt $avant) ("{0} avant, {1} après" -f $avant, $apres)
            } catch { Noter 'Sauvegarde immédiate créée' $false $_.Exception.Message }
        }
    }

    Noter 'Base de données présente dans donnees' (Test-Path (Join-Path $donnees 'scolia.db')) (Join-Path $donnees 'scolia.db')
    $journaux = Join-Path $donnees 'journaux'
    Noter 'Journaux écrits' ((Test-Path $journaux) -and ((Get-ChildItem $journaux -ErrorAction SilentlyContinue).Count -gt 0)) $journaux -Avertissement
}

# 4. Client natif
if ($Client) {
    if (-not (Test-Path $exeClient)) {
        Noter 'Client natif lancé' $false 'SCOLIA-Bureau.exe introuvable'
    } elseif (-not $deja) {
        Noter 'Client natif lancé' $false 'serveur indisponible'
    } else {
        $env:SCOLIA_SERVEUR = "http://localhost:$Port/"
        $client = Start-Process -FilePath $exeClient -WorkingDirectory $Dossier -PassThru
        Remove-Item Env:\SCOLIA_SERVEUR -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 12
        $client.Refresh()
        $fenetre = $client.MainWindowHandle -ne 0
        Noter 'Fenêtre du client native ouverte' ((-not $client.HasExited) -and $fenetre) ("titre : {0}" -f $client.MainWindowTitle)
        if (-not $client.HasExited) {
            [void]$client.CloseMainWindow()
            Start-Sleep -Seconds 8
            $client.Refresh()
            Noter 'Fermeture propre du client' $client.HasExited ''
        }
    }
}

# 5. Arrêt du serveur lancé par ce script
if ($demarre -and $processusServeur -and -not $processusServeur.HasExited) {
    try { Stop-Process -Id $processusServeur.Id -Force; Start-Sleep -Seconds 2 } catch { }
    $encore = $null
    try { $encore = Invoke-RestMethod -Uri "http://localhost:$Port/api/sante" -TimeoutSec 2 } catch { }
    Noter 'Serveur arrêté par le script' ($null -eq $encore) ''
}

# 6. Rapport
$resultats | Format-Table -AutoSize | Out-String | Write-Host
$resultats | Format-Table -AutoSize | Out-String -Width 200 | Set-Content -Path $rapport -Encoding UTF8
Write-Host ("Rapport enregistré : {0}" -f $rapport)
Write-Host ("Échecs : {0}   Avertissements : {1}" -f $ko, $avertissements)
if ($ko -gt 0) { exit 1 } else { exit 0 }
