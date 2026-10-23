$ErrorActionPreference='Stop'
# V80 : le noyau C# doit produire exactement les mêmes résultats que les boucles
# PowerShell qu'il remplace (clés de recherche, tables de grilles, rapprochement,
# filtre des apprenants). Fonctionne sous Windows PowerShell 5.1 et PowerShell 7.
$source=Join-Path $PSScriptRoot 'AccelerationScolia.cs'
$onWindows=($PSVersionTable.PSVersion.Major -le 5 -or $IsWindows)
if(-not ('ITMFast' -as [type])){
    if($onWindows){
        . (Join-Path $PSScriptRoot 'CompilationCache.ps1')
        Import-ITMCompiledSource -Path $source -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Data,System.Xml
    }else{
        $text=[IO.File]::ReadAllText($source)
        $text=$text.Substring(0,$text.IndexOf('// ===== WINFORMS ====='))
        $text=$text.Replace('using System.Windows.Forms;','')
        Add-Type -TypeDefinition $text -IgnoreWarnings
    }
}
$failures=0
function Check([bool]$condition,[string]$message){if(-not $condition){$script:failures++;Write-Host ('ECHEC : '+$message)}}

# ---------- Références : copies fidèles du code PowerShell V79 ----------
$script:refKeys=New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
function Ref-Key([object]$value){
    $raw=[string]$value
    if($script:refKeys.ContainsKey($raw)){return $script:refKeys[$raw]}
    $t=$raw.Trim().ToUpperInvariant().Normalize([Text.NormalizationForm]::FormD)
    $builder=New-Object Text.StringBuilder
    foreach($character in $t.ToCharArray()){
        if([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark){[void]$builder.Append($character)}
    }
    $result=$builder.ToString().Normalize([Text.NormalizationForm]::FormC)
    $script:refKeys[$raw]=$result
    return $result
}
function Ref-Table($rows,$columns){
    $table=New-Object Data.DataTable
    foreach($column in $columns){$columnType=$(if($column.Count -gt 2 -and $column[2] -is [type]){$column[2]}else{[string]});[void]$table.Columns.Add([string]$column[1],$columnType)}
    $table.BeginLoadData()
    foreach($entry in @($rows)) {
        if($null -eq $entry){continue}; $r=$table.NewRow()
        foreach($column in $columns){ $v=$entry.([string]$column[0]); $isText=$table.Columns[[string]$column[1]].DataType -eq [string];$r[[string]$column[1]]=$(if($null -eq $v){if($isText){''}else{[DBNull]::Value}}elseif($isText){[string]$v}else{$v}) }
        $table.Rows.Add($r)
    }
    $table.EndLoadData()
    return ,$table
}
function Ref-RowKey($row,$keys){$b=New-Object Text.StringBuilder;foreach($key in $keys){$value=[string]$row[$key];[void]$b.Append($value.Length).Append(':').Append($value)};return $b.ToString()}
function Ref-Sync([Data.DataTable]$target,[Data.DataTable]$source,$keys){
    if($target.Columns.Count -ne $source.Columns.Count){return $false}
    for($i=0;$i -lt $target.Columns.Count;$i++){if($target.Columns[$i].ColumnName -cne $source.Columns[$i].ColumnName -or $target.Columns[$i].DataType -ne $source.Columns[$i].DataType){return $false}}
    if(-not $keys.Count){
        if($target.Rows.Count -ne $source.Rows.Count){return $false}
        for($i=0;$i -lt $target.Rows.Count;$i++){for($j=0;$j -lt $target.Columns.Count;$j++){if(-not [object]::Equals($target.Rows[$i][$j],$source.Rows[$i][$j])){return $false}}}
        return $true
    }
    $old=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $incoming=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach($row in $target.Rows){$key=Ref-RowKey $row $keys;if($old.ContainsKey($key)){return $false};$old.Add($key,$row)}
    foreach($row in $source.Rows){$key=Ref-RowKey $row $keys;if($incoming.ContainsKey($key)){return $false};$incoming.Add($key,$row)}
    foreach($key in $old.Keys){if(-not $incoming.ContainsKey($key)){$target.Rows.Remove($old[$key])}}
    foreach($row in $source.Rows){
        $key=Ref-RowKey $row $keys
        if(-not $old.ContainsKey($key)){$target.ImportRow($row);continue}
        $existing=$old[$key]
        for($j=0;$j -lt $target.Columns.Count;$j++){if(-not [object]::Equals($existing[$j],$row[$j])){$existing[$j]=$row[$j]}}
    }
    return $true
}
function Table-Text([Data.DataTable]$t){
    $lines=New-Object 'System.Collections.Generic.List[string]'
    $lines.Add((@($t.Columns|ForEach-Object {$_.ColumnName+'/'+$_.DataType.Name}) -join '|'))
    foreach($row in $t.Rows){$lines.Add((@($row.ItemArray|ForEach-Object {if($_ -is [DBNull]){'<null>'}else{$_.GetType().Name+'='+[string]$_}}) -join '|'))}
    return ($lines -join "`n")
}

# ---------- 1. Clés de recherche ----------
$samples=@('','  ','é','Élève  Kabila ','ÇA VA','naïve','ñandú','Ångström','ǅemal','straße','İstanbul','ﬁ','Ä'+[char]0x0301,'TML1',"Tab`tNom",'ŒUVRE','À côté – fin',[string][char]0x00A0+'x'+[char]0x202F,'𝒜lpha','日本語','a'+[char]0x0308+'b',123,12.5,$null,[decimal]55000,(Get-Date '2026-10-01'),$true)
$random=New-Object Random 80
for($i=0;$i -lt 2500;$i++){
    $length=$random.Next(0,14);$chars=New-Object char[] $length
    for($j=0;$j -lt $length;$j++){$pick=$random.Next(0,10);$chars[$j]=[char]$(if($pick -lt 5){$random.Next(32,127)}elseif($pick -lt 8){$random.Next(0xC0,0x180)}elseif($pick -lt 9){$random.Next(0x300,0x370)}else{$random.Next(0x2000,0x2100)})}
    $samples+=,(New-Object string (,$chars))
}
$keyDiff=0
foreach($sample in $samples){if((Ref-Key $sample) -cne [ITMFast]::Key([string]$sample)){$keyDiff++;if($keyDiff -le 3){Write-Host ('  clé différente pour : '+[string]$sample)}}}
Check ($keyDiff -eq 0) ('Key : '+$keyDiff+' différence(s) sur '+$samples.Count+' textes.')

# ---------- 2. Construction des tables ----------
$rows=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 400;$i++){
    switch($i%4){
        0{$rows.Add([pscustomobject]@{Matricule=('INF'+$i.ToString('000'));Nom="Nom $i";Verse=[decimal]($i*1250.5);Mois=$(if($i%3){'Octobre'}else{$null});Date=(Get-Date '2026-09-01').AddDays($i);Rang=$i;Ok=($i%2 -eq 0)})}
        1{$rows.Add(@{Matricule=('ACC'+$i);Nom=('Élève '+$i);Verse=$i;Mois='Mai';Rang=[long]$i})}
        2{$rows.Add(('{"Matricule":"TLM'+$i+'","Nom":"Json \"cité\"","Verse":'+$i+'.25,"Mois":null,"Rang":'+$i+'}' | ConvertFrom-Json))}
        3{$rows.Add($null)}
    }
}
$columns=@(@('Matricule','Matricule'),@('Nom','Nom complet'),@('Verse','Montant (FC)'),@('Mois','Mois'),@('Date','Date'),@('Absent','Colonne absente'),@('Ok','Validé'))
$typed=@(@('Matricule','Matricule'),@('Verse','Montant',[decimal]),@('Rang','Rang',[long]),@('Date','Date',[datetime]),@('Absent','Vide',[int]))
foreach($set in @(@{Name='texte';Columns=$columns},@{Name='typé';Columns=$typed})){
    $expected=Ref-Table $rows.ToArray() $set.Columns
    $props=New-Object 'System.Collections.Generic.List[string]';$names=New-Object 'System.Collections.Generic.List[string]';$types=New-Object 'System.Collections.Generic.List[type]'
    foreach($column in $set.Columns){$props.Add([string]$column[0]);$names.Add([string]$column[1]);$types.Add($(if($column.Count -gt 2 -and $column[2] -is [type]){$column[2]}else{[string]}))}
    $actual=[ITMFast]::BuildTable($rows.ToArray(),$props.ToArray(),$names.ToArray(),$types.ToArray())
    Check ((Table-Text $expected) -ceq (Table-Text $actual)) ('BuildTable '+$set.Name+' : contenu différent de Set-Grid V79.')
}
$empty=[ITMFast]::BuildTable(@(),[string[]]@('A'),[string[]]@('A'),[type[]]@([string]))
Check ($empty.Rows.Count -eq 0 -and $empty.Columns.Count -eq 1) 'BuildTable : table vide incorrecte.'

# ---------- 3. Rapprochement des tables liées ----------
function New-Sample([int]$seed,[int]$count,[bool]$duplicate=$false){
    $r=New-Object Random $seed
    $t=New-Object Data.DataTable;[void]$t.Columns.Add('Matricule',[string]);[void]$t.Columns.Add('Mois',[string]);[void]$t.Columns.Add('Montant',[decimal])
    $used=@{}
    for($i=0;$i -lt $count;$i++){
        $mat='M'+$r.Next(0,[Math]::Max(2,[int]($count*1.5)));$mois=@('Septembre','Octobre','')[$r.Next(0,3)]
        if(-not $duplicate -and $used.ContainsKey($mat+'|'+$mois)){continue};$used[$mat+'|'+$mois]=$true
        $row=$t.NewRow();$row['Matricule']=$mat;$row['Mois']=$mois;$row['Montant']=$(if($r.Next(0,5)){[decimal]$r.Next(0,90000)}else{[DBNull]::Value});$t.Rows.Add($row)
    }
    return ,$t
}
$syncDiff=0;$cases=0
foreach($seed in 1..60){
    foreach($keys in @(@('Matricule','Mois'),@('Matricule'),@())){
        $cases++
        $targetA=New-Sample $seed 40;$targetB=$targetA.Copy();$incoming=New-Sample ($seed+1000) 35 ($seed%7 -eq 0)
        if($seed%5 -eq 0){$incoming=$targetA.Copy()}
        $refResult=Ref-Sync $targetA $incoming $keys
        $fastResult=[ITMFast]::SyncTable($targetB,$incoming,[string[]]$keys)
        if($refResult -ne $fastResult -or (Table-Text $targetA) -cne (Table-Text $targetB)){$syncDiff++}
    }
}
Check ($syncDiff -eq 0) ('SyncTable : '+$syncDiff+' différence(s) sur '+$cases+' cas.')

# ---------- 4. Filtre des apprenants ----------
$index=@(for($i=0;$i -lt 300;$i++){[pscustomobject]@{Entry=[pscustomobject]@{Matricule='M'+$i};Text=(Ref-Key ('M'+$i+' Nom'+($i%17)+' Élève '+@('INF1','ACC2','TLM1')[$i%3]));Promotion=@('INF1','ACC2','TLM1')[$i%3]}})
foreach($case in @(@{S='';P='Toutes les promotions'},@{S='nom1';P='Toutes les promotions'},@{S='ELEVE nom3';P='ACC2'},@{S='';P='TLM1'},@{S='M29 eleve';P='inf1'},@{S='zzz';P='Toutes les promotions'})){
    $terms=@((Ref-Key $case.S) -split '\s+' | Where-Object {$_});$promoKey=(Ref-Key $case.P).Replace(' ','').Replace('-','')
    $expected=New-Object 'System.Collections.Generic.List[object]'
    foreach($item in $index){
        if($case.P -ne 'Toutes les promotions' -and $item.Promotion -ne $promoKey){continue}
        $match=$true;foreach($term in $terms){if($item.Text.IndexOf($term,[StringComparison]::Ordinal) -lt 0){$match=$false;break}}
        if($match){$expected.Add($item.Entry)}
    }
    $actual=[ITMFast]::FilterLearners($index,[string[]]$terms,($case.P -eq 'Toutes les promotions'),$promoKey)
    $same=($expected.Count -eq $actual.Count);if($same){for($k=0;$k -lt $actual.Count;$k++){if(-not [object]::ReferenceEquals($expected[$k],$actual[$k])){$same=$false;break}}}
    Check $same ('FilterLearners : résultat différent pour « '+$case.S+' » / '+$case.P)
}

# ---------- 5. Mesure indicative ----------
$big=@(for($i=0;$i -lt 1500;$i++){[pscustomobject]@{Matricule='INF'+$i;Nom='Nom';PostNom='Post';Prenom='Prénom';Sexe='F';Promotion='INF1';Contact1='0800';Statut='Actif'}})
$cols=@(@('Matricule','Matricule'),@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Sexe','Sexe'),@('Promotion','Promotion'),@('Contact1','Contact parent'),@('Statut','Statut'))
$watch=[Diagnostics.Stopwatch]::StartNew();[void](Ref-Table $big $cols);$before=$watch.Elapsed.TotalMilliseconds
$watch.Restart();[void][ITMFast]::BuildTable($big,[string[]]@($cols|ForEach-Object {$_[0]}),[string[]]@($cols|ForEach-Object {$_[1]}),[type[]]@($cols|ForEach-Object {[string]}));$after=$watch.Elapsed.TotalMilliseconds
Write-Host ('Mesure : tableau de 1500 dossiers V79 {0:N0} ms -> V80 {1:N0} ms' -f $before,$after)

if($failures){Write-Host ('ECHEC TestsAccelerationV80 : '+$failures+' contrôle(s).');exit 1}
Write-Host 'PASS TestsAccelerationV80 : Key, BuildTable, SyncTable et FilterLearners identiques au comportement V79.'
