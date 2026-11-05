# Signature Authenticode pour les exécutables SCOLIA officiels uniquement.
# Requiert un certificat de signature de code valide stocké dans les secrets GitHub Actions.
# Ne journalise jamais le certificat, le mot de passe ni la clé privée.
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) { throw 'La signature Authenticode SCOLIA requiert Windows.' }
if ([string]::IsNullOrWhiteSpace($env:SCOLIA_WINDOWS_SIGNING_PFX) -or
    [string]::IsNullOrWhiteSpace($env:SCOLIA_WINDOWS_SIGNING_PASSWORD)) {
    throw 'Secrets Authenticode absents : configurer SCOLIA_WINDOWS_SIGNING_PFX et SCOLIA_WINDOWS_SIGNING_PASSWORD. Publication bloquée.'
}
if ($Files.Count -eq 0) { throw 'Aucun exécutable à signer.' }
foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
        [IO.Path]::GetExtension($file) -ne '.exe') {
        throw "Fichier Windows à signer introuvable ou non exécutable : $file"
    }
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$temporaryPfx = Join-Path $tempRoot ("scolia-sign-" + [Guid]::NewGuid().ToString('N') + '.pfx')
$leaf = $null
try {
    $raw = [Convert]::FromBase64String($env:SCOLIA_WINDOWS_SIGNING_PFX)
    [IO.File]::WriteAllBytes($temporaryPfx, $raw)
    [Array]::Clear($raw, 0, $raw.Length)
    $securePassword = ConvertTo-SecureString $env:SCOLIA_WINDOWS_SIGNING_PASSWORD -AsPlainText -Force
    $imported = @(Import-PfxCertificate -FilePath $temporaryPfx -CertStoreLocation Cert:\CurrentUser\My -Password $securePassword -ErrorAction Stop)
    $leaf = $imported | Where-Object {
        $_.HasPrivateKey -and
        ($_.EnhancedKeyUsageList | Where-Object {
            $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' -or
            $_.Value -eq '1.3.6.1.5.5.7.3.3' -or
            $_.FriendlyName -eq 'Code Signing'
        })
    } | Select-Object -First 1

    if ($null -eq $leaf) {
        throw 'Le certificat fourni ne contient pas de clé privée pour la signature de code Windows.'
    }
    $utcNow = (Get-Date).ToUniversalTime()
    if ($leaf.NotBefore.ToUniversalTime() -gt $utcNow -or $leaf.NotAfter.ToUniversalTime() -le $utcNow) {
        throw 'Le certificat de signature Windows n’est pas valide à la date actuelle.'
    }
    if ($env:SCOLIA_WINDOWS_CERT_THUMBPRINT -and
        $leaf.Thumbprint -ne $env:SCOLIA_WINDOWS_CERT_THUMBPRINT.Replace(':','').Replace(' ','').ToUpperInvariant()) {
        throw 'Empreinte de certificat Windows inattendue : publication refusée.'
    }
    Remove-Item -LiteralPath $temporaryPfx -Force
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source
    if (-not $signtool) {
        $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $signtool = Get-ChildItem $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $signtool) { throw 'Windows SDK SignTool introuvable : publication refusée.' }
    foreach ($file in $Files) {
        & $signtool sign /s My /sha1 $leaf.Thumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $file
        if ($LASTEXITCODE -ne 0) { throw "Échec de la signature Authenticode : $file" }
        & $signtool verify /pa /all /v $file
        if ($LASTEXITCODE -ne 0) { throw "Signature Authenticode non vérifiée : $file" }
        $signature = Get-AuthenticodeSignature -FilePath $file
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $leaf.Thumbprint) {
            throw "Signature Windows invalide : $file"
        }
        Write-Host "Signature de code vérifiée : $(Split-Path -Leaf $file) / $($leaf.Thumbprint)"
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPfx) { Remove-Item -LiteralPath $temporaryPfx -Force }
    if ($null -ne $leaf) {
        $certPath = 'Cert:\CurrentUser\My\' + $leaf.Thumbprint
        if (Test-Path $certPath) { Remove-Item $certPath -Force }
    }
}
