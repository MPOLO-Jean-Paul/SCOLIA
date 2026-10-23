param([string]$Classeur,[string]$Session,[switch]$Onboarding)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'IconesFenetres.ps1')
. (Join-Path $PSScriptRoot 'LocalisationClasseurs.ps1')
. (Join-Path $PSScriptRoot 'InstallationSession.ps1')
$xl = $null; $wb = $null; $folder = $null; $success = $false
$oldAutoFill = $null
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
function Verify-Code($component, [string]$expected, [string]$name) {
    $cm = $component.CodeModule
    $actual = if ($cm.CountOfLines -gt 0) { $cm.Lines(1, $cm.CountOfLines) } else { '' }
    if (-not (Same-Code $expected $actual)) {
        $expected | Set-Content -LiteralPath (Join-Path $folder ($name+'_ATTENDU.txt')) -Encoding UTF8
        $actual | Set-Content -LiteralPath (Join-Path $folder ($name+'_RELU.txt')) -Encoding UTF8
        $left = Code-Tokens $expected; $right = Code-Tokens $actual
        $at = 0
        while ($at -lt [Math]::Min($left.Count,$right.Count) -and $left[$at] -ceq $right[$at]) { $at++ }
        throw "Le code relu du module $name differe du correctif (element $at, $($left.Count) / $($right.Count) elements). Les deux versions sont dans le dossier du journal."
    }
}
function Show-Status([string]$message, [int]$step = -1) {
    $outputFolder=$(if($folder){$folder}else{$script:installOutputFolder})
    Write-ITMInstallStatus $message $step $outputFolder
}
function Invoke-ITMInstallationCheck([string]$message,[scriptblock]$action) {
    Show-Status $message
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try{return (& $action)}
    finally{
        $watch.Stop()
        Write-ITMInstallLog ($message+' | Duree : '+$watch.Elapsed.TotalSeconds.ToString('F1',[Globalization.CultureInfo]::InvariantCulture)+' s')
    }
}
function Last-Row($ws) {
    $found = $ws.Cells.Find('*', $ws.Cells.Item(1,1), -4123, 2, 1, 2, $false, $false, $false)
    if ($null -eq $found) { return 4 }
    return [Math]::Max(4, [int]$found.Row)
}
function Snapshot($ws, [int]$columns, [int]$start = 5) {
    $last = Last-Row $ws
    $records = New-Object 'System.Collections.Generic.List[object]'
    if ($last -lt $start) { return ,$records }
    $v = $ws.Range($ws.Cells.Item($start,1), $ws.Cells.Item($last,$columns)).Value2
    $f = $ws.Range($ws.Cells.Item($start,1), $ws.Cells.Item($last,$columns)).Formula
    for ($r=1; $r -le ($last-$start+1); $r++) {
        if($r % 2000 -eq 0){Show-Status ("Lecture de "+$ws.Name+" : "+$r+" / "+($last-$start+1)+" lignes...")}

        $constants = New-Object 'System.Collections.Generic.List[object]'
        $values = New-Object object[] $columns
        for ($c=1; $c -le $columns; $c++) {
            $value=$v[$r,$c]; $formula=$f[$r,$c]; $values[$c-1]=$value
            if ($formula -is [string] -and $formula.StartsWith('=')) { continue }
            if ($null -ne $value -and [string]$value -ne '') { $constants.Add([ordered]@{col=$c;value=$value}) }
        }
        if ($constants.Count -gt 0) {
            $signature=ConvertTo-Json -InputObject $constants.ToArray() -Depth 6 -Compress
            $records.Add([pscustomobject]@{Row=$r+$start-1;Signature=$signature;Values=$values})
        }
    }
    return ,$records
}
function Compact-Sheet($ws, [int]$columns, $before, [bool]$moveHeader) {
    $shift=0
    if ($moveHeader) { [void]$ws.Rows.Item(3).Insert(-4121); $shift=1 }
    $last=Last-Row $ws
    $dest=5
    $offset=0
    while($offset -lt $before.Count){
        $origin=[int]$before[$offset].Row+$shift
        $length=1
        while($offset+$length -lt $before.Count -and $length -lt 1000 -and [int]$before[$offset+$length].Row+$shift -eq $origin+$length){$length++}
        if($origin -ne $dest){
            [void]$ws.Range($ws.Cells.Item($origin,1),$ws.Cells.Item($origin+$length-1,$columns)).Copy($ws.Cells.Item($dest,1))
        }
        $dest+=$length;$offset+=$length
        if($offset % 1000 -eq 0){Show-Status ("Regroupement de "+$ws.Name+" : "+$offset+" / "+$before.Count+" lignes...")}
    }
    if ($last -ge $dest) { [void]$ws.Range($ws.Cells.Item($dest,1),$ws.Cells.Item($last,$columns)).ClearContents() }
    $t=$ws.ListObjects.Item(1)
    [void]$t.Resize($ws.Range($ws.Cells.Item(4,1),$ws.Cells.Item([Math]::Max(5,$dest-1),$columns)))
    $ws.PageSetup.PrintTitleRows='$1:$4'
    $ws.PageSetup.PrintArea=$ws.Range($ws.Cells.Item(1,1),$ws.Cells.Item([Math]::Max(5,$dest-1),$columns)).Address()
    $after=Snapshot $ws $columns
    if ($after.Count -ne $before.Count) { throw "Nombre de lignes modifie dans $($ws.Name). Installation annulee." }
    for ($i=0; $i -lt $before.Count; $i++) {
        if ($before[$i].Signature -ne $after[$i].Signature) { throw "Valeur modifiee dans $($ws.Name), ancienne ligne $($before[$i].Row). Installation annulee." }
        if ($after[$i].Row -ne $i+5) { throw "Ligne vide restante dans $($ws.Name)." }
    }
}
function Update-References($book) {
    $maps=@{}
    foreach($name in @('APPRENANTS','PAIEMENTS')) {
        $t=$book.Worksheets.Item($name).ListObjects.Item(1)
        $headers=@{}
        for($i=1;$i -le $t.ListColumns.Count;$i++) { $headers[[string][char](64+$i)]=[string]$t.ListColumns.Item($i).Name }
        $maps[$name]=@{table=[string]$t.Name;headers=$headers}
    }
    $pattern='(?i)''?(APPRENANTS|PAIEMENTS)''?!\$?([A-O])\$?\d+:\$?([A-O])\$?\d+'
    $replace = [Text.RegularExpressions.MatchEvaluator]{param($m)
        $map=$maps[$m.Groups[1].Value.ToUpperInvariant()]
        $left=$map.headers[$m.Groups[2].Value.ToUpperInvariant()]
        $right=$map.headers[$m.Groups[3].Value.ToUpperInvariant()]
        if(-not $left -or -not $right) { return $m.Value }
        if($left -eq $right) { return "$($map.table)[$left]" }
        return "$($map.table)[[$left]:[$right]]"
    }
    foreach($ws in $book.Worksheets) {
        $formulaCells=$null
        try { $formulaCells=$ws.UsedRange.SpecialCells(-4123) } catch { }
        if($null -ne $formulaCells) {
            foreach($area in $formulaCells.Areas){
                $formulas=$area.Formula
                if($formulas -is [array]){
                    $rowMin=$formulas.GetLowerBound(0);$rowMax=$formulas.GetUpperBound(0)
                    $colMin=$formulas.GetLowerBound(1);$colMax=$formulas.GetUpperBound(1)
                    for($r=$rowMin;$r -le $rowMax;$r++){
                        for($c=$colMin;$c -le $colMax;$c++){
                            $old=[string]$formulas[$r,$c]
                            if(-not [regex]::IsMatch($old,$pattern)){continue}
                            $new=[regex]::Replace($old,$pattern,$replace)
                            if($new -ne $old){$area.Cells.Item($r-$rowMin+1,$c-$colMin+1).Formula=$new}
                        }
                    }
                }else{
                    $old=[string]$formulas
                    $new=[regex]::Replace($old,$pattern,$replace)
                    if($new -ne $old){$area.Formula=$new}
                }
            }
        }
    }
    foreach($name in $book.Names) {
        $old=[string]$name.RefersTo
        $new=[regex]::Replace($old,$pattern,$replace)
        if($new -ne $old) { $name.RefersTo=$new }
    }
    $s=$book.Worksheets.Item('SITUATION')
    $t=$book.Worksheets.Item('APPRENANTS').ListObjects.Item(1)
    $indices=@(1,2,3,4,8)
    for($i=1;$i -le 5;$i++) {
        $header=$t.ListColumns.Item($indices[$i-1]).Name
        $s.Cells.Item(5,$i).Formula='=IFERROR(INDEX('+ $t.Name +'['+$header+'],ROWS($A$5:A5)),"")'
    }
    $last=[Math]::Max((Last-Row $s), [Math]::Max(5, [int]$t.Range.Row+[int]$t.Range.Rows.Count-1))
    [void]$s.Range("A5:P$last").FillDown()
}
function Create-Audit($learners,$payments,[string]$path) {
    $issues=New-Object 'System.Collections.Generic.List[object]'
    $seen=@{}
    foreach($r in $learners) {
        $v=$r.Values; $why=New-Object 'System.Collections.Generic.List[string]'
        $key=([string]$v[0]).Trim().ToUpperInvariant()
        if($key.Length -ne 6) { $why.Add('Matricule a verifier') }
        if($seen.ContainsKey($key)) { $why.Add('Matricule deja present') } else { $seen[$key]=$true }
        if(([string]$v[4]) -notin @('M','F')) { $why.Add('Sexe ou colonnes decalees') }
        if(([string]$v[9]) -notmatch '^\d{10}$') { $why.Add('Contact parent 1 a verifier') }
        if([string]$v[10] -ne '' -and ([string]$v[10]) -notmatch '^\d{10}$') { $why.Add('Contact parent 2 a verifier') }
        if($why.Count) { $issues.Add([pscustomobject]@{Feuille='APPRENANTS';AncienneLigne=$r.Row;Identifiant=$v[0];Nom=([string]$v[1]+' '+$v[2]+' '+$v[3]);Mois='';Montant='';Observation=($why -join '. ')}) }
    }
    foreach($r in $payments) {
        $v=$r.Values; $why=New-Object 'System.Collections.Generic.List[string]'
        if([string]$v[0] -eq '' -or [string]$v[2] -eq '') { $why.Add('Identifiant ou numero manquant : partie de paiement isolee') }
        if([string]$v[6] -eq '') { $why.Add('Mois manquant') }
        if($null -eq $v[8] -or [string]$v[8] -eq '') { $why.Add('Montant paye manquant') }
        if(-not $seen.ContainsKey(([string]$v[3]).Trim().ToUpperInvariant())) { $why.Add('Matricule absent des apprenants') }
        if($why.Count) { $issues.Add([pscustomobject]@{Feuille='PAIEMENTS';AncienneLigne=$r.Row;Identifiant=$v[2];Nom=$v[4];Mois=$v[6];Montant=$v[8];Observation=($why -join '. ')}) }
    }
    if($issues.Count) { $issues | Export-Csv -LiteralPath $path -Delimiter ';' -NoTypeInformation -Encoding UTF8 }
    else { 'Aucune anomalie de saisie detectee par ces controles.' | Set-Content -LiteralPath ($path+'.txt') -Encoding UTF8 }
    return $issues.Count
}
function Get-ITMInstallerLastCell($sheet,[int]$order=1) {
    return $sheet.Cells.Find('*',$sheet.Cells.Item(1,1),-4123,2,$order,2,$false,$false,$false)
}
function New-ITMInstallerBlankSource {
    $root=[IO.Path]::GetDirectoryName($PSScriptRoot)
    $template=Join-Path (Join-Path $root 'ressources') 'FICHIER_SOURCE.xlsm'
    if(-not [IO.File]::Exists($template)){throw 'Le modèle de création SCOLIA est absent.'}
    $dir=Join-Path $root 'sources';[void][IO.Directory]::CreateDirectory($dir)
    $final=Join-Path $dir ('Source_SCOLIA_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'.xlsm')
    $temp=Join-Path $dir ('creation-'+[Guid]::NewGuid().ToString('N')+'.xlsm')
    $excel=$null;$book=$null;$published=$false
    try {
        Show-Status 'Création d une nouvelle source SCOLIA...' 2
        [IO.File]::Copy($template,$temp,$false)
        Unblock-File -LiteralPath $temp -ErrorAction SilentlyContinue
        $excel=New-Object -ComObject Excel.Application
        $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3;$excel.ScreenUpdating=$false
        $book=$excel.Workbooks.Open($temp,0,$false)
        if($book.ReadOnly){throw 'Le modèle de création est ouvert en lecture seule.'}
        foreach($required in @('PARAMETRES','PROMOTIONS','APPRENANTS','PAIEMENTS','SITUATION','ACCUEIL','FORM_PAIEMENT','FICHE_APPRENANT')){
            try{$null=$book.Worksheets.Item($required)}catch{throw ('Feuille manquante dans le modèle : '+$required)}
        }
        foreach($sheetName in @('APPRENANTS','PAIEMENTS')){
            $sheet=$book.Worksheets.Item($sheetName)
            try{$sheet.Unprotect()}catch{}
            if($sheet.ListObjects.Count -lt 1){throw ('Tableau structuré absent dans '+$sheetName+'.')}
            $table=$sheet.ListObjects.Item(1);$header=[int]$table.HeaderRowRange.Row
            if($header -eq 3){[void]$sheet.Rows.Item(3).Insert(-4121);$header=4;$table=$sheet.ListObjects.Item(1)}
            if($header -ne 4){throw ('En-tête inattendu dans '+$sheetName+'.')}
            $last=Get-ITMInstallerLastCell $sheet
            $lastColumn=Get-ITMInstallerLastCell $sheet 2
            $cols=[Math]::Max($(if($sheetName -eq 'APPRENANTS'){15}else{14}),$(if($lastColumn){[int]$lastColumn.Column}else{1}))
            $bottom=[Math]::Max($header+1,$(if($last){[int]$last.Row}else{$header+1}))
            [void]$sheet.Range($sheet.Cells.Item($header+1,1),$sheet.Cells.Item($bottom,$cols)).ClearContents()
            [void]$table.Resize($sheet.Range($sheet.Cells.Item($header,1),$sheet.Cells.Item($header+1,$table.Range.Columns.Count)))
            [void]$sheet.Range($sheet.Cells.Item($header+1,1),$sheet.Cells.Item($header+1,$cols)).ClearContents()
        }
        try{$journal=$book.Worksheets.Item('JOURNAL_ITM')}catch{$journal=$book.Worksheets.Add();$journal.Name='JOURNAL_ITM'}
        $journalLast=Get-ITMInstallerLastCell $journal
        if($journalLast -and $journalLast.Row -ge 2){[void]$journal.Range($journal.Cells.Item(2,1),$journal.Cells.Item($journalLast.Row,9)).ClearContents()}
        $headers=@('Date','Utilisateur','Action','Matricule','Numero','Avant','Apres','Motif','Annee')
        for($i=0;$i -lt $headers.Count;$i++){$journal.Cells.Item(1,$i+1).Value2=$headers[$i]}
        foreach($property in @('ITM_DataSet','ITM_SegmentCount','ITM_AnneeCloturee')){try{$book.CustomDocumentProperties.Item($property).Delete()}catch{}}
        [void]$book.Save();[void]$book.Close($false);$book=$null
        [IO.File]::Move($temp,$final);$published=$true
        Show-Status 'Nouvelle source créée. Application des corrections...' 4
        return $final
    } finally {
        if($book){try{$book.Close($false)}catch{}}
        if($excel){try{$excel.Quit()}catch{};try{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}catch{}}
        [GC]::Collect();[GC]::WaitForPendingFinalizers()
        if(-not $published -and [IO.File]::Exists($temp)){try{[IO.File]::Delete($temp)}catch{}}
    }
}

function Install-ITM([string]$Classeur) {
    $xl = $null; $wb = $null; $folder = $null; $success = $false; $oldAutoFill = $null; $failure = '';$script:installOutputFolder=''
try {
    . (Join-Path $PSScriptRoot 'TestsInstallationSession.ps1')
    Write-ITMInstallLog (Test-ITMInstallSession)
    . (Join-Path $PSScriptRoot 'FonctionsGestion.ps1')
    . (Join-Path $PSScriptRoot 'TestsHistorique.ps1')
    Write-ITMInstallLog (Test-ITMPaymentHistory)
    . (Join-Path $PSScriptRoot 'TestsLocalisation.ps1')
    $locationTest=Test-ITMWorkbookLocation
    Write-ITMInstallLog $locationTest
    Show-Status 'Verification du classeur selectionne...' 5
    $source=(Resolve-Path -LiteralPath $Classeur).Path
    if([IO.Path]::GetExtension($source) -ne '.xlsm') { throw 'Selectionnez un fichier .xlsm.' }
    $lock=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $lock.Dispose()
    $installations=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'data/installations'
    [void][IO.Directory]::CreateDirectory($installations)
    $folder=Join-Path $installations ('Installation_SCOLIA_'+[DateTime]::Now.ToString('yyyyMMdd_HHmmss_fff'))
    [void][IO.Directory]::CreateDirectory($folder)
    $script:installOutputFolder=$folder
    Show-Status 'Creation de la sauvegarde et de la copie de travail...' 8
    $backups=Join-Path $folder 'Sauvegardes'; [void][IO.Directory]::CreateDirectory($backups)
    Copy-Item -LiteralPath $source -Destination (Join-Path $backups 'AVANT_INSTALLATION.xlsm')
    $working=Join-Path $folder 'EN_COURS.xlsm'
    Copy-Item -LiteralPath $source -Destination $working
    Unblock-File -LiteralPath $working
    Show-Status 'Ouverture de la copie dans Excel...' 12
    $existingExcel=@(Get-Process -Name EXCEL -ErrorAction SilentlyContinue | ForEach-Object {$_.Id})
    $xl=New-Object -ComObject Excel.Application
    if(-not (Save-ITMInstallExcelOwner $xl $existingExcel)){
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($xl);$xl=$null
        throw 'Excel ne fournit pas une instance isolée. Fermez Excel puis relancez pour protéger vos autres fichiers.'
    }
    $xl.Visible=$false; $xl.DisplayAlerts=$false; $xl.EnableEvents=$false; $xl.AutomationSecurity=3; $xl.ScreenUpdating=$false; $xl.AskToUpdateLinks=$false
    $oldAutoFill=$xl.AutoCorrect.AutoFillFormulasInLists
    $xl.AutoCorrect.AutoFillFormulasInLists=$false
    $wb=$xl.Workbooks.Open($working,0,$false)
    $xl.Calculation=-4135
    $dataGroup=''
    try{$dataGroup=[string]$wb.CustomDocumentProperties.Item('ITM_DataSet').Value}catch{}
    if($dataGroup){throw 'Ce classeur est un segment de data. Pour cette mise à jour d''interface, ouvrez APPLICATION.vbs et reconnectez la base complète ; ne réinstallez pas un segment isolé.'}
    if($wb.ReadOnly) { throw 'La copie de travail est en lecture seule.' }
    try { $project=$wb.VBProject; $count=$project.VBComponents.Count }
    catch { throw "Excel bloque l'acces au projet VBA. Dans Excel : Fichier > Options > Centre de gestion de la confidentialite > Parametres > Parametres des macros > cocher Acces approuve au modele d'objet du projet VBA. Fermez Excel puis relancez. Aucun parametre de securite n'a ete modifie par cet installateur." }
    if($project.Protection -ne 0) { throw 'Le projet VBA est verrouille. Aucune correction ne peut etre appliquee.' }
    foreach($ref in $project.References) { if($ref.IsBroken) { throw "Reference VBA manquante : $($ref.Name)." } }
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Show-Status 'Controle de compatibilite des modules VBA...' 20
    $plan=New-Object 'System.Collections.Generic.List[object]'
    foreach($item in $manifest) {
        $component=$null
        try { $component=$project.VBComponents.Item($item.name) } catch { }
        $replacement=Get-Content -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'Corrections') $item.file) -Raw -Encoding UTF8
        if((Code-Hash $replacement) -ne $item.after) { throw "Correctif endommage : $($item.name)." }
        if($null -eq $component) {
            if($null -ne $item.before) { throw "Module absent : $($item.name). Le fichier n'est pas compatible." }
        } else {
            $cm=$component.CodeModule
            $existing=if($cm.CountOfLines -gt 0) { $cm.Lines(1,$cm.CountOfLines) } else { '' }
            $hash=Code-Hash $existing
            $previousOK=$false
            foreach($compatibilityPath in (@($item.compatibilityFile)+@($item.compatibilityFiles))) {
                if(-not $compatibilityPath){continue}
                $previousCode=Get-Content -LiteralPath (Join-Path $PSScriptRoot $compatibilityPath) -Raw -Encoding UTF8
                if((Code-Hash $previousCode) -notin @($item.compatible)) { throw "Fichier de compatibilite endommage : $($item.name)." }
                if($hash -ne $item.before -and $hash -ne $item.after -and $hash -notin @($item.compatible) -and -not $previousOK){$previousOK=Same-Code $previousCode $existing}
            }
            if($hash -ne $item.before -and $hash -ne $item.after -and $hash -notin @($item.compatible) -and -not $previousOK -and -not (Same-Code $replacement $existing)) { throw "Le code de $($item.name) a change. Arret pour proteger vos modifications. Utilisez la version du classeur jointe ou faites adapter le correctif a votre nouveau code." }
        }
        $needsUpdate=($null -eq $component -or $hash -ne $item.after)
        $plan.Add(@{item=$item;component=$component;code=$replacement;needsUpdate=$needsUpdate})
    }
    $a=$wb.Worksheets.Item('APPRENANTS'); $p=$wb.Worksheets.Item('PAIEMENTS')
    if($a.ProtectContents -or $p.ProtectContents) { throw 'Les feuilles de donnees sont protegees.' }
    if($a.ListObjects.Count -ne 1 -or $p.ListObjects.Count -ne 1) { throw 'Structure de tableaux non reconnue.' }
    $moveHeader=$false
    if([string]$a.Cells.Item(3,1).Value2 -eq 'Matricule') { $moveHeader=$true }
    elseif([string]$a.Cells.Item(4,1).Value2 -ne 'Matricule') { throw 'Titres des apprenants non reconnus.' }
    if([string]$p.Cells.Item(4,1).Value2 -ne 'ID') { throw 'Titres des paiements non reconnus.' }
    $learnerColumns=[Math]::Max(15,[int]$a.ListObjects.Item(1).ListColumns.Count)
    Show-Status 'Lecture des apprenants et controle des valeurs sources...' 25
    $learners=Snapshot $a $learnerColumns $(if($moveHeader){4}else{5})
    Show-Status 'Lecture des paiements et controle des valeurs sources...' 28
    $payments=Snapshot $p 14
    Show-Status "Donnees trouvees : $($learners.Count) lignes apprenants, $($payments.Count) lignes paiements." 30
    $sumBefore=[decimal]0
    foreach($r in $payments) { if($r.Values[8] -is [ValueType]) { $sumBefore+=[decimal]$r.Values[8] } }
    $issueCount=Create-Audit $learners $payments (Join-Path $folder 'DONNEES_A_VERIFIER.csv')
    Show-Status 'Regroupement des lignes et controle des valeurs conservees...' 40
    Compact-Sheet $a $learnerColumns $learners $moveHeader
    Show-Status 'Regroupement des paiements, sans changer les montants...' 44
    Compact-Sheet $p 14 $payments $false
    Show-Status 'Mise a jour des formules et references...' 47
    Update-References $wb
    $moduleIndex=0
    foreach($entry in $plan) {
        $moduleIndex++
        Show-Status ('Installation du module '+$entry.item.name+'...') (50+[int](20*$moduleIndex/$plan.Count))
        $component=$entry.component
        if(-not $entry.needsUpdate){Write-ITMInstallLog ('Module deja a jour : '+$entry.item.name);continue}
        if($null -eq $component) { $component=$project.VBComponents.Add(1); $component.Name=$entry.item.name }
        $cm=$component.CodeModule
        if($cm.CountOfLines -gt 0) {
            $cm.Lines(1,$cm.CountOfLines) | Set-Content -LiteralPath (Join-Path $backups ($entry.item.name+'.txt')) -Encoding UTF8
            [void]$cm.DeleteLines(1,$cm.CountOfLines)
        }
        $code = $entry.code.TrimStart([char]0xFEFF) -replace '\r?\n', "`r`n"
        [void]$cm.InsertLines(1,$code)
        Verify-Code $component $entry.code $entry.item.name
    }
    Show-Status 'Verification des boutons et du total des paiements...' 75
    $buttonInventory=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'boutons.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $buttonLog=New-Object 'System.Collections.Generic.List[string]'
    foreach($form in $buttonInventory.PSObject.Properties) {
        $component=$project.VBComponents.Item($form.Name)
        foreach($buttonName in $form.Value) {
            $control=$component.Designer.Controls.Item([string]$buttonName)
            if($null -eq $control) { throw "Bouton absent : $($form.Name).$buttonName" }
            $line=$component.CodeModule.ProcStartLine(([string]$buttonName+'_Click'),0)
            if($line -lt 1) { throw "Action absente : $($form.Name).$buttonName" }
            $buttonLog.Add("$($form.Name) : $buttonName -> gestionnaire Click present")
        }
    }
    $buttonLog | Set-Content -LiteralPath (Join-Path $folder 'CONTROLE_BOUTONS.txt') -Encoding UTF8
    Show-Status 'Recalcul Excel et verification du total des paiements...' 77
    $xl.CalculateFullRebuild()
    $xl.Calculation=-4105
    $sumAfter=[decimal]0
    $check=Snapshot $p 14
    foreach($r in $check) { if($r.Values[8] -is [ValueType]) { $sumAfter+=[decimal]$r.Values[8] } }
    if($sumAfter -ne $sumBefore) { throw 'Le total des montants payes a change. Installation annulee.' }
    [void]$wb.Save()
    [void]$wb.Close($false); $wb=$null
    Show-Status 'Reouverture et verification du code enregistre...' 82
    # Verification par Excel du module installe, dans cette instance seulement.
    $xl.AutomationSecurity=1
    $wb=$xl.Workbooks.Open($working,0,$false)
    $xl.AutomationSecurity=3
    foreach($entry in $plan) { Verify-Code ($wb.VBProject.VBComponents.Item($entry.item.name)) $entry.code $entry.item.name }
    Show-Status 'Controles Excel de structure et des exports...' 90
    $verified=$xl.Run("'EN_COURS.xlsm'!ModuleFiabilite.ITMVerifierInstallation")
    if(-not $verified) { throw 'Controle VBA de la structure non valide.' }
    $exportCheck=$xl.Run("'EN_COURS.xlsm'!ModuleExportsFiables.ITMTesterExports")
    Show-Status 'Test des exports a grand volume en memoire...' 92
    $volumeCheck=$xl.Run("'EN_COURS.xlsm'!ModuleExportsFiables.ITMTesterVolumeExports")
    if(-not $volumeCheck) { throw 'Controle des exports a grand volume non valide.' }
    @([string]$exportCheck, 'Test en memoire : 1500 soldes, 1501 versements, 500 soldes par promotion. Aucune fausse donnee ajoutee au classeur.') | Set-Content -LiteralPath (Join-Path $folder 'CONTROLE_EXPORTS.txt') -Encoding UTF8
    Show-Status 'Verification de la passerelle et des controles de saisie...' 94
    $bridgeVersion=Invoke-ITMInstallationCheck 'Verification de la version de la passerelle...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppVersion")}
    if($bridgeVersion -ne 'ITM-DESKTOP-4'){throw 'La passerelle de l''application n''est pas valide.'}
    if($script:extendedChecks){
        Show-Status 'Verification approfondie : lecture de deux classeurs temporaires...' 95
        if(-not (Invoke-ITMInstallationCheck 'Test approfondi : copies Excel et paiements entre classeurs...' {$xl.Run("'EN_COURS.xlsm'!ModuleStockageITM.ITMTesterStockage")})){throw 'Le test des classeurs multiples a échoué.'}
        Show-Status 'Verification approfondie : basculement, sauvegarde et restauration de copies temporaires...' 95
        if(-not (& (Join-Path $PSScriptRoot 'TestsStockage.ps1') -Excel $xl -Reference $wb -Progress {param($message) Show-Status $message})){throw 'Le test natif du basculement et de la récupération a échoué.'}
    }else{Write-ITMInstallLog 'Tests approfondis de copies Excel, basculement, sauvegarde et restauration non demandes ; controles des donnees et exports maintenus.'}

    . (Join-Path $PSScriptRoot 'Ergonomie.ps1')
    if(-not (Invoke-ITMInstallationCheck 'Verification de la saisie automatique des dates...' {Test-ITMDateInput})){throw 'Le test de saisie des dates a échoué.'}
    $script:inputHelp.Dispose()
    if($script:extendedChecks){
        Show-Status 'Verification approfondie : lecture complete via la passerelle...' 96
        $snapshotJSON=[string]$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppSnapshot")
        $snapshotObject=$snapshotJSON | ConvertFrom-Json
        if(@($snapshotObject.learners).Count -ne $learners.Count -or @($snapshotObject.payments).Count -ne $payments.Count){throw 'La passerelle ne relit pas toutes les données.'}
        $administrationJSON=[string]$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppAdministration")
        $administrationObject=$administrationJSON | ConvertFrom-Json
    }
    $invalidDossier=[string](Invoke-ITMInstallationCheck 'Controle : inscription vide refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppDossier", '', '', '', '', '', '', '', '', '', '', '', '', '', '')})
    $invalidPayment=[string](Invoke-ITMInstallationCheck 'Controle : paiement sans apprenant refuse...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppPaiement", '', 0)})
    $dossierRejected=$invalidDossier | ConvertFrom-Json
    $paymentRejected=$invalidPayment | ConvertFrom-Json
    if($dossierRejected.ok -or $paymentRejected.ok){throw 'Les contrôles de saisie de la passerelle ne rejettent pas une saisie vide.'}
    $futureISO=(Get-Date).AddDays(1).ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $pastISO=(Get-Date).AddDays(-1).ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $invalidDate=[string](Invoke-ITMInstallationCheck 'Controle : date de recu impossible refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppPaiement", '', 0, '2026-02-30')})
    $futureDate=[string](Invoke-ITMInstallationCheck 'Controle : date de recu future refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppPaiement", '', 0, $futureISO)})
    $pastDate=[string](Invoke-ITMInstallationCheck 'Controle : date anterieure et absence d apprenant...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppPaiement", '', 0, $pastISO)})
    $invalidDateResult=$invalidDate | ConvertFrom-Json;$futureDateResult=$futureDate | ConvertFrom-Json;$pastDateResult=$pastDate | ConvertFrom-Json
    if($invalidDateResult.ok -or $futureDateResult.ok -or $pastDateResult.ok -or $invalidDateResult.error -notmatch 'date' -or $futureDateResult.error -notmatch 'date' -or $pastDateResult.error -notmatch 'Apprenant'){throw 'Le contrôle des dates de reçu a échoué. Aucune saisie de test valide ne doit être ajoutée.'}
    @('Passerelle ITM-DESKTOP-4 reconnue.', ('Lecture complete JSON approfondie demandee : '+$script:extendedChecks), 'Inscription vide et paiement sans apprenant rejetés sans écriture.') | Set-Content -LiteralPath (Join-Path $folder 'CONTROLE_APPLICATION.txt') -Encoding UTF8
    $invalidCorrection=[string](Invoke-ITMInstallationCheck 'Controle : correction de paiement invalide refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppCorrectionPaiement", '', 0, '', $false)})
    $invalidPromotion=[string](Invoke-ITMInstallationCheck 'Controle : promotion invalide refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppPromotion", '', '', '', 0, 0, $false)})
    $invalidClosure=[string](Invoke-ITMInstallationCheck 'Controle : cloture invalide refusee...' {$xl.Run("'EN_COURS.xlsm'!ModuleApplicationITM.ITMAppCloture", '', '')})
    foreach($reply in @($invalidCorrection,$invalidPromotion,$invalidClosure)){
        if(($reply | ConvertFrom-Json).ok){throw 'Une operation de gestion invalide a ete acceptee.'}
    }
    @(('Lecture approfondie de l''administration demandee : '+$script:extendedChecks),'Correction, promotion et cloture invalides refusees sans ecriture.') | Set-Content -LiteralPath (Join-Path $folder 'CONTROLE_GESTION_AVANCEE.txt') -Encoding UTF8
    Show-Status 'Verification des interfaces Excel...' 97
    $uiCheck=Invoke-ITMInstallationCheck 'Verification des quatre formulaires Excel...' {$xl.Run("'EN_COURS.xlsm'!ModuleInterfacePremium.ITMVerifierInterface")}
    [string]$uiCheck | Set-Content -LiteralPath (Join-Path $folder 'CONTROLE_INTERFACES.txt') -Encoding UTF8
    Show-Status 'Dernier controle des apprenants et paiements conserves...' 98
    $finalLearners=Snapshot ($wb.Worksheets.Item('APPRENANTS')) $learnerColumns
    $finalPayments=Snapshot ($wb.Worksheets.Item('PAIEMENTS')) 14
    foreach($pair in @(@{before=$learners;after=$finalLearners},@{before=$payments;after=$finalPayments})) {
        if($pair.before.Count -ne $pair.after.Count) { throw 'Les donnees ont change pendant les controles des interfaces.' }
        for($j=0;$j -lt $pair.before.Count;$j++) {
            if($pair.before[$j].Signature -ne $pair.after[$j].Signature) { throw 'Une valeur a change pendant les controles des interfaces.' }
        }
    }
    Show-Status 'Finalisation du classeur verifie...' 99
    [void]$wb.Close($false); $wb=$null
    Assert-ITMInstallParent
    $final=Join-Path $folder 'SYSTEME_SECRETARIAT_CORRIGE.xlsm'
    Move-Item -LiteralPath $working -Destination $final
    @("Installation terminee.","Apprenants conserves : $($learners.Count)","Paiements conserves : $($payments.Count)","Total des montants deja renseignes conserve : $sumAfter FC", "Anomalies de saisie a verifier : $issueCount",'Premiere ligne de donnees : 5 dans les deux feuilles.', 'Aucun mois ni montant manquant n a ete invente.', 'Le fichier source est reste intact.', "Classeur a utiliser : $final") | Set-Content -LiteralPath (Join-Path $folder 'RESULTAT_INSTALLATION.txt') -Encoding UTF8
    $success=$true
    $script:finalPath=$final
    [void](Set-ITMLocationPreference 'SourceInstallation' ([IO.Path]::GetFullPath($Classeur)))
    $memorized=Set-ITMLocationPreference 'Classeur' $final
    [void](Set-ITMLocationPreference 'ConnexionAutomatique' 'oui')
    if(-not $memorized){Write-ITMInstallLog "Le choix n'a pas pu être mémorisé dans Windows. Le fichier corrigé sera retrouvé dans data/installations."} 
    Show-Status ('Installation reussie. '+$learners.Count+' lignes apprenants et '+$payments.Count+' lignes paiements conservees.') 100
} catch {
    $message=$_.Exception.Message
    $failure=$message
    Show-Status ('Installation interrompue : '+$message)
    if($folder) { ($message+"`r`n"+$_.ScriptStackTrace+"`r`n"+$_.Exception.ToString()) | Set-Content -LiteralPath (Join-Path $folder 'ERREUR_INSTALLATION.txt') -Encoding UTF8 }
} finally {
    if($null -ne $wb) { try { [void]$wb.Close($false) } catch { } }
    if($null -ne $xl) {
        try { if($null -ne $oldAutoFill) { $xl.AutoCorrect.AutoFillFormulasInLists=$oldAutoFill }; $xl.AutomationSecurity=3; $xl.Quit() } catch { }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($xl)
    }
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    if(-not $success -and $folder) {
        $incomplete=Join-Path $folder 'EN_COURS.xlsm'
        if(Test-Path -LiteralPath $incomplete) { Remove-Item -LiteralPath $incomplete -Force -ErrorAction SilentlyContinue }
    }
    if($folder) { try { Copy-ITMInstallLog (Join-Path $folder 'JOURNAL_INSTALLATION.txt') } catch { } }
}

    Write-ITMInstallResult ([pscustomobject]@{Success=$success;Final=$(if($success){$final}else{''});Folder=$folder;Error=$failure})
}

# Le traitement Excel vit dans un processus caché ; cette branche ne construit aucune fenêtre.
if($Session){
    Initialize-ITMInstallWorker $Session
    try{
        $workerSource=[string]$script:installRequest.Source
        if($script:installRequest.PSObject.Properties['NewSource'] -and [bool]$script:installRequest.NewSource){$workerSource=New-ITMInstallerBlankSource}
        Install-ITM $workerSource
    }
    catch{Write-ITMInstallResult ([pscustomobject]@{Success=$false;Final='';Folder='';Error=$_.Exception.Message})}
    exit
}

if(-not ('ITMLoginForm' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing') -IgnoreWarnings -ErrorAction Stop}
[Windows.Forms.Application]::EnableVisualStyles()
$script:form = New-Object Windows.Forms.Form;Set-ITMWindowIcon $script:form
$form.Text = 'SCOLIA | Installation des corrections'
$form.Size = New-Object Drawing.Size(760,560)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'Sizable'
$form.MinimumSize = New-Object Drawing.Size(540,460)
$form.MaximizeBox = $true
$form.Font = New-Object Drawing.Font('Segoe UI',10)
$form.BackColor = [Drawing.Color]::FromArgb(245,245,245)
$title = New-Object ITMReadableLabel
$title.Text = 'SCOLIA'
$title.Location = New-Object Drawing.Point(24,20)
$title.Size = New-Object Drawing.Size(700,34)
$title.Font = New-Object Drawing.Font('Segoe UI',18,[Drawing.FontStyle]::Bold)
$title.ForeColor = [Drawing.Color]::FromArgb(24,64,120)
$form.Controls.Add($title)
$description = New-Object ITMReadableLabel
$description.Text = "Préparation de la base SCOLIA : données, paiements, tableau de bord et modules.`r`nChoisissez un classeur existant ou créez une nouvelle source. Fermez Excel avant le traitement."
$description.Location = New-Object Drawing.Point(24,66)
$description.Size = New-Object Drawing.Size(700,52)
$form.Controls.Add($description)
$script:pathBox = New-Object Windows.Forms.TextBox
$pathBox.Location = New-Object Drawing.Point(24,128)
$pathBox.Size = New-Object Drawing.Size(414,30)
$pathBox.ReadOnly = $true
$form.Controls.Add($pathBox)
$browse = New-Object ITMLoginButton
$browse.SquareCorners=$true
$browse.Text = 'Choisir...'
$browse.Location = New-Object Drawing.Point(450,126)
$browse.Size = New-Object Drawing.Size(126,32)
$form.Controls.Add($browse)
$script:newSourceButton = New-Object ITMLoginButton
$newSourceButton.SquareCorners=$true
$newSourceButton.Text = 'Nouvelle source'
$newSourceButton.Location = New-Object Drawing.Point(588,126)
$newSourceButton.Size = New-Object Drawing.Size(128,32)
$newSourceButton.Primary=$true
$form.Controls.Add($newSourceButton)
$script:status = New-Object ITMReadableLabel
$status.Text = 'Choisissez un classeur existant ou créez une nouvelle source. Le fichier original reste intact.'
$status.Location = New-Object Drawing.Point(24,176)
$status.Size = New-Object Drawing.Size(692,42)
$form.Controls.Add($status)
$script:progress = New-Object ScoliaProgressBar
$progress.Location = New-Object Drawing.Point(24,222)
$progress.Size = New-Object Drawing.Size(692,18)
$form.Controls.Add($progress)
$script:log = New-Object Windows.Forms.TextBox
$log.Location = New-Object Drawing.Point(24,288)
$log.Size = New-Object Drawing.Size(692,142)
$log.Multiline = $true
$log.ReadOnly = $true
$log.ScrollBars = 'Vertical'
$form.Controls.Add($log)
$script:extended=New-Object ITMReadableCheckBox
$extended.Text='Vérification approfondie (basculement, sauvegarde et restauration ; plus longue)'
$extended.Location=New-Object Drawing.Point(24,250)
$extended.Size=New-Object Drawing.Size(692,30)
$extended.Checked=$false
$form.Controls.Add($extended)
$script:openButton = New-Object ITMLoginButton
$openButton.SquareCorners=$true
$openButton.Text = 'Voir le fichier corrige'
$openButton.Location = New-Object Drawing.Point(24,450)
$openButton.Size = New-Object Drawing.Size(200,42)
$openButton.Enabled = $false
$form.Controls.Add($openButton)
$script:installButton = New-Object ITMLoginButton
$installButton.SquareCorners=$true
$installButton.Text = 'Installer les corrections'
$installButton.Primary = $true
$installButton.Location = New-Object Drawing.Point(434,450)
$installButton.Size = New-Object Drawing.Size(282,42)
$installButton.BackColor = [Drawing.Color]::FromArgb(24,64,120)
$installButton.ForeColor = [Drawing.Color]::White
$installButton.FlatStyle = 'Flat'
$installButton.Enabled = $false
$form.Controls.Add($installButton)
$script:busy = $false
$script:finalPath = ''
$browse.Add_Click({
    $dialog = New-Object Windows.Forms.OpenFileDialog
    try {
        $dialog.Title = 'Selectionnez votre fichier ACTUEL avec les dernieres saisies'
        $dialog.Filter = 'Classeur Excel avec macros (*.xlsm)|*.xlsm'
        $location=Get-ITMWorkbookLocation -Purpose Installation -Explicit $pathBox.Text
        Set-ITMWorkbookDialogLocation $dialog $location
        if($dialog.ShowDialog($form) -eq [Windows.Forms.DialogResult]::OK) {
            Set-ITMInstallerLocation (Get-ITMWorkbookLocation -Purpose Installation -Explicit $dialog.FileName)
        }
    } finally { $dialog.Dispose() }
})
$newSourceButton.Add_Click({
    if($script:busy){return}
    try{Start-ITMInstallSession '' $extended.Checked $true}catch{[void][ITMErrorDialog]::Show($form,$_.Exception.Message,'Nouvelle source')}
})
$installButton.Add_Click({
    if($script:busy){return}
    try{Start-ITMInstallSession $pathBox.Text $extended.Checked}catch{
        [void][ITMErrorDialog]::Show($form,$_.Exception.Message,'Installation')
    }
})
$openButton.Add_Click({
    if($script:finalPath -and (Test-Path -LiteralPath $script:finalPath)) {
        Start-Process explorer.exe -ArgumentList ('/select,"'+$script:finalPath+'"')
    }elseif($script:diagnosticFolder -and [IO.Directory]::Exists($script:diagnosticFolder)){
        Start-Process explorer.exe -ArgumentList ('"'+$script:diagnosticFolder+'"')
    }
})
$form.Add_FormClosing({param($sender,$eventArgs)
    if($script:busy){
        $eventArgs.Cancel=$true
        if([ITMMessageBox]::Show($form,'Arrêter l''installation ? Le fichier source et sa sauvegarde seront conservés.','Installation en cours','YesNo','Question') -eq 'Yes'){Stop-ITMInstallSession 'Installation arretee a votre demande.'}
    }
})
function Set-ITMInstallerLocation($location) {
    $script:installerLocation=$location
    $pathBox.Text=$location.Path
    $installButton.Enabled=$location.Installable
    if($script:sourceFolder){$script:sourceFolder.Enabled=[bool]$location.Directory}
    if($location.Managed){$status.Text='Base data détectée. Elle est déjà gérée par SCOLIA et ne doit pas être réinstallée comme source.'}
    elseif($location.Path -and -not $location.Exists){$status.Text='Classeur mémorisé indisponible. Rebranchez son support ou cliquez sur Choisir.'}
    else{$status.Text=$location.Reason}
}
$script:sourceFolder=New-Object ITMLoginButton
$sourceFolder.SquareCorners=$true
$sourceFolder.Text='Dossier source'
$sourceFolder.Location=New-Object Drawing.Point(236,450)
$sourceFolder.Size=New-Object Drawing.Size(186,42)
$form.Controls.Add($sourceFolder)
$sourceFolder.Add_Click({
    if($script:busy){
        if([ITMMessageBox]::Show($form,'Arrêter l''installation ? Votre fichier source reste intact.','Arrêter','YesNo','Question') -eq 'Yes'){Stop-ITMInstallSession 'Installation arretee a votre demande.'}
    }else{try{Open-ITMWorkbookFolder $script:installerLocation}catch{[void][ITMMessageBox]::Show($form,$_.Exception.Message,'Dossier du classeur','OK','Information')}}
})
$script:installTimer=New-Object Windows.Forms.Timer
$installTimer.Interval=500
$installTimer.Add_Tick({try{Update-ITMInstallSession}catch{Stop-ITMInstallSession ('Erreur du suivi : '+$_.Exception.Message)}})
Set-ITMInstallerLocation (Get-ITMWorkbookLocation -Purpose Installation -Explicit $Classeur)
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
Enable-ITMDialogResponsive $form
$installerHelp=New-Object Windows.Forms.ToolTip;$installerHelp.InitialDelay=450;$installerHelp.AutoPopDelay=12000
$installerHelp.SetToolTip($browse,"Choisit un classeur existant à préparer.`r`nRaccourci : Ctrl+O")
$installerHelp.SetToolTip($newSourceButton,"Crée un nouveau classeur vide puis applique automatiquement les corrections.`r`nRaccourci : Ctrl+N")
$installerHelp.SetToolTip($installButton,"Installe les corrections dans une copie contrôlée du classeur.`r`nRaccourci : Ctrl+Enter")
$installerHelp.SetToolTip($extended,"Active les essais de basculement, sauvegarde et restauration, plus longs.`r`nRaccourci : Alt+V")
$form.KeyPreview=$true
$form.Add_KeyDown({param($sender,$e)
    $key=[int]$e.KeyData;$button=$null;$recognized=$true
    if($key -eq ([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::O)){$button=$browse}
    elseif($key -eq ([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::N)){$button=$newSourceButton}
    elseif($key -eq ([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::Enter)){$button=$installButton}
    elseif($key -eq ([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::L)){$button=$openButton}
    elseif($key -eq ([int][Windows.Forms.Keys]::Control -bor [int][Windows.Forms.Keys]::D)){if(-not $script:busy){$button=$sourceFolder}}
    elseif($key -eq [int][Windows.Forms.Keys]::Escape){if($script:busy){$button=$sourceFolder}else{$recognized=$false}}
    elseif($key -eq ([int][Windows.Forms.Keys]::Alt -bor [int][Windows.Forms.Keys]::V)){if(-not $script:busy -and $extended.Enabled){$extended.Checked=-not $extended.Checked}}
    else{$recognized=$false}
    if($recognized){$e.Handled=$true;$e.SuppressKeyPress=$true;if($button -and $button.Enabled){$button.PerformClick()}}
})
$installTimer.Add_Tick({
    if($script:lastInstallerBusy -ne $script:busy){
        $script:lastInstallerBusy=$script:busy
        $installerHelp.SetToolTip($sourceFolder,$(if($script:busy){"Demande l'arrêt de l'installation sans modifier le fichier source.`r`nRaccourci : Escape"}else{"Ouvre le dossier du classeur source détecté.`r`nRaccourci : Ctrl+D"}))
    }
    $helpMode=($openButton.Text -like '*journal*')
    if($null -eq $script:lastInstallerHelpMode -or $script:lastInstallerHelpMode -ne $helpMode){
        $script:lastInstallerHelpMode=$helpMode
        $installerHelp.SetToolTip($openButton,$(if($helpMode){"Ouvre le journal de l'installation pour examiner l'erreur.`r`nRaccourci : Ctrl+L"}else{"Ouvre le fichier corrigé après une installation réussie.`r`nRaccourci : Ctrl+L"}))
    }
})
$installerHelp.SetToolTip($sourceFolder,"Ouvre le dossier du classeur source détecté.`r`nRaccourci : Ctrl+D")
$installerHelp.SetToolTip($openButton,"Ouvre le fichier corrigé après une installation réussie.`r`nRaccourci : Ctrl+L")
$form.Add_Disposed({$installerHelp.Dispose()})
$form.Add_Shown({if(Get-Command Close-ScoliaTransition -ErrorAction SilentlyContinue){Close-ScoliaTransition}})
[void]$form.ShowDialog()
$installTimer.Stop();$installTimer.Dispose()
$form.Dispose()
foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}
