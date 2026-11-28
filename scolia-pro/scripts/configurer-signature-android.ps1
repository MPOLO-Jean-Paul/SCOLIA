# Configure en securite les secrets GitHub de la signature officielle Android SCOLIA.
# Ne jamais ajouter le keystore au depot ni partager son mot de passe.
# Prerequis : GitHub CLI (gh auth login), Java JDK (keytool), PowerShell 7 ou Windows PowerShell.
# Usage : .\configurer-signature-android.ps1 -Keystore "C:\Chemin\vers\scolia.keystore"
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Keystore,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Depot = 'MPOLO-Jean-Paul/SCOLIA'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$attendu = 'E5:B5:F7:FA:41:54:11:EE:AC:4B:F7:34:84:90:24:FF:CF:7F:93:50:0C:60:13:83:99:31:B0:20:38:F5:89:80'

function Set-SecretFromMemory {
    param([string]$Nom, [string]$Valeur, [string]$Repository)
    # Le secret n'apparait ni dans les arguments du processus ni dans les fichiers locaux.
    $cmd = (Get-Command gh -ErrorAction Stop).Source
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo.FileName = $cmd
    $p.StartInfo.Arguments = "secret set $Nom --repo $Repository"
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.RedirectStandardInput = $true
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    try {
        if (-not $p.Start()) { throw "Impossible de demarrer GitHub CLI." }
        $p.StandardInput.Write($Valeur)
        $p.StandardInput.Close()
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) {
            # Ne pas imprimer de sortie ni de trace susceptible de contenir le secret.
            throw "Echec d'enregistrement de $Nom. Verifiez les autorisations GitHub."
        }
    }
    finally { $p.Dispose() }
}

$mot = $null
try {
    $file = (Resolve-Path -LiteralPath $Keystore -ErrorAction Stop).ProviderPath
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Fichier de cle introuvable."
    }
    $null = Get-Command keytool -ErrorAction Stop
    $null = Get-Command gh -ErrorAction Stop
    & gh auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI n'est pas connecte. Executez gh auth login."
    }

    $secure = Read-Host 'Mot de passe de la CLE ORIGINALE (masque)' -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $mot = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    if ([string]::IsNullOrEmpty($mot)) { throw "Mot de passe vide refuse." }

    # Utiliser la variable d'environnement plutot que -storepass <mot> qui
    # exposerait le mot de passe dans la liste des processus.
    $env:SCOLIA_KEYSTORE_PASS = $mot
    $details = & keytool -list -v -keystore $file -alias scolia -storepass:env SCOLIA_KEYSTORE_PASS 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Cle ou mot de passe invalide, ou alias scolia absent."
    }
    $expr = '(?im)^\s*SHA256\s*:\s*((?:[A-Fa-f0-9]{2}:){31}[A-Fa-f0-9]{2})\s*$'
    $empreinte = [regex]::Match(($details -join "`n"), $expr).Groups[1].Value.ToUpperInvariant()
    if ($empreinte -ne $attendu) {
        throw "CERTIFICAT INCOMPATIBLE. Cette cle ne peut pas mettre a jour SCOLIA 1.2.16. Aucun secret n'a ete modifie."
    }

    Write-Host 'Empreinte officielle confirmee. Enregistrement chiffre dans GitHub Actions...'
    $base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))
    Set-SecretFromMemory -Nom 'SCOLIA_ANDROID_KEYSTORE' -Valeur $base64 -Repository $Depot
    Set-SecretFromMemory -Nom 'SCOLIA_ANDROID_KEYSTORE_PASS' -Valeur $mot -Repository $Depot
    Write-Host 'Les deux secrets ont ete configures dans GitHub Actions.'
    Write-Host 'La publication reste conditionnee aux tests Android/Windows et a la verification de signature du pipeline.'
}
finally {
    Remove-Item Env:SCOLIA_KEYSTORE_PASS -ErrorAction SilentlyContinue
    $mot = $null
}
