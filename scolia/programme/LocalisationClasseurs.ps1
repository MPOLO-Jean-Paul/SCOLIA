# Recherche locale uniquement : aucun classeur ouvert, déplacé ou modifié.
$script:locationKey='HKCU:\Software\ITMCD\Secretariat'
function Get-ITMLocationPreference([string]$name) {
    try{return [string](Get-ItemProperty -LiteralPath $script:locationKey -Name $name -ErrorAction Stop).$name}catch{return ''}
}
function Set-ITMLocationPreference([string]$name,[string]$value) {
    try{
        if(-not (Test-Path -LiteralPath $script:locationKey)){[void](New-Item -Path $script:locationKey -Force -ErrorAction Stop)}
        [void](New-ItemProperty -LiteralPath $script:locationKey -Name $name -Value $value -PropertyType String -Force -ErrorAction Stop)
        return $true
    }catch{return $false}
}
function Read-ITMLocationIndex([string]$folder) {
    try{
        $m=$null
        try{$m=Get-Content -LiteralPath (Join-Path $folder 'registre.json') -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json}catch{}
        if(-not $m){
            $tx=Get-Content -LiteralPath (Join-Path $folder 'transaction.json') -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json
            $m=$tx.Index
        }
        if($m.Version -ne 1){return $null}
        if([string]$m.Id -notmatch '^[a-f0-9]{32}$' -or [string]$m.Active -notmatch '^classeur-[0-9]{6}\.xlsm$'){return $null}
        if([IO.Path]::GetFileName($folder) -ne [string]$m.Id){return $null}
        if(@($m.Segments).Count -eq 0 -or [string]$m.Active -notin @($m.Segments)){return $null}
        $seen=@{}
        foreach($segment in @($m.Segments)){if([string]$segment -notmatch '^classeur-[0-9]{6}\.xlsm$' -or $seen.ContainsKey([string]$segment)){return $null};$seen[[string]$segment]=$true}
        return $m
    }catch{return $null}
}
function New-ITMLocationResult([string]$path,[string]$folder,[string]$reason,[bool]$managed=$false,[bool]$ambiguous=$false) {
    if($path){$folder=[IO.Path]::GetDirectoryName($path)}
    [pscustomobject]@{Path=$path;Directory=$folder;Reason=$reason;Managed=$managed;Ambiguous=$ambiguous;Exists=([bool]$path -and [IO.File]::Exists($path));Installable=([bool]$path -and -not $managed -and [IO.File]::Exists($path))}
}
function Resolve-ITMLocationPath([string]$path,[string]$root,[bool]$preferLocal=$false) {
    if(-not $path -or [IO.Path]::GetExtension($path) -ine '.xlsm'){return $null}
    try{$path=[IO.Path]::GetFullPath($path)}catch{return $null}
    $folder=[IO.Path]::GetDirectoryName($path)
    $index=Read-ITMLocationIndex $folder
    # Pour un choix mémorisé, la copie déplacée avec cette application est prioritaire.
    # Un choix explicite conserve toujours le dossier désigné par l'utilisateur.
    if($preferLocal){
        $id=[IO.Path]::GetFileName($folder)
        if($id -match '^[a-f0-9]{32}$'){
            $local=Join-Path (Join-Path $root 'data') $id
            $localIndex=Read-ITMLocationIndex $local
            if($localIndex -and [string]$localIndex.Id -eq $id){$folder=$local;$index=$localIndex}
        }
    }
    if(-not $index){
        $id=[IO.Path]::GetFileName($folder)
        if($id -match '^[a-f0-9]{32}$'){
            $moved=Join-Path (Join-Path $root 'data') $id
            $index=Read-ITMLocationIndex $moved
            if($index){$folder=$moved}
        }
    }
    if($index){return New-ITMLocationResult (Join-Path $folder $index.Active) $folder 'Base actuelle détectée dans data.' $true}
    if([IO.File]::Exists((Join-Path $folder 'registre.json')) -or [IO.File]::Exists((Join-Path $folder 'transaction.json'))){return New-ITMLocationResult $path $folder 'Le registre de cette base doit être vérifié. Choisissez son dossier actuel.' $true}
    return New-ITMLocationResult $path $folder 'Classeur mémorisé détecté.'
}
function Get-ITMWorkbookLocation {
    param([ValidateSet('Connexion','Installation')][string]$Purpose='Connexion',
        [string]$Root=([IO.Path]::GetDirectoryName($PSScriptRoot)),[string]$Explicit='',
        [string]$Connection='',[string]$Source='')
    if(-not $PSBoundParameters.ContainsKey('Connection')){$Connection=Get-ITMLocationPreference 'Classeur'}
    if(-not $PSBoundParameters.ContainsKey('Source')){$Source=Get-ITMLocationPreference 'SourceInstallation'}
    $dataFolder=Join-Path $Root 'data'
    # Un choix explicite ou mémorisé indisponible ne doit jamais être remplacé par une ancienne copie.
    $found=Resolve-ITMLocationPath $Explicit $Root
    if($found){return $found}
    $found=Resolve-ITMLocationPath $Connection $Root $true
    if($found){return $found}
    $bases=@();$unavailableBases=0
    if([IO.Directory]::Exists($dataFolder)){
        foreach($dir in @(Get-ChildItem -LiteralPath $dataFolder -Directory -ErrorAction SilentlyContinue)){
            $index=Read-ITMLocationIndex $dir.FullName
            if($index -and -not $index.Closed){$bases+=New-ITMLocationResult (Join-Path $dir.FullName $index.Active) $dir.FullName 'Base actuelle détectée dans data.' $true}
            elseif([IO.File]::Exists((Join-Path $dir.FullName 'registre.json')) -or [IO.File]::Exists((Join-Path $dir.FullName 'transaction.json'))){$unavailableBases++}
        }
    }
    if($bases.Count -eq 1){return $bases[0]}
    if($bases.Count -gt 1){return New-ITMLocationResult '' $dataFolder 'Plusieurs bases détectées : choisissez la base à utiliser.' $false $true}
    if($unavailableBases -gt 0){return New-ITMLocationResult '' $dataFolder 'Bases archivées ou registre à vérifier : choisissez le classeur actuel.' $false $true}
    if($Purpose -eq 'Installation'){
        $found=Resolve-ITMLocationPath $Source $Root
        if($found){return $found}
    }
    # Seulement les sorties complètes : ni EN_COURS, ni sauvegarde, ni segment isolé.
    foreach($container in @((Join-Path $dataFolder 'installations'),(Join-Path $Root 'archives'))){
        if(-not [IO.Directory]::Exists($container)){continue}
        $prepared=@(Get-ChildItem -LiteralPath $container -Directory -ErrorAction SilentlyContinue | Where-Object {
            [IO.File]::Exists((Join-Path $_.FullName 'RESULTAT_INSTALLATION.txt')) -and
            [IO.File]::Exists((Join-Path $_.FullName 'SYSTEME_SECRETARIAT_CORRIGE.xlsm'))
        } | Sort-Object Name -Descending)
        if($prepared.Count){return New-ITMLocationResult (Join-Path $prepared[0].FullName 'SYSTEME_SECRETARIAT_CORRIGE.xlsm') $prepared[0].FullName 'Dernière installation réussie détectée.'}
    }
    if($Purpose -eq 'Installation'){
        return New-ITMLocationResult '' $dataFolder 'Aucun classeur source prêt n''a été détecté. Choisissez votre classeur actuel ou utilisez Nouvelle source.'
    }
    return New-ITMLocationResult '' $dataFolder 'Choisissez votre classeur actuel.'
}
function Set-ITMWorkbookDialogLocation($dialog,$location) {
    $folder=$location.Directory
    # Support absent : remonter au premier dossier accessible, sans ouvrir un autre classeur.
    while($folder -and -not [IO.Directory]::Exists($folder)){
        $parent=[IO.Path]::GetDirectoryName($folder)
        if($parent -eq $folder){break};$folder=$parent
    }
    if($folder -and [IO.Directory]::Exists($folder)){$dialog.InitialDirectory=$folder}
    if($location.Exists){$dialog.FileName=[IO.Path]::GetFileName($location.Path)}
    $dialog.RestoreDirectory=$true;$dialog.CheckFileExists=$true;$dialog.Multiselect=$false
}
function Open-ITMWorkbookFolder($location) {
    if(-not [IO.Directory]::Exists($location.Directory)){throw 'Le dossier du classeur est indisponible. Rebranchez son support ou utilisez Choisir.'}
    if($location.Exists){Start-Process explorer.exe -ArgumentList ('/select,"'+$location.Path+'"')}
    else{Start-Process explorer.exe -ArgumentList ('"'+$location.Directory+'"')}
}

function Test-ITMWorkbookSet([string]$path) {
    $folder=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))
    $pending=Join-Path $folder 'transaction.json'
    # La récupération vérifie toutes les empreintes avant de restaurer le moindre fichier.
    if([IO.File]::Exists($pending)){return}
    if(-not [IO.File]::Exists((Join-Path $folder 'registre.json'))){return}
    $index=Read-ITMLocationIndex $folder
    if(-not $index){throw ('Registre de base invalide : '+$folder+'. Choisissez le dossier complet ou restaurez une sauvegarde complète.')}
    $missing=@($index.Segments | Where-Object {-not [IO.File]::Exists((Join-Path $folder $_))})
    if($missing.Count){throw ('Base incomplète. Classeur(s) manquant(s) : '+($missing -join ', ')+"`r`nDossier : "+$folder+"`r`nReconnectez le support ou restaurez la base complète. Aucun autre classeur ne sera ouvert.")}
}
