if(-not (Test-Path function:Import-ITMCompiledSource)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
. (Join-Path $PSScriptRoot 'GrillesParametrables.ps1')
# Grade import works on a new XLSX copy. Original templates and school data stay intact.
if(-not ('ITMGradeMatcher' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'CotesMatching.cs')}
if(-not ('ITMGradeExcel' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'CotesExcel.cs')}
function Get-ITMGradesFolder {
    $dir=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'scolarite\deliberations';[void][IO.Directory]::CreateDirectory($dir);return $dir
}
function Open-ITMGradeExcel {
    $excel=[ITMGradeExcel]::Create();try{$excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3;return ,$excel}catch{try{$excel.Quit()}catch{};$excel.Dispose();throw}
}

function Get-ITMGridLayout($sheet,[string]$promotion='',$override=$null) {
    $code=[ITMGradeMatcher]::Key([string]$sheet.Name).Replace(' ','')
    $spec=@{INF1=@(12,59,2,9,11,60);INF2=@(34,65,3,31,33,23);ACC1=@(12,51,2,9,11,63);ACC2=@(14,31,2,11,13,24)}
    $map=$(if($override){$override}elseif($promotion){Get-ITMGridMap $promotion}else{Get-ITMGridMap $code})
    if($map){Test-ITMGridMap $map;if($sheet.Name -cne $map.Sheet){throw 'Nom de feuille différent du paramétrage.'};$code=Promo $map.Promotion;$v=@([int]$map.FirstRow,[int]$map.LastRow,[int]$map.NameColumn,[int]$map.CourseRow,[int]$map.MaximumRow,[int]$map.LastColumn)}elseif($spec.ContainsKey($code)){$v=$spec[$code]}else{throw 'Cette grille doit être paramétrée : cliquez Paramétrer la grille.'}
    # Validate the name header at its actual location (merged headings may start higher).
    $heading=[string]$sheet.Cells.Item($(if($map){[int]$map.NameHeaderRow}elseif($code -eq 'INF2'){28}elseif($code -eq 'ACC2'){8}else{6}),$v[2]).Text
    if(($map -and [ITMGradeMatcher]::Key($heading) -notlike '*NOM*') -or (-not $map -and [ITMGradeMatcher]::Key($heading) -notlike '*NOMS*POSTNOMS*')){throw 'La structure de la feuille diffère du modèle fourni.'}
    $last=$v[1];$bottom=[int]$sheet.UsedRange.Row+[int]$sheet.UsedRange.Rows.Count-1
    for($r=$v[1]+1;$r -le [Math]::Min($bottom,50000);$r++){
        $number=$sheet.Cells.Item($r,$v[2]-1).Value2;$name=[string]$sheet.Cells.Item($r,$v[2]).Text
        if($null -eq $number -or [string]::IsNullOrWhiteSpace($name)){break}
        if($map){$sequence=[decimal]0;if(-not [decimal]::TryParse([string]$number,[Globalization.NumberStyles]::Number,[Globalization.CultureInfo]::InvariantCulture,[ref]$sequence) -or $sequence -ne ($r-$v[0]+1)){break}}
        $last=$r
    }
    $courses=@();$calculated=@{}
    for($col=$v[2]+1;$col -lt $v[5];$col++){
        $maximum=$sheet.Cells.Item($v[4],$col)
        if($maximum.HasFormula){$calculated[$col]=[string]$maximum.FormulaR1C1;continue}
        if($null -eq $maximum.Value2 -or [double]$maximum.Value2 -le 0){continue}
        $parts=@();foreach($r in @(@(($v[3]-1),$v[3],($v[3]+1))|Where-Object {$_ -ge 1 -and $_ -lt $v[0]})){
            $cell=$sheet.Cells.Item($r,$col);$t=[string]$(if($cell.MergeCells){$cell.MergeArea.Cells.Item(1,1).Text}else{$cell.Text})
            if($t.Trim() -and $parts -notcontains $t.Trim()){$parts+=$t.Trim()}
        }
        $courses+=[pscustomobject]@{Column=$col;Title=($parts -join ' / ');Maximum=[decimal]$maximum.Value2}
    }
    [pscustomobject]@{Code=$code;Sheet=[string]$sheet.Name;FirstRow=$v[0];LastRow=$last;NameColumn=$v[2];CourseRow=$v[3];MaximumRow=$v[4];LastColumn=$v[5];Courses=$courses;Calculated=$calculated}
}
function Read-ITMGridLayout([string]$path,[string]$promotion){
    $excel=$null;$book=$null
    try{$excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($path,0,$true);$sheet=Get-ITMGridSheet $book $promotion;return Get-ITMGridLayout $sheet $promotion}
    finally{if($book){try{$book.Close($false)}catch{}};if($excel){try{$excel.Quit()}catch{};$excel.Dispose()}}
}
function Get-ITMRowDecision($sheet,$layout,[int]$row){
    $values=[object[]]@(foreach($course in $layout.Courses){$value=$sheet.Cells.Item($row,$course.Column).Value2;,$value})
    $maxima=[decimal[]]@($layout.Courses | ForEach-Object {$_.Maximum})
    try{return [ITMDeliberation]::Evaluate($values,$maxima)}catch{throw ('Délibération impossible à la ligne '+$row+' : '+$_.Exception.Message)}
}
function Get-ITMDecisionFormula($layout,[int]$row){
    $refs=@();$fails=@()
    foreach($course in $layout.Courses){
        $number=[int]$course.Column;$letters='';while($number -gt 0){$number--;$letters=[char](65+$number%26)+$letters;$number=[int][Math]::Floor($number/26)}
        $ref=$letters+$row;$refs+=$ref;$minimum=([decimal]$course.Maximum*[decimal]0.5).ToString('G29',[Globalization.CultureInfo]::InvariantCulture);$fails+=($ref+'<'+$minimum)
    }
    # SUM of input courses matches the unrounded weighted total; absent courses keep their maxima.
    $maximumValue=[decimal]0;foreach($course in $layout.Courses){$maximumValue+=[decimal]$course.Maximum};$maximum=$maximumValue.ToString([Globalization.CultureInfo]::InvariantCulture)
    return '=IF(COUNT('+($refs -join ',')+')<'+$refs.Count+',IF(SUM('+($refs -join ',')+')*100/'+$maximum+'>=50,"AA","D"),IF(OR('+($fails -join ',')+'),"A","P"))'
}
function Set-ITMGridDecisions($sheet,$layout){
    $decisions=@()
    for($row=$layout.FirstRow;$row -le $layout.LastRow;$row++){
        $name=[string]$sheet.Cells.Item($row,$layout.NameColumn).Text;if(-not $name.Trim()){continue}
        $result=Get-ITMRowDecision $sheet $layout $row
        foreach($course in $layout.Courses){
            $cell=$sheet.Cells.Item($row,$course.Column);$value=$cell.Value2
            if($value -is [string] -and -not [string]::IsNullOrWhiteSpace($value)){
                if($cell.HasFormula){throw ('La cote calculée de la ligne '+$row+' renvoie du texte. Corrigez cette formule pour obtenir un nombre.')}
                $single=[ITMDeliberation]::Evaluate([object[]]@($value),[decimal[]]@($course.Maximum));$cell.Value2=[double]$single.Total
            }
        }
        $sheet.Cells.Item($row,$layout.LastColumn).Formula=Get-ITMDecisionFormula $layout $row
        $decisions+=[pscustomobject]@{Ligne=$row;Nom=$name;Total=$result.Total;Pourcentage=$result.Percentage;CotesManquantes=$result.Missing;Echecs=$result.Failed;Decision=$result.Decision}
    }
    return $decisions
}
function Test-ITMGridDecisions($sheet,$layout,$rows){
    foreach($row in $rows){if([string]$sheet.Cells.Item($row.Ligne,$layout.LastColumn).Value2 -ne $row.Decision){throw ('Décision Excel différente du contrôle à la ligne '+$row.Ligne)}}
}

function Sort-ITMGridLearners($sheet,$layout){
    $names=[string[]]@(foreach($a in @($script:data.learners | Where-Object {(Promo $_.Promotion) -eq (Promo $layout.Code)} | Sort-Object -Culture 'fr-FR' -Property @{Expression={[ITMGradeMatcher]::Key([string]$_.Nom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.PostNom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.Prenom)}})){
        ([string]$a.Nom+' '+[string]$a.PostNom+' '+[string]$a.Prenom).Trim()
    })
    # A single matrix read/write moves identities, every grade and relative formulas together.
    $range=$sheet.Range($sheet.Cells.Item($layout.FirstRow,$layout.NameColumn-1),$sheet.Cells.Item($layout.LastRow,$layout.LastColumn))
    $order=[ITMGridAlphabetical]::Sort([Array]$range.FormulaValues,$names)
    if($order.Changed){$range.FormulaValues=$order.Matrix}
    for($i=0;$i -lt $order.Names.Length;$i++){
        $row=$layout.FirstRow+$i
        if([string]$sheet.Cells.Item($row,$layout.NameColumn).Text -ne $order.Names[$i]){throw 'Le classement alphabétique de la grille ne correspond pas au contrôle.'}
        foreach($col in $layout.Calculated.Keys){$sheet.Cells.Item($row,[int]$col).FormulaR1C1=$layout.Calculated[$col]}
    }
}

function New-ITMPromotionGrid([string]$path,[string]$promotion){
    Assert-ITMPermission 'Administrer'
    if(-not $script:book){throw 'Connectez la base des apprenants.'}
    $learners=@($script:data.learners | Where-Object {(Promo $_.Promotion) -eq (Promo $promotion) -and (Key $_.Statut) -notin @('SORTI','SORTIE','SUSPENDU','SUSPENDUE')} | Sort-Object -Culture 'fr-FR' -Property @{Expression={[ITMGradeMatcher]::Key([string]$_.Nom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.PostNom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.Prenom)}})
    if(-not $learners.Count){throw 'Aucun apprenant actif dans cette promotion.'}
    $id=[Guid]::NewGuid().ToString('N');$dir=Get-ITMGradesFolder;$temp=Join-Path $dir ($id+'.tmp.xlsx');$final=Join-Path $dir ('Grille_'+(Promo $promotion)+'_'+$id.Substring(0,8)+'.xlsx');$excel=$null;$book=$null
    try{
        [IO.File]::Copy($path,$temp,$false);$excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($temp,0,$false);if($book.ReadOnly){throw 'Copie de grille en lecture seule.'}
        $sheet=Get-ITMGridSheet $book $promotion;$layout=Get-ITMGridLayout $sheet $promotion
        $capacity=$layout.LastRow-$layout.FirstRow+1
        if($learners.Count -gt $capacity){
            $extra=$learners.Count-$capacity;[void]$sheet.Rows.Item(($layout.LastRow+1).ToString()+':'+($layout.LastRow+$extra).ToString()).Insert();$layout.LastRow+=$extra
        }
        for($row=$layout.FirstRow;$row -le $layout.LastRow;$row++){
            if($row -ge $layout.FirstRow+$capacity){[void]$sheet.Rows.Item($layout.FirstRow).Copy($sheet.Rows.Item($row))}
            [void]$sheet.Range($sheet.Cells.Item($row,$layout.NameColumn-1),$sheet.Cells.Item($row,$layout.LastColumn)).ClearContents()
            $i=$row-$layout.FirstRow;if($i -ge $learners.Count){continue}
            $a=$learners[$i];$sheet.Cells.Item($row,$layout.NameColumn-1).Value2=[double]($i+1);$sheet.Cells.Item($row,$layout.NameColumn).Value2=([string]$a.Nom+' '+[string]$a.PostNom+' '+[string]$a.Prenom).Trim()
            foreach($col in $layout.Calculated.Keys){$sheet.Cells.Item($row,[int]$col).FormulaR1C1=$layout.Calculated[$col]}
        }
        # Only the selected promotion is published: historical grades from other sheets are excluded.
        for($i=$book.Worksheets.Count;$i -ge 1;$i--){if($book.Worksheets.Item($i).Name -ne $sheet.Name){[void]$book.Worksheets.Item($i).Delete()}}
        Sort-ITMGridLearners $sheet $layout;$decisions=@(Set-ITMGridDecisions $sheet $layout);[void]$excel.CalculateFull();Test-ITMGridDecisions $sheet $layout $decisions;if(Test-Path function:Set-ScoliaWorkbookPersonnel){Set-ScoliaWorkbookPersonnel $book};[void]$book.Save();$book.Close($false);$book=$null;[IO.File]::Move($temp,$final);return $final
    }finally{if($book){try{$book.Close($false)}catch{}};if($excel){try{$excel.Quit()}catch{};$excel.Dispose()};if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}

function Read-ITMGradePlan($config){
    if(-not $script:book -or $script:payNeedsRefresh){throw 'Connectez et actualisez la base des apprenants.'}
    foreach($path in @($config.Grid,$config.Notes)){if([IO.Path]::GetExtension($path) -ne '.xlsx' -or -not [IO.File]::Exists($path)){throw 'Choisissez une grille et un fichier de cotes XLSX existants.'}}
    if($config.NotesNameColumn -eq $config.NotesGradeColumn){throw 'Les colonnes du nom et de la cote doivent être différentes.'}
    if([decimal]$config.Maximum -le 0){throw 'Barème source strictement positif requis.'}
    if(-not $config.Promotion -or -not $config.Course){throw 'Choisissez la promotion et renseignez le cours.'}
    foreach($key in @('FirstRow','LastRow','NameColumn','CourseRow','NotesFirstRow','NotesNameColumn','NotesGradeColumn')){if([int]$config.$key -lt 1){throw ('Précisez le repère : '+$key)}}
    if($config.LastRow -lt $config.FirstRow -or $config.LastRow -gt 50000 -or $config.NameColumn -gt 200 -or $config.CourseRow -gt 50000){throw 'Repères de grille incorrects.'}
    $learners=[ITMGradeLearner[]]@(foreach($a in @($script:data.learners | Where-Object {(Promo $_.Promotion) -eq (Promo $config.Promotion) -and (Key $_.Statut) -notin @('SORTI','SORTIE','SUSPENDU','SUSPENDUE')} | Sort-Object -Culture 'fr-FR' -Property @{Expression={[ITMGradeMatcher]::Key([string]$_.Nom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.PostNom)}},@{Expression={[ITMGradeMatcher]::Key([string]$_.Prenom)}})){
        $item=New-Object ITMGradeLearner;$item.Id=[string]$a.Matricule;$item.Name=([string]$a.Nom+' '+[string]$a.PostNom+' '+[string]$a.Prenom).Trim();$item
    })
    if(-not $learners.Count){throw 'Aucun apprenant actif dans cette promotion.'}
    $excel=$null;$grid=$null;$notes=$null
    try{
        $gridHash=(Get-FileHash -LiteralPath $config.Grid -Algorithm SHA256).Hash;$notesHash=(Get-FileHash -LiteralPath $config.Notes -Algorithm SHA256).Hash
        $excel=Open-ITMGradeExcel;$grid=$excel.Workbooks.Open($config.Grid,0,$true)
        $sheet=$(if($config.Sheet){$grid.Worksheets.Item($config.Sheet)}else{Get-ITMGridSheet $grid $config.Promotion})
        $layout=Get-ITMGridLayout $sheet $config.Promotion;Assert-ITMGradeSelection $layout $config
        $config.NameColumn=$layout.NameColumn;$config.FirstRow=$layout.FirstRow;$config.CourseRow=$layout.CourseRow
        $config.LastRow=$layout.LastRow
        $column=[int]$config.CourseColumn
        if($column -le 0){
            $matches=@(for($c=1;$c -le 200;$c++){$cell=$sheet.Cells.Item($config.CourseRow,$c);if([ITMGradeMatcher]::Key([string]$cell.Text) -eq [ITMGradeMatcher]::Key($config.Course)){if($cell.MergeCells -and $cell.MergeArea.Columns.Count -gt 1){throw 'Le cours possède plusieurs sous-colonnes. Précisez la colonne exacte de la cote.'};$c}})
            if($matches.Count -ne 1){throw 'Le cours ne correspond pas à une colonne unique. Vérifiez son intitulé, la ligne des cours et la colonne cible.'};$column=[int]$matches[0]
        }
        $course=@($layout.Courses | Where-Object {$_.Column -eq $column});if($course.Count -ne 1){throw 'Choisissez une cellule de saisie de cours, pas un total ni une décision.'}
        if($column -gt 200 -or $column -eq $config.NameColumn){throw 'Colonne de cote invalide ou identique à celle des noms.'}
        $capacity=$config.LastRow-$config.FirstRow+1;if($capacity -lt $learners.Count){throw 'La plage de la grille est trop courte pour cette promotion.'}
        $existing=@{};$nonEmpty=0
        for($row=$config.FirstRow;$row -le $config.LastRow;$row++){
            $key=[ITMGradeMatcher]::Key([string]$sheet.Cells.Item($row,$config.NameColumn).Text)
            if($key){$nonEmpty++;if($existing.ContainsKey($key)){throw 'La grille contient des noms complets répétés.'};$existing[$key]=$row}
        }
        $mapping=@{}
        if($nonEmpty){
            if($nonEmpty -ne $learners.Count){throw 'La liste de la grille diffère de la promotion. Utilisez une grille vide ou la grille exacte de cette promotion.'}
            foreach($learner in $learners){$key=[ITMGradeMatcher]::Key($learner.Name);if(-not $existing.ContainsKey($key)){throw ('Nom absent de la grille : '+$learner.Name)};$mapping[$learner.Id]=[int]$existing[$key]}
        }else{for($i=0;$i -lt $learners.Count;$i++){$mapping[$learners[$i].Id]=$config.FirstRow+$i}}
        $notes=$excel.Workbooks.Open($config.Notes,0,$true);$noteSheet=$notes.Worksheets.Item(1)
        $bottom=[int]$noteSheet.UsedRange.Row+[int]$noteSheet.UsedRange.Rows.Count-1
        if($bottom -gt 50000 -or $config.NotesNameColumn -gt 200 -or $config.NotesGradeColumn -gt 200){throw 'Fichier de cotes trop grand ou repères incorrects.'}
        if($bottom -lt $config.NotesFirstRow){throw 'Aucune cote après la ligne de départ choisie.'}
        $nameValues=$noteSheet.Range($noteSheet.Cells.Item($config.NotesFirstRow,$config.NotesNameColumn),$noteSheet.Cells.Item($bottom,$config.NotesNameColumn)).Value2
        $gradeValues=$noteSheet.Range($noteSheet.Cells.Item($config.NotesFirstRow,$config.NotesGradeColumn),$noteSheet.Cells.Item($bottom,$config.NotesGradeColumn)).Value2
        $input=[ITMGradeInput[]]@(for($row=$config.NotesFirstRow;$row -le $bottom;$row++){
            $offset=$row-$config.NotesFirstRow+1
            $name=[string]$(if($nameValues -is [Array]){$nameValues.GetValue($offset,1)}else{$nameValues});$value=$(if($gradeValues -is [Array]){$gradeValues.GetValue($offset,1)}else{$gradeValues})
            if([string]::IsNullOrWhiteSpace($name) -and $null -eq $value){continue}
            $number=[decimal]0
            if($null -eq $value -or -not [decimal]::TryParse([string]$value,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$number)){
                if(-not [decimal]::TryParse([string]$value,[Globalization.NumberStyles]::Float,$script:fr,[ref]$number)){throw ('Cote non numérique ou absente à la ligne '+$row)}
            }
            $item=New-Object ITMGradeInput;$item.Name=$name;$item.Grade=$number;$item.SourceRow=$row;$item
        })
        if(-not $input.Count){throw 'Le fichier ne contient aucune cote dans les colonnes choisies.'}
        $result=[ITMGradeMatcher]::Match($learners,$input,[decimal]$config.Maximum)
        $rows=@(foreach($match in $result){$target=$(if($match.Id){[int]$mapping[$match.Id]}else{0});$problem=$match.Problem
            if($target -gt 0){$cell=$sheet.Cells.Item($target,$column);if($cell.HasFormula -and -not $config.Replace){$problem='Cote calculée déjà présente : remplacement non autorisé'}elseif($null -ne $cell.Value2 -and -not $config.Replace){$problem='Cote déjà présente : remplacement non autorisé'}}
            [pscustomobject]@{Nom=$match.Name;Matricule=$match.Id;Cote=([decimal]$match.Grade*[decimal]$course[0].Maximum/[decimal]$config.Maximum);LigneSource=$match.SourceRow;LigneGrille=$target;Probleme=$problem}
        })
        Assert-ITMGradeHash $config.Grid $gridHash;Assert-ITMGradeHash $config.Notes $notesHash
        return [pscustomobject]@{Config=$config;Layout=$layout;Sheet=[string]$sheet.Name;Column=$column;Learners=$learners;Mapping=$mapping;Rows=$rows;EmptyGrid=($nonEmpty -eq 0);GridHash=$gridHash;NotesHash=$notesHash}
    }finally{
        if($notes){try{$notes.Close($false)}catch{}}
        if($grid){try{$grid.Close($false)}catch{}}
        if($excel){try{$excel.Quit()}catch{};$excel.Dispose()}
    }
}
function Test-ITMSavedGrade($actual,[decimal]$expected){
    if($null -eq $actual -or $actual -is [bool]){return $false}
    # Excel uses floating point: tolerate rounding, never a changed or missing grade.
    $parsed=[decimal]0
    if(-not [decimal]::TryParse([string]$actual,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$parsed)){return $false}
    $tolerance=[Math]::Max([decimal]0.000000000001,[Math]::Abs($expected)*[decimal]0.000000000001)
    return [Math]::Abs($parsed-$expected) -le $tolerance
}
function Assert-ITMGradeSelection($layout,$config){
    if([ITMGradeMatcher]::Key($layout.Code).Replace(' ','') -ne [ITMGradeMatcher]::Key([string]$config.Promotion).Replace(' ','')){throw 'La feuille de grille ne correspond pas à la promotion choisie. Sélectionnez à nouveau la grille et le cours.'}
    if([int]$config.CourseColumn -gt 0){
        $selected=@($layout.Courses | Where-Object {$_.Column -eq [int]$config.CourseColumn})
        if($selected.Count -ne 1 -or [ITMGradeMatcher]::Key([string]$selected[0].Title) -ne [ITMGradeMatcher]::Key([string]$config.Course)){throw 'Le cours ou sa colonne a changé. Sélectionnez à nouveau le cours et relancez l''analyse.'}
    }
}
function Assert-ITMGradeHash([string]$path,[string]$expected){
    if($expected -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash){throw 'Un fichier a changé. Relancez l''analyse avant de remplir la grille.'}
}
function Set-ITMGradeBusy($context,[bool]$busy){
    $context.Running=$busy
    foreach($field in $context.Fields.Values){$field.Enabled=-not $busy}
    $context.Replace.Enabled=-not $busy
    $context.Open.Enabled=(-not $busy -and [bool]$context.Output)
    $context.Save.Enabled=(-not $busy -and $null -ne $context.Plan -and @($context.Plan.Rows | Where-Object {$_.Probleme}).Count -eq 0)
}
function Save-ITMGradePlan($plan){
    Assert-ITMPermission 'Administrer'
    if(-not $plan -or -not @($plan.Rows).Count){throw 'Analysez les fichiers avant l''import.'}
    if(@($plan.Rows | Where-Object {$_.Probleme}).Count){throw 'Corrigez les correspondances avant de remplir la grille.'}
    if($plan.GridHash -ne (Get-FileHash -LiteralPath $plan.Config.Grid -Algorithm SHA256).Hash -or $plan.NotesHash -ne (Get-FileHash -LiteralPath $plan.Config.Notes -Algorithm SHA256).Hash){throw 'Un fichier a changé. Relancez l''analyse.'}
    $dir=Get-ITMGradesFolder;$id=[Guid]::NewGuid().ToString('N');$temp=Join-Path $dir ($id+'.tmp.xlsx');$final=Join-Path $dir ('Grille_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+$id.Substring(0,6)+'.xlsx')
    $excel=$null;$book=$null
    try{
        [IO.File]::Copy($plan.Config.Grid,$temp,$false);Assert-ITMGradeHash $temp $plan.GridHash;$excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($temp,0,$false);if($book.ReadOnly){throw 'Copie de grille en lecture seule.'}
        $sheet=$book.Worksheets.Item($plan.Sheet)
        if($plan.EmptyGrid){foreach($a in $plan.Learners){$cell=$sheet.Cells.Item([int]$plan.Mapping[$a.Id],$plan.Config.NameColumn);if($cell.HasFormula){throw 'La colonne des noms contient une formule.'};$cell.Value2=[string]$a.Name}}
        foreach($row in $plan.Rows){$cell=$sheet.Cells.Item($row.LigneGrille,$plan.Column);if($cell.HasFormula -and -not $plan.Config.Replace){throw 'Remplacement de cote non autorisé.'};$cell.Value2=[double]$row.Cote}
        foreach($row in $plan.Rows){if(-not (Test-ITMSavedGrade $sheet.Cells.Item($row.LigneGrille,$plan.Column).Value2 ([decimal]$row.Cote))){throw 'La cote enregistrée ne correspond pas au fichier importé.'}}
        Sort-ITMGridLearners $sheet $plan.Layout
        $decisions=@(Set-ITMGridDecisions $sheet $plan.Layout)
        [void]$excel.CalculateFull();Test-ITMGridDecisions $sheet $plan.Layout $decisions;if(Test-Path function:Set-ScoliaWorkbookPersonnel){Set-ScoliaWorkbookPersonnel $book};[void]$book.Save()
        $positions=@{};for($r=$plan.Layout.FirstRow;$r -le $plan.Layout.LastRow;$r++){$key=[ITMGradeMatcher]::Key([string]$sheet.Cells.Item($r,$plan.Layout.NameColumn).Text);if($key){$positions[$key]=$r}}
        $writtenRows=@(foreach($entry in $plan.Rows){
            $name=[string]$entry.Nom;$learner=@($plan.Learners | Where-Object {$_.Id -eq $entry.Matricule});if($learner.Count -eq 1){$name=[string]$learner[0].Name}
            $key=[ITMGradeMatcher]::Key($name)
            if(-not $positions.ContainsKey($key) -or -not (Test-ITMSavedGrade $sheet.Cells.Item([int]$positions[$key],$plan.Column).Value2 ([decimal]$entry.Cote))){throw 'La cote de l''apprenant ne correspond plus au contrôle après classement.'}
            $record=$entry.PSObject.Copy();$record.LigneGrille=[int]$positions[$key];$record
        })
        $book.Close($false);$book=$null
        $log=[pscustomobject]@{Date=(Get-Date -Format o);Promotion=$plan.Config.Promotion;Cours=$plan.Config.Course;Grille=$plan.Config.Grid;Source=$plan.Config.Notes;Sortie=$final;GrilleSHA256=$plan.GridHash;SourceSHA256=$plan.NotesHash;ReglesDecision='Vide : AA si >=50 %, sinon D ; sans vide : A si échec <50 % du cours, sinon P';Decisions=$decisions;Cotes=$writtenRows}
        [IO.File]::WriteAllText((Join-Path $dir ($id+'.json')),($log | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true))
        [IO.File]::Move($temp,$final);return $final
    }finally{if($book){try{$book.Close($false)}catch{}};if($excel){try{$excel.Quit()}catch{};$excel.Dispose()};if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Show-ITMGrades {
    Assert-ITMPermission 'Administrer'
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text='Cotes et grille de délibération';$d.Size=New-Object Drawing.Size(1060,780);$d.MinimumSize=New-Object Drawing.Size(500,500);$d.StartPosition='CenterParent'
    $outer=New-Object Windows.Forms.TableLayoutPanel;$outer.Dock='Fill';$outer.ColumnCount=1;$outer.RowCount=3;$outer.Padding=New-Object Windows.Forms.Padding(14);$d.Controls.Add($outer)
    [void]$outer.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',400)));[void]$outer.RowStyles.Add((New-Object Windows.Forms.RowStyle('Percent',100)));[void]$outer.RowStyles.Add((New-Object Windows.Forms.RowStyle('AutoSize',0)))
    $form=New-Object Windows.Forms.TableLayoutPanel;$form.Dock='Fill';$form.AutoScroll=$true;$form.ColumnCount=2;[void]$form.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent',44)));[void]$form.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent',56)));$outer.Controls.Add($form,0,0)
    $fields=@{};$spec=@(@('Promotion','Promotion',''),@('Course','Intitulé exact du cours',''),@('Grid','Grille XLSX (double-cliquez)',''),@('Notes','Fichier de cotes XLSX (double-cliquez)',''),@('Sheet','Feuille de grille (vide = première)',''),@('FirstRow','Première ligne des apprenants','12'),@('LastRow','Dernière ligne disponible','59'),@('NameColumn','Colonne du nom complet (numéro)','2'),@('CourseRow','Ligne des intitulés de cours','9'),@('CourseColumn','Colonne de cote (0 = détection)','0'),@('NotesFirstRow','Première ligne de cotes du fichier','2'),@('NotesNameColumn','Colonne du nom dans les cotes','1'),@('NotesGradeColumn','Colonne de la cote dans le fichier','2'),@('Maximum','Barème du fichier de cotes (conversion automatique)','20'))
    for($i=0;$i -lt $spec.Count;$i++){
        $form.RowCount=$i+1;[void]$form.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',34)))
        $label=New-Object ITMReadableLabel;$label.Text=$spec[$i][1];$label.Dock='Fill';$label.TextAlign='MiddleLeft';$form.Controls.Add($label,0,$i)
        if($i -eq 1){$box=New-Object Windows.Forms.ComboBox;$box.DropDownStyle='DropDownList';$form.Controls.Add($box)}elseif($i -eq 0){$box=New-Combo $form 0 0 300;Set-Choices $box @($script:data.learners | ForEach-Object {$_.Promotion} | Sort-Object -Unique) ''}else{$box=New-Object Windows.Forms.TextBox;$box.Text=$spec[$i][2];$form.Controls.Add($box)}
        $box.Dock='Fill';$form.SetCellPosition($box,(New-Object Windows.Forms.TableLayoutPanelCellPosition(1,$i)));$fields[$spec[$i][0]]=$box
        if($spec[$i][0] -in @('Grid','Notes')){$box.ReadOnly=$true;$box.Add_DoubleClick({param($sender,$e)$picker=New-Object Windows.Forms.OpenFileDialog;$picker.Filter='Classeur Excel|*.xlsx';try{if($picker.ShowDialog($sender.FindForm()) -eq 'OK'){$sender.Text=$picker.FileName}}finally{$picker.Dispose()}})}
    }
    foreach($key in @('Sheet','FirstRow','LastRow','NameColumn','CourseRow','CourseColumn')){$box=$fields[$key];$pos=$form.GetCellPosition($box);$form.GetControlFromPosition(0,$pos.Row).Visible=$false;$box.Visible=$false;$form.RowStyles[$pos.Row].Height=0}
    $form.RowCount++;[void]$form.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',42)))
    $replace=New-Object ITMReadableCheckBox;$replace.Text='Autoriser le remplacement des cotes déjà présentes dans la copie';$replace.Dock='Fill';$form.Controls.Add($replace,0,$spec.Count);$form.SetColumnSpan($replace,2)
    $grid=New-Grid $outer 0 0 980 220;$grid.Dock='Fill';$outer.SetRow($grid,1)
    $actions=New-Object Windows.Forms.FlowLayoutPanel;$actions.Dock='Fill';$actions.AutoSize=$true;$outer.Controls.Add($actions,0,2)
    $analyse=New-Button $actions 'Analyser et prévisualiser' 0 0 240;$save=New-Button $actions 'Remplir une copie de la grille' 0 0 270 $true;$open=New-Button $actions 'Ouvrir la grille remplie' 0 0 220;$save.Enabled=$false;$open.Enabled=$false
    $context=[pscustomobject]@{Fields=$fields;Replace=$replace;Plan=$null;Preview=$grid;Save=$save;Open=$open;Output='';Dialog=$d;Layout=$null;Running=$false}

    $review=New-Button $actions 'Vérifier la délibération' 0 0 230;$review.Tag=$context;$review.Add_Click({param($sender,$e)try{if(-not $sender.Tag.Running){Show-ITMDeliberation $sender.Tag.Fields.Grid.Text $sender.Tag.Fields.Promotion.Text}}catch{App-Error $_}})
    $prepare=New-Button $actions 'Préparer pour la promotion' 0 0 230
    $prepare.Tag=$context;$prepare.Add_Click({param($sender,$e)$c=$sender.Tag;if($c.Running){return};Set-ITMGradeBusy $c $true;$sender.Enabled=$false
        try{Start-ITMActivity 'Préparation de la grille...' $sender;$c.Fields.Grid.Text=New-ITMPromotionGrid $c.Fields.Grid.Text $c.Fields.Promotion.Text;$c.Output=$c.Fields.Grid.Text;$c.Open.Enabled=$true;$c.Plan=$null;$c.Save.Enabled=$false}
        catch{App-Error $_}finally{try{Stop-ITMActivity}catch{App-Error $_}finally{Set-ITMGradeBusy $c $false;$sender.Enabled=$true}}
    })
    $loadLayout={param($sender,$e)$c=$sender.Tag;if(-not $c -or $c.Running){return}
        $c.Layout=$null;$c.Plan=$null;$c.Save.Enabled=$false;$c.Fields.Sheet.Text='';$c.Fields.CourseColumn.Text='0';$c.Fields.Course.Items.Clear()
        if(-not [IO.File]::Exists($c.Fields.Grid.Text) -or -not $c.Fields.Promotion.Text){return}
        try{$l=Read-ITMGridLayout $c.Fields.Grid.Text $c.Fields.Promotion.Text;$c.Layout=$l
            foreach($k in @('FirstRow','LastRow','NameColumn','CourseRow')){$c.Fields[$k].Text=[string]$l.$k}
            $c.Fields.Sheet.Text=$l.Sheet;$c.Fields.Course.Items.Clear();foreach($course in $l.Courses){[void]$c.Fields.Course.Items.Add($course.Title)}
        }catch{App-Error $_}
    }
    $mapping=New-Button $actions 'Paramétrer la grille' 0 0 220;$mapping.Tag=$context;$mapping.Add_Click({param($sender,$e)try{$c=$sender.Tag;if($c.Running){return};if(Show-ITMGridMapping $c.Fields.Grid.Text $c.Fields.Promotion.Text){& $loadLayout $c.Fields.Grid $null}}catch{App-Error $_}})
    $fields.Grid.Tag=$context;$fields.Promotion.Tag=$context;$fields.Grid.Add_TextChanged($loadLayout);$fields.Promotion.Add_SelectedIndexChanged($loadLayout)
    $fields.Course.Tag=$context;$fields.Course.Add_SelectedIndexChanged({param($sender,$e)$c=$sender.Tag;if($c.Layout -and $sender.SelectedIndex -ge 0){$c.Fields.CourseColumn.Text=[string]$c.Layout.Courses[$sender.SelectedIndex].Column}})
    foreach($control in $fields.Values){$control.Tag=$context;$control.Add_TextChanged({param($sender,$e)$c=$sender.Tag;if($c.PSObject.Properties['Context']){$c=$c.Context};$c.Plan=$null;$c.Save.Enabled=$false})};$replace.Tag=$context;$replace.Add_CheckedChanged({param($sender,$e)$c=$sender.Tag;if($c.PSObject.Properties['Context']){$c=$c.Context};$c.Plan=$null;$c.Save.Enabled=$false})
    $analyse.Tag=$context;$analyse.Add_Click({param($sender,$e)try{
        $c=$sender.Tag;if($c.Running){return};$config=[pscustomobject]@{Replace=$c.Replace.Checked}
        foreach($key in $c.Fields.Keys){$value=$c.Fields[$key].Text.Trim();if($key -in @('FirstRow','LastRow','NameColumn','CourseRow','CourseColumn','NotesFirstRow','NotesNameColumn','NotesGradeColumn')){$value=[int]$value}elseif($key -eq 'Maximum'){$value=[decimal]$value};$config | Add-Member -NotePropertyName $key -NotePropertyValue $value}
        $c.Save.Enabled=$false;$c.Plan=$null;Set-ITMGradeBusy $c $true
        try{Start-ITMActivity 'Traitement de la grille...' $sender;$c.Plan=Read-ITMGradePlan $config}finally{try{Stop-ITMActivity}finally{Set-ITMGradeBusy $c $false}}
        Set-Grid $c.Preview $c.Plan.Rows @(@('Nom','Nom complet'),@('Cote','Cote'),@('LigneSource','Ligne source'),@('LigneGrille','Ligne grille'),@('Probleme','À corriger'))
        $c.Save.Enabled=(@($c.Plan.Rows | Where-Object {$_.Probleme}).Count -eq 0)
    }catch{App-Error $_}})
    $fields.Notes.Tag=[pscustomobject]@{Context=$context;Analyse=$analyse};$fields.Notes.Add_TextChanged({param($sender,$e)$c=$sender.Tag.Context;if(-not $c.Running -and $c.Fields.Course.Text -and [IO.File]::Exists($c.Fields.Grid.Text) -and [IO.File]::Exists($sender.Text)){$sender.Tag.Analyse.PerformClick()}})
    $save.Tag=$context;$save.Add_Click({param($sender,$e)try{
        $c=$sender.Tag;if($c.Running){return};if(-not $c.Plan){throw 'Analysez les fichiers avant l''import.'};Set-ITMGradeBusy $c $true
        try{Start-ITMActivity 'Traitement de la grille...' $sender;$c.Output=Save-ITMGradePlan $c.Plan;$c.Open.Enabled=$true;$c.Fields.Grid.Text=$c.Output;$script:status.Text='Grille remplie : '+$c.Output}finally{try{Stop-ITMActivity}finally{Set-ITMGradeBusy $c $false}}
    }catch{App-Error $_}})
    $open.Tag=$context;$open.Add_Click({param($sender,$e)try{if(-not $sender.Tag.Running -and $sender.Tag.Output){Start-Process -FilePath $sender.Tag.Output}}catch{App-Error $_}})
    Apply-ITMThemeForm $d;if((Test-Path function:Mount-ScoliaWorkspaceForm) -and (Mount-ScoliaWorkspaceForm $d)){return}
    try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}

function Show-ITMDeliberation([string]$path,[string]$promotion){
    Assert-ITMPermission 'Administrer'
    $excel=$null;$book=$null;$rows=@();$layout=$null;$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    try{
        $excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($path,0,$true);$sheet=Get-ITMGridSheet $book $promotion;$layout=Get-ITMGridLayout $sheet $promotion
        for($r=$layout.FirstRow;$r -le $layout.LastRow;$r++){
            $name=[string]$sheet.Cells.Item($r,$layout.NameColumn).Text;if(-not $name.Trim()){continue}
            $result=Get-ITMRowDecision $sheet $layout $r
            $rows+=[pscustomobject]@{Ligne=$r;Nom=$name;Total=$result.Total;Pourcentage=$result.Percentage;CotesManquantes=$result.Missing;Echecs=$result.Failed;Decision=$result.Decision}
        }
    }finally{if($book){try{$book.Close($false)}catch{}};if($excel){try{$excel.Quit()}catch{};$excel.Dispose()}}
    Assert-ITMGradeHash $path $hash
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text='Délibération — validation du jury';$d.Size=New-Object Drawing.Size(1000,700);$d.MinimumSize=New-Object Drawing.Size(520,450);$d.StartPosition='CenterParent'
    $table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Fill';$table.ColumnCount=1;$table.RowCount=3;$table.Padding=New-Object Windows.Forms.Padding(14);$d.Controls.Add($table)
    [void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',60)));[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Percent',100)));[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',60)))
    $note=New-Object ITMReadableLabel;$note.Dock='Fill';$note.Text='Décisions automatiques : vide → AA dès 50 %, D en dessous ; sans vide → A si un cours est sous 50 %, sinon P. Zéro est une cote, pas un vide.';$table.Controls.Add($note,0,0)
    $g=New-Grid $table 0 0 900 500;$g.Dock='Fill';$table.SetRow($g,1)
    Set-Grid $g $rows @(@('Ligne','Ligne'),@('Nom','Apprenant'),@('Total','Total'),@('Pourcentage','%'),@('CotesManquantes','Cotes absentes'),@('Echecs','Échecs'),@('Decision','Décision automatique'))
    $context=[pscustomobject]@{Rows=$rows;Grid=$g;Path=$path;Hash=$hash;Layout=$layout;Promotion=$promotion}
    $g.Tag=$context
    $save=New-Button $table 'Enregistrer une copie délibérée' 0 0 380 $true;$save.Dock='Fill';$table.SetRow($save,2);$save.Tag=$context
    $save.Add_Click({param($sender,$e)$c=$sender.Tag;$excel=$null;$book=$null;$temp='';$sender.Enabled=$false
        try{
            if($c.Hash -ne (Get-FileHash -LiteralPath $c.Path -Algorithm SHA256).Hash){throw 'La grille a changé : rouvrez la délibération.'}
            $dir=Get-ITMGradesFolder;$id=[Guid]::NewGuid().ToString('N');$temp=Join-Path $dir ($id+'.tmp.xlsx');$final=Join-Path $dir ('Deliberation_'+$id.Substring(0,8)+'.xlsx');[IO.File]::Copy($c.Path,$temp,$false);Assert-ITMGradeHash $temp $c.Hash
            $excel=Open-ITMGradeExcel;$book=$excel.Workbooks.Open($temp,0,$false);if($book.ReadOnly){throw 'Copie en lecture seule.'};$sheet=$book.Worksheets.Item($c.Layout.Sheet)
            Sort-ITMGridLearners $sheet $c.Layout
            $decisions=@(Set-ITMGridDecisions $sheet $c.Layout);[void]$excel.CalculateFull();Test-ITMGridDecisions $sheet $c.Layout $decisions
            if(Test-Path function:Set-ScoliaWorkbookPersonnel){Set-ScoliaWorkbookPersonnel $book};[void]$book.Save();$book.Close($false);$book=$null
            [IO.File]::WriteAllText((Join-Path $dir ($id+'.json')),([pscustomobject]@{Date=(Get-Date -Format o);Utilisateur=[Environment]::UserName;Promotion=$c.Promotion;Source=$c.Path;SourceSHA256=$c.Hash;Sortie=$final;ReglesDecision='Vide : AA si >=50 %, sinon D ; sans vide : A si échec <50 % du cours, sinon P';Decisions=$decisions} | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true))
            [IO.File]::Move($temp,$final);Start-Process -FilePath $final
        }catch{App-Error $_}finally{if($book){try{$book.Close($false)}catch{}};if($excel){try{$excel.Quit()}catch{};$excel.Dispose()};if($temp -and [IO.File]::Exists($temp)){[IO.File]::Delete($temp)};$sender.Enabled=$true}
    })
    Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
