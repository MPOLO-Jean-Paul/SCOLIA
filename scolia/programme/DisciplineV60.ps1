# Registre disciplinaire : décisions auditées et effets calculés sans double écriture Excel.
function Assert-ScoliaDisciplineAccess([switch]$Write){
 Assert-ITMPermission 'Lire'
 if($script:scolia -and -not (Test-ScoliaPermission 'Scolarite')){throw 'La discipline est réservée à l espace scolaire du secrétariat.'}
 if(-not $script:store){throw 'Connectez le dossier data scolaire avant d ouvrir Discipline.'}
 if($Write){Assert-ITMPermission 'Saisie';if($script:data.closed){throw 'Année clôturée : le registre disciplinaire reste consultable uniquement.'}}
}
function Assert-ScoliaDisciplineRevision($Document,[int]$ExpectedRevision){
 if(-not $Document -or [string]$Document.Revision -notmatch '^\d+$' -or [int]$Document.Revision -ne $ExpectedRevision){throw 'Le registre disciplinaire a changé. Actualisez avant de valider la décision.'}
}
function Get-ScoliaDisciplineMat([string]$Matricule){return $Matricule.Trim().ToUpperInvariant()}
function Get-ScoliaDisciplineDate([string]$Value){return ConvertTo-ScoliaPersonnelDate $Value}
function Assert-ScoliaDisciplineDocument($Document){
 Assert-ScoliaFeatureDocument $Document $Document.Context;$ids=@{}
 foreach($r in @($Document.Items)){
  if(-not $r -or $r.Kind -cne 'Discipline' -or (ConvertTo-ScoliaPersonnelId $r.Id) -cne [string]$r.Id -or $ids.ContainsKey([string]$r.Id)){throw 'Identité disciplinaire invalide ou dupliquée.'};$ids[$r.Id]=$true
  if([string]$r.Year -cne [string]$Document.Context.Year -or [string]::IsNullOrWhiteSpace([string]$r.Matricule) -or $r.Matricule -cne (Get-ScoliaDisciplineMat $r.Matricule)){throw 'Année ou matricule disciplinaire invalide.'}
  if([string]::IsNullOrWhiteSpace([string]$r.Identity) -or [string]::IsNullOrWhiteSpace([string]$r.Promotion) -or ([string]$r.Reason).Trim().Length -lt 5 -or ([string]$r.Reason).Length -gt 2000){throw 'Identité, promotion et motif disciplinaire obligatoires.'}
  if($r.RequestedType -cnotin @('Temporary','Permanent','Parents') -or $r.Type -cnotin @('Temporary','Permanent','Parents') -or $r.State -cnotin @('Validated','Cancelled') -or $r.Automatic -isnot [bool]){throw 'Type ou état disciplinaire invalide.'}
  if(($r.RequestedType -ceq 'Parents' -and $r.Type -cne 'Parents') -or ($r.RequestedType -ceq 'Permanent' -and $r.Type -cne 'Permanent') -or ($r.RequestedType -ceq 'Temporary' -and $r.Type -cnotin @('Temporary','Permanent'))){throw 'Transformation de sanction invalide.'}
  if($r.Automatic -ne ($r.RequestedType -ceq 'Temporary' -and $r.Type -ceq 'Permanent') -or ($r.Automatic -and [int]$r.TemporaryOrdinal -lt 2) -or ($r.Type -ceq 'Temporary' -and [int]$r.TemporaryOrdinal -ne 1)){throw 'Règle de récidive disciplinaire incohérente.'}
  $bounds=Get-ScoliaAttendanceYearBounds $r.Year ([int]$r.StartMonth);$decision=Get-ScoliaDisciplineDate $r.DecisionDate
  if($decision -lt $bounds.Start -or $decision -gt $bounds.End){throw 'Décision hors de l année scolaire.'}
  if($r.RequestedType -ceq 'Temporary'){
   $start=Get-ScoliaDisciplineDate $r.StartDate;$end=Get-ScoliaDisciplineDate $r.EndDate
   if($start -lt $decision -or $end -lt $start -or $end -gt $bounds.End){throw 'Période de renvoi temporaire invalide.'}
  }elseif($r.StartDate -or $r.EndDate){throw 'Une durée ne concerne que les renvois temporaires demandés.'}
  if($r.Type -ceq 'Parents'){
   [void](ConvertTo-ScoliaPersonnelInstant $r.MeetingAt)
   if(([datetimeoffset]::Parse($r.MeetingAt)).Date -lt $decision -or $r.ParentOutcome -cnotin @('Awaiting','Received','Absent')){throw 'Convocation ou suivi du parent invalide.'}
  }elseif($r.MeetingAt -or $r.ParentOutcome){throw 'Rendez-vous associé à une sanction non parentale.'}
  Assert-ScoliaPersonnelActor $r.CreatedBy;[void](ConvertTo-ScoliaPersonnelInstant $r.CreatedAt)
  if($r.State -ceq 'Cancelled' -and ([string]$r.CancelReason).Trim().Length -lt 5){throw 'Une annulation doit conserver son motif.'}
  if(-not @($r.Audit).Count){throw 'Historique disciplinaire absent.'}
  if([string]$r.RequestHash -cnotmatch '^[a-f0-9]{64}$' -or $r.RequestHash -cne (Get-ScoliaDisciplineRequestHash $r)){throw 'Le contenu de la décision disciplinaire a changé. Aucune admission autorisée avant vérification.'}
 }
}
function New-ScoliaDisciplineRequest {
 param($Learner,[ValidateSet('Temporary','Permanent','Parents')][string]$Type,[string]$Reason,[string]$DecisionDate,[string]$StartDate='',[string]$EndDate='',[string]$MeetingAt='',[string]$Id='')
 if(-not $Learner){throw 'Sélectionnez un apprenant.'}
 if($Reason.Trim().Length -lt 5 -or $Reason.Length -gt 2000){throw 'Indiquez le motif ou la cause : 5 à 2000 caractères.'}
 $snapshot=[ordered]@{};foreach($field in @('Nom','PostNom','Prenom','Naissance','Promotion','Annee','Tuteur','Contact1','Contact2')){$snapshot[$field]=[string]$Learner.$field}
 return [pscustomobject]@{Id=$(if($Id){ConvertTo-ScoliaPersonnelId $Id}else{[guid]::NewGuid().ToString('D')});Matricule=(Get-ScoliaDisciplineMat $Learner.Matricule);RequestedType=$Type;Reason=$Reason.Trim();DecisionDate=$DecisionDate;StartDate=$StartDate;EndDate=$EndDate;MeetingAt=$MeetingAt;Learner=[pscustomobject]$snapshot}
}
function Get-ScoliaDisciplineRequestHash($Request){
 return Get-ScoliaFeatureHash (([pscustomobject][ordered]@{Matricule=$Request.Matricule;Type=$Request.RequestedType;Reason=$Request.Reason;DecisionDate=$Request.DecisionDate;StartDate=$Request.StartDate;EndDate=$Request.EndDate;MeetingAt=$Request.MeetingAt})|ConvertTo-Json -Compress)
}
function Get-ScoliaDisciplineTemporaryCount($Document,[string]$Matricule){return @($Document.Items|Where-Object {$_.Matricule -ceq (Get-ScoliaDisciplineMat $Matricule) -and $_.RequestedType -ceq 'Temporary' -and $_.State -ceq 'Validated'}).Count}
function Add-ScoliaDisciplineDecision {
 param($Document,$Request,[int]$ExpectedRevision,[string]$Actor,[datetimeoffset]$Now=[datetimeoffset]::Now,[object[]]$PriorDocuments=@())
 Assert-ScoliaDisciplineRevision $Document $ExpectedRevision;Assert-ScoliaDisciplineDocument $Document;Assert-ScoliaPersonnelActor $Actor
 $decision=Get-ScoliaDisciplineDate $Request.DecisionDate;$bounds=Get-ScoliaAttendanceYearBounds $Document.Context.Year
 if($decision -gt $Now.Date -or $decision -lt $bounds.Start -or $decision -gt $bounds.End){throw 'La décision doit appartenir à cette année et ne peut pas être future.'}
 if(($Request.Learner.Annee -replace '\s','') -cne $Document.Context.Year){throw 'L apprenant appartient à une autre année scolaire.'}
 if(@($Document.Items|Where-Object Id -ceq $Request.Id).Count){throw 'Cette décision est déjà enregistrée. Actualisez le registre.'}
 $hash=Get-ScoliaDisciplineRequestHash $Request
 if(@($Document.Items|Where-Object {$_.State -ceq 'Validated' -and $_.RequestHash -ceq $hash}).Count){throw 'Une décision identique existe déjà : aucun second renvoi créé.'}
 $existingPermanent=@($Document.Items|Where-Object {$_.Matricule -ceq $Request.Matricule -and $_.Type -ceq 'Permanent' -and $_.State -ceq 'Validated'})
 if($existingPermanent.Count -and $Request.RequestedType -cne 'Parents'){throw 'Un renvoi définitif est déjà enregistré. Une nouvelle sanction n est pas nécessaire.'}
 if($Request.RequestedType -cne 'Parents'){
  foreach($prior in $PriorDocuments){if($prior.Context.Id -cne $Document.Context.Id){throw 'Historique d une autre base.'};Assert-ScoliaDisciplineDocument $prior}
  $priorState=Get-ScoliaDisciplineLearnerStatus $PriorDocuments $Request.Matricule $Document.Context.Year $Now.Date
  if($priorState.Mode -ceq 'Permanent'){throw 'Un renvoi définitif antérieur est toujours valide pour cet apprenant.'}
 }
 if($Request.RequestedType -ceq 'Temporary'){
  $last=@($Document.Items|Where-Object {$_.Matricule -ceq $Request.Matricule -and $_.RequestedType -ceq 'Temporary' -and $_.State -ceq 'Validated'}|Sort-Object DecisionDate -Descending|Select-Object -First 1)
  if($last.Count -and $decision -lt (Get-ScoliaDisciplineDate $last[0].DecisionDate)){throw 'Ce renvoi précède une décision déjà enregistrée. Corrigez l ordre des décisions avant de valider.'}
 }
 $count=Get-ScoliaDisciplineTemporaryCount $Document $Request.Matricule
 $type=$Request.RequestedType;$automatic=($type -ceq 'Temporary' -and $count -ge 1);if($automatic){$type='Permanent'}
 $r=[pscustomobject]@{Kind='Discipline';Id=$Request.Id;Year=$Document.Context.Year;Matricule=$Request.Matricule;Identity=($Request.Learner.Nom+' '+$Request.Learner.PostNom+' '+$Request.Learner.Prenom).Trim();Promotion=$Request.Learner.Promotion;Learner=$Request.Learner;RequestedType=$Request.RequestedType;Type=$type;Automatic=$automatic;TemporaryOrdinal=$(if($Request.RequestedType -ceq 'Temporary'){$count+1}else{0});Reason=$Request.Reason;DecisionDate=$Request.DecisionDate;StartDate=$Request.StartDate;EndDate=$Request.EndDate;StartMonth=$bounds.StartMonth;MeetingAt=$Request.MeetingAt;ParentOutcome=$(if($type -ceq 'Parents'){'Awaiting'}else{''});State='Validated';CancelReason='';RequestHash=$hash;CreatedAt=$Now.ToString('o');CreatedBy=$Actor;Audit=@([pscustomobject]@{At=$Now.ToString('o');By=$Actor;Action='Create';Reason=$Request.Reason;Before='';After=$type})}
 $candidate=[pscustomobject]@{Schema=1;Context=$Document.Context;Revision=$Document.Revision;Items=@($Document.Items)+@($r)}
 Assert-ScoliaDisciplineDocument $candidate;$Document.Items=$candidate.Items;return $r
}
function Update-ScoliaDisciplineDecision {
 param($Document,[string]$Id,[ValidateSet('Cancel','Received','Absent')][string]$Action,[string]$Reason,[int]$ExpectedRevision,[string]$Actor,[datetimeoffset]$Now=[datetimeoffset]::Now)
 Assert-ScoliaDisciplineRevision $Document $ExpectedRevision;Assert-ScoliaDisciplineDocument $Document;Assert-ScoliaPersonnelActor $Actor
 if($Reason.Trim().Length -lt 5 -or $Reason.Length -gt 2000){throw 'Précisez un motif ou compte rendu de 5 à 2000 caractères.'}
 $match=@($Document.Items|Where-Object Id -ceq $Id);if($match.Count -ne 1 -or $match[0].State -cne 'Validated'){throw 'Décision absente ou déjà annulée.'}
 $copy=$match[0]|ConvertTo-Json -Depth 20|ConvertFrom-ScoliaJson;$before=$copy.State+' / '+$copy.ParentOutcome
 if($Action -ceq 'Cancel'){$copy.State='Cancelled';$copy.CancelReason=$Reason.Trim()}
 else{
  if($copy.Type -cne 'Parents'){throw 'Le suivi du parent concerne uniquement une convocation.'}
  if($Action -ceq 'Absent' -and $Now -lt [datetimeoffset]::Parse($copy.MeetingAt)){throw 'Le rendez-vous n est pas encore passé : le parent ne peut pas être déclaré absent.'}
  $copy.ParentOutcome=$Action
 }
 $copy.Audit=@($copy.Audit)+@([pscustomobject]@{At=$Now.ToString('o');By=$Actor;Action=$Action;Reason=$Reason.Trim();Before=$before;After=$copy.State+' / '+$copy.ParentOutcome})
 $candidate=[pscustomobject]@{Schema=1;Context=$Document.Context;Revision=$Document.Revision;Items=@($Document.Items|Where-Object Id -cne $Id)+@($copy)}
 Assert-ScoliaDisciplineDocument $candidate;$Document.Items=$candidate.Items
}
function Read-ScoliaDisciplineState {
 Assert-ScoliaDisciplineAccess;$context=Get-ScoliaFeatureContext;$document=Read-ScoliaFeatureStore -Name discipline;Assert-ScoliaDisciplineDocument $document
 return [pscustomobject]@{Context=$context;Document=$document}
}
function Invoke-ScoliaDisciplineUpdate {
 param([scriptblock]$Change,[int]$ExpectedRevision,[switch]$Administrative)
 Assert-ScoliaDisciplineAccess -Write;if($Administrative){Assert-ITMPermission 'Administrer'}
 $context=Get-ScoliaFeatureContext;$path=Get-ScoliaFeatureStorePath discipline;$gates=@()
 try{
  foreach($lock in @((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),($path+'.lock'))){try{$gates+=[IO.File]::Open($lock,'OpenOrCreate','ReadWrite','None')}catch{throw 'Un enregistrement, une sauvegarde ou un nettoyage est en cours. Réessayez après actualisation.'}}
  $state=Read-ScoliaDisciplineState;Assert-ScoliaDisciplineRevision $state.Document $ExpectedRevision
  $before=$state.Document|ConvertTo-Json -Depth 20|ConvertFrom-ScoliaJson
  [void](& $Change $state.Document);Assert-ScoliaDisciplineDocument $state.Document
  $needsAdmin=Assert-ScoliaDisciplineHistoryUnchanged $before $state.Document
  if($needsAdmin){Assert-ITMPermission 'Administrer'}
  $current=Get-ScoliaFeatureContext
  if($current.Id -cne $context.Id -or $current.Year -cne $context.Year -or $current.LocalKey -cne $context.LocalKey -or (Get-ScoliaFeatureStorePath discipline) -cne $path){throw 'Base, année ou compte modifié : décision non enregistrée.'}
  Assert-ScoliaDisciplineAccess -Write;if($Administrative -or $needsAdmin){Assert-ITMPermission 'Administrer'}
  $state.Document.Revision=$ExpectedRevision+1;Write-ITMAtomicJson $path $state.Document;return $state.Document
 }finally{for($i=$gates.Count-1;$i -ge 0;$i--){$gates[$i].Dispose()}}
}
function Get-ScoliaDisciplineDocuments([switch]$Force, $Files=$null) {
 $context=Get-ScoliaFeatureContext;$documents=New-Object 'System.Collections.Generic.List[object]'
 if($null -eq $Files){$Files=@(Get-ChildItem -LiteralPath $script:storeFolder -Filter 'scolia-v55-discipline-*.json' -File -ErrorAction Stop)}
 $files=@($Files|Sort-Object Name)
 $stamp=([IO.Path]::GetFullPath($script:storeFolder)+'|'+$context.Id+'|'+$context.Year+'|'+(@($files|ForEach-Object {$_.Name+'|'+$_.Length+'|'+$_.LastWriteTimeUtc.Ticks}) -join ';'))
 if(-not $Force -and $script:disciplineCache -and $script:disciplineCache.Stamp -ceq $stamp){return $script:disciplineCache.Documents}
 foreach($file in $files){
  try{$doc=[IO.File]::ReadAllText($file.FullName,[Text.Encoding]::UTF8)|ConvertFrom-ScoliaJson -ErrorAction Stop}catch{throw 'Registre disciplinaire illisible. Corrigez le fichier avant de reprendre les admissions.'}
  if([string]$doc.Context.Id -cnotmatch '^[a-f0-9]{64}$' -or [string]$doc.Context.Year -cnotmatch '^\d{4}-\d{4}$'){throw 'Contexte du registre disciplinaire invalide.'}
  if($doc.Context.Id -cne $context.Id){continue}
  $expected='scolia-v55-discipline-'+(Get-ScoliaFeatureHash ($context.Id+'|'+$doc.Context.Year))+'.json'
  if($file.Name -cne $expected -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Nom ou emplacement du registre disciplinaire invalide.'}
  Assert-ScoliaDisciplineDocument $doc;$documents.Add($doc)
 }
 $script:disciplineCache=[pscustomobject]@{Stamp=$stamp;Documents=$documents.ToArray()}
 return $script:disciplineCache.Documents
}
function Get-ScoliaDisciplineLearnerStatus {
 param([object[]]$Documents,[string]$Matricule,[string]$Year,[datetime]$Date=(Get-Date),$Index=$null)
 $mat=Get-ScoliaDisciplineMat $Matricule
 if($null -ne $Index){$events=@($Index[$mat])}else{$events=@($Documents|ForEach-Object {$_.Items}|Where-Object {$_.Matricule -ceq $mat -and $_.State -ceq 'Validated'})}
 $permanent=@($events|Where-Object {$_.Type -ceq 'Permanent' -and (Get-ScoliaDisciplineDate $_.DecisionDate) -le $Date.Date})
 if($permanent.Count){return [pscustomobject]@{Mode='Permanent';Status='Sorti';Description='Renvoi définitif';Record=$permanent[-1]}}
 $temporary=@($events|Where-Object {$_.Type -ceq 'Temporary' -and $_.Year -ceq $Year -and (Get-ScoliaDisciplineDate $_.StartDate) -le $Date.Date -and (Get-ScoliaDisciplineDate $_.EndDate) -ge $Date.Date})
 if($temporary.Count){return [pscustomobject]@{Mode='Temporary';Status='Suspendu';Description=('Renvoi temporaire jusqu au '+(Get-ScoliaDisciplineDate $temporary[-1].EndDate).ToString('dd/MM/yyyy'));Record=$temporary[-1]}}
 return [pscustomobject]@{Mode='None';Status='';Description='';Record=$null}
}
function Sync-ScoliaDisciplineSnapshot([datetime]$Date=(Get-Date)){
 if(-not $script:store -or -not $script:storeFolder -or -not $script:data -or -not $script:book){return $false}
 $files=@(Get-ChildItem -LiteralPath $script:storeFolder -Filter 'scolia-v55-discipline-*.json' -File -ErrorAction Stop)
 if(-not $files.Count){return $false}
 $documents=@(Get-ScoliaDisciplineDocuments -Files $files);$changed=$false
 $index=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
 foreach($document in $documents){foreach($record in $document.Items){
  if($record.State -cne 'Validated'){continue}
  if(-not $index.ContainsKey([string]$record.Matricule)){$index.Add([string]$record.Matricule,(New-Object 'System.Collections.Generic.List[object]'))}
  $index[[string]$record.Matricule].Add($record)
 }}
 foreach($learner in @($script:data.learners)){
  $status=Get-ScoliaDisciplineLearnerStatus $documents ([string]$learner.Matricule) ([string]$learner.Annee) $Date $index
  if($status.Mode -ne 'None' -and -not $learner.PSObject.Properties['DisciplineBaseStatus']){$learner|Add-Member NoteProperty DisciplineBaseStatus ([string]$learner.Statut)}
  if($learner.PSObject.Properties['DisciplineBaseStatus']){
   $expected=$(if($status.Mode -ne 'None'){$status.Status}else{[string]$learner.DisciplineBaseStatus})
   if([string]$learner.Statut -cne $expected){$learner.Statut=$expected;$changed=$true}
   if($learner.PSObject.Properties['DisciplineInfo']){$learner.DisciplineInfo=$status.Description}else{$learner|Add-Member NoteProperty DisciplineInfo $status.Description}
  }
 }
 if($changed){$script:scoliaRosterPending=$true;$script:dataRevision++;if($script:viewRevision){$script:viewRevision.Clear()}}
 return $changed
}
function Assert-ScoliaDisciplineAdmission([string]$Matricule,[datetime]$Date=(Get-Date)){
 if(-not $script:store -or -not $script:storeFolder){return}
 $context=Get-ScoliaFeatureContext;$status=Get-ScoliaDisciplineLearnerStatus @(Get-ScoliaDisciplineDocuments) $Matricule $context.Year $Date
 if($status.Mode -ne 'None'){throw ('Admission ou carte bloquée pour '+$Matricule+' : '+$status.Description+'. Consultez Discipline.')}
}
function Get-ScoliaDisciplineRows($Document,[datetimeoffset]$Now=[datetimeoffset]::Now,[switch]$IncludeCancelled){
 Assert-ScoliaDisciplineDocument $Document
 return @(foreach($r in @($Document.Items|Sort-Object DecisionDate,CreatedAt -Descending)){
  if($r.State -ceq 'Cancelled' -and -not $IncludeCancelled){continue}
  $type=switch($r.Type){'Temporary'{'Renvoi temporaire'};'Permanent'{'Renvoi définitif'};default{'Convocation des parents'}}
  $period='';$days=$null;$state='';$returnDate=''
  if($r.Type -ceq 'Temporary'){$start=Get-ScoliaDisciplineDate $r.StartDate;$end=Get-ScoliaDisciplineDate $r.EndDate;$days=($end-$start).Days+1;$period=$start.ToString('dd/MM/yyyy')+' → '+$end.ToString('dd/MM/yyyy');$returnDate=$end.AddDays(1).ToString('dd/MM/yyyy');$state=$(if($Now.Date -lt $start){'À venir'}elseif($Now.Date -le $end){'En cours'}else{'Terminé'})}
  elseif($r.Type -ceq 'Permanent'){$state=$(if($r.Automatic){'Définitif · deuxième renvoi'}else{'Définitif'})}
  else{$period=([datetimeoffset]::Parse($r.MeetingAt)).ToString('dd/MM/yyyy HH:mm');$state=switch($r.ParentOutcome){'Received'{'Parent reçu'};'Absent'{'Parent absent'};default{'Parent attendu'}}}
  if($r.State -ceq 'Cancelled'){$state='Annulé'}
  [pscustomobject]@{Key=$r.Id;Matricule=$r.Matricule;Identity=$r.Identity;Promotion=$r.Promotion;Date=(Get-ScoliaDisciplineDate $r.DecisionDate).ToString('dd/MM/yyyy');DecisionDate=(Get-ScoliaDisciplineDate $r.DecisionDate);Type=$type;Reason=$r.Reason;Period=$period;Days=$days;ReturnDate=$returnDate;State=$state;Phone=$r.Learner.Contact1;Automatic=$r.Automatic;Record=$r}
 })
}

function Assert-ScoliaDisciplineHistoryUnchanged($Before,$After){
 $administrative=$false
 $immutable=@('Kind','Id','Year','Matricule','Identity','Promotion','Learner','RequestedType','Type','Automatic','TemporaryOrdinal','Reason','DecisionDate','StartDate','EndDate','StartMonth','MeetingAt','RequestHash','CreatedAt','CreatedBy')
 foreach($old in @($Before.Items)){
  $matches=@($After.Items|Where-Object Id -ceq $old.Id);if($matches.Count -ne 1){throw 'Une décision disciplinaire ne peut pas être supprimée.'};$new=$matches[0]
  foreach($field in $immutable){if(($old.$field|ConvertTo-Json -Depth 10 -Compress) -cne ($new.$field|ConvertTo-Json -Depth 10 -Compress)){throw 'Les décisions et motifs enregistrés sont immuables. Annulez avec un motif puis créez une décision corrigée.'}}
  if(@($new.Audit).Count -lt @($old.Audit).Count){throw 'L historique disciplinaire ne peut pas être effacé.'}
  for($i=0;$i -lt @($old.Audit).Count;$i++){if(($old.Audit[$i]|ConvertTo-Json -Depth 10 -Compress) -cne ($new.Audit[$i]|ConvertTo-Json -Depth 10 -Compress)){throw 'Une ancienne ligne du journal disciplinaire ne peut pas être modifiée.'}}
  if($old.State -ceq 'Cancelled' -and (($old|ConvertTo-Json -Depth 20 -Compress) -cne ($new|ConvertTo-Json -Depth 20 -Compress))){throw 'Une annulation reste conservée. Créez une nouvelle décision si nécessaire.'}
  if($old.State -cne $new.State -or $old.CancelReason -cne $new.CancelReason){$administrative=$true}
 }
 return $administrative
}
