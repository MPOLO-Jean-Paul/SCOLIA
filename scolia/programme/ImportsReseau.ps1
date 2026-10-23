# Imports réseau : le secrétariat publie ses données, chaque reçu garde sa référence.
function Get-ScoliaSourceDataSet {
 if($script:store -and $script:store.Id){return [string]$script:store.Id}
 $id=Get-ITMBookProperty $script:book 'ITM_DataSet'
 if(-not $id){$id=Get-ITMBookProperty $script:book 'ITM_ScoliaId'}
 if(-not $id){$id=$script:scolia.Id}
 return [string]$id
}
function Get-ScoliaIncomingReceiptMap {
 $map=@{};$active=$script:book
 try{
  $books=$(if($script:store){@($script:storeBooks)}else{@($script:book)})
  foreach($b in $books){
   $script:book=$b
   $macro=Get-ITMExcelMacroName $b 'ModuleGestionAvancee' 'ITMJournalLire'
   $journal=[string](Invoke-ITMExcelRun $script:excel $macro @()) | ConvertFrom-Json -ErrorAction Stop
   foreach($row in @($journal)){
    if($row.Action -ne 'SCOLIA_TRANSFERT'){continue}
    $sourceReceipt=[string]$row.Avant;$durableId=([string]$row.Numero).ToLowerInvariant();$sourceKey=Key $sourceReceipt
    if([string]::IsNullOrWhiteSpace($sourceKey) -or $durableId -notmatch '^[0-9a-fA-F]{32}$'){throw 'Journal de transfert incomplet ou corrompu : référence source / identifiant durable invalide.'}
    if($map.ContainsKey($sourceKey) -and $map[$sourceKey] -ne $durableId){throw ('Journal de transfert ambigu : le reçu '+$sourceReceipt+' pointe vers plusieurs opérations. Lancez le rapprochement avant un nouvel import.')}
    $map[$sourceKey]=$durableId
   }
  }
 }finally{$script:book=$active}
 return $map
}
function Add-ScoliaPublishedPayments($roster) {
 $source=Get-ScoliaSourceDataSet;$incoming=Get-ScoliaIncomingReceiptMap
 # A receipt is a common accounting reference and must identify one learner only.
 # Multi-month lines for the same learner remain valid and are grouped below.
 $receiptOwners=@{}
 foreach($row in @($script:data.payments)){
  $receipt=[string]$row.Numero;$receiptKey=Key $receipt;$learnerKey=Key ([string]$row.Matricule)
  if([string]::IsNullOrWhiteSpace($receiptKey)){throw 'Un paiement sans numéro doit être corrigé avant sa publication réseau.'}
  if([string]::IsNullOrWhiteSpace($learnerKey)){throw ('Le reçu '+$receipt+' ne contient aucun matricule. Corrigez-le avant sa publication réseau.')}
  if($receiptOwners.ContainsKey($receiptKey) -and $receiptOwners[$receiptKey] -ne $learnerKey){throw ('Référence de reçu réutilisée pour plusieurs apprenants : '+$receipt+'. Corrigez le doublon avant la synchronisation.')}
  $receiptOwners[$receiptKey]=$learnerKey
 }
 $script:scoliaOutgoingIds=@{};$roster.AppliedFormat='1';$roster.AppliedPayments=New-Object 'System.Collections.Generic.List[ScoliaEntry]'
 $people=@{};foreach($a in @($script:data.learners)){$people[(Key $a.Matricule)]=$a}
 foreach($group in @($script:data.payments | Group-Object { (Key $_.Numero)+'|'+(Key $_.Matricule) } )){
  $rows=@($group.Group);$first=$rows[0];$receipt=[string]$first.Numero
  $received=$incoming.ContainsKey((Key $receipt))
  if([string]::IsNullOrWhiteSpace($receipt)){throw 'Un paiement sans numéro doit être corrigé avant sa publication réseau.'}
  $entry=New-Object ScoliaEntry;$entry.Id=$(if($received){$incoming[(Key $receipt)]}else{[ScoliaLedger]::SecretariatId($script:scolia.Id,$source,$roster.Year,$receipt,[string]$first.Matricule)})
  $entry.SchoolId=$script:scolia.Id;$entry.Kind='Paiement';$entry.Compte='Secrétariat';$entry.Matricule=[string]$first.Matricule;$entry.Annee=$roster.Year
  $entry.Reference='SEC-'+$entry.Id.Substring(0,12)+'-'+$receipt;$entry.Description='Reçu secrétariat : '+$receipt
  $entry.OperatorId=$script:scoliaSession.AccountId;$entry.OperatorName='Import du secrétariat';$entry.ConfigRevision=$script:scoliaSession.Revision
  $a=$people[(Key $first.Matricule)]
  if($a){foreach($n in @('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Adresse')){$entry.$n=[string]$a.$n}}
  else{$entry.Nom=[string]$first.NomComplet;$entry.Description+=' · dossier apprenant retiré ; identité du reçu conservée'}
  $entry.Promotion=[string]$first.Promotion
  $dates=@($rows|ForEach-Object {[string]$_.Date}|Select-Object -Unique)
  $promos=@($rows|ForEach-Object {Promo $_.Promotion}|Select-Object -Unique)
  if($dates.Count -ne 1 -or $promos.Count -ne 1){throw ('Reçu ambigu : '+$receipt+'. Plusieurs dates ou promotions ; contrôlez ses lignes.')}
  $dateValue=[datetime]::MinValue
  if(-not [datetime]::TryParseExact($dates[0],[string[]]@('dd/MM/yyyy','d/M/yyyy','yyyy-MM-dd'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$dateValue)){throw ('Date incorrecte du reçu '+$receipt)}
  $entry.Date=$dateValue.ToString('yyyy-MM-dd')
  foreach($month in [ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months)){
   $sum=[decimal]0;foreach($r in $rows){if((Key $r.Mois) -eq (Key $month)){$sum += [decimal]$r.Verse}}
   if($sum -gt 0){$part=New-Object ScoliaPart;$part.Mois=$month;$part.Montant=$sum;$entry.Parts.Add($part);$entry.Montant+=$sum}
  }
  $total=[decimal]0;foreach($r in $rows){if([decimal]$r.Verse -lt 0){throw ('Montant négatif dans le reçu '+$receipt)};$total += [decimal]$r.Verse}
  if($total -eq 0){continue}
  if($total -ne $entry.Montant){throw ('Mois non reconnu dans le reçu '+$receipt+'. Vérifiez le calendrier scolaire.')}
  [ScoliaLedger]::ValidateDraft($entry,$script:scolia.Id)
  $roster.AppliedPayments.Add($entry)
  if($received){if(-not $roster.Imported.Contains($entry.Id)){$roster.Imported.Add($entry.Id)};continue}
  $roster.Payments.Add($entry);if(-not $roster.Imported.Contains($entry.Id)){$roster.Imported.Add($entry.Id)}
  $script:scoliaOutgoingIds[$entry.Id]=$true
 }
 foreach($e in @($script:scoliaEntries|Where-Object {$_.Kind -eq 'Paiement' -and [ScoliaSharing]::YearKey($_.Annee) -eq [ScoliaSharing]::YearKey($roster.Year)})){
  $state=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$e.Id)
  if((Test-ScoliaApplied $e $state) -and (-not [ScoliaFinance]::IsTuition($e) -or $e.Cancelled -eq '1')){$copy=Read-ScoliaAppliedReceipt $e;if($copy){$roster.AppliedPayments.Add($copy);if(-not $roster.Imported.Contains($e.Id)){$roster.Imported.Add($e.Id)}}}
 }
 $roster.PublishedUtc=[datetime]::UtcNow.ToString('o')
}
function Test-ScoliaOutgoingReceipt($entry) {
 if($entry.CommonReceipt -eq '1' -or $entry.Revision){return $false}
 if($script:scoliaOutgoingIds -and $script:scoliaOutgoingIds.ContainsKey([string]$entry.Id)){return $true}
 return ($entry.Compte -eq 'Secrétariat')
}
function Get-ScoliaImportedRosterPath {
 return (Join-Path $script:scoliaLocalRoot ('apprenants-importes-'+$script:scolia.Id+'-'+[ScoliaSharing]::YearKey($script:scolia.Year)+'.xml'))
}
function Get-ScoliaImportedLearners {
 if($script:scoliaSession.Role -in @('Secrétariat','Mixte') -and $script:book){$published=[ScoliaSharing]::ReadRoster($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year);if($published.PublishedUtc){[ScoliaSharing]::SaveLocalRoster((Get-ScoliaImportedRosterPath),$published,$script:scolia.Id,$script:scolia.Year)}}
 $r=[ScoliaSharing]::LoadLocalRoster((Get-ScoliaImportedRosterPath),$script:scolia.Id,$script:scolia.Year)
 return @($r.Learners | Sort-Object Promotion,Nom,PostNom,Prenom,Matricule)
}
function Import-ScoliaLearners($roster) {
 Assert-ITMPermission 'Encaisser'
 [ScoliaSharing]::SaveLocalRoster((Get-ScoliaImportedRosterPath),$roster,$script:scolia.Id,$script:scolia.Year)
 return @($roster.Learners).Count
}
function Get-ScoliaImportHistoryRows {
 $rows=@();$accounting=($script:scoliaSession.Role -eq 'Comptabilité' -or ($script:scoliaSession.Role -eq 'Mixte' -and $script:currentPage -eq 'Comptabilité'))
 foreach($e in @($script:scoliaEntries|Sort-Object Date,Reference,Id)){
  if($e.Kind -ne 'Paiement' -or [ScoliaSharing]::YearKey($e.Annee) -ne [ScoliaSharing]::YearKey($script:scolia.Year)){continue}
  if($accounting){if($e.Compte -ne 'Secrétariat'){continue};$date=$e.Created}
  else{
   if(Test-ScoliaOutgoingReceipt $e){continue}
   $state=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$e.Id)
   if(-not $state -or $state.Status -ne 'Importé'){continue};$date=$state.Updated
  }
  $rows+=[pscustomobject]@{Date=$e.Date;Reference=$e.Reference;Matricule=$e.Matricule;Nom=($e.Nom+' '+$e.PostNom+' '+$e.Prenom).Trim();Promotion=$e.Promotion;Montant=$e.Montant;Categorie=$(if($e.CategoryName){$e.CategoryName}else{[ScoliaFinance]::CategoryKey($e)});Mois=(@($e.Parts|ForEach-Object {$_.Mois+' : '+$_.Montant}) -join ' ; ');Origine=$e.Compte;Etat='Importé';Importation=$date;Observation=''}
 }
 return $rows
}
function Get-ScoliaTransferRows($entries) {
 return @($entries|Sort-Object Date,Reference,Id|ForEach-Object {[pscustomobject]@{Date=$_.Date;Reference=$_.Reference;Matricule=$_.Matricule;Nom=($_.Nom+' '+$_.PostNom+' '+$_.Prenom).Trim();Promotion=$_.Promotion;Montant=$_.Montant;Categorie=$(if($_.CategoryName){$_.CategoryName}else{[ScoliaFinance]::CategoryKey($_)});Mois=(@($_.Parts|ForEach-Object {$_.Mois+' : '+$_.Montant}) -join ' ; ');Origine=$_.Compte;Etat='Disponible';Importation='';Observation=''}})
}
function Show-ScoliaImports([ValidateSet('Paiements','Apprenants')][string]$Mode='Paiements',[bool]$History=$false,[bool]$Drafts=$false) {
 Assert-ITMPermission 'Lire'
 $accounting=($script:scoliaSession.Role -eq 'Comptabilité' -or ($script:scoliaSession.Role -eq 'Mixte' -and $script:currentPage -eq 'Comptabilité'))
 if($Mode -eq 'Apprenants'){Assert-ITMPermission 'Encaisser'}elseif(-not $accounting){Assert-ITMPermission 'Recevoir'}
 $screen=New-ScoliaFeatureDialog ('Importation réseau · '+$Mode) '' 1120 720
 $d=$screen.Form;$header=$screen.SubtitleLabel;$grid=$screen.Grid;$bar=$screen.Actions
 $state=@{Task=$null;Kind='';Snapshot=$null;Queue=@();Index=0;Imported=0;Existing=0;Errors=0;Rows=@();History=$History;ImportAfterRead=$false;Drafts=$Drafts}
 $columns=@(@('Date','Date du reçu'),@('Reference','Référence'),@('Matricule','Matricule'),@('Nom','Nom complet'),@('Promotion','Promotion'),@('Montant','Montant FC'),@('Categorie','Motif de paiement'),@('Mois','Affectation mensuelle'),@('Origine','Origine'),@('Etat','État'),@('Importation','Importé le (UTC)'),@('Observation','Observation'))
 $learnerColumns=@(@('Promotion','Promotion'),@('Matricule','Matricule'),@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Annee','Année'),@('Sexe','Sexe'),@('Naissance','Naissance'),@('Lieu','Lieu de naissance'),@('Contact1','Contact parent 1'),@('Contact2','Contact parent 2'),@('Adresse','Adresse'),@('Tuteur','Tuteur'),@('Inscription','Inscription'),@('Statut','Statut'))
 $showRows={
  if($Mode -eq 'Apprenants'){
   $rows=$(if($state.History){@(Get-ScoliaImportedLearners)}elseif($state.Drafts){@(Get-ScoliaPendingLearners $state.Snapshot)}elseif($state.Snapshot){@($state.Snapshot.Roster.Learners|Sort-Object Promotion,Nom,PostNom,Prenom,Matricule)}else{@()})
   Set-Grid $grid $rows $learnerColumns
  }else{$rows=$(if($state.History){@(Get-ScoliaImportHistoryRows)}elseif($state.Drafts){@(Get-ScoliaTransferRows @(Get-ScoliaPendingPayments $state.Snapshot $accounting) | ForEach-Object {$_.Etat='Brouillon reçu';$_})}else{@($state.Rows)});Set-Grid $grid $rows $columns}
 }
 $finish={
  $state.Kind='';$state.Task=$null;$import.Enabled=$true;$read.Enabled=$true;$view.Enabled=$true;$draftView.Enabled=$true;$close.Enabled=$true
  if($Mode -eq 'Paiements'){Refresh-ScoliaFinance;if(-not $accounting -and $script:data){Refresh-Payments}}
  & $showRows
 }
 $beginImport={
  $state.History=$false;$state.Drafts=$false;$state.Imported=0;$state.Existing=0;$state.Errors=0
  if($Mode -eq 'Apprenants'){
   $count=Import-ScoliaLearners $state.Snapshot.Roster;$state.History=$true;$header.Text=$count.ToString()+' apprenant(s) importé(s), classés par promotion puis par nom. Ils sont disponibles dans Encaisser.';& $finish
  }elseif($accounting){$state.Kind='Comptabilité';$state.Task=[ScoliaLedger]::ImportSecretariatAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year);$header.Text='Import des paiements du secrétariat en cours...'}
  else{
   if(-not $script:book -or -not $script:data -or $script:data.closed){throw 'Connectez une base ouverte du secrétariat avant lʼimport.'}
   if((Get-ScoliaConnectedYear) -ne [ScoliaSharing]::YearKey($script:scolia.Year)){throw 'La base ouverte ne correspond pas à lʼannée du partage.'}
   $state.Queue=@($state.Snapshot.Entries|Where-Object {$_.Kind -eq 'Paiement' -and -not (Test-ScoliaOutgoingReceipt $_) -and (Test-ScoliaReceiptYear $_)}|Sort-Object Date,Reference,Id)
   $state.Rows=@();$state.Index=0;$state.Kind='Secrétariat'
  }
 }
 $startRead={param([bool]$importNow)
  if($state.Kind){return};$state.ImportAfterRead=$importNow;$state.Kind='Lecture';$state.Task=[ScoliaSharing]::ReadAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year)
  foreach($b in @($import,$read,$view,$draftView,$close)){$b.Enabled=$false};$header.Text='Lecture du dossier réseau... '+$script:scoliaRoot
 }
 $import=Add-ScoliaButton $bar ('Importer tous les '+$Mode.ToLowerInvariant()) {try{& $startRead $true}catch{$header.Text=$_.Exception.Message;& $finish}}
 $read=Add-ScoliaButton $bar 'Actualiser le réseau' {try{$state.History=$false;$state.Drafts=$false;& $startRead $false}catch{$header.Text=$_.Exception.Message;& $finish}}
 $view=Add-ScoliaButton $bar 'Visualiser lʼimportation' {try{$state.History=$true;$state.Drafts=$false;& $showRows;$header.Text='Données déjà importées pour lʼannée '+$script:scolia.Year+'.'}catch{$header.Text=$_.Exception.Message}}
 $draftView=Add-ScoliaButton $bar 'Brouillons reçus' {try{$state.History=$false;$state.Drafts=$true;& $showRows;$header.Text='Réception automatique : nouveaux enregistrements et modifications en attente dʼimport. Ils sont conservés sur ce poste.'}catch{$header.Text=$_.Exception.Message}}
 $close=Add-ScoliaButton $bar 'Fermer' {$d.Close()};$d.CancelButton=$close
 $timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $timer.Add_Tick({
  if($state.TickBusy){return};$state.TickBusy=$true
  try{
   if($state.Kind -eq 'Secrétariat'){
    if($state.Index -ge $state.Queue.Count){$header.Text='Import terminé : '+$state.Imported+' nouveau(x), '+$state.Existing+' déjà importé(s), '+$state.Errors+' à vérifier.';$script:scoliaRosterPending=$true;& $finish;return}
    $e=$state.Queue[$state.Index];$row=@(Get-ScoliaTransferRows @($e))[0]
    try{
     $result=$null;$script:scoliaBatchReply=$null;$script:scoliaBatchError=''
     # The durable workbook reference is checked by the VBA import even after a lost acknowledgement.
     Do-Work {try{$script:scoliaBatchReply=Import-ScoliaReceipt $e $true}catch{$script:scoliaBatchError=$_.Exception.Message}} ('Import du reçu '+$e.Reference+'...')
     $result=$script:scoliaBatchReply;$script:scoliaBatchReply=$null
     if($script:scoliaBatchError){throw $script:scoliaBatchError}
     if(-not $result){throw 'Import non confirmé. Vérifiez le reçu puis réessayez.'}
     if($result.already){$state.Existing++;$row.Etat='Déjà importé'}else{$state.Imported++;$row.Etat='Importé'}
     $row.Importation=[datetime]::UtcNow.ToString('o')
    }catch{$state.Errors++;$row.Etat='À vérifier';$row.Observation=$_.Exception.Message;try{Set-ScoliaReceiptState $e 'Erreur' '' $row.Observation}catch{}}
    $state.Rows+=,$row;$state.Index++;$header.Text='Import : '+$state.Index+' / '+$state.Queue.Count+' reçus traités.';& $showRows;return
   }
   if(-not $state.Task -or -not $state.Task.IsCompleted){return}
   $task=$state.Task;$state.Task=$null;if($task.IsFaulted){throw $task.Exception.GetBaseException()}
   if($state.Kind -eq 'Lecture'){
    $state.Snapshot=$task.Result;Save-ScoliaAutomaticInbox $task.Result;$script:scoliaEntries=@($task.Result.Entries);$script:scoliaRoster=$task.Result.Roster
    if($Mode -eq 'Paiements'){$available=$(if($accounting){@($task.Result.Roster.Payments)}else{@($task.Result.Entries|Where-Object {$_.Kind -eq 'Paiement' -and -not (Test-ScoliaOutgoingReceipt $_) -and [ScoliaSharing]::YearKey($_.Annee) -eq [ScoliaSharing]::YearKey($script:scolia.Year)})});$state.Rows=@(Get-ScoliaTransferRows $available)}
    if($state.ImportAfterRead){& $beginImport}else{$header.Text='Réseau actualisé · année '+$script:scolia.Year+'. Cliquez sur Importer pour récupérer toutes les données disponibles.';& $finish}
   }elseif($state.Kind -eq 'Comptabilité'){
    $result=$task.Result;$state.Rows=@($result.Items);$header.Text='Import terminé : '+$result.Imported+' nouveau(x), '+$result.Existing+' déjà importé(s), '+$result.Errors+' à vérifier.'
    # Refresh after the committed batch; if this read fails, the next import recognises the saved IDs.
    $state.Kind='Après import';$state.Task=[ScoliaSharing]::ReadAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year)
   }elseif($state.Kind -eq 'Après import'){$state.Snapshot=$task.Result;Save-ScoliaAutomaticInbox $task.Result;$script:scoliaEntries=@($task.Result.Entries);$script:scoliaRoster=$task.Result.Roster;& $finish}
  }catch{$header.Text='Réseau / import : '+$_.Exception.Message+' Les données locales restent conservées. Consultez Brouillons reçus ou réessayez.';& $finish}finally{$state.TickBusy=$false}
 })
 $d.Add_FormClosing({param($sender,$e)if($state.Kind){$e.Cancel=$true}})
 $wasRunning=($script:scoliaTimer -and $script:scoliaTimer.Enabled);if($wasRunning){$script:scoliaTimer.Stop()};$script:scoliaReadTask=$null
 Apply-ITMThemeForm $d;$timer.Start()
 try{$cached=Get-ScoliaAutomaticInbox;if($cached){$state.Snapshot=$cached.Snapshot;$script:scoliaEntries=@($cached.Snapshot.Entries);$state.Rows=@(Get-ScoliaTransferRows @(Get-ScoliaPendingPayments $cached.Snapshot $accounting));$header.Text='Brouillons conservés sur ce poste · dernière réception : '+$cached.UpdatedUtc};& $showRows;& $startRead $false;[void]$d.ShowDialog($script:main)}finally{$timer.Stop();$timer.Dispose();$d.Dispose();if($wasRunning){$script:scoliaTimer.Start()}}
}

function Get-ScoliaInboxRole {
 switch($script:scoliaSession.Role){'Comptabilité'{return 'comptabilite'};'Secrétariat'{return 'secretariat'};'Mixte'{return 'mixte'};default{throw 'Connectez un compte SCOLIA.'}}
}
function Save-ScoliaAutomaticInbox($snapshot) {
 if(-not $script:scoliaSession -or -not $script:scolia){return}
 [void][ScoliaInboxCache]::Save($script:scoliaLocalRoot,$script:scolia.Id,$script:scolia.Year,(Get-ScoliaInboxRole),$snapshot)
}
function Get-ScoliaAutomaticInbox {
 return [ScoliaInboxCache]::Read($script:scoliaLocalRoot,$script:scolia.Id,$script:scolia.Year,(Get-ScoliaInboxRole))
}
function Get-ScoliaPendingPayments($snapshot,[bool]$accounting) {
 if(-not $snapshot){return @()}
 if($accounting){
  $known=@{};foreach($e in @($snapshot.Entries)){$known[$e.Id]=$e}
  return @($snapshot.Roster.Payments|Where-Object {-not $known.ContainsKey($_.Id) -or -not [ScoliaLedger]::SamePayment($_,$known[$_.Id])}|Sort-Object Date,Reference,Id)
 }
 return @($snapshot.Entries|Where-Object {
  if($_.Kind -ne 'Paiement' -or (Test-ScoliaOutgoingReceipt $_) -or [ScoliaSharing]::YearKey($_.Annee) -ne [ScoliaSharing]::YearKey($script:scolia.Year)){$false}
  else{$s=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$_.Id);(-not (Test-ScoliaApplied $_ $s))}
 }|Sort-Object Date,Reference,Id)
}
function Get-ScoliaPendingLearners($snapshot) {
 if(-not $snapshot){return @()};$known=@{}
 foreach($a in @(Get-ScoliaImportedLearners)){$known[[ScoliaLedger]::Key($a.Matricule)]=$a}
 return @($snapshot.Roster.Learners|Where-Object {-not [ScoliaInboxCache]::SameLearner($_,$known[[ScoliaLedger]::Key($_.Matricule)])}|Sort-Object Promotion,Nom,PostNom,Prenom,Matricule)
}
