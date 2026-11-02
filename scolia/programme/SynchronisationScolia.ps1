function Get-ScoliaDraftFolder {return (Join-Path $script:scoliaLocalRoot ('brouillons-caisse\'+$script:scolia.Id))}
function Get-ScoliaReconciliation {
 $payments=@($script:scoliaEntries|Where-Object {$_.Kind -eq 'Paiement' -and ([ScoliaSharing]::YearKey($_.Annee)) -eq ([ScoliaSharing]::YearKey($script:scolia.Year))})
 $imported=@($script:scoliaRoster.Imported);$rows=@();$cash=[decimal]0;$received=[decimal]0
 foreach($e in $payments){$cash+=$e.Montant;$done=($e.Id -in $imported);if($done){$received+=$e.Montant};$state=$(if($done){'Reçu par le secrétariat'}else{'À recevoir'})
  if(-not $done -and (Test-ScoliaPermission 'Recevoir')){$local=[ScoliaLedger]::State((Get-ScoliaStateRoot),$script:scolia.Id,$e.Id);if($local){$state=$(if($local.Status -eq 'Importé'){'Importé · publication en attente'}else{$local.Status})}}
  $rows+=[pscustomobject]@{Id=$e.Id;Date=$e.Date;Reference=$e.Reference;Apprenant=($e.Nom+' '+$e.PostNom+' '+$e.Prenom);Promotion=$e.Promotion;Montant=$e.Montant;Operateur=$e.OperatorName;Etat=$state}
 };return [pscustomobject]@{Cash=$cash;Received=$received;Remaining=($cash-$received);Rows=$rows}
}
function Show-ScoliaSync {
 Assert-ITMPermission 'Lire';$d=New-ScoliaDialog 'Synchronisation et rapprochement' 1060 700
 $header=New-Label $d '' 0 0 980 145;$header.Dock='Top';$header.AutoSize=$true;$header.MaximumSize=New-Object Drawing.Size(980,0);$header.Padding=New-Object Windows.Forms.Padding(16)
 $grid=New-Grid $d 0 0 980 420;$grid.Dock='Fill';$bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.AutoSizeMode='GrowAndShrink';$bar.MinimumSize=New-Object Drawing.Size(0,65);$d.Controls.Add($bar);$d.Controls.SetChildIndex($grid,0)
 $refresh={try{$balance=Get-ScoliaReconciliation;$header.Text='État : '+$script:scoliaSync.State+' · Dernière réception : '+$script:scoliaSync.LastSuccess+"`r`n"+$script:scoliaRoot+"`r`nEncaissé : "+(Money $balance.Cash)+' FC · Reçu : '+(Money $balance.Received)+' FC · À recevoir : '+(Money $balance.Remaining)+' FC'+"`r`n"+$script:scoliaSync.LastError;Set-Grid $grid $balance.Rows @(@('Date','Date'),@('Reference','Référence'),@('Apprenant','Apprenant'),@('Promotion','Promotion'),@('Montant','Montant FC'),@('Operateur','Opérateur'),@('Etat','État'))}catch{$header.Text=$_.Exception.Message}}
 [void](Add-ScoliaButton $bar 'Actualiser le réseau' {try{Request-ScoliaRefresh}catch{$header.Text=$_.Exception.Message}})
 [void](Add-ScoliaButton $bar 'Exporter le rapprochement' {try{Assert-ITMPermission 'Exporter';Save-ITMCsv @((Get-ScoliaReconciliation).Rows) 'SCOLIA_Rapprochement.csv'}catch{$header.Text=$_.Exception.Message}})
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});$timer=New-Object Windows.Forms.Timer;$timer.Interval=1200;$timer.Add_Tick($refresh);& $refresh;Apply-ITMThemeForm $d;$timer.Start()
 try{[void]$d.ShowDialog($script:main)}finally{$timer.Stop();$timer.Dispose();$d.Dispose()}
}
function Get-ScoliaDraftRows {
 $folder=Get-ScoliaDraftFolder;if(-not [IO.Directory]::Exists($folder)){return @()}
 $rows=@();foreach($file in [IO.Directory]::GetFiles($folder,'*.xml')){try{$e=[ScoliaLedger]::ReadDraft($file,$script:scolia.Id);if($e.OperatorId -cne $script:scoliaSession.AccountId){continue};$rows+=[pscustomobject]@{Id=$e.Id;Date=$e.Date;Reference=$e.Reference;Apprenant=($e.Nom+' '+$e.PostNom+' '+$e.Prenom);Promotion=$e.Promotion;Annee=$e.Annee;Montant=$e.Montant;Type=$e.Kind;Etat='Brouillon · non comptabilisé';Fichier=$file}}catch{$rows+=[pscustomobject]@{Id=[IO.Path]::GetFileNameWithoutExtension($file);Reference='';Etat=('Fichier à vérifier : '+$_.Exception.Message);Fichier=$file}}};return @($rows|Sort-Object Date,Reference)
}
function Assert-ScoliaDraftSend($entry){
 Assert-ITMPermission 'Encaisser';if($script:scoliaCommitTask){throw 'Une opération attend son résultat.'}
 if([IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))){throw 'Vérifiez lʼopération en attente avant de valider un brouillon.'}
 if($entry.OperatorId -cne $script:scoliaSession.AccountId){throw 'Ce brouillon appartient à un autre compte.'}
 # Les brouillons de caisse appartiennent à l'espace Comptabilité. Les anciennes versions
 # enregistraient parfois le rôle de session (Secrétariat/Mixte) : on le normalise après
 # vérification de l'identité du compte, afin qu'une reprise légitime ne soit pas bloquée.
 if($entry.Compte -notin @('Comptabilité','Secrétariat','Mixte')){throw 'Origine du brouillon de caisse invalide.'}
 $entry.Compte='Comptabilité'
 [void](Read-ScoliaConfiguration);if($script:scoliaSession.Revision -ne $script:scolia.Revision){throw 'Les paramètres ont changé. Reconnectez votre compte avant de valider.'}
 if(([ScoliaSharing]::YearKey($entry.Annee)) -ne ([ScoliaSharing]::YearKey($script:scolia.Year))){throw 'Ce brouillon concerne une autre année. Reconnectez cette année pour le valider.'}
}
function Show-ScoliaDrafts {
 Assert-ITMPermission 'Encaisser';$d=New-ScoliaDialog 'Brouillons de caisse hors connexion' 1080 650;$grid=New-Grid $d 0 0 990 430;$grid.Dock='Fill'
 $message=New-Label $d 'Un brouillon ne produit ni reçu définitif ni recette. Validez-le lorsque le réseau est disponible ; le solde et la référence seront contrôlés.' 0 0 1000 90;$message.Dock='Top';$message.Padding=New-Object Windows.Forms.Padding(16)
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.AutoSizeMode='GrowAndShrink';$bar.MinimumSize=New-Object Drawing.Size(0,65);$d.Controls.Add($bar);$d.Controls.SetChildIndex($grid,0)
 $context=[pscustomobject]@{Task=$null;File='';Entry=$null};$reload={Set-Grid $grid @(Get-ScoliaDraftRows) @(@('Date','Date'),@('Reference','Référence'),@('Apprenant','Apprenant'),@('Promotion','Promotion'),@('Annee','Année'),@('Montant','Montant FC'),@('Type','Type'),@('Etat','État'),@('Fichier','Fichier'));$grid.Columns['Fichier'].Visible=$false}
 $send=Add-ScoliaButton $bar 'Valider le brouillon sélectionné' {try{
  if($context.Task){throw 'Validation en cours.'};if($grid.SelectedRows.Count -ne 1){throw 'Sélectionnez un brouillon.'};$file=[string]$grid.SelectedRows[0].Cells['Fichier'].Value;$entry=[ScoliaLedger]::ReadDraft($file,$script:scolia.Id);Assert-ScoliaDraftSend $entry
  $fee=[decimal]0;if($entry.Kind -eq 'Paiement'){$classes=@($script:scolia.Classes|Where-Object {$_.Code -eq $entry.Promotion});if($classes.Count -ne 1){throw 'La promotion du brouillon nʼexiste plus.'};$fee=[decimal]$classes[0].Fee};$entry.ConfigRevision=$script:scoliaSession.Revision
  Write-ScoliaJson (Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json') ([pscustomobject]@{Id=$entry.Id;SchoolId=$script:scolia.Id;Root=$script:scoliaRoot;Reference=$entry.Reference;Montant=$entry.Montant;Date=$entry.Date})
  $context.File=$file;$context.Entry=$entry;$context.Task=[ScoliaLedger]::CommitAsync($script:scoliaRoot,$script:scolia.Id,$entry,[int]$script:scolia.StartMonth,[int]$script:scolia.Months,$fee);$script:scoliaCommitTask=$context.Task;$send.Enabled=$false;$remove.Enabled=$false;$grid.Enabled=$false;Start-ITMActivity 'Validation du brouillon…' $send;$message.Text='Validation en cours. La référence technique reste identique lors dʼune reprise.'
 }catch{$message.Text=$_.Exception.Message}}
 $remove=Add-ScoliaButton $bar 'Annuler le brouillon' {try{
  if($context.Task -or $grid.SelectedRows.Count -ne 1){throw 'Sélectionnez un brouillon disponible.'};$file=[string]$grid.SelectedRows[0].Cells['Fichier'].Value;$entry=[ScoliaLedger]::ReadDraft($file,$script:scolia.Id);if($entry.OperatorId -cne $script:scoliaSession.AccountId){throw 'Autre compte.'};$pending=Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json';if([IO.File]::Exists($pending) -and ([IO.File]::ReadAllText($pending)|ConvertFrom-Json).Id -ceq $entry.Id){throw 'Vérifiez cette opération avant de lʼannuler.'}
  if([ITMMessageBox]::Show($d,'Annuler définitivement ce brouillon non comptabilisé ?','SCOLIA','YesNo','Question') -eq 'Yes'){[IO.File]::Delete($file);Write-ITMUserAudit 'Brouillon annulé' $entry.Id;& $reload}
 }catch{$message.Text=$_.Exception.Message}}
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});$timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $timer.Add_Tick({if(-not $context.Task -or -not $context.Task.IsCompleted){return};$task=$context.Task;$context.Task=$null;$script:scoliaCommitTask=$null;Stop-ITMActivity
  try{if($task.IsFaulted){throw $task.Exception.GetBaseException()};$saved=$task.Result;$script:scoliaLastReceipt=$saved;[IO.File]::Delete((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'));[IO.File]::Delete($context.File);Write-ITMUserAudit 'Brouillon validé en caisse' $saved.Id;Request-ScoliaRefresh;$message.Text='Opération comptabilisée : '+$saved.Reference}
  catch{$cause=$_.Exception.GetBaseException();if($cause -is [InvalidOperationException] -or $cause -is [ArgumentException] -or $cause -is [IO.InvalidDataException]){[IO.File]::Delete((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))};$message.Text=$cause.Message+' Le brouillon est conservé. Après une coupure, vérifiez lʼopération en attente avant de réessayer.'}
  finally{$send.Enabled=$true;$remove.Enabled=$true;$grid.Enabled=$true;& $reload}
 });$d.Add_FormClosing({param($sender,$e)if($context.Task){$e.Cancel=$true}});& $reload;Apply-ITMThemeForm $d;$timer.Start()
 try{[void]$d.ShowDialog($script:main)}finally{$timer.Stop();$timer.Dispose();$d.Dispose()}
}
