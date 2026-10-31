if(-not (Test-Path function:Invoke-ITMExcelRunNoArguments)){. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')}
# Mise a jour limitee aux deux modules de performance, sur les segments data seulement.
function Normalize-Code([string]$text) {
    $lines = $text -split '\r?\n' | Where-Object { $_ -notmatch '^Attribute ' } | ForEach-Object { $_.Trim() }
    return (($lines -join "`n").Trim())
}
function Code-Hash([string]$text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((Normalize-Code $text))))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Code-Tokens([string]$text) {
    $text = $text.TrimStart([char]0xFEFF) -replace '(?m)^Attribute [^\r\n]*(?:\r?\n|$)', ''
    $pattern = @'
"(?:""|[^"\r\n])*"|'[^\r\n]*|[ \t]+_\r?\n|\r?\n|[ \t]+|[\p{L}_][\p{L}\p{N}_]*|(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?|<>|<=|>=|.
'@
    $tokens = New-Object 'System.Collections.Generic.List[string]'
    $skipRem = $false
    foreach ($match in [regex]::Matches($text, $pattern)) {
        $token = $match.Value
        if ($token -match '^\r?\n$') {
            $skipRem = $false
            if ($tokens.Count -gt 0 -and $tokens[$tokens.Count-1] -ne '<EOL>') { $tokens.Add('<EOL>') }
            continue
        }
        if ($skipRem -or $token.StartsWith("'") -or $token -match '^[ \t]+(?:_\r?\n)?$') { continue }
        if ($token -ieq 'Rem' -and ($tokens.Count -eq 0 -or $tokens[$tokens.Count-1] -in @('<EOL>',':'))) { $skipRem = $true; continue }
        if (-not $token.StartsWith('"')) { $token = $token.ToUpperInvariant() }
        $tokens.Add($token)
    }
    if ($tokens.Count -gt 0 -and $tokens[$tokens.Count-1] -eq '<EOL>') { $tokens.RemoveAt($tokens.Count-1) }
    return ,$tokens
}
function Same-Code([string]$expected, [string]$actual) {
    $left = Code-Tokens $expected; $right = Code-Tokens $actual
    if ($left.Count -ne $right.Count) { return $false }
    for ($i=0; $i -lt $left.Count; $i++) { if ($left[$i] -cne $right[$i]) { return $false } }
    return $true
}

function Test-ITMPerformanceBook($book) {
    try{
        $firstMacro=Get-ITMExcelMacroName $book 'ModuleFiabilite' 'ITMPerformanceVersion'
        $secondMacro=Get-ITMExcelMacroName $book 'ModuleApplicationITM' 'ITMAppPerformanceVersion'
        return ((Invoke-ITMExcelRunNoArguments $script:excel $firstMacro) -eq 'ITM-PERF-2' -and (Invoke-ITMExcelRunNoArguments $script:excel $secondMacro) -eq 'SCOLIA-PERF-7')
    }catch{return $false}
}
function Update-ITMManagedRuntime {
    $script:runtimeUpdateNote=''
    if(-not $script:store){return}
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $targets=@('ModuleFiabilite','ModuleApplicationITM');$plan=New-Object 'System.Collections.Generic.List[object]'
    foreach($book in $script:storeBooks){
        if(Test-ITMPerformanceBook $book){continue}
        try{$project=$book.VBProject;$components=$project.VBComponents;$count=$components.Count}catch{
            $script:runtimeUpdateNote='Mise à jour des calculs Excel disponible : activez Accès approuvé au modèle d''objet VBA dans Excel, fermez Excel et relancez APPLICATION.vbs. La connexion actuelle est conservée.'
            return
        }
        if($project.Protection -ne 0){$script:runtimeUpdateNote='Projet VBA verrouillé : optimisation Excel non appliquée. La connexion actuelle est conservée.';return}
        foreach($item in $manifest){
            $component=$null;try{$component=$components.Item([string]$item.name)}catch{}
            if(-not $component){$script:runtimeUpdateNote='Passerelle ancienne : optimisation Excel non appliquée. Préservez votre base et ses sauvegardes.';return}
            if($item.name -in $targets -and [int]$component.Type -ne 1){$script:runtimeUpdateNote='Type de module personnalisé : optimisation automatique refusée.';return}
            $cm=$component.CodeModule;$actual=$(if($cm.CountOfLines){$cm.Lines(1,$cm.CountOfLines)}else{''})
            $expected=Get-Content -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'Corrections') $item.file) -Raw -Encoding UTF8
            if((Code-Hash $expected) -ne [string]$item.after){throw ('Correctif de performance endommagé : '+$item.name)}
            if(Same-Code $actual $expected){continue}
            # Only the immediately preceding known module may be replaced automatically.
            $prior='Compatibilite/'+$item.name+'_AvantPerformance.txt'
            if($item.name -notin $targets -or -not [IO.File]::Exists((Join-Path $PSScriptRoot $prior))){$script:runtimeUpdateNote='Code personnalisé ou ancienne passerelle : optimisation Excel automatique non appliquée. Aucun module n''a été remplacé.';return}
            $matchedPrevious=$false
            foreach($known in @($prior)+@($item.compatibilityFiles)){
                if(-not $known -or -not [IO.File]::Exists((Join-Path $PSScriptRoot $known))){continue}
                $previous=Get-Content -LiteralPath (Join-Path $PSScriptRoot $known) -Raw -Encoding UTF8
                if((Code-Hash $previous) -in @($item.compatible) -and (Same-Code $actual $previous)){$matchedPrevious=$true;break}
            }
            if(-not $matchedPrevious){$script:runtimeUpdateNote='Module personnalisé : mise à jour automatique refusée sans modifier les classeurs.';return}
            $plan.Add([pscustomobject]@{Book=$book;Module=$cm;Name=[string]$item.name;Code=$expected})
        }
    }
    if(-not $plan.Count){return}
    Set-ITMActivityMessage 'Sauvegarde et optimisation des calculs Excel...'
    Start-ITMStoreTransaction
    try{
        foreach($entry in $plan){
            if($entry.Module.CountOfLines){[void]$entry.Module.DeleteLines(1,$entry.Module.CountOfLines)}
            [void]$entry.Module.InsertLines(1,($entry.Code.TrimStart([char]0xFEFF) -replace '\r?\n',"`r`n"))
            $actual=$entry.Module.Lines(1,$entry.Module.CountOfLines)
            if(-not (Same-Code $actual $entry.Code)){throw ('Vérification de la mise à jour impossible : '+$entry.Name)}
        }
        foreach($book in $script:storeBooks){if(-not (Test-ITMPerformanceBook $book)){throw ('Passerelle de performance non vérifiée : '+$book.Name)}}
        Complete-ITMStoreTransaction
    }catch{
        # Keep the durable recovery journal. No partial upgraded base is used.
        throw ('Mise à jour interrompue ; sauvegarde de récupération conservée. Relancez l''application pour restaurer la base complète. '+$_.Exception.Message)
    }
}
function Reset-ITMCompletedSaveLock {
    # Called only between synchronous Excel.Run calls in our private Excel instance.
    # A dirty workbook or an uncommitted store transaction must never be reset.
    if(-not $script:book){return}
    try{
        $liveBook=Resolve-ITMLiveWorkbook $script:book
        $saved=[bool](Get-ITMExcelProperty $liveBook 'Saved')
        if(-not $saved){return}
        if($script:storeFolder -and [IO.File]::Exists((Join-Path $script:storeFolder 'transaction.json'))){return}
        $macro=Get-ITMExcelMacroName $liveBook 'ModuleFiabilite' 'ITMFinSaisieSansSituation'
        # Optional cleanup must never turn a saved operation into a reported failure.
        try{[void](Invoke-ITMExcelRunNoArguments $script:excel $macro)}catch{}
    }catch{}
}
function Invoke-ITMWriteMacro([string]$name,[object[]]$arguments=@()) {
    $saveWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    Reset-ITMCompletedSaveLock
    $script:snapshotValid=$false
    $script:book=Resolve-ITMLiveWorkbook $script:book
    $macro=Api-Name $name
    if($arguments.Count -notin @(0,1,2,3,4,6,7,8,11,14)){throw 'Nombre de paramètres de saisie incorrect.'}
    $reply=Invoke-ITMExcelRun $script:excel $macro $arguments
    $value=[string]$reply | ConvertFrom-Json
    if($value -and $value.PSObject.Properties['ok'] -and $value.ok){
        # Mark durable success before optional cleanup, audit or display can fail.
        $script:committedThisAction=$true
        Reset-ITMCompletedSaveLock
    }
    return [string]$reply
    } finally {
        $saveWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample ('Ecriture Excel: '+$name) $saveWatch.Elapsed.TotalMilliseconds}
    }
}

function Write-ITMConnectionDiagnostic([string]$phase,$failure) {
    try{
        $folder=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'journaux/connexions'
        [void][IO.Directory]::CreateDirectory($folder)
        $path=Join-Path $folder ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N')+'.txt')
        $lines=New-Object 'System.Collections.Generic.List[string]'
        $lines.Add('SCOLIA | Diagnostic de connexion');$lines.Add('Date : '+[DateTimeOffset]::Now.ToString('o'));$lines.Add('Étape : '+$phase)
        $lines.Add('Édition : 101');$lines.Add('Sous-étape data : '+[string]$script:storeConnectionStep);$lines.Add('Opération Excel : '+[string]$script:connectionOperation);$lines.Add('PowerShell : '+$PSVersionTable.PSVersion.ToString())
        if($script:connectionMacro){$lines.Add('Dernier appel Excel : '+$script:connectionMacro)}
        if($failure.InvocationInfo){$lines.Add('Script : '+$failure.InvocationInfo.ScriptName+' | Ligne : '+$failure.InvocationInfo.ScriptLineNumber)}
        $errorObject=$failure.Exception
        while($errorObject){$lines.Add($errorObject.GetType().FullName+' : '+$errorObject.Message);if($errorObject.StackTrace){$lines.Add($errorObject.StackTrace)};$errorObject=$errorObject.InnerException}
        if($failure.ScriptStackTrace){$lines.Add($failure.ScriptStackTrace)}
        [IO.File]::WriteAllText($path,($lines -join [Environment]::NewLine),[Text.Encoding]::UTF8)
        return $path
    }catch{return ''}
}
