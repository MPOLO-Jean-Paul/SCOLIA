# Rapport de rentrée : lecture seule des données scolaires, DOCX natif et PDF via Word.
. (Join-Path $PSScriptRoot 'SignatairesV63.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-ScoliaRentreeYearKey([string]$Value){return ($Value -replace '[^0-9]','')}
function Get-ScoliaRentreePopulation($Data,[string]$Year){
 $yearKey=Get-ScoliaRentreeYearKey $Year
 $rows=@($Data.learners|Where-Object {(Get-ScoliaRentreeYearKey ([string]$_.Annee)) -eq $yearKey -and (Key ([string]$_.Statut)) -notin @('SORTI','SORTIE','REVOQUE','REVOQUEE','RENVOYE','RENVOYEE')})
 $ids=@{};foreach($a in $rows){
  $id=Key ([string]$a.Matricule)
  if(-not $id -or -not ([string]$a.Nom).Trim() -or -not ([string]$a.Promotion).Trim()){throw 'Un dossier retenu est incomplet (matricule, nom ou promotion). Corrigez-le avant le rapport.'}
  if($ids.ContainsKey($id)){throw ('Matricule en double : '+$a.Matricule+'. Corrigez les dossiers avant le rapport.')};$ids[$id]=$true
 }
 $groups=@($Data.promotions|Where-Object {(Get-ScoliaRentreeYearKey ([string]$_.Annee)) -eq $yearKey}|ForEach-Object {Promo ([string]$_.Code)})+@($rows|ForEach-Object {Promo ([string]$_.Promotion)})
 $stats=@(foreach($code in @($groups|Where-Object {$_}|Sort-Object -Unique)){
  $items=@($rows|Where-Object {(Promo ([string]$_.Promotion)) -eq $code});$f=0;$m=0;$u=0
  foreach($a in $items){switch(Key ([string]$a.Sexe)){'F'{$f++};'FEMININ'{$f++};'FEMME'{$f++};'M'{$m++};'MASCULIN'{$m++};'HOMME'{$m++};default{$u++}}}
  $configured=@($Data.promotions|Where-Object {(Promo ([string]$_.Code)) -eq $code -and (Get-ScoliaRentreeYearKey ([string]$_.Annee)) -eq $yearKey}|Select-Object -First 1)
  $label=$code;if($configured.Count -and ([string]$configured[0].Designation).Trim()){$label=[string]$configured[0].Designation+' ('+$code+')'}
  [pscustomobject]@{Promotion=$code;Label=$label;F=$f;M=$m;Unknown=$u;Total=$items.Count}
 })
 return [pscustomobject]@{Learners=@($rows|Sort-Object Promotion,Nom,PostNom,Prenom,Matricule);Stats=$stats;F=[int](($stats|Measure-Object F -Sum).Sum);M=[int](($stats|Measure-Object M -Sum).Sum);Unknown=[int](($stats|Measure-Object Unknown -Sum).Sum);Total=$rows.Count}
}
function Get-ScoliaRentreeSettingsPath($Context){
 $folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SCOLIA/rapports-rentree'
 [void][IO.Directory]::CreateDirectory($folder)
 return (Join-Path $folder ((Get-ScoliaFeatureHash ($Context.Id+'|'+$Context.Year+'|'+$Context.LocalKey))+'.json'))
}
function Get-ScoliaRentreeModel($Context){
 $path=Get-ScoliaRentreeSettingsPath $Context
 if(Test-Path -LiteralPath $path){$model=[IO.File]::ReadAllText($path)|ConvertFrom-Json -ErrorAction Stop;if($model.Schema -ne 1 -or $model.Context.Id -cne $Context.Id -or $model.Context.Year -cne $Context.Year){throw 'Préparation du rapport incompatible. Le fichier est conservé.'};return (Update-ScoliaRentreeLayoutModel $model)}
 $base=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Assets/RapportRentree.json'))|ConvertFrom-Json
 return [pscustomobject]@{Schema=1;LayoutVersion=2;Context=$Context;Blocks=$base.Blocks;Meta=[pscustomobject]@{Ville='À préciser';Localisation='Les limites administratives et autres mentions sont à confirmer par la direction.';Dates='Dates de rentrée et de clôture à préciser selon le calendrier officiel.';Reunion='Date et compte rendu à préciser.';Formation='Le programme et les dates de formation du personnel sont à préciser.';Internat='La situation de lʼinternat est à préciser par la direction.';Signataire='';SignatoryRole='Prefet';Mois=9}}
}
function Update-ScoliaRentreeLayoutModel($Model){
 if(-not $Model.Meta.PSObject.Properties['SignatoryRole']){$Model.Meta|Add-Member NoteProperty SignatoryRole 'Prefet'}
 $updated=New-Object 'System.Collections.Generic.List[object]'
 foreach($b in @($Model.Blocks)){
  if($b.kind -in @('text','heading') -and [string]$b.text -ceq 'LA DIRECTION / LE PRÉFET DES ÉTUDES'){continue}
  if($b.kind -eq 'text' -and ([string]$b.text).Contains('Nom et signature :')){$b=[pscustomobject]@{kind='signature';source='RapportRentree';label='Signature automatique du responsable'}}
  if($b.kind -eq 'table' -and [string]$b.label -like '2.1.1. Organisation*' -and @($b.rows).Count -eq 0){$b.kind='auto';$b|Add-Member NoteProperty source 'administration' -Force}
  $updated.Add($b)
 }
 $Model.Blocks=$updated.ToArray();$Model|Add-Member NoteProperty LayoutVersion 2 -Force
 return $Model
}
function New-ScoliaRentreeSignature($Signatory){
 $name=$(if($Signatory.Configured){$Signatory.Name}else{'Nom à renseigner dans Administration'})
 $label=ConvertTo-ScoliaRentreeXml $Signatory.Label;$name=ConvertTo-ScoliaRentreeXml $name
 return ('<w:tbl><w:tblPr><w:tblW w:w="9354" w:type="dxa"/><w:tblBorders><w:top w:val="nil"/><w:left w:val="nil"/><w:bottom w:val="nil"/><w:right w:val="nil"/><w:insideH w:val="nil"/><w:insideV w:val="nil"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="4354"/><w:gridCol w:w="5000"/></w:tblGrid><w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr><w:tcW w:w="4354" w:type="dxa"/></w:tcPr><w:p/></w:tc><w:tc><w:tcPr><w:tcW w:w="5000" w:type="dxa"/></w:tcPr><w:p><w:pPr><w:ind w:firstLine="0"/><w:keepNext/><w:jc w:val="center"/><w:spacing w:before="240" w:after="100"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>'+ $label +'</w:t></w:r></w:p><w:p><w:pPr><w:ind w:firstLine="0"/><w:keepNext/><w:jc w:val="center"/><w:spacing w:after="720"/></w:pPr><w:r><w:t>'+ $name +'</w:t></w:r></w:p><w:p><w:pPr><w:ind w:firstLine="0"/><w:jc w:val="center"/></w:pPr><w:r><w:rPr><w:i/><w:sz w:val="20"/></w:rPr><w:t>Signature et cachet</w:t></w:r></w:p></w:tc></w:tr></w:tbl>')
}
function ConvertTo-ScoliaRentreeXml([string]$Value){return [Security.SecurityElement]::Escape($Value)}
function New-ScoliaRentreeParagraph([string]$Text,[string]$Style='Normal'){
 $styleXml=ConvertTo-ScoliaRentreeXml $Style;$lines=@($Text -split "`r?`n");$runs=''
 foreach($line in $lines){if($runs){$runs+='<w:r><w:br/></w:r>'};$runs+='<w:r><w:t xml:space="preserve">'+(ConvertTo-ScoliaRentreeXml $line)+'</w:t></w:r>'}
 return ('<w:p><w:pPr><w:pStyle w:val="'+$styleXml+'"/><w:widowControl/>'+$(if($Text -ceq 'I. INTRODUCTION' -or $Text -ceq 'ANNEXES'){'<w:pageBreakBefore/>'}else{''})+$(if($Style -eq 'Title' -or $Style -like 'Cover*'){'<w:keepNext/><w:jc w:val="center"/>'}elseif($Style -like 'Heading*'){'<w:keepNext/>'}elseif($Style -eq 'SignatureDate'){'<w:keepNext/><w:jc w:val="right"/>'}elseif($Style -eq 'PlanEntry'){'<w:jc w:val="left"/>'}elseif($Text.Contains("`n")){'<w:jc w:val="left"/>'}else{'<w:jc w:val="both"/>'})+'</w:pPr>'+$runs+'</w:p>')
}
function New-ScoliaRentreeTable($Headers,$Rows){
 $heads=@($Headers);if(-not $heads.Count){return ''}
 $weights=@(foreach($h in $heads){switch -Regex ([string]$h){'^N°$'{0.6;break};'^F$|^M$|^Sexe$'{0.6;break};'^Nom|^Section|^Matière|^Désignation|^Libellé|^Titre'{2.5;break};'^Observation'{1.6;break};'^Réunion'{1.8;break};default{1.2}}})
 $totalWeight=($weights|Measure-Object -Sum).Sum;$widths=@(foreach($weight in $weights){[int][Math]::Floor(9354*$weight/$totalWeight)});$widths[$widths.Count-1]+=9354-[int](($widths|Measure-Object -Sum).Sum)
 $xml='<w:tbl><w:tblPr><w:tblW w:w="9354" w:type="dxa"/><w:tblLayout w:type="fixed"/><w:tblBorders>'
 foreach($b in @('top','left','bottom','right','insideH','insideV')){$xml+='<w:'+$b+' w:val="single" w:sz="4" w:color="808080"/>'}
 $xml+='</w:tblBorders><w:tblCellMar><w:top w:w="70" w:type="dxa"/><w:left w:w="80" w:type="dxa"/><w:bottom w:w="70" w:type="dxa"/><w:right w:w="80" w:type="dxa"/></w:tblCellMar></w:tblPr><w:tblGrid>'
 for($i=0;$i -lt $heads.Count;$i++){$xml+='<w:gridCol w:w="'+$widths[$i]+'"/>'};$xml+='</w:tblGrid>'
 $all=New-Object 'System.Collections.Generic.List[object]';$all.Add([object[]]$heads)
 foreach($r in @($Rows)){$all.Add([object[]]$r)}
 if($all.Count -eq 1){$empty=New-Object string[] $heads.Count;$empty[$(if($heads[0] -eq 'N°' -and $heads.Count -gt 1){1}else{0})]='Non renseigné';$all.Add($empty)}
 for($row=0;$row -lt $all.Count;$row++){
  $xml+='<w:tr><w:trPr><w:cantSplit/>'+$(if($row -eq 0){'<w:tblHeader/>'}else{''})+'</w:trPr>'
  for($col=0;$col -lt $heads.Count;$col++){
   $value=$(if($col -lt $all[$row].Count){[string]$all[$row][$col]}else{''})
   $xml+='<w:tc><w:tcPr><w:tcW w:w="'+$widths[$col]+'" w:type="dxa"/>'+$(if($row -eq 0){'<w:shd w:fill="E8ECF0"/>'}else{''})+'</w:tcPr><w:p><w:pPr><w:ind w:left="0" w:firstLine="0"/><w:spacing w:after="40" w:line="264" w:lineRule="auto"/></w:pPr><w:r><w:rPr><w:sz w:val="'+$(if($heads.Count -gt 8){'16'}else{'20'})+'"/>'+$(if($row -eq 0){'<w:b/>'}else{''})+'</w:rPr><w:t xml:space="preserve">'+(ConvertTo-ScoliaRentreeXml (Resolve-ScoliaDocumentPersonnelText $value))+'</w:t></w:r></w:p></w:tc>'
  };$xml+='</w:tr>'
 };return ($xml+'</w:tbl>'+ (New-ScoliaRentreeParagraph ''))
}
function Get-ScoliaRentreeBody($Model,$Population,$Teaching=$null){
 $role=$(if($Model.Meta.PSObject.Properties['SignatoryRole']){[string]$Model.Meta.SignatoryRole}else{'Prefet'});$signatory=Get-ScoliaDocumentSignatory 'RapportRentree' $role
 $values=@{INSTITUT=(Get-ITMInstitutionName);ANNEE=$Model.Context.Year;ADRESSE=[string]$script:institutionProfile.Address;VILLE=$Model.Meta.Ville;LOCALISATION=$Model.Meta.Localisation;DATES=$Model.Meta.Dates;REUNION=$Model.Meta.Reunion;FORMATION=$Model.Meta.Formation;INTERNAT=$Model.Meta.Internat;SIGNATAIRE=($signatory.Label+"`n"+$signatory.Name);DATE=(Get-Date).ToString('dd/MM/yyyy');POPULATION=('Lʼeffectif retenu pour '+$Model.Context.Year+' est de '+$Population.Total+' apprenants : '+$Population.F+' de sexe féminin, '+$Population.M+' de sexe masculin et '+$Population.Unknown+' au sexe non renseigné. Les tableaux et listes sont établis à partir du registre SCOLIA au '+(Get-Date).ToString('dd/MM/yyyy HH:mm')+'. Les dossiers sortis et les renvois définitifs sont exclus ; les suspensions temporaires restent dans lʼeffectif inscrit. Il sʼagit de la situation actuelle du registre, et non dʼune reconstitution historique du jour de rentrée.')}
 $body='';foreach($line in @($script:institutionProfile.Header)){$body+=New-ScoliaRentreeParagraph $line 'CoverHeader'}
 $body+=New-ScoliaRentreeParagraph 'RAPPORT DE LA RENTRÉE SCOLAIRE' 'Title';$body+=New-ScoliaRentreeParagraph ($values.INSTITUT+' - Année scolaire '+$values.ANNEE) 'CoverYear'
 $body+=New-ScoliaRentreeParagraph ('Effectif : '+$Population.Total+' | F : '+$Population.F+' | M : '+$Population.M+' | Sexe non renseigné : '+$Population.Unknown) 'CoverSummary'
 $body+='<w:p><w:r><w:br w:type="page"/></w:r></w:p>'
 $inPlan=$true
 foreach($block in @($Model.Blocks)){
  if($block.kind -in @('text','heading')){
   $text=Resolve-ScoliaDocumentPersonnelText ([string]$block.text);if($text -ceq 'I. INTRODUCTION'){$inPlan=$false};foreach($key in $values.Keys){$text=$text.Replace('{'+$key+'}',[string]$values[$key])}
   $style=$(if($block.kind -eq 'heading'){if($text -match '^[IVX]+\. ' -or $text -in @('PLAN DU RAPPORT','ANNEXES')){'Heading1'}elseif($text -match '^\d+\.\d+\.\d+'){'Heading3'}else{'Heading2'}}elseif($inPlan){'PlanEntry'}elseif($text -like 'Fait à *'){'SignatureDate'}else{'Normal'});$body+=New-ScoliaRentreeParagraph $text $style
  }elseif($block.kind -eq 'signature'){$body+=New-ScoliaRentreeSignature $signatory}
  elseif($block.kind -eq 'table'){$body+=New-ScoliaRentreeTable $block.headers $block.rows}
  elseif($block.source -eq 'administration'){
   $rows=New-Object 'System.Collections.Generic.List[object]';$i=0
   foreach($role in @('Proviseur','Prefet','Secretaire')){$person=Get-ScoliaInstitutionSignatory $role;$i++;$rows.Add([object[]]@($i,$(if($person.Configured){$person.Name}else{'À renseigner'}),'À renseigner',$person.Label))}
   $body+=New-ScoliaRentreeTable $block.headers $rows.ToArray()
  }
  elseif($block.source -eq 'population'){
   $rows=New-Object 'System.Collections.Generic.List[object]';$i=0
   foreach($s in $Population.Stats){$i++;$rows.Add([object[]]@($i,$s.Label,$s.F,$s.M,$s.Unknown,$s.Total,'À préciser','À préciser'))}
   $rows.Add([object[]]@('','TOTAL',$Population.F,$Population.M,$Population.Unknown,$Population.Total,'Non renseigné','Non renseigné'))
   $body+=New-ScoliaRentreeTable @('N°','Section / Classe','F','M','Non précisé','Total','Nationaux','Étrangers') $rows.ToArray()
   $body+=New-ScoliaRentreeParagraph 'La nationalité ne figure pas dans le registre des apprenants ; elle ne peut pas être déduite du nom ou du lieu de naissance.'
  }elseif($block.source -eq 'learners'){
   foreach($g in @($Population.Learners|Group-Object {Promo ([string]$_.Promotion)}|Sort-Object Name)){
    $classLabel=$g.Name;$classStat=@($Population.Stats|Where-Object {$_.Promotion -eq $g.Name});if($classStat.Count){$classLabel=$classStat[0].Label}
    $body+=New-ScoliaRentreeParagraph ($classLabel+' - '+$g.Count+' apprenant(s)') 'Heading2'
    $rows=New-Object 'System.Collections.Generic.List[object]';$i=0;foreach($a in $g.Group){$i++;$rows.Add([object[]]@($i,$a.Matricule,($a.Nom+' '+$a.PostNom+' '+$a.Prenom).Trim(),$a.Sexe))}
    $body+=New-ScoliaRentreeTable @('N°','Matricule','Nom post-nom et prénom','Sexe') $rows.ToArray()
   }
   if(-not $Population.Total){$body+=New-ScoliaRentreeParagraph 'Aucun apprenant retenu pour cette année scolaire.'}
  }elseif($block.source -eq 'teachers'){
   $rows=New-Object 'System.Collections.Generic.List[object]';$i=0
   if($Teaching){foreach($t in @($Teaching.Personnel.Items|Where-Object {$_.Kind -eq 'Teacher'}|Sort-Object Nom)){$i++;$rows.Add([object[]]@($i,$t.Nom,'À préciser','À préciser','Enseignant'))}}
   $body+=New-ScoliaRentreeTable $block.headers $rows.ToArray()
  }elseif($block.source -eq 'courses'){
   $rows=New-Object 'System.Collections.Generic.List[object]'
   if($Teaching){foreach($c in @(Get-ScoliaTeachingRows $Teaching.Document $Teaching.Personnel)){$rows.Add([object[]]@($c.Promotion,$c.Course,$c.Teacher,$c.Hours,$c.State))}}
   $body+=New-ScoliaRentreeTable @('Classe','Matière','Enseignant','Heures prévues','État') $rows.ToArray()
  }elseif($block.source -eq 'fees'){
   $rows=New-Object 'System.Collections.Generic.List[object]';$n=0
   foreach($s in $Population.Stats){$fee=@($script:data.promotions|Where-Object {(Promo $_.Code) -eq $s.Promotion -and (Get-ScoliaRentreeYearKey $_.Annee) -eq (Get-ScoliaRentreeYearKey $Model.Context.Year)})
    if($fee.Count -gt 1){throw ('Tarif ambigu pour '+$s.Promotion)}
    $n++
    if($fee.Count -and -not [string]::IsNullOrWhiteSpace([string]$fee[0].Mensuel)){
     $amount=[decimal]$fee[0].Mensuel;if($amount -lt 0){throw ('Tarif mensuel négatif pour '+$s.Promotion)};$monthly=$amount*$s.Total
     $rows.Add([object[]]@($n,('Frais mensuels - '+$s.Label),$s.Total,$amount,$monthly,($monthly*3),($monthly*[int]$Model.Meta.Mois),'Devise du tarif SCOLIA'))
    }else{$rows.Add([object[]]@($n,('Frais mensuels - '+$s.Label),$s.Total,'Non renseigné','Non renseigné','Non renseigné','Non renseigné','Tarif absent'))}
   }
   $body+=New-ScoliaRentreeTable $block.headers $rows.ToArray();$body+=New-ScoliaRentreeParagraph ('Hypothèse : '+$Model.Meta.Mois+' mois facturables, tous les inscrits concernés, avant exonérations et prises en charge. Les montants conservent la devise des tarifs SCOLIA. Les autres recettes sont à renseigner ci-dessous.')
  }
 }
 return $body
}
function Write-ScoliaRentreeDocx($Model,$Population,[string]$Path,$Teaching=$null){
 $body=Get-ScoliaRentreeBody $Model $Population $Teaching
 $temp=Join-Path ([IO.Path]::GetTempPath()) ('scolia-rentree-'+[Guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($temp)
 try{
  [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'Assets/RapportRentreeBase.docx'),$temp)
  $docPath=Join-Path $temp 'word/document.xml';[xml]$xml=[IO.File]::ReadAllText($docPath)
  $ns=New-Object Xml.XmlNamespaceManager $xml.NameTable;$ns.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
  $node=$xml.SelectSingleNode('//w:body/w:p[w:r/w:t="{BODY}"]',$ns);if(-not $node){throw 'Modèle du rapport incomplet.'}
  [xml]$generated=('<root xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'+$body+'</root>')
  foreach($part in @($generated.DocumentElement.ChildNodes)){[void]$node.ParentNode.InsertBefore($xml.ImportNode($part,$true),$node)};[void]$node.ParentNode.RemoveChild($node)
  $xml.Save($docPath)
  foreach($partFile in @(Get-ChildItem -LiteralPath (Join-Path $temp 'word') -File|Where-Object {$_.Name -match '^(header|footer)\d+\.xml$'})){
   [xml]$partXml=[IO.File]::ReadAllText($partFile.FullName)
   foreach($t in @($partXml.GetElementsByTagName('t','http://schemas.openxmlformats.org/wordprocessingml/2006/main'))){$t.InnerText=$t.InnerText.Replace('{INSTITUT}',(Get-ITMInstitutionName)).Replace('{ANNEE}',$Model.Context.Year)}
   $partXml.Save($partFile.FullName)
  }
  # Existing logo relationship points to a packaged image. No source institution image remains.
  $logo=Get-ITMInstitutionLogo;if($logo -and (Test-Path -LiteralPath $logo)){Add-Type -AssemblyName System.Drawing;$image=[Drawing.Image]::FromFile($logo);try{$image.Save((Join-Path $temp 'word/media/image1.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$image.Dispose()}}
  $zip=Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.rentree-'+[Guid]::NewGuid().ToString('N')+'.docx')
  try{[IO.Compression.ZipFile]::CreateFromDirectory($temp,$zip);Move-Item -LiteralPath $zip -Destination $Path -Force}finally{if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip -Force}}
 }finally{Remove-Item -LiteralPath $temp -Recurse -Force}
}
function Export-ScoliaRentreePdf([string]$Docx,[string]$Pdf){
 $word=$null;$doc=$null
 try{
  try{$word=New-Object -ComObject Word.Application -ErrorAction Stop}catch{throw 'Lʼexport PDF nécessite Microsoft Word installé sur ce poste. Le Word modifiable peut être exporté sans Word.'}
  $word.Visible=$false;$word.DisplayAlerts=0
  $doc=$word.Documents.Open($Docx,$false,$true);[void]$doc.Fields.Update();$doc.Repaginate()
  $temp=Join-Path ([IO.Path]::GetDirectoryName($Pdf)) ('.rentree-'+[Guid]::NewGuid().ToString('N')+'.pdf')
  try{$doc.ExportAsFixedFormat($temp,17);if(-not (Test-Path -LiteralPath $temp)){throw 'Word nʼa pas créé le PDF.'};Move-Item -LiteralPath $temp -Destination $Pdf -Force}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
 }finally{if($doc){$doc.Close(0);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($doc)};if($word){$word.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word)}}
}
function Save-ScoliaRentreeEditor($State){
 if($State.Editing -lt 0){return};$b=$State.Model.Blocks[$State.Editing]
 if($b.kind -in @('text','heading')){$b.text=$State.Editor.Text}
 elseif($b.kind -eq 'table'){
  $rows=New-Object 'System.Collections.Generic.List[object]'
  foreach($line in @($State.Editor.Text -split "`r?`n")){
   if(-not $line.Trim()){continue};$cells=@($line.Split([char]9))
   if($cells.Count -ne @($b.headers).Count){throw ('Tableau : chaque ligne doit contenir '+@($b.headers).Count+' colonnes séparées par une tabulation. Utilisez la touche Tab.')}
   $rows.Add([object[]]$cells)
  };$b.rows=$rows.ToArray()
 }
 foreach($field in $State.Fields.Keys){if($field -eq 'SignatoryRole'){$State.Model.Meta.SignatoryRole=[string]$State.Fields[$field].SelectedItem.Role}else{$State.Model.Meta.$field=$State.Fields[$field].Text.Trim()}}
 $months=0;if(-not [int]::TryParse([string]$State.Model.Meta.Mois,[ref]$months) -or $months -lt 1 -or $months -gt 12){throw 'Le nombre de mois facturables doit être compris entre 1 et 12.'};$State.Model.Meta.Mois=$months
}
function Set-ScoliaRentreeEditor($State){
 $index=$State.List.SelectedIndex;if($index -lt 0){return}
 $b=$State.Model.Blocks[$index];$State.Editing=$index
 if($b.kind -eq 'table'){
  $State.Hint.Text='Colonnes : '+($b.headers -join ' | ')+'. Une ligne par enregistrement ; Tab sépare les colonnes. Copier-coller depuis Excel est possible.'
  $State.Editor.ReadOnly=$false;$State.Editor.Text=(@($b.rows|ForEach-Object {@($_) -join "`t"}) -join "`r`n")
 }elseif($b.kind -eq 'auto' -or $b.kind -eq 'signature'){$State.Hint.Text='Rubrique automatique : '+$b.source;$State.Editor.ReadOnly=$true;$State.Editor.Text='Cette rubrique est calculée au moment de lʼexport à partir du registre connecté. Elle ne peut pas être modifiée ici.'}
 else{$State.Hint.Text='Texte modifiable. Les mentions {INSTITUT}, {ANNEE}, {ADRESSE} et les autres champs entre accolades sont remplacés automatiquement.';$State.Editor.ReadOnly=$false;$State.Editor.Text=[string]$b.text}
}
function Assert-ScoliaRentreeContext($State){
 Assert-ITMPermission 'Exporter';$now=Get-ScoliaFeatureContext
 if($now.Id -cne $State.Context.Id -or $now.Year -cne $State.Context.Year -or $now.LocalKey -cne $State.Context.LocalKey){throw 'La base, lʼannée ou le compte a changé. Rouvrez la préparation du rapport.'}
}
function Save-ScoliaRentreePreparation($State){
 Assert-ScoliaRentreeContext $State;Save-ScoliaRentreeEditor $State
 $path=Get-ScoliaRentreeSettingsPath $State.Context;$temp=$path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
 try{[IO.File]::WriteAllText($temp,($State.Model|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($true));Move-Item -LiteralPath $temp -Destination $path -Force}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
 $State.Message.Text='Préparation conservée pour cet institut, cette année et ce compte.'
}
function Save-ScoliaRentreeExport($State,[ValidateSet('docx','pdf')][string]$Format){
 Assert-ScoliaRentreeContext $State;Save-ScoliaRentreeEditor $State
 $picker=New-Object Windows.Forms.SaveFileDialog;$picker.DefaultExt=$Format;$picker.AddExtension=$true;$picker.OverwritePrompt=$true
 $picker.Filter=$(if($Format -eq 'pdf'){'Document PDF (*.pdf)|*.pdf'}else{'Document Word modifiable (*.docx)|*.docx'})
 $folder=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'exports/rapports-rentree';[void][IO.Directory]::CreateDirectory($folder);$picker.InitialDirectory=$folder
 $name=(Get-ITMInstitutionName) -replace '[^\p{L}\p{N}_-]','_';$picker.FileName='Rapport_rentree_'+$name+'_'+$State.Context.Year+'.'+$Format
 try{
  if($picker.ShowDialog($State.Form) -ne 'OK'){return}
  $script:rentreeWork=[pscustomobject]@{State=$State;Path=$picker.FileName;Format=$Format}
  $State.Word.Enabled=$false;$State.Pdf.Enabled=$false;$State.Save.Enabled=$false
  Do-Work {
   $w=$script:rentreeWork;Assert-ScoliaRentreeContext $w.State
   Refresh-Data $true
   if(Test-Path function:Sync-ScoliaDisciplineSnapshot){[void](Sync-ScoliaDisciplineSnapshot)}
   $pop=Get-ScoliaRentreePopulation $script:data $w.State.Context.Year
   $teaching=$null
   if($script:store -and (-not $script:scolia -or (Test-ScoliaPermission 'Scolarite'))){$teaching=Read-ScoliaTeachingState}
   $summary='Année : '+$w.State.Context.Year+"`r`n"+(Get-ITMInstitutionName)+"`r`n"+$pop.Total+' inscrits, F : '+$pop.F+', M : '+$pop.M+', sexe non renseigné : '+$pop.Unknown+"`r`nToutes les promotions de lʼinstitut. Situation actuelle du registre ; sortis exclus, suspensions temporaires incluses.`r`nLes informations non renseignées apparaîtront comme telles. Confirmer lʼexport ?"
   if([ITMMessageBox]::Show($w.State.Form,$summary,'Contrôle du rapport de rentrée','OKCancel','Information') -ne 'OK'){return}
   $tempDoc=Join-Path ([IO.Path]::GetTempPath()) ('rentree-'+[Guid]::NewGuid().ToString('N')+'.docx')
   try{
    if($w.Format -eq 'docx'){Write-ScoliaRentreeDocx $w.State.Model $pop $w.Path $teaching}
    else{Write-ScoliaRentreeDocx $w.State.Model $pop $tempDoc $teaching;Export-ScoliaRentreePdf $tempDoc $w.Path}
   }finally{if(Test-Path -LiteralPath $tempDoc){Remove-Item -LiteralPath $tempDoc -Force}}
   $script:committedThisAction=$true;$script:lastReport=$w.Path;$script:status.Text='Rapport de rentrée exporté : '+$w.Path;$w.State.Message.Text=$script:status.Text
   try{Write-ITMUserAudit ('Rapport de rentrée '+$w.Format+' - '+$pop.Total+' inscrits')}catch{$w.State.Message.Text+=' (journal indisponible)'}
  } 'Création du rapport de rentrée...'
 }finally{$picker.Dispose();$State.Word.Enabled=$true;$State.Pdf.Enabled=$true;$State.Save.Enabled=$true;$script:rentreeWork=$null}
}
function Show-ScoliaRentreeDialog([bool]$PdfFirst=$false){
 Assert-ITMPermission 'Exporter';$context=Get-ScoliaFeatureContext;$model=Update-ScoliaRentreeLayoutModel (Get-ScoliaRentreeModel $context)
 $form=New-Object Windows.Forms.Form;$form.Text='Rapport de la rentrée - '+(Get-ITMInstitutionName)+' - '+$context.Year;$form.Size=New-Object Drawing.Size(1080,740);$form.MinimumSize=New-Object Drawing.Size(620,440);$form.StartPosition='CenterParent';$form.AutoScaleMode='Dpi'
 $state=[pscustomobject]@{Form=$form;Context=$context;Model=$model;Editing=-1;Fields=@{};List=$null;Editor=$null;Hint=$null;Message=$null;Word=$null;Pdf=$null;Save=$null};$form.Tag=$state
 $layout=New-Object Windows.Forms.TableLayoutPanel;$layout.Dock='Fill';$layout.RowCount=3;$layout.ColumnCount=1
 [void]$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('Percent',100));[void]$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('AutoSize'));[void]$layout.RowStyles.Add([Windows.Forms.RowStyle]::new('AutoSize'));$form.Controls.Add($layout)
 $tabs=New-Object Windows.Forms.TabControl;$tabs.Dock='Fill';$layout.Controls.Add($tabs,0,0)
 $identity=New-Object Windows.Forms.TabPage;$identity.Text='Informations de rentrée';$identity.AutoScroll=$true;[void]$tabs.TabPages.Add($identity)
 $fields=New-Object Windows.Forms.TableLayoutPanel;$fields.Dock='Top';$fields.AutoSize=$true;$fields.ColumnCount=1;$fields.Padding=New-Object Windows.Forms.Padding(12);$identity.Controls.Add($fields)
 foreach($pair in @(@('Ville','Lieu de signature'),@('Localisation','Situation géographique détaillée'),@('Dates','Dates de rentrée et de clôture'),@('Reunion','Réunion de rentrée'),@('Formation','Formation pédagogique'),@('Internat','Situation de lʼinternat'),@('SignatoryRole','Fonction du signataire (nom automatique depuis Administration)'),@('Mois','Nombre de mois facturables pour la prévision des frais mensuels'))){
  $label=New-Object Windows.Forms.Label;$label.Text=$pair[1];$label.AutoSize=$true;$label.Dock='Top';$fields.Controls.Add($label)
  if($pair[0] -eq 'SignatoryRole'){
   $roleBox=New-Object Windows.Forms.ComboBox;$roleBox.Dock='Top';$roleBox.DropDownStyle='DropDownList';$roleBox.DisplayMember='Label'
   foreach($r in @('Prefet','Proviseur','Secretaire')){$person=Get-ScoliaInstitutionSignatory $r;$display=$person.Label+' - '+$(if($person.Configured){$person.Name}else{'nom à renseigner dans Administration'});$item=[pscustomobject]@{Role=$r;Label=$display};[void]$roleBox.Items.Add($item);if($r -eq $model.Meta.SignatoryRole){$roleBox.SelectedItem=$item}}
   if($roleBox.SelectedIndex -lt 0){$roleBox.SelectedIndex=0};$fields.Controls.Add($roleBox);$state.Fields['SignatoryRole']=$roleBox;continue
  }
  $text=New-Object Windows.Forms.TextBox;$text.Text=[string]$model.Meta.($pair[0]);$text.Dock='Top';$text.Margin=New-Object Windows.Forms.Padding(0,4,0,12);$text.Multiline=$pair[0] -ne 'Mois';$text.Height=$(if($text.Multiline){55}else{28});$fields.Controls.Add($text);$state.Fields[$pair[0]]=$text
 }
 $page=New-Object Windows.Forms.TabPage;$page.Text='Canevas textes et tableaux';[void]$tabs.TabPages.Add($page)
 $editorLayout=New-Object Windows.Forms.TableLayoutPanel;$editorLayout.Dock='Fill';$editorLayout.ColumnCount=1;$editorLayout.RowCount=3
 [void]$editorLayout.RowStyles.Add([Windows.Forms.RowStyle]::new('Absolute',140));[void]$editorLayout.RowStyles.Add([Windows.Forms.RowStyle]::new('Absolute',65));[void]$editorLayout.RowStyles.Add([Windows.Forms.RowStyle]::new('Percent',100));$page.Controls.Add($editorLayout)
 $list=New-Object Windows.Forms.ListBox;$list.Dock='Fill';$list.HorizontalScrollbar=$true;$state.List=$list;$editorLayout.Controls.Add($list,0,0)
 foreach($b in $model.Blocks){[void]$list.Items.Add(($list.Items.Count+1).ToString()+' - '+$b.label)}
 $hint=New-Object Windows.Forms.Label;$hint.Dock='Fill';$hint.Padding=New-Object Windows.Forms.Padding(8);$state.Hint=$hint;$editorLayout.Controls.Add($hint,0,1)
 $editor=New-Object Windows.Forms.TextBox;$editor.Multiline=$true;$editor.AcceptsTab=$true;$editor.AcceptsReturn=$true;$editor.ScrollBars='Both';$editor.Dock='Fill';$editor.WordWrap=$true;$state.Editor=$editor;$editorLayout.Controls.Add($editor,0,2)
 $list.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;try{Save-ScoliaRentreeEditor $s;Set-ScoliaRentreeEditor $s}catch{App-Error $_}})
 $actions=New-Object Windows.Forms.FlowLayoutPanel;$actions.Dock='Fill';$actions.AutoSize=$true;$layout.Controls.Add($actions,0,1)
 foreach($entry in @(@('Save','Conserver la préparation'),@('Word','Exporter en Word'),@('Pdf','Exporter en PDF'))){$button=New-Object Windows.Forms.Button;$button.Text=$entry[1];$button.AutoSize=$true;$button.Padding=New-Object Windows.Forms.Padding(12,7,12,7);$actions.Controls.Add($button);$state.($entry[0])=$button}
 $state.Save.Add_Click({param($sender,$e)try{Save-ScoliaRentreePreparation $sender.FindForm().Tag}catch{App-Error $_}})
 $state.Word.Add_Click({param($sender,$e)try{Save-ScoliaRentreeExport $sender.FindForm().Tag 'docx'}catch{App-Error $_}})
 $state.Pdf.Add_Click({param($sender,$e)try{Save-ScoliaRentreeExport $sender.FindForm().Tag 'pdf'}catch{App-Error $_}})
 $message=New-Object Windows.Forms.Label;$message.Dock='Fill';$message.AutoSize=$true;$message.MaximumSize=New-Object Drawing.Size(900,0);$message.Padding=New-Object Windows.Forms.Padding(10);$message.Text='Les effectifs sont actualisés à chaque export. Renseignez les rubriques propres à lʼinstitut. PDF : Microsoft Word requis. Les filtres promotion et mois de la page Exports ne sʼappliquent pas.';$state.Message=$message;$layout.Controls.Add($message,0,2)
 Apply-ITMThemeForm $form;$list.SelectedIndex=0;$form.ActiveControl=$state.List
 try{[void]$form.ShowDialog($script:main)}finally{$form.Dispose()}
}
