param(
    [ValidateSet('Application','Installer')][string]$Mode='Application',
    [Parameter(Mandatory=$true)][string]$Journal
)
$ErrorActionPreference='Stop'
$launcherVersion='V79 - ultra performances et dossier modifiable'
. (Join-Path $PSScriptRoot 'CompilationCache.ps1')
try {
    $target=Join-Path $PSScriptRoot ($Mode+'.ps1')
    if(-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw ('Fichier manquant : '+$target) }
    if([Environment]::Version.Major -lt 4 -or $PSVersionTable.PSVersion -lt [version]'5.1' -or ($PSVersionTable.PSEdition -and $PSVersionTable.PSEdition -ne 'Desktop')) {
        throw 'Cette application exige Windows PowerShell 5.1 et .NET Framework 4.'
    }
    # Validation syntaxique mise en cache par empreinte SHA-256.
    # Un script modifié est toujours reparsé ; un fichier inchangé et déjà validé
    # n'impose plus une analyse complète à chaque lancement.
    $scriptFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Where-Object {$_.Name -notlike 'Tests*.ps1'} | Sort-Object Name)
    # Les scripts critiques sont toujours relus et contrôlés par SHA-256.
    # Seule l'analyse syntaxique est réutilisée si le contenu est identique
    # à celui déjà validé ; une modification impose une nouvelle analyse.
    $criticalSyntax=@('FinanceSynchronisee.ps1','ImportsReseau.ps1','ScoliaConfiguration.ps1','Application.ps1','ScoliaIntegration.ps1','PrisesEnCharge.ps1','PerformanceInterface.ps1','Personnalisation.ps1','MiseAJourPerformance.ps1','InstallationSession.ps1','TransitionExperience.ps1','EtatEspace.ps1','InterfaceResponsive.ps1','ThemesAffichage.ps1','Ergonomie.ps1','FonctionnalitesV55.ps1','InscriptionBrouillonsV55.ps1','PresencesV55.ps1','ReinscriptionV55.ps1','CentreRechercheV55.ps1','PersonnelV56.ps1','SalairesV56.ps1','ServicesPersonnelV56.ps1','InterfacePersonnelV56.ps1','SuiviApprenantsV56.ps1')
    $cacheRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ITMCD/cache/syntaxe'
    $cacheKey=([Security.Cryptography.SHA256]::Create())
    try {
        $cacheIdentity=$PSScriptRoot+'|'+$PSVersionTable.PSVersion.ToString()+'|'+[Environment]::Is64BitProcess
        $cacheName=([BitConverter]::ToString($cacheKey.ComputeHash([Text.Encoding]::UTF8.GetBytes($cacheIdentity)))).Replace('-','')+'.json'
    } finally {$cacheKey.Dispose()}
    $cachePath=Join-Path $cacheRoot $cacheName
    $syntaxCache=@{}
    try {if([IO.File]::Exists($cachePath)){$loaded=[IO.File]::ReadAllText($cachePath)|ConvertFrom-Json;foreach($prop in $loaded.PSObject.Properties){$syntaxCache[$prop.Name]=[string]$prop.Value}}}catch{$syntaxCache=@{}}
    $nextCache=@{}
    foreach($scriptFile in $scriptFiles){
        # Le cache précédent recalculait le SHA-256 de tous les scripts à chaque démarrage.
        # Taille + date UTC permettent d'éviter toute lecture du contenu lorsqu'un fichier
        # est inchangé. Dès qu'une métadonnée change, le hash et l'analyse syntaxique sont refaits.
        $meta=[string]$scriptFile.Length+'|'+[string]$scriptFile.LastWriteTimeUtc.Ticks
        $cached=$(if($syntaxCache.ContainsKey($scriptFile.FullName)){[string]$syntaxCache[$scriptFile.FullName]}else{''})
        $forceParse=($scriptFile.Name -in $criticalSyntax)
        if(-not $forceParse -and $cached.StartsWith($meta+'|')){$nextCache[$scriptFile.FullName]=$cached;continue}
        $hash=(Get-FileHash -LiteralPath $scriptFile.FullName -Algorithm SHA256).Hash
        $nextCache[$scriptFile.FullName]=$meta+'|'+$hash
        # Compatibilité avec l'ancien cache path -> hash : première migration sans reparser.
        if($cached -eq $hash -or $cached.EndsWith('|'+$hash)){continue}
        $tokens=$null;$parseErrors=$null
        [void][Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName,[ref]$tokens,[ref]$parseErrors)
        if($parseErrors.Count -gt 0){
            $detail=($parseErrors | ForEach-Object {'Ligne '+$_.Extent.StartLineNumber+', colonne '+$_.Extent.StartColumnNumber+' : '+$_.Message}) -join [Environment]::NewLine
            throw ('Erreur de syntaxe dans '+$scriptFile.FullName+':'+[Environment]::NewLine+$detail)
        }
    }
    try {
        [void][IO.Directory]::CreateDirectory($cacheRoot)
        $cacheTemp=$cachePath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        [IO.File]::WriteAllText($cacheTemp,($nextCache|ConvertTo-Json -Depth 4),[Text.Encoding]::UTF8)
        if([IO.File]::Exists($cachePath)){[IO.File]::Replace($cacheTemp,$cachePath,$null)}else{[IO.File]::Move($cacheTemp,$cachePath)}
    } catch {try{if($cacheTemp -and [IO.File]::Exists($cacheTemp)){[IO.File]::Delete($cacheTemp)}}catch{}}
    # L'indicateur de transition est charge avant le parcours d'accueil afin qu'il
    # reste fluide pendant les passages entre les fenetres. Il utilise son propre thread UI.
    if(-not ('ITMActivity' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
    . (Join-Path $PSScriptRoot 'TransitionExperience.ps1')
    Show-ScoliaTransition 'Préparation de SCOLIA...'
    if($Mode -eq 'Application'){
        . (Join-Path $PSScriptRoot 'LocalisationClasseurs.ps1')
        $root=[IO.Path]::GetDirectoryName($PSScriptRoot)
        $prepared=Get-ITMWorkbookLocation -Purpose Connexion -Root $root
        Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'InstancesApplication.cs')
        $multiple=$false
        if($prepared.Exists){
            try{
                $settings=Get-ItemProperty -LiteralPath 'HKCU:\Software\ITMCD\Secretariat\Affichage' -ErrorAction Stop
                if($settings.PSObject.Properties['SettingsJson']){$settings=[string]$settings.SettingsJson | ConvertFrom-Json -ErrorAction Stop}
                $multiple=($settings.PSObject.Properties['WindowMode'] -and $settings.WindowMode -eq 'Plusieurs fenêtres')
            }catch{}
        }
        if(-not [ITMApplicationInstances]::Enter([bool]$multiple)){exit 0}
        if(-not $prepared.Exists){
            Close-ScoliaTransition
            & (Join-Path $PSScriptRoot 'Installer.ps1') -Onboarding
            $prepared=Get-ITMWorkbookLocation -Purpose Connexion -Root $root
            if(-not $prepared.Exists){exit 0}
        }
    }
    Update-ScoliaTransition 'Chargement des écrans et de vos préférences...'
    & $target
    exit 0
} catch {
    $failure=$_
    $healthHash='indisponible'
    try {$healthHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'EtatEspace.ps1') -Algorithm SHA256).Hash} catch {}
    $report=@(
        'SCOLIA - diagnostic de lancement',
        ('Version du lanceur : '+$launcherVersion),
        ('Dossier du programme : '+$PSScriptRoot),
        ('Empreinte EtatEspace.ps1 : '+$healthHash),
        ('Date : '+(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
        ('Module : '+$Mode),
        ('PowerShell : '+$PSVersionTable.PSVersion),
        ('Processus 64 bits : '+[Environment]::Is64BitProcess),
        '',
        ('Cause : '+$failure.Exception.Message),
        '',
        $failure.InvocationInfo.PositionMessage,
        $failure.ScriptStackTrace,
        '',
        'Fermez Excel avant de connecter un classeur. Conservez ce journal pour le diagnostic.'
    ) -join [Environment]::NewLine
    try { [IO.File]::WriteAllText($Journal,$report,[Text.Encoding]::Unicode) } catch {}
    exit 2
} finally {
    if(Get-Command Close-ScoliaTransition -ErrorAction SilentlyContinue){Close-ScoliaTransition}
    if('ITMApplicationInstances' -as [type]){[ITMApplicationInstances]::Leave()}
}
