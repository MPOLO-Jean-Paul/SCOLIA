# Cours par promotion, attribution unique et charge pédagogique annuelle.
function Get-ScoliaTeachingKey([string]$Value){
 return ((ConvertTo-ScoliaSearchKey $Value).Trim() -replace '\s+',' ')
}
function Assert-ScoliaTeachingAccess([switch]$Write){
 Assert-ITMPermission 'Lire'
 if($script:scolia -and -not (Test-ScoliaPermission 'Scolarite')){throw 'La gestion pédagogique est réservée à l espace du secrétariat.'}
 if($Write){Assert-ITMPermission 'Saisie';if($script:data.closed){throw 'Année clôturée : les cours et attributions restent consultables.'}}
 if(-not $script:store){throw 'Connectez le dossier data scolaire préparé avant de gérer les cours.'}
}
function Get-ScoliaTeachingPromotions {
 $context=Get-ScoliaFeatureContext
 return @($script:data.promotions|Where-Object {$_.Active -and [string]$_.Annee -ceq $context.Year}|ForEach-Object {Get-ScoliaTeachingKey ([string]$_.Code)}|Where-Object {$_}|Sort-Object -Unique)
}
function Assert-ScoliaCourse([object]$Course){
 if(-not $Course -or $Course.Kind -cne 'Course' -or (ConvertTo-ScoliaPersonnelId ([string]$Course.Id)) -cne [string]$Course.Id){throw 'Identité du cours invalide.'}
 if([string]::IsNullOrWhiteSpace([string]$Course.Promotion) -or [string]$Course.Promotion -cne (Get-ScoliaTeachingKey ([string]$Course.Promotion)) -or ([string]$Course.Promotion).Length -gt 32){throw 'Promotion du cours invalide.'}
 if([string]$Course.Number -notmatch '^[1-9][0-9]{0,3}$'){throw 'Numéro du cours : entier de 1 à 9999.'}
 if([string]::IsNullOrWhiteSpace([string]$Course.Name) -or ([string]$Course.Name).Length -gt 160){throw 'Nom du cours obligatoire, limité à 160 caractères.'}
 foreach($field in @('Hours','Maximum')){
  $value=[decimal]0
  if(-not [decimal]::TryParse([string]$Course.$field,[Globalization.NumberStyles]::Number,[Globalization.CultureInfo]::InvariantCulture,[ref]$value) -or $value -le 0 -or $value -gt 10000 -or [Math]::Round($value,2) -ne $value){throw 'Volume horaire et cotation maximale : nombres positifs, au plus 10000 et deux décimales.'}
 }
 if($Course.Active -isnot [bool]){throw 'État du cours invalide.'}
}
function Assert-ScoliaTeachingDocument([object]$Document,[object]$Personnel){
 Assert-ScoliaFeatureDocument $Document $Document.Context;Assert-ScoliaPersonnelDocument $Personnel
 if($Personnel.Context.Id -cne $Document.Context.Id -or $Personnel.Context.Year -cne $Document.Context.Year){throw 'Les enseignants et les cours appartiennent à des bases ou années différentes.'}
 $ids=@{};$numbers=@{};$titles=@{};$courses=@{};$assigned=@{}
 $teachers=@{};foreach($teacher in @($Personnel.Items|Where-Object {$_.Kind -ceq 'Teacher'})){$teachers[[string]$teacher.Id]=$teacher}
 foreach($item in @($Document.Items)){
  if(-not $item -or $item.Kind -cnotin @('Course','Assignment')){throw 'Donnée pédagogique inconnue.'}
  $id=ConvertTo-ScoliaPersonnelId ([string]$item.Id)
  if($id -cne [string]$item.Id -or $ids.ContainsKey($id)){throw 'Identifiant pédagogique dupliqué ou invalide.'};$ids[$id]=$true
  if($item.Kind -ceq 'Course'){
   Assert-ScoliaCourse $item;$key=[string]$item.Promotion+'|'+[string]$item.Number
   if($numbers.ContainsKey($key)){throw 'Un numéro de cours existe déjà dans cette promotion.'};$numbers[$key]=$true;$courses[$id]=$item
   $title=[string]$item.Promotion+'|'+(Get-ScoliaTeachingKey ([string]$item.Name))
   if($item.Active){if($titles.ContainsKey($title)){throw 'Ce cours existe déjà dans cette promotion.'};$titles[$title]=$true}
  }
 }
 foreach($item in @($Document.Items|Where-Object {$_.Kind -ceq 'Assignment'})){
  if($item.Active -isnot [bool] -or -not $courses.ContainsKey([string]$item.CourseId) -or -not $teachers.ContainsKey([string]$item.TeacherId)){throw 'Attribution sans cours ou enseignant valide.'}
  if($item.Active){
   if(-not $courses[[string]$item.CourseId].Active){throw 'Un cours archivé ne peut pas rester attribué.'}
   if($assigned.ContainsKey([string]$item.CourseId)){throw 'Ce cours est déjà attribué à un enseignant dans cette promotion.'}
   $assigned[[string]$item.CourseId]=$true
  }
 }
}
function New-ScoliaCourse {
 param([string]$Promotion,[int]$Number,[string]$Name,[decimal]$Hours,[decimal]$Maximum,[string]$Id='',[bool]$Active=$true)
 $record=[pscustomobject]@{Kind='Course';Id=$(if($Id){ConvertTo-ScoliaPersonnelId $Id}else{[guid]::NewGuid().ToString('D')});Promotion=(Get-ScoliaTeachingKey $Promotion);Number=$Number;Name=$Name.Trim();Hours=$Hours;Maximum=$Maximum;Active=$Active;Audit=@()}
 Assert-ScoliaCourse $record;return $record
}
function Add-ScoliaTeachingAudit($Record,$Previous,[string]$Actor,[datetimeoffset]$Now){
 Assert-ScoliaPersonnelActor $Actor
 $fields=$(if($Record.Kind -ceq 'Course'){@('Promotion','Number','Name','Hours','Maximum','Active')}else{@('CourseId','TeacherId','Active')})
 $before=[ordered]@{};$after=[ordered]@{};foreach($field in $fields){if($Previous){$before[$field]=$Previous.$field};$after[$field]=$Record.$field}
 $priorAudit=@();if($Previous){$priorAudit=@($Previous.Audit)}
 $Record.Audit=$priorAudit+@([pscustomobject]@{At=$Now.ToString('o');By=$Actor;Action=$(if($Previous){'Update'}else{'Create'});Before=[pscustomobject]$before;After=[pscustomobject]$after})
}
function Set-ScoliaCourse {
 param($Document,$Personnel,$Course,[int]$ExpectedRevision,[string[]]$Promotions,[string]$Actor,[datetimeoffset]$Now=[datetimeoffset]::Now)
 Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaTeachingDocument $Document $Personnel;Assert-ScoliaCourse $Course
 if($Course.Promotion -cnotin $Promotions){throw 'Choisissez une promotion active de cette année scolaire.'}
 if(@($Document.Items|Where-Object {$_.Id -ceq $Course.Id -and $_.Kind -cne 'Course'}).Count){throw 'Identifiant déjà utilisé pour une autre donnée pédagogique.'}
 $previous=@($Document.Items|Where-Object {$_.Kind -ceq 'Course' -and $_.Id -ceq $Course.Id})
 if($previous.Count -and $previous[0].Promotion -cne $Course.Promotion){throw 'La promotion d un cours enregistré est fixe. Créez le cours dans la nouvelle classe.'}
 $copy=New-ScoliaCourse -Promotion $Course.Promotion -Number $Course.Number -Name $Course.Name -Hours $Course.Hours -Maximum $Course.Maximum -Id $Course.Id -Active $Course.Active
 Add-ScoliaTeachingAudit $copy $(if($previous.Count){$previous[0]}else{$null}) $Actor $Now
 $candidate=[pscustomobject]@{Schema=$Document.Schema;Context=$Document.Context;Revision=$Document.Revision;Items=@($Document.Items|Where-Object {$_.Id -cne $copy.Id})+@($copy)}
 Assert-ScoliaTeachingDocument $candidate $Personnel;$Document.Items=$candidate.Items
}
function Set-ScoliaCourseAssignment {
 param($Document,$Personnel,[string]$CourseId,[string]$TeacherId,[int]$ExpectedRevision,[string]$Actor,[string]$Id='',[bool]$Active=$true,[datetimeoffset]$Now=[datetimeoffset]::Now)
 Assert-ScoliaPersonnelRevision $Document $ExpectedRevision;Assert-ScoliaTeachingDocument $Document $Personnel
 $courses=@($Document.Items|Where-Object {$_.Kind -ceq 'Course' -and $_.Id -ceq $CourseId});$teachers=@($Personnel.Items|Where-Object {$_.Kind -ceq 'Teacher' -and $_.Id -ceq $TeacherId})
 if($courses.Count -ne 1 -or $teachers.Count -ne 1){throw 'Sélectionnez un cours et un enseignant du registre.'}
 $assignmentId=$(if($Id){ConvertTo-ScoliaPersonnelId $Id}else{[guid]::NewGuid().ToString('D')})
 if(@($Document.Items|Where-Object {$_.Id -ceq $assignmentId -and $_.Kind -cne 'Assignment'}).Count){throw 'Identifiant déjà utilisé pour une autre donnée pédagogique.'}
 $previous=@($Document.Items|Where-Object {$_.Kind -ceq 'Assignment' -and $_.Id -ceq $assignmentId})
 if($previous.Count -and $previous[0].CourseId -cne $CourseId){throw 'Le cours d une attribution enregistrée est fixe.'}
 if($Active){
  if(-not $courses[0].Active){throw 'Réactivez le cours avant de l attribuer.'}
  $bounds=Get-ScoliaAttendanceYearBounds $Document.Context.Year
  $start=ConvertTo-ScoliaPersonnelDate $teachers[0].ActiveFrom;$end=$(if($teachers[0].ActiveTo){ConvertTo-ScoliaPersonnelDate $teachers[0].ActiveTo}else{[datetime]::MaxValue.Date})
  if($start -gt $bounds.End -or $end -lt $bounds.Start){throw 'Cet enseignant est hors de sa période d activité pour cette année scolaire.'}
 }
 $copy=[pscustomobject]@{Kind='Assignment';Id=$assignmentId;CourseId=$CourseId;TeacherId=$TeacherId;Active=$Active;Audit=@()}
 Add-ScoliaTeachingAudit $copy $(if($previous.Count){$previous[0]}else{$null}) $Actor $Now
 $candidate=[pscustomobject]@{Schema=$Document.Schema;Context=$Document.Context;Revision=$Document.Revision;Items=@($Document.Items|Where-Object {$_.Id -cne $copy.Id})+@($copy)}
 Assert-ScoliaTeachingDocument $candidate $Personnel;$Document.Items=$candidate.Items
}
function Read-ScoliaTeachingState {
 Assert-ScoliaTeachingAccess
 $context=Get-ScoliaFeatureContext;$document=Read-ScoliaFeatureStore -Name enseignements;$personnel=Read-ScoliaFeatureStore -Name personnel
 Assert-ScoliaTeachingDocument $document $personnel
 return [pscustomobject]@{Context=$context;Document=$document;Personnel=$personnel;Promotions=@(Get-ScoliaTeachingPromotions)}
}
function Invoke-ScoliaTeachingUpdate([scriptblock]$Change,[int]$ExpectedRevision){
 Assert-ScoliaTeachingAccess -Write;$context=Get-ScoliaFeatureContext;$coursePath=Get-ScoliaFeatureStorePath enseignements;$gates=@()
 try{
  try{$gates+=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Une sauvegarde, restauration ou un nettoyage est en cours. Réessayez après sa fin.'}
  foreach($name in @('personnel','enseignements')){
   $path=Get-ScoliaFeatureStorePath -Name $name;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
   try{$gates+=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}catch{throw 'Un autre utilisateur enregistre les enseignants ou les cours. Actualisez puis réessayez.'}
  }
  $state=Read-ScoliaTeachingState;Assert-ScoliaPersonnelRevision $state.Document $ExpectedRevision
  [void](& $Change $state.Document $state.Personnel $state.Promotions)
  Assert-ScoliaTeachingDocument $state.Document $state.Personnel;Assert-ScoliaFeatureDocument $state.Document $context
  $current=Get-ScoliaFeatureContext
  if($current.Id -cne $context.Id -or $current.Year -cne $context.Year -or $current.LocalKey -cne $context.LocalKey -or (Get-ScoliaFeatureStorePath enseignements) -cne $coursePath){throw 'Base, année ou compte modifié : enregistrement interrompu.'}
  Assert-ScoliaTeachingAccess -Write;$state.Document.Revision=$ExpectedRevision+1
  Write-ITMAtomicJson $coursePath $state.Document;return $state.Document
 }finally{for($i=$gates.Count-1;$i -ge 0;$i--){$gates[$i].Dispose()}}
}
function Get-ScoliaTeachingRows($Document,$Personnel,[string]$Promotion='',[switch]$IncludeArchived){
 Assert-ScoliaTeachingDocument $Document $Personnel;$teachers=@{};foreach($t in @($Personnel.Items|Where-Object {$_.Kind -ceq 'Teacher'})){$teachers[$t.Id]=$t}
 return @(foreach($course in @($Document.Items|Where-Object {$_.Kind -ceq 'Course'}|Sort-Object Promotion,Number,Name)){
  if(($Promotion -and $course.Promotion -cne $Promotion) -or (-not $IncludeArchived -and -not $course.Active)){continue}
  $assignments=@($Document.Items|Where-Object {$_.Kind -ceq 'Assignment' -and $_.CourseId -ceq $course.Id -and $_.Active});$assignment=$(if($assignments.Count){$assignments[0]}else{$null})
  [pscustomobject]@{Key=$course.Id;Promotion=$course.Promotion;Number=$course.Number;Course=$course.Name;Hours=[decimal]$course.Hours;Maximum=[decimal]$course.Maximum;Teacher=$(if($assignment){$teachers[$assignment.TeacherId].Nom}else{'Non attribué'});State=$(if($course.Active){'Actif'}else{'Archivé'});TeacherId=$(if($assignment){$assignment.TeacherId}else{''});AssignmentId=$(if($assignment){$assignment.Id}else{''})}
 })
}
function Get-ScoliaTeacherTeachingSummary($Document,$Personnel){
 $rows=@(Get-ScoliaTeachingRows $Document $Personnel)
 return @(foreach($teacher in @($Personnel.Items|Where-Object {$_.Kind -ceq 'Teacher'}|Sort-Object Nom,Id)){
  $own=@($rows|Where-Object {$_.TeacherId -ceq $teacher.Id});$hours=[decimal]0;foreach($row in $own){$hours+=$row.Hours}
  [pscustomobject]@{Key=$teacher.Id;Teacher=$teacher.Nom;Courses=$own.Count;Promotions=@($own.Promotion|Sort-Object -Unique).Count;Hours=$hours;Details=$own;ActiveFrom=$teacher.ActiveFrom;ActiveTo=$teacher.ActiveTo}
 })
}

function Get-ScoliaNextCourseNumber($Document,[string]$Promotion){
 $used=@{};foreach($course in @($Document.Items|Where-Object {$_.Kind -eq 'Course' -and $_.Promotion -ceq $Promotion})){$used[[int]$course.Number]=$true}
 for($number=1;$number -le 9999;$number++){if(-not $used.ContainsKey($number)){return $number}}
 throw 'Tous les numéros de cours disponibles dans cette classe sont utilisés.'
}
