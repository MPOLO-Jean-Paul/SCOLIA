. (Join-Path $PSScriptRoot 'PaiementsDirectsV71.ps1')
. (Join-Path $PSScriptRoot 'ReferencesRecusV69.ps1')
if(-not ('ScoliaLedger' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}
. (Join-Path $PSScriptRoot 'ScoliaConfiguration.ps1')
. (Join-Path $PSScriptRoot 'ScoliaReseau.ps1')
. (Join-Path $PSScriptRoot 'SynchronisationScolia.ps1')
. (Join-Path $PSScriptRoot 'ImportsReseau.ps1')
. (Join-Path $PSScriptRoot 'FinanceSynchronisee.ps1')
$script:scoliaSync=[pscustomobject]@{LastSuccess='';LastError='';State='Initialisation';FilesRead=0};$script:scoliaReader=$null;$script:scoliaReaderKey='';
$script:scoliaRoster=$null;$script:scoliaRosterTask=$null;$script:scoliaEntries=@();$script:scoliaReadTask=$null;$script:scoliaTimer=$null;$script:scoliaLedgerStamp='';$script:scoliaImporting=$false
$script:scoliaLocalRoot=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'configuration'
$script:scoliaAutoFile=Join-Path $script:scoliaLocalRoot 'reception-scolia.json';$script:scoliaAuto=$false
if([IO.File]::Exists($script:scoliaAutoFile)){$script:scoliaAuto=[bool](([IO.File]::ReadAllText($script:scoliaAutoFile)|ConvertFrom-Json).Automatic)}
function Get-ScoliaStateRoot {if($script:storeFolder){return (Join-Path $script:storeFolder 'scolia')};return (Join-Path $script:scoliaLocalRoot ('receptions-'+$script:scolia.Id))}
function Set-ScoliaReceiptState($entry,[string]$status,[string]$mat='', [string]$message=''){$s=New-Object ScoliaState;$s.Id=$entry.Id;$s.SchoolId=$script:scolia.Id;$s.Status=$status;$s.Matricule=$mat;$s.Message=$message;$s.Revision=$entry.Revision;[ScoliaLedger]::SaveState((Get-ScoliaStateRoot),$s)}
function Find-ScoliaLearner($entry){
 if(-not $script:data){return @()};return @($script:data.learners|Where-Object {([ScoliaLedger]::Key($_.Nom)) -eq ([ScoliaLedger]::Key($entry.Nom)) -and ([ScoliaLedger]::Key($_.PostNom)) -eq ([ScoliaLedger]::Key($entry.PostNom)) -and ([ScoliaLedger]::Key($_.Prenom)) -eq ([ScoliaLedger]::Key($entry.Prenom)) -and (Promo $_.Promotion) -eq (Promo $entry.Promotion) -and (($_.Annee -replace '\s','') -eq ($entry.Annee -replace '\s',''))})
}
function Add-ScoliaDiagnostic {
 if(-not $script:data){return};$issues=New-Object 'System.Collections.Generic.List[object]';foreach($issue in $script:data.issues){if($issue.Feuille -ne 'SCOLIA · Dossier à compléter'){$issues.Add($issue)}}
 foreach($a in @($script:data.learners)){
  $missing=@();foreach($field in @('Sexe','Naissance','Lieu','Contact1')){if([string]::IsNullOrWhiteSpace([string]$a.$field)){$missing+=$field}}
  if($missing.Count){$issues.Add([pscustomobject]@{Feuille='SCOLIA · Dossier à compléter';Ligne=$a.Row;Matricule=$a.Matricule;Numero='';Observation=('Informations manquantes : '+($missing -join ', ')+'. Complétez le dossier dans Apprenants.')})}
 };$script:data.issues=$issues.ToArray()
}
function Import-ScoliaReceipt($entry,[bool]$create=$false){
 Assert-ITMPermission 'Recevoir';if(-not $script:book -or -not $script:data -or $script:data.closed){throw 'Connectez une base de secrétariat préparée dont lʼannée est ouverte.'}
 if((Test-ScoliaOutgoingReceipt $entry)){throw 'Ce paiement provient déjà du secrétariat et ne doit pas y être réimporté.'}
 if($entry.SchoolId -cne $script:scolia.Id -or $entry.Kind -ne 'Paiement'){throw 'Transfert dʼune autre institution ou de type incorrect.'}
 if(-not (Test-ScoliaReceiptYear $entry)){throw 'Le transfert ne correspond pas à lʼannée du classeur connecté, ou ses années sont ambiguës. Connectez la base de cette année avant de réessayer.'}
 $state=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$entry.Id)
 if(Test-ScoliaApplied $entry $state){return [pscustomobject]@{ok=$true;already=$true;matricule=$entry.Matricule}}
 if(-not [ScoliaFinance]::IsTuition($entry)){
  Save-ScoliaAppliedReceipt $entry;Set-ScoliaReceiptState $entry 'Importé' $entry.Matricule;Write-ITMUserAudit 'Motif de paiement reçu' $entry.Id;$script:scoliaRosterPending=$true
  return [pscustomobject]@{ok=$true;already=$false;matricule=$entry.Matricule}
 }
 if($entry.Revision){$revisionReply=Import-ScoliaRevision $entry;if($revisionReply){Save-ScoliaAppliedReceipt $entry;Set-ScoliaReceiptState $entry 'Importé' $entry.Matricule;[void](Update-ITMAfterWrite $entry.Matricule $true);Write-ITMUserAudit 'Correction reçue' ($entry.Id+' / '+$entry.Revision);return $revisionReply}}
 $match=@(Find-ScoliaLearner $entry);if($match.Count -gt 1){throw 'Identité ambiguë : rapprochez les doublons avant import.'};if($match.Count -eq 0 -and -not $create){throw 'Apprenant inconnu : conservé en brouillon.'}
 $mat=$(if($match.Count -eq 1){[string]$match[0].Matricule}else{''})
 $packed=@($mat,$entry.Nom,$entry.PostNom,$entry.Prenom,$entry.Promotion,$entry.Annee,$entry.Sexe,$entry.Naissance,$entry.Lieu,$entry.Contact1,$entry.Contact2,$entry.Adresse,($entry.Reference+' · '+$entry.Compte))|ForEach-Object {([string]$_ -replace '[\t\r\n]',' ').Trim()}
 $args=Get-ScoliaFinanceImportArguments $entry $packed $create
 if($script:store){if($match.Count -eq 0){Ensure-ITMCapacity 'learners'};Ensure-ITMCapacity 'payments' $entry.Parts.Count;Start-ITMStoreTransaction}
 try{$reply=Invoke-ITMJsonDirect 'ITMAppScoliaImport' $args;if($script:store){Complete-ITMStoreTransaction};$script:committedThisAction=$true}catch{if($script:store){Undo-ITMStoreTransaction};throw}
 # Durable reference in the workbook is authoritative if this acknowledgement fails.
 Save-ScoliaAppliedReceipt $entry;Set-ScoliaReceiptState $entry 'Importé' ([string]$reply.matricule)
 # Un transfert ne modifie qu'un dossier et ses paiements : actualisation ciblée,
 # sans recharger toute la base Excel à chaque réception automatique.
 [void](Update-ITMAfterWrite ([string]$reply.matricule) $true)
 Write-ITMUserAudit 'Transfert comptable reçu' $entry.Id
 return $reply
}
function Get-ScoliaConnectedYear {
 if(-not $script:book -or -not $script:data){return ''}
 $years=@($script:data.promotions|ForEach-Object {[ScoliaSharing]::YearKey([string]$_.Annee)}|Where-Object {$_}|Select-Object -Unique)
 if($years.Count -eq 1){return $years[0]};return ''
}
function Test-ScoliaReceiptYear($entry){
 $year=Get-ScoliaConnectedYear
 return ($year -and ([ScoliaSharing]::YearKey([string]$entry.Annee)) -eq $year)
}
function Get-ScoliaInboxRows([string]$year='') {
 if(-not $year){$year=Get-ScoliaConnectedYear;if(-not $year){$year=$script:scolia.Year}}
 $rows=@();foreach($e in $script:scoliaEntries){if((Test-ScoliaOutgoingReceipt $e) -or $e.Kind -ne 'Paiement' -or ([ScoliaSharing]::YearKey($e.Annee)) -ne ([ScoliaSharing]::YearKey($year))){continue};$s=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$e.Id);if((Test-ScoliaApplied $e $s)){continue}
 $rows+=[pscustomobject]@{Id=$e.Id;Date=$e.Date;Reference=$e.Reference;Nom=($e.Nom+' '+$e.PostNom+' '+$e.Prenom);Promotion=$e.Promotion;Annee=$e.Annee;Montant=(Money $e.Montant);Etat=$(if($s){$s.Status}else{'À recevoir'});Observation=$(if($s){$s.Message}else{''})}}
 return $rows
}
function Receive-ScoliaNext {
 # Reception into local drafts is unconditional; workbook import is an explicit preference.
 if($script:busy -or $script:scoliaImporting -or -not $script:book -or -not $script:data -or $script:data.closed -or -not (Test-ScoliaPermission 'Recevoir')){return}
 foreach($e in $script:scoliaEntries){if((Test-ScoliaOutgoingReceipt $e) -or $e.Kind -ne 'Paiement' -or -not (Test-ScoliaReceiptYear $e)){continue};$s=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$e.Id);if((Test-ScoliaApplied $e $s) -or ($s -and $s.Status -eq 'Erreur' -and [string]$s.Revision -ceq [string]$e.Revision -and -not (Test-ScoliaReceiptAutoRecovery $e $s))){continue}
  if(-not $script:scoliaAuto -and -not (Test-ScoliaReceiptAutoRecovery $e $s) -and -not ($e.Revision -and (($s -and $s.Status -eq 'Importé') -or ($e.Compte -eq 'Secrétariat' -and $e.CommonReceipt -ne '1')))){continue}
  $match=@(Find-ScoliaLearner $e)
  if($match.Count -eq 0 -and -not $script:scoliaAuto){if(-not $s){Set-ScoliaReceiptState $e 'Brouillon' '' 'Apprenant absent : sélectionner puis importer, ou activer la récupération automatique.'};continue}
  $script:scoliaImporting=$true
  try{Do-Work {try{[void](Import-ScoliaReceipt $e $script:scoliaAuto)}catch{Set-ScoliaReceiptState $e 'Erreur' '' $_.Exception.Message;$script:status.Text='Un transfert demande une vérification dans Données > Transférés en brouillon.'}} 'Réception dʼun paiement de comptabilité...'}catch{$script:status.Text='Transfert conservé : '+$_.Exception.Message}finally{$script:scoliaImporting=$false}
  break
 }
}
function Start-ScoliaMonitor {
 if($script:scoliaTimer){return};$script:scoliaTimer=New-Object Windows.Forms.Timer;$script:scoliaTimer.Interval=2000
 $script:scoliaTimer.Add_Tick({
  if($script:scoliaMonitorRunning){return}
  $script:scoliaMonitorRunning=$true
  try{
  if(-not $script:scoliaSession -or $script:busy -or [Windows.Forms.Application]::OpenForms.Count -gt 1){return}
  if(-not $script:scoliaNetworkHost -and ([datetime]::UtcNow-$script:scoliaNetworkCheckUtc).TotalSeconds -ge 30){$script:scoliaNetworkCheckUtc=[datetime]::UtcNow;Start-ScoliaNetworkHost}
  if($script:scoliaRosterTask -and $script:scoliaRosterTask.IsCompleted -and $script:scoliaRosterTask.IsFaulted){$script:scoliaRosterPending=$true}
  if($script:scoliaRosterPending -and (-not $script:scoliaRosterTask -or $script:scoliaRosterTask.IsCompleted)){Publish-ScoliaRoster}
  $readContext=$script:scoliaRoot+'|'+$script:scolia.Id+'|'+$script:scolia.Year+'|'+$script:scoliaLocalRoot+'|'+(Get-ScoliaInboxRole)
  if($script:scoliaReadTask){if(-not $script:scoliaReadTask.IsCompleted){return};$task=$script:scoliaReadTask;$script:scoliaReadTask=$null
   if($script:scoliaReadContext -cne $readContext){return} # Never apply a previous school/year/session response.
   if($task.IsFaulted){$script:scoliaSync.State='Hors connexion';$script:scoliaSync.LastError=$task.Exception.GetBaseException().Message;$script:status.Text='Partage SCOLIA indisponible : '+$task.Exception.GetBaseException().Message;return}
   $script:scoliaSync.State='Connecté';$script:scoliaSync.LastSuccess=[datetime]::Now.ToString('yyyy-MM-dd HH:mm:ss');$script:scoliaSync.LastError='';$script:scoliaSync.FilesRead=$script:scoliaReader.LastFilesRead
   # The worker persisted and verified the inbox before delivering this result.
   $receivedEntries=@($task.Result.Entries);$script:scoliaRoster=$task.Result.Roster;$stamp=($readContext+'|'+$receivedEntries.Count.ToString()+'|'+$(if($receivedEntries.Count){(@($receivedEntries|ForEach-Object {$_.Id+':'+$_.Revision}) -join '|')}else{''}))
   if($stamp -ne $script:scoliaLedgerStamp){
    $script:scoliaLedgerStamp=$stamp;$script:scoliaEntries=$receivedEntries
    if($null -eq $script:viewRevision){$script:viewRevision=@{}}
    $script:viewRevision.Remove('Comptabilité')
    # Never rebuild a hidden journal. On screen, Set-Grid reconciles changed rows.
    if($script:currentPage -eq 'Comptabilité'){Refresh-ScoliaFinance;$script:viewRevision['Comptabilité']=$script:dataRevision}
   }
   Receive-ScoliaNext
  }else{$key=$readContext;if(-not $script:scoliaReader -or $script:scoliaReaderKey -cne $key){$script:scoliaReader=New-Object ScoliaIncrementalReader($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year);$script:scoliaReaderKey=$key};$script:scoliaReadContext=$readContext;$script:scoliaReadTask=$script:scoliaReader.ReadAndCacheAsync($script:scoliaLocalRoot,(Get-ScoliaInboxRole))}
 }catch{$script:status.Text='Réception SCOLIA : '+$_.Exception.Message}finally{$script:scoliaMonitorRunning=$false}});$script:scoliaTimer.Start()
}
function Show-ScoliaInbox {
 Assert-ITMPermission 'Recevoir';$d=New-ScoliaDialog 'Transférés en brouillon' 1080 670;$grid=New-Grid $d 16 60 980 470;$grid.Dock='Fill';$bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.AutoSizeMode='GrowAndShrink';$bar.MinimumSize=New-Object Drawing.Size(0,56);$d.Controls.Add($bar)
 $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=72;$header.Padding=New-Object Windows.Forms.Padding(12,8,12,8);$d.Controls.Add($header)
 $years=New-Object Windows.Forms.ComboBox;$years.DropDownStyle='DropDownList';$years.Dock='Right';$years.Width=140;$header.Controls.Add($years)
 $info=New-Label $header 'Année des transferts' 0 0 600 35;$info.Dock='Fill';$header.Controls.SetChildIndex($info,0)
 $context=@{Task=$null};$timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $refresh={Set-Grid $grid @(Get-ScoliaInboxRows $years.Text) @(@('Id','Identifiant transfert'),@('Date','Date'),@('Reference','Référence caisse'),@('Nom','Nom complet'),@('Promotion','Promotion'),@('Annee','Année'),@('Montant','Montant'),@('Etat','État'),@('Observation','Observation'))}
 $fillYears={ $selected=$years.Text;if(-not $selected){$selected=Get-ScoliaConnectedYear;if(-not $selected){$selected=$script:scolia.Year}};$values=@(@($script:scoliaEntries|Where-Object {$_.Kind -eq 'Paiement'}|ForEach-Object {[ScoliaSharing]::YearKey($_.Annee)})+[ScoliaSharing]::YearKey($script:scolia.Year)+[ScoliaSharing]::YearKey($selected)|Where-Object {$_}|Sort-Object -Descending -Unique);$years.Items.Clear();$years.Items.AddRange([object[]]$values);$years.SelectedItem=[ScoliaSharing]::YearKey($selected) }
 $years.Add_SelectedIndexChanged({& $refresh})
 [void](Add-ScoliaButton $bar 'Importer / réessayer la sélection' {try{if($grid.SelectedRows.Count -ne 1){throw 'Sélectionnez un transfert.'};$id=[string]$grid.SelectedRows[0].Cells['Identifiant transfert'].Value;$entries=@($script:scoliaEntries|Where-Object {$_.Id -ceq $id});if($entries.Count -ne 1){throw 'Transfert absent de la dernière lecture. Actualisez la liste.'};$entry=$entries[0]
  if(-not (Test-ScoliaReceiptYear $entry)){throw 'Connectez le classeur correspondant à lʼannée sélectionnée avant lʼimport.'}
  if([ITMMessageBox]::Show($d,'Importer ou réessayer ce paiement ? Un paiement déjà intégré sera reconnu grâce à sa référence de transfert. Le dossier sera créé uniquement si nécessaire.','SCOLIA','YesNo','Question') -ne 'Yes'){return}
  Do-Work {[void](Import-ScoliaReceipt $entry $true)} 'Import du transfert sélectionné...'; & $refresh
 }catch{[void][ITMMessageBox]::Show($d,$_.Exception.Message,'SCOLIA')}})
 $read=Add-ScoliaButton $bar 'Actualiser le partage' {if($context.Task){return};$context.Task=[ScoliaSharing]::ReadAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year);$read.Enabled=$false;$info.Text='Lecture du partage en cours...';$timer.Start()}
 $timer.Add_Tick({if(-not $context.Task -or -not $context.Task.IsCompleted){return};$timer.Stop();$task=$context.Task;$context.Task=$null;$read.Enabled=$true
  try{if($task.IsFaulted){throw $task.Exception.GetBaseException()};$script:scoliaEntries=@($task.Result.Entries);$script:scoliaRoster=$task.Result.Roster;& $fillYears;& $refresh;Refresh-ScoliaFinance;$info.Text='Journal actualisé à '+(Get-Date -Format 'HH:mm:ss')+'.'}
  catch{$info.Text='Partage inaccessible. Réessayez.';$script:status.Text='Liste précédente conservée : '+$_.Exception.Message}
 })
 $close=Add-ScoliaButton $bar 'Fermer' {$d.Close()};$d.CancelButton=$close;$d.Controls.SetChildIndex($grid,0);$d.Controls.SetChildIndex($bar,1);$d.Controls.SetChildIndex($header,2)
 $wasRunning=($script:scoliaTimer -and $script:scoliaTimer.Enabled);if($wasRunning){$script:scoliaTimer.Stop()}
 & $fillYears;& $refresh;Apply-ITMThemeForm $d;$read.PerformClick();try{[void]$d.ShowDialog($script:main)}finally{$timer.Stop();$timer.Dispose();$d.Dispose();if($wasRunning){$script:scoliaTimer.Start()}}
}
function Show-ScoliaEntry([bool]$expense=$false,[switch]$LayoutOnly){
 Assert-ITMPermission 'Encaisser';if($expense){Assert-ITMPermission 'Comptabilite'};if(-not $expense){$cashLearners=@(Get-ScoliaPaymentLearners);if(-not $cashLearners.Count){throw 'Aucun apprenant de cette année nʼest disponible. Connectez les données du secrétariat ou importez les apprenants dans le compte comptable.'}};if([IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))){throw 'Une écriture attend une vérification. Utilisez Vérifier opération en attente avant une nouvelle saisie.'};$d=New-ScoliaDialog $(if($expense){'Nouvelle dépense'}else{'Encaisser un paiement'}) 820 590;$t=New-ScoliaTable $d;$fields=@{};$learnerFields=@('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Adresse')
 if(-not $expense){$t.Tag='scolia:cash-layout';$t.AutoScroll=$false;$d.AutoScroll=$false}
 if(-not $expense){
  $selector=New-Object Windows.Forms.ComboBox;$selector.DropDownStyle='DropDownList';$selector.AutoCompleteSource='ListItems';$selector.AutoCompleteMode='SuggestAppend'
  $selector.Items.AddRange([object[]]@($cashLearners|ForEach-Object {$_.Promotion+' · '+$_.Nom+' '+$_.PostNom+' '+$_.Prenom+' · '+$_.Matricule}))
  [void](Add-ScoliaField $t 'Apprenant' $selector)
  $summaryRow=$t.RowCount;$t.RowCount++;[void]$t.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
  $identitySummary=New-Label $t 'Sélectionnez un apprenant importé du secrétariat.' 0 0 650 50 10 $true
  $identitySummary.AutoSize=$true;$identitySummary.Dock='Top';$identitySummary.Margin=New-Object Windows.Forms.Padding(0,2,0,12)
  $t.SetCellPosition($identitySummary,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$summaryRow)));$t.SetColumnSpan($identitySummary,2)
  $selector.Add_SelectedIndexChanged({if($selector.SelectedIndex -ge 0){$a=$cashLearners[$selector.SelectedIndex];$identitySummary.Text=($a.Nom+' '+$a.PostNom+' '+$a.Prenom).Trim()+[Environment]::NewLine+'Matricule : '+$a.Matricule+'   ·   Classe : '+$a.Promotion}})
 }else{$fields['Description']=Add-ScoliaText $t 'Objet / catégorie de dépense'}
 $receiptIdentity=$null
 $reference=Add-ScoliaText $t $(if($expense){'Référence de la dépense'}else{'Référence automatique'})
 if(-not $expense){$receiptIdentity=New-ScoliaReceiptIdentity;$reference.Text=$receiptIdentity.Reference;$reference.ReadOnly=$true;$reference.AccessibleDescription='Référence générée automatiquement, conservée dans le journal, les brouillons et la synchronisation.'}
 $feeOptions=@();$category=$null
 if(-not $expense){$feeOptions=@([pscustomobject]@{Code='SCOLARITE';Name='Scolarité · mensualités'})+@([ScoliaFinance]::Fees($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year).Items|Where-Object {$_.Active});$category=New-Object Windows.Forms.ComboBox;$category.DropDownStyle='DropDownList';$category.Items.AddRange([object[]]@($feeOptions|ForEach-Object {$_.Name+' ['+$_.Code+']'}));$category.SelectedIndex=0;[void](Add-ScoliaField $t 'Motif du paiement' $category)}
 $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Short';$date.MaxDate=[datetime]::Today;[void](Add-ScoliaField $t 'Date de lʼopération' $date)
 $amount=New-Object Windows.Forms.NumericUpDown;$amount.Maximum=100000000000;$amount.Minimum=1;$amount.ThousandsSeparator=$true;[void](Add-ScoliaField $t 'Montant en FC' $amount)
 $messageRow=$t.RowCount;$t.RowCount++;[void]$t.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
 $message=New-Label $t $(if($expense){'Renseignez la référence et le libellé de la dépense.'}else{'La référence du reçu est générée automatiquement et partagée entre les comptes. Seule la scolarité est répartie par mois.'}) 0 0 450 56;$t.SetCellPosition($message,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$messageRow)));$t.SetColumnSpan($message,2);$message.Dock='Top';$message.AutoSize=$true;$message.Margin=New-Object Windows.Forms.Padding(0,8,0,0)
 $flow=New-Object Windows.Forms.FlowLayoutPanel;$flow.Dock='Bottom';$flow.AutoSize=$true;$flow.AutoSizeMode='GrowAndShrink';$flow.MinimumSize=New-Object Drawing.Size(0,62);$flow.FlowDirection='RightToLeft';$flow.Padding=New-Object Windows.Forms.Padding(12,6,12,6);$d.Controls.Add($flow);$t.BringToFront();$pending=$null
 $buildEntry={
  Assert-ITMPermission 'Encaisser';$entry=New-Object ScoliaEntry;$entry.Id=$(if($expense){[guid]::NewGuid().ToString('N')}else{$receiptIdentity.Id});$entry.Kind=$(if($expense){'Depense'}else{'Paiement'});$entry.Annee=$script:scolia.Year;$entry.Reference=$(if($expense){$reference.Text.Trim()}else{$receiptIdentity.Reference});$entry.Date=$date.Value.ToString('yyyy-MM-dd');$typedAmount=Get-ScoliaTypedAmount $amount.Text $amount.Maximum;if($null -eq $typedAmount -or $typedAmount -le 0){throw 'Saisissez un montant positif en FC, sans fraction.'};$amount.Value=$typedAmount;$entry.Montant=$typedAmount;$entry.Compte=$(if($expense){'Comptabilité'}else{Get-ScoliaPaymentAccount});$entry.OperatorId=$script:scoliaSession.AccountId;$entry.OperatorName=$script:scoliaSession.Name;$entry.ConfigRevision=$script:scoliaSession.Revision
  foreach($name in $fields.Keys){$entry.$name=$fields[$name].Text.Trim()};if(-not $expense){if($selector.SelectedIndex -lt 0){throw 'Sélectionnez un apprenant importé.'};$a=$cashLearners[$selector.SelectedIndex];$entry.Matricule=$a.Matricule;$entry.Promotion=$a.Promotion;$entry.CommonReceipt='1';$entry.Category=$feeOptions[$category.SelectedIndex].Code;$entry.CategoryName=$feeOptions[$category.SelectedIndex].Name;foreach($n in $learnerFields){$entry.$n=[string]$a.$n}};return $entry
 }
 $save=Add-ScoliaButton $flow 'Enregistrer lʼopération' {try{
  if($script:scoliaCommitTask){throw 'Une opération de caisse attend son résultat. Ne ressaisissez pas le même paiement.'}
  if(-not $expense -and (Get-ScoliaPaymentAccount) -eq 'Secrétariat'){
   Start-ITMActivity 'Préparation du paiement du secrétariat…' $save
   try{Sync-ScoliaRosterForPayment}finally{Stop-ITMActivity}
  }
  $entry=& $buildEntry;$entry.SchoolId=$script:scolia.Id;[ScoliaLedger]::ValidateDraft($entry,$script:scolia.Id);$fee=[decimal]0;if(-not $expense){$fee=[decimal](@($script:scolia.Classes|Where-Object {$_.Code -eq $entry.Promotion})[0].Fee)}
  # Save the operation identity locally before the remote commit, for crash recovery.
  Write-ScoliaJson (Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json') ([pscustomobject]@{Id=$entry.Id;SchoolId=$script:scolia.Id;Root=$script:scoliaRoot;Reference=$entry.Reference;Montant=$entry.Montant;Date=$entry.Date})
  $script:scoliaCommitTask=[ScoliaLedger]::CommitAsync($script:scoliaRoot,$script:scolia.Id,$entry,[int]$script:scolia.StartMonth,[int]$script:scolia.Months,$fee)
  $save.Enabled=$false;$cancel.Enabled=$false;Start-ITMActivity 'Enregistrement en comptabilité...' $save;$message.Text='Enregistrement en cours ; conservez cette fenêtre ouverte.';$poll.Start()
 }catch{$message.Text=$_.Exception.Message}}
 $draft=Add-ScoliaButton $flow 'Conserver en brouillon' {try{if($script:scoliaCommitTask){throw 'Attendez le résultat de lʼenregistrement.'};if([IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))){throw 'Vérifiez lʼopération en attente avant de conserver un nouveau brouillon.'};$entry=& $buildEntry;[void][ScoliaLedger]::SaveDraft((Get-ScoliaDraftFolder),$script:scolia.Id,$entry);Write-ITMUserAudit 'Brouillon de caisse conservé' $entry.Id;$script:status.Text='Brouillon local conservé ; aucun paiement nʼest comptabilisé avant sa validation.';$d.Close()}catch{$message.Text=$_.Exception.Message}}
 $cancel=Add-ScoliaButton $flow 'Fermer' {$d.Close()};$d.CancelButton=$cancel;$poll=New-Object Windows.Forms.Timer;$poll.Interval=150
 $poll.Add_Tick({if(-not $script:scoliaCommitTask.IsCompleted){return};$poll.Stop();$task=$script:scoliaCommitTask;$script:scoliaCommitTask=$null;Stop-ITMActivity
  try{if($task.IsFaulted){throw $task.Exception.GetBaseException()};$saved=$task.Result;$script:scoliaLastReceipt=$saved;[IO.File]::Delete((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'));Write-ITMUserAudit 'Écriture de caisse SCOLIA' $saved.Id;$script:status.Text='Opération enregistrée : '+$saved.Reference
   if($saved.Kind -eq 'Paiement' -and (Test-ScoliaPermission 'Recevoir') -and $script:book){try{[void](Import-ScoliaReceipt $saved $false)}catch{$script:status.Text='Reçu '+$saved.Reference+' enregistré dans le journal. Import au secrétariat à reprendre : '+$_.Exception.Message;[void][ITMMessageBox]::Show($d,$script:status.Text,'Paiement enregistré · import à reprendre')}}
   $script:scoliaLedgerStamp='';$d.DialogResult='OK';$d.Close()}catch{$cause=$_.Exception.GetBaseException();$definite=($cause -is [InvalidOperationException] -or $cause -is [ArgumentException] -or $cause -is [IO.InvalidDataException]);if($definite){[IO.File]::Delete((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))};$message.Text=$cause.Message+' Vérifiez lʼopération en attente avant de ressaisir après une coupure réseau.';$save.Enabled=$definite;$cancel.Enabled=$true}
 });$d.Add_FormClosing({param($sender,$e)if($script:scoliaCommitTask){$e.Cancel=$true}})
 if(-not $expense -and $script:payMat){for($i=0;$i -lt $cashLearners.Count;$i++){if($cashLearners[$i].Matricule -eq $script:payMat){$selector.SelectedIndex=$i;break}};if($script:payAmount -and (Get-ScoliaTypedAmount $script:payAmount.Text $script:payAmount.Maximum) -gt 0){$amount.Value=Get-ScoliaTypedAmount $script:payAmount.Text $script:payAmount.Maximum};if($script:payDate){$date.Value=$script:payDate.Value}}
 if(-not $expense){Enable-ScoliaCashTableLayout $t}
 Apply-ITMThemeForm $d;if($LayoutOnly){return [pscustomobject]@{Form=$d;Table=$t;Reference=$reference;Identity=$receiptIdentity;Selector=$selector;Message=$message;Amount=$amount;Date=$date;Footer=$flow;Poll=$poll;BuildEntry=$buildEntry}}
 try{[void]$d.ShowDialog($script:main)}finally{$poll.Stop();$poll.Dispose();$d.Dispose()}
}
function Initialize-ScoliaFinanceIndex {
 $year=[ScoliaSharing]::YearKey($script:scolia.Year)
 if($script:scoliaFinanceIndex -and $script:scoliaFinanceIndex.Year -ceq $year -and [object]::ReferenceEquals($script:scoliaFinanceIndex.Source,$script:scoliaEntries)){return}
 $items=@($script:scoliaEntries|Where-Object {([ScoliaSharing]::YearKey($_.Annee)) -eq $year}|Sort-Object Date,Reference,Id)
 $index=New-Object 'System.Collections.Generic.List[object]';$income=[decimal]0;$expense=[decimal]0
 foreach($entry in $items){
  if($entry.Kind -eq 'Paiement'){$income+=[decimal]$entry.Montant}elseif($entry.Kind -eq 'Depense'){$expense+=[decimal]$entry.Montant}
  $text=Key ($entry.Reference+' '+$entry.Nom+' '+$entry.PostNom+' '+$entry.Prenom+' '+$entry.Promotion+' '+$entry.Description+' '+$entry.Kind+' '+$entry.OperatorName)
  $row=[pscustomobject]@{Id=$entry.Id;Date=$entry.Date;Reference=$entry.Reference;Type=$entry.Kind;Nom=($entry.Nom+' '+$entry.PostNom+' '+$entry.Prenom);Promotion=$entry.Promotion;Montant=(Money $entry.Montant);Categorie=$(if($entry.CategoryName){$entry.CategoryName}else{[ScoliaFinance]::CategoryKey($entry)});Etat=$(if($entry.Cancelled -eq '1'){'Annulé'}elseif($entry.Revision){'Corrigé v'+$entry.Revision}else{'Validé'});Compte=$entry.Compte;Operateur=$entry.OperatorName;Libelle=$entry.Description;Mois=(@($entry.Parts|ForEach-Object {$_.Mois}) -join ', ')}
  $index.Add([pscustomobject]@{Text=$text;Row=$row})
 }
 $script:scoliaFinanceIndex=@{Source=$script:scoliaEntries;Year=$year;Items=$items;Rows=$index.ToArray();Income=$income;Expense=$expense}
}
function Get-ScoliaFinanceRows {
 Initialize-ScoliaFinanceIndex
 $terms=@((Key $script:scoliaFinanceQuery) -split '\s+'|Where-Object {$_})
 foreach($item in $script:scoliaFinanceIndex.Rows){
  $matches=$true;foreach($term in $terms){if($item.Text.IndexOf($term,[StringComparison]::Ordinal) -lt 0){$matches=$false;break}}
  if($matches){$item.Row}
 }
}
function Refresh-ScoliaFinance {
 if(-not $script:scoliaFinanceGrid){return};Initialize-ScoliaFinanceIndex;$items=$script:scoliaFinanceIndex.Items;$income=$script:scoliaFinanceIndex.Income;$expense=$script:scoliaFinanceIndex.Expense
 $script:scoliaFinanceSummary.Text='Recettes : '+(Money $income)+'     Dépenses : '+(Money $expense)+'     Solde des opérations SCOLIA : '+(Money ($income-$expense))
 $visibleRows=@(Get-ScoliaFinanceRows)
 $script:scoliaFinanceSummary.Text+='     Journal affiché : '+$visibleRows.Count+' / '+$items.Count
 Set-Grid $script:scoliaFinanceGrid $visibleRows @(@('Date','Date'),@('Reference','Référence'),@('Type','Opération'),@('Categorie','Motif de paiement'),@('Etat','État'),@('Nom','Nom complet'),@('Promotion','Promotion'),@('Montant','Montant'),@('Mois','Mois affectés'),@('Libelle','Libellé'),@('Compte','Compte'),@('Operateur','Opérateur'),@('Id','Identifiant opération'))
 $script:scoliaFinanceGrid.Columns['Identifiant opération'].Visible=$false
 if($script:scoliaDashboard){$script:scoliaDashboard.Text=$script:scoliaFinanceSummary.Text+"`r`n`r`n"+$items.Count+' opération(s) dans le journal de lʼannée '+$script:scolia.Year+"`r`nLes paiements sont transmis au secrétariat par le dossier SCOLIA connecté."}
}
function Resolve-ScoliaReceipt([string]$reference,[string]$kind,[string]$year){
 $matches=@($script:scoliaEntries|Where-Object {$_.SchoolId -ceq $script:scolia.Id -and $_.Reference -ceq $reference -and $_.Kind -ceq $kind -and ([ScoliaSharing]::YearKey($_.Annee)) -eq ([ScoliaSharing]::YearKey($year))})
 if($matches.Count -ne 1){throw 'Opération introuvable ou ambiguë. Actualisez le journal puis sélectionnez la pièce.'};return $matches[0]
}
function Show-ScoliaReceipt {
 Assert-ITMPermission 'Exporter';$e=$script:scoliaLastReceipt
 if($script:scoliaFinanceGrid -and $script:scoliaFinanceGrid.SelectedRows.Count -eq 1){$row=$script:scoliaFinanceGrid.SelectedRows[0];$e=Resolve-ScoliaReceipt ([string]$row.Cells['Référence'].Value) ([string]$row.Cells['Opération'].Value) $script:scolia.Year}
 if(-not $e -or $e.SchoolId -cne $script:scolia.Id -or ([ScoliaSharing]::YearKey($e.Annee)) -ne ([ScoliaSharing]::YearKey($script:scolia.Year))){throw 'Sélectionnez une opération de caisse de cette année.'}
 $title=$(if($e.Kind -eq 'Depense'){'DÉPENSE'}else{'PAIEMENT'})
 Print-FichePreview ('SCOLIA · '+$script:scolia.School+"`r`nPIÈCE DE CAISSE — "+$title+' : '+$e.Reference+"`r`nAnnée scolaire : "+$e.Annee+"`r`nMotif : "+$(if($e.CategoryName){$e.CategoryName}else{[ScoliaFinance]::CategoryKey($e)})+"`r`nÉtat : "+$(if($e.Cancelled -eq '1'){'ANNULÉ'}elseif($e.Revision){'CORRIGÉ v'+$e.Revision}else{'VALIDÉ'})+"`r`nDate : "+$e.Date+"`r`nCompte : "+$e.Compte+"`r`nEnregistré par : "+$e.OperatorName+$(if($e.Revision){"`r`nCorrigé par : "+$e.CorrectionAuthor+"`r`nMotif de correction : "+$e.CorrectionReason}else{''})+"`r`n"+$e.Nom+' '+$e.PostNom+' '+$e.Prenom+"`r`nClasse : "+$e.Promotion+"`r`nMontant : "+(Money $e.Montant)+"`r`n"+(@($e.Parts|ForEach-Object {$_.Mois+' : '+(Money $_.Montant)}) -join "`r`n")+"`r`n"+$e.Description+"`r`nRéférence de transfert : "+$e.Id)
}
function Apply-ScoliaWorkbookConfiguration {
 Assert-ITMPermission 'Administrer';if(-not $script:book){throw 'Connectez ou générez dʼabord un classeur préparé depuis Données.'}
 $args=@($script:scolia.Id,$script:scolia.Year,([ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months) -join '|'),(@($script:scolia.Classes|ForEach-Object {$_.Code}) -join '|'),(@($script:scolia.Classes|ForEach-Object {[decimal]$_.Fee|ForEach-Object {$_.ToString('0',[Globalization.CultureInfo]::InvariantCulture)}}) -join '|'),$true)
 if($script:store){Start-ITMStoreTransaction};try{[void](Invoke-ITMJsonDirect 'ITMAppScoliaConfiguration' $args);if($script:store){Complete-ITMStoreTransaction}}catch{if($script:store){Undo-ITMStoreTransaction};throw};Refresh-Data
}
function Show-ScoliaAbout {Show-TextDetail 'À propos de SCOLIA' ("SCOLIA — Gestion scolaire et comptable`r`n`r`nGestion des apprenants, promotions, mensualités, prises en charge, cartes dʼaccès aux examens, côtes et délibération. Espace de caisse avec recettes, dépenses, journal et reçus. Transfert des paiements entre postes sur un dossier réseau, contrôle des doublons, brouillons et diagnostic des informations manquantes.`r`n`r`nModes : Secrétariat, Comptabilité et Mixte.`r`nVersion : 102 · Finance synchronisée`r`n`r`nProgrammeur : Jean Paul MPOLO LONO`r`nPhone : +243 977 152 825`r`nEmail : jeanpaulmpolo242@gmail.com")}
function Set-ScoliaWorkspace {
 if(-not $script:scoliaSession -or -not $script:nav.ContainsKey('Comptabilité')){return}
 if($script:scoliaAdminText){$script:scoliaAdminText.Text='SCOLIA · '+$script:scolia.School+"`r`nParamètres de lʼétablissement, calendrier, classes et protection des comptes."};if($script:scoliaDataText){$script:scoliaDataText.Text='Dossier SCOLIA connecté'+"`r`n"+$script:scoliaRoot+"`r`nAnnée : "+$script:scolia.Year+"`r`nLes paiements sont publiés dans ce journal et reçus par le secrétariat."}
 $r=$script:scoliaSession.Role;$all=@('Tableau de bord','Apprenants','Paiements','Exports','Diagnostic','Administration','Préférences','Données','Scolarité','Comptabilité')
 $allowed=$(if($r -eq 'Comptabilité'){@('Tableau de bord','Comptabilité','Diagnostic','Administration','Préférences','Données')}elseif($r -eq 'Secrétariat'){$all}else{$all})
 $script:tabsNames=$allowed;foreach($name in $script:nav.Keys){$script:nav[$name].Visible=($name -in $allowed)}
 $script:ribbonPage='';Update-ITMAccountButton;$script:main.Text='SCOLIA | '+$script:scolia.School+' | '+$r;$script:brandTitle.Text='SCOLIA';$script:brandNote.Text=$script:scolia.School+' · '+$r
 foreach($overlay in @($script:scoliaAdminOverlay,$script:scoliaDataOverlay)){if($overlay){$overlay.Visible=($r -eq 'Comptabilité');if($overlay.Visible){$overlay.BringToFront()}}}
 if($script:scoliaDashboard){$script:scoliaDashboard.Visible=($r -eq 'Comptabilité');if($r -eq 'Comptabilité'){$script:scoliaDashboard.BringToFront()}}
 Show-Page $(if($r -eq 'Comptabilité'){'Comptabilité'}else{'Tableau de bord'});Update-WorkspaceState
}
function Build-ScoliaWorkspace {
 $page=New-Object Windows.Forms.Panel;$page.Dock='Fill';$page.Visible=$false;$script:pages['Comptabilité']=$page;$script:pages['Données'].Parent.Controls.Add($page)
 $button=New-Object ITMWorkspaceTab;$button.Text='Comptabilité';$button.Tag='Comptabilité';$button.Font=$script:main.Font;$button.Add_Click({Show-Page 'Comptabilité'});$script:tabs.Controls.Add($button);$script:nav['Comptabilité']=$button;$script:tabsNames+=@('Comptabilité')
 foreach($active in @($true,$false)){
  $navIconKey='Comptabilité__' + [string]$active
  $script:navIcons[$navIconKey]=New-NavIcon 'Comptabilité' $active
 }
 $header=New-Object Windows.Forms.FlowLayoutPanel;$header.Dock='Top';$header.AutoSize=$true;$header.Padding=New-Object Windows.Forms.Padding(16);$page.Controls.Add($header)
 $script:scoliaFinanceSummary=New-Label $header '' 0 0 950 55 13 $true;$script:scoliaFinanceSummary.AutoSize=$true
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Top';$bar.AutoSize=$true;$bar.Padding=New-Object Windows.Forms.Padding(16);$page.Controls.Add($bar)
 [void](Add-ScoliaButton $bar 'Corriger / annuler le reçu' {try{Show-ScoliaCorrection}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Rapprochement des comptes' {try{Show-ScoliaReconciliation}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Historique des corrections' {try{Show-ScoliaRevisionHistory}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Motifs de paiement' {try{Show-ScoliaFees}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Comptes nominatifs' {try{Show-ScoliaAccounts}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Encaisser' {try{Show-ScoliaEntry}catch{App-Error $_}});[void](Add-ScoliaButton $bar 'Nouvelle dépense' {try{Show-ScoliaEntry $true}catch{App-Error $_}});[void](Add-ScoliaButton $bar 'Journal CSV' {try{Save-ITMCsv @(Get-ScoliaFinanceRows) 'SCOLIA_Journal.csv'}catch{App-Error $_}});[void](Add-ScoliaButton $bar 'Reçu / imprimer' {try{Show-ScoliaReceipt}catch{App-Error $_}});[void](Add-ScoliaButton $bar 'Vérifier opération en attente' {try{Show-ScoliaPending}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Importer les paiements' {try{Show-ScoliaImports}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Visualiser lʼimportation' {try{Show-ScoliaImports 'Paiements' $true}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Brouillons reçus' {try{Show-ScoliaImports 'Paiements' $false $true}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Importer les apprenants' {try{Show-ScoliaImports 'Apprenants'}catch{App-Error $_}})
 $script:scoliaFinanceQuery=''
 $searchLabel=New-Object Windows.Forms.Label;$searchLabel.Text='Rechercher dans le journal';$searchLabel.AutoSize=$true;$searchLabel.Margin=New-Object Windows.Forms.Padding(6,12,6,6);$bar.Controls.Add($searchLabel)
 $financeSearch=New-Object Windows.Forms.TextBox;$financeSearch.Width=240;$financeSearch.Margin=New-Object Windows.Forms.Padding(6,8,6,6);$financeSearch.AccessibleName='Rechercher par référence, nom, promotion ou libellé';$bar.Controls.Add($financeSearch)
 $script:scoliaFinanceSearch=$financeSearch
 $script:financeSearchTimer=New-Object Windows.Forms.Timer;$script:financeSearchTimer.Interval=180
 $financeSearch.Add_TextChanged({param($sender,$e)$script:scoliaFinanceQuery=$sender.Text;$script:financeSearchTimer.Stop();$script:financeSearchTimer.Start()})
 $script:financeSearchTimer.Add_Tick({if($script:busy){return};$script:financeSearchTimer.Stop();try{Refresh-ScoliaFinance}catch{App-Error $_}})
 $script:main.Add_FormClosed({$script:financeSearchTimer.Stop();$script:financeSearchTimer.Dispose()})
 $script:scoliaFinanceGrid=New-Grid $page 16 120 1000 400;$script:scoliaFinanceGrid.Dock='Fill';$page.Controls.SetChildIndex($header,2);$page.Controls.SetChildIndex($bar,1);$page.Controls.SetChildIndex($script:scoliaFinanceGrid,0)
 $script:scoliaDashboard=New-Object ITMReadableLabel;$script:scoliaDashboard.Dock='Fill';$script:scoliaDashboard.Padding=New-Object Windows.Forms.Padding(28);$script:scoliaDashboard.Font=New-Object Drawing.Font('Segoe UI',16);$script:pages['Tableau de bord'].Controls.Add($script:scoliaDashboard);$script:scoliaDashboard.Visible=$false
 # Manual batch import is available in both workspaces.

 $dataBar=New-Object Windows.Forms.FlowLayoutPanel;$dataBar.Dock='Bottom';$dataBar.AutoSize=$true;$script:pages['Données'].Controls.Add($dataBar)
 [void](Add-ScoliaButton $dataBar 'Transférés en brouillon' {try{Show-ScoliaInbox}catch{App-Error $_}});[void](Add-ScoliaButton $dataBar 'Appliquer la configuration SCOLIA' {try{Do-Work {Apply-ScoliaWorkbookConfiguration} 'Configuration des classes et mensualités...'}catch{App-Error $_}})
 [void](Add-ScoliaButton $dataBar 'Importer les paiements' {try{Show-ScoliaImports}catch{App-Error $_}});[void](Add-ScoliaButton $dataBar 'Visualiser lʼimportation' {try{Show-ScoliaImports 'Paiements' $true}catch{App-Error $_}})
 [void](Add-ScoliaButton $dataBar 'Brouillons reçus' {try{Show-ScoliaImports 'Paiements' $false $true}catch{App-Error $_}})
 $adminBar=New-Object Windows.Forms.FlowLayoutPanel;$adminBar.Dock='Bottom';$adminBar.AutoSize=$true;$script:pages['Administration'].Controls.Add($adminBar)
 [void](Add-ScoliaButton $adminBar 'Motifs de paiement' {try{Show-ScoliaFees}catch{App-Error $_}});[void](Add-ScoliaButton $adminBar 'Comptes nominatifs' {try{Show-ScoliaAccounts}catch{App-Error $_}})
 [void](Add-ScoliaButton $adminBar 'Paramètres SCOLIA' {try{Show-ScoliaSettings}catch{App-Error $_}});[void](Add-ScoliaButton $adminBar 'À propos de SCOLIA' {Show-ScoliaAbout})
 $prefBar=New-Object Windows.Forms.FlowLayoutPanel;$prefBar.Dock='Bottom';$prefBar.AutoSize=$true;$script:pages['Préférences'].Controls.Add($prefBar)
 $auto=New-Object ITMReadableCheckBox;$auto.Text='Importer automatiquement les paiements reçus (brouillons toujours conservés)';$auto.AutoSize=$true;$auto.Checked=$script:scoliaAuto;$auto.Margin=New-Object Windows.Forms.Padding(20,12,20,12);$prefBar.Controls.Add($auto)
 $auto.Add_CheckedChanged({param($sender,$e)if($script:scoliaChangingAuto){return};try{Assert-ITMPermission 'Recevoir';Write-ScoliaJson $script:scoliaAutoFile ([pscustomobject]@{Automatic=$sender.Checked});$script:scoliaAuto=$sender.Checked}catch{$script:scoliaChangingAuto=$true;try{$sender.Checked=$script:scoliaAuto}finally{$script:scoliaChangingAuto=$false};App-Error $_}})
 $script:scoliaAutoControl=$auto
 foreach($target in @('Administration','Données')){
  $overlay=New-Object Windows.Forms.Panel;$overlay.Dock='Fill';$overlay.Visible=$false;$script:pages[$target].Controls.Add($overlay)
  $table=New-ScoliaTable $overlay;$table.AutoScroll=$true
  if($target -eq 'Administration'){
   $text=New-Label $table ('SCOLIA · '+$script:scolia.School+"`r`nParamètres de lʼétablissement, calendrier, classes et protection des comptes.") 0 0 620 90 13 $true;$table.RowCount=1;$table.SetColumnSpan($text,2)
   $flow=New-Object Windows.Forms.FlowLayoutPanel;$flow.AutoSize=$true;[void](Add-ScoliaField $table 'Configuration' $flow)
   [void](Add-ScoliaButton $flow 'Paramètres SCOLIA' {try{Show-ScoliaSettings}catch{App-Error $_}});[void](Add-ScoliaButton $flow 'À propos de SCOLIA' {Show-ScoliaAbout});$script:scoliaAdminText=$text;$script:scoliaAdminOverlay=$overlay
  }else{
   $text=New-Label $table ('Dossier SCOLIA connecté'+"`r`n"+$script:scoliaRoot+"`r`nAnnée : "+$script:scolia.Year+"`r`nLes paiements sont publiés dans ce journal et reçus par le secrétariat.") 0 0 640 130 12;$table.RowCount=1;$table.SetColumnSpan($text,2)
   $flow=New-Object Windows.Forms.FlowLayoutPanel;$flow.AutoSize=$true;[void](Add-ScoliaField $table 'Partage et contrôle' $flow)
   [void](Add-ScoliaButton $flow 'Dossier SCOLIA' {try{Open-ScoliaFolder}catch{App-Error $_}});[void](Add-ScoliaButton $flow 'Vérifier opération en attente' {try{Show-ScoliaPending}catch{App-Error $_}});$script:scoliaDataText=$text;$script:scoliaDataOverlay=$overlay
  }
 }
}
function Publish-ScoliaRoster {
 if(Get-Command Sync-ScoliaDisciplineSnapshot -ErrorAction SilentlyContinue){[void](Sync-ScoliaDisciplineSnapshot)}
 if(-not $script:scoliaSession -or -not (Test-ScoliaPermission 'Recevoir') -or -not $script:data -or -not $script:book){return}
 if((Get-ScoliaConnectedYear) -ne ([ScoliaSharing]::YearKey($script:scolia.Year))){$script:scoliaRosterPending=$false;return}
 $publicationKey=([string]$script:connectedWorkbookPath+'|'+[string]$script:storeFolder+'|'+[string]$script:scoliaRoot+'|'+[string]$script:scolia.Id+'|'+[string]$script:scolia.Year+'|'+[string]$script:scolia.Revision+'|'+[string]$script:scoliaSession.AccountId+'|'+[string]$script:dataRevision+'|'+[string]$script:peLedger.Revision)
 if($script:scoliaRosterTask -and -not $script:scoliaRosterTask.IsCompleted){if($script:scoliaRosterPreparedKey -cne $publicationKey){$script:scoliaRosterPending=$true};return}
 if($script:storeLock -and $script:storeLock.CanWrite -and -not $script:excelBorrowed -and -not $script:scoliaRosterPending -and $script:scoliaRosterTask -and -not $script:scoliaRosterTask.IsFaulted -and -not $script:scoliaRosterTask.IsCanceled -and $script:scoliaRosterPreparedKey -ceq $publicationKey){$script:scoliaRosterPending=$false;return}

 if($script:scoliaRosterTask -and $script:scoliaRosterTask.IsFaulted){$script:status.Text='Répertoire réseau non actualisé : '+$script:scoliaRosterTask.Exception.GetBaseException().Message}
 $roster=New-Object ScoliaRoster;$roster.SchoolId=$script:scolia.Id;$roster.Year=$script:scolia.Year
 $references=Invoke-ITMJsonDirect 'ITMAppScoliaReferences';foreach($id in @($references)){if([string]$id -match '^[a-f0-9]{32}$'){$roster.Imported.Add([string]$id)}}
 foreach($a in $script:data.learners){if(($a.Annee -replace '\s','') -ne ($script:scolia.Year -replace '\s','')){continue};$l=New-Object ScoliaLearner
  foreach($n in @('Matricule','Nom','PostNom','Prenom','Promotion','Annee','Sexe','Naissance','Lieu','Contact1','Contact2','Adresse','Tuteur','Inscription','Statut')){$l.$n=[string]$a.$n};$f=$script:fees[(Promo $a.Promotion)];if(-not $f){throw ('Publication impossible : tarif manquant pour la promotion '+$a.Promotion)};$l.Mensuel=[decimal]$f.Mensuel;$l.Annuel=Get-ITMPECAnnual $a
  foreach($m in [ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months)){$part=New-Object ScoliaPart;$part.Mois=$m;$part.Montant=Paid $a.Matricule $m;$l.Paid.Add($part);$coverage=New-Object ScoliaPart;$coverage.Mois=$m;$coverage.Montant=Get-ITMPECCoverage $a $m;$l.Covered.Add($coverage)}
  $roster.Learners.Add($l)
 };Add-ScoliaPublishedPayments $roster
 $script:scoliaRosterPending=$false;$script:scoliaRosterTask=[ScoliaSharing]::PublishAsync($script:scoliaRoot,$roster);$script:scoliaRosterPreparedKey=$publicationKey
}

function Show-ScoliaPending {
 Assert-ITMPermission 'Encaisser';if($script:scoliaCommitTask -and -not $script:scoliaCommitTask.IsCompleted){throw 'Un enregistrement est encore en cours. Attendez son résultat avant de vérifier lʼopération.'};$file=Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'
 if(-not [IO.File]::Exists($file)){$script:status.Text='Aucune opération de caisse en attente.';return}
 $pending=[IO.File]::ReadAllText($file)|ConvertFrom-Json
 if($pending.SchoolId -cne $script:scolia.Id -or $pending.Root -cne $script:scoliaRoot){throw 'Cette opération concerne un autre dossier SCOLIA. Reconnectez son emplacement avant de vérifier.'}
 $dialog=New-ScoliaDialog 'Vérifier opération en attente' 620 360;$label=New-Label $dialog ('Vérification de '+$pending.Reference+' dans le journal partagé...') 24 60 540 100;$label.Dock='Fill';$label.Padding=New-Object Windows.Forms.Padding(24)
 $task=[ScoliaSharing]::ReadAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year);$timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $timer.Add_Tick({if(-not $task.IsCompleted){return};$timer.Stop();Stop-ITMActivity
  try{if($task.IsFaulted){throw $task.Exception.GetBaseException()};$saved=@($task.Result.Entries|Where-Object {$_.Id -ceq $pending.Id})
   if($saved.Count -eq 1){$script:scoliaLastReceipt=$saved[0];[IO.File]::Delete($file);$script:scoliaEntries=@($task.Result.Entries);Refresh-ScoliaFinance;$script:status.Text='Opération déjà enregistrée : '+$pending.Reference+'. Ne la ressaisissez pas.'}
   else{if([ITMMessageBox]::Show($dialog,'Cette opération ne figure pas dans le journal vérifié. Lever le blocage pour permettre une nouvelle saisie ?','SCOLIA','YesNo','Question') -eq 'Yes'){[IO.File]::Delete($file);$script:status.Text='Journal vérifié : opération absente, nouvelle saisie autorisée.'}}
   $dialog.Close()
  }catch{$label.Text='Vérification impossible : '+$_.Exception.Message+'. Lʼopération reste en attente.';$button=Add-ScoliaButton $dialog 'Fermer' {$dialog.Close()};$button.Dock='Bottom'}
 });$dialog.Add_FormClosing({param($sender,$args)if(-not $task.IsCompleted){$args.Cancel=$true}});Apply-ITMThemeForm $dialog;Start-ITMActivity 'Vérification du journal...' $label;$timer.Start()
 try{[void]$dialog.ShowDialog($script:main)}finally{Stop-ITMActivity;$timer.Stop();$timer.Dispose();$dialog.Dispose()}
}

function Initialize-ScoliaBlankWorkbook {
 if(-not $script:scolia -or -not $script:data -or @($script:data.learners).Count -or @($script:data.payments).Count){return}
 $expected=[ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months);$different=(($expected -join '|') -cne (@($script:data.months) -join '|'))
 if(@($script:data.promotions).Count -ne @($script:scolia.Classes).Count){$different=$true}
 foreach($c in $script:scolia.Classes){$f=@($script:data.promotions|Where-Object {(Promo $_.Code) -eq (Promo $c.Code)});if($f.Count -ne 1 -or [decimal]$f[0].Mensuel -ne [decimal]$c.Fee -or ($f[0].Annee -replace '\s','') -ne ($script:scolia.Year -replace '\s','')){$different=$true}}
 if($different){Apply-ScoliaWorkbookConfiguration}
}

function Open-ScoliaFolder {Assert-ITMPermission 'Lire';if(-not [IO.Directory]::Exists($script:scoliaRoot)){throw 'Dossier partagé indisponible.'};Start-Process -FilePath 'explorer.exe' -ArgumentList ('"'+$script:scoliaRoot+'"')}
function Request-ScoliaRefresh {if(-not $script:scoliaReadTask){$script:scoliaReadTask=[ScoliaSharing]::ReadAsync($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year)};$script:scoliaLedgerStamp='';Refresh-ScoliaFinance}
