# Shared receipts, immutable revisions, configurable payment purposes and personal accounts.
function Get-ScoliaAppliedPath($entry){return (Join-Path (Get-ScoliaStateRoot) ('pieces-appliquees/'+$script:scolia.Id+'-'+$entry.Id+'.xml'))}
function Save-ScoliaAppliedReceipt($entry){
 $path=Get-ScoliaAppliedPath $entry;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));$tmp=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
 try{$stream=[IO.File]::Create($tmp);try{(New-Object Xml.Serialization.XmlSerializer([ScoliaEntry])).Serialize($stream,$entry);$stream.Flush($true)}finally{$stream.Dispose()};if([IO.File]::Exists($path)){[IO.File]::Replace($tmp,$path,$path+'.bak')}else{[IO.File]::Move($tmp,$path)}}finally{if([IO.File]::Exists($tmp)){[IO.File]::Delete($tmp)}}
 $script:scoliaEntries=@($script:scoliaEntries|Where-Object {$_.Id -cne $entry.Id})+@($entry.Copy());$script:scoliaRosterPending=$true
}
function Read-ScoliaAppliedReceipt($entry){
 $path=Get-ScoliaAppliedPath $entry;if(-not [IO.File]::Exists($path)){return $null};$settings=New-Object Xml.XmlReaderSettings;$settings.DtdProcessing='Prohibit';$settings.XmlResolver=$null;$r=[Xml.XmlReader]::Create($path,$settings)
 try{$e=(New-Object Xml.Serialization.XmlSerializer([ScoliaEntry])).Deserialize($r);if($e.Id -cne $entry.Id -or $e.SchoolId -cne $script:scolia.Id){throw 'Pièce locale dʼun autre établissement.'};return $e}finally{$r.Dispose()}
}
function Test-ScoliaApplied($entry,$state){return ($state -and $state.Status -eq 'Importé' -and [string]$state.Revision -ceq [string]$entry.Revision)}
function Assert-ScoliaFinanceWorkbook {
 try{$macro=Api-Name 'ITMAppScoliaFinanceVersion';$version=[string](Invoke-ITMExcelRun $script:excel $macro @());if($version -cne 'SCOLIA-FINANCE-2'){throw 'Version ancienne'}}catch{throw 'La passerelle Excel doit être mise à jour. Fermez lʼapplication, installez les corrections fournies puis reconnectez la base. Vos paiements restent dans le journal partagé.'}
}
function Import-ScoliaRevision($entry){
 Assert-ScoliaFinanceWorkbook
 $map=Get-ScoliaIncomingReceiptMap;$number='';foreach($n in $map.Keys){if($map[$n] -ceq $entry.Id){$number=$n;break}}
 if(-not $number -and $entry.Compte -eq 'Secrétariat' -and $entry.CommonReceipt -ne '1'){
  $dataset=Get-ScoliaSourceDataSet
  foreach($p in @($script:data.payments|Group-Object {Key ([string]$_.Numero)})){
   $rows=@($p.Group);$receipt=[string]$rows[0].Numero;$owners=@($rows|ForEach-Object {Key ([string]$_.Matricule)}|Where-Object {$_}|Select-Object -Unique)
   if($owners.Count -ne 1){if($owners.Count -gt 1){throw ('Référence de reçu ambiguë dans le secrétariat : '+$receipt+'. Corrigez le doublon avant de synchroniser une correction.')};continue}
   if([ScoliaLedger]::SecretariatId($script:scolia.Id,$dataset,$entry.Annee,$receipt,[string]$rows[0].Matricule) -ceq $entry.Id){$number=$receipt;break}
  }
 }
 if(-not $number){
  # A cancellation arriving before the initial import must never create a payment.
  if($entry.Cancelled -eq '1'){return [pscustomobject]@{ok=$true;matricule=$entry.Matricule;already=$false}}
  return $null
 }
 $before=Read-ScoliaAppliedReceipt $entry
 if(-not $before){$before=[ScoliaLedger]::ReadVerifiedFile((Join-Path $script:scoliaRoot ('journal/'+$entry.Id+'.xml')),$script:scolia.Id)}
 $args=@($entry.Id,$number,$entry.Matricule,(@($entry.Parts|ForEach-Object {$_.Mois}) -join '|'),(@($entry.Parts|ForEach-Object {$_.Montant.ToString('0',[Globalization.CultureInfo]::InvariantCulture)}) -join '|'),$entry.Date,[int]$entry.Revision,$entry.CorrectionReason,$before.Date,(@($before.Parts|ForEach-Object {$_.Mois}) -join '|'),(@($before.Parts|ForEach-Object {$_.Montant.ToString('0',[Globalization.CultureInfo]::InvariantCulture)}) -join '|'))
 if($script:store){Start-ITMStoreTransaction}
 try{$result=Invoke-ITMJsonDirect 'ITMAppScoliaSynchroniser' $args;if($script:store){Complete-ITMStoreTransaction};return $result}catch{if($script:store){Undo-ITMStoreTransaction};throw}
}
function Show-ScoliaFees {
 Assert-ITMPermission 'GererFrais';$catalog=[ScoliaFinance]::Fees($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year)
 $screen=New-ScoliaFeatureDialog 'Configuration · Motifs de paiement' 'La scolarité mensuelle utilise les tarifs des promotions. Ajoutez ici les autres motifs : inscription, examens, stage… Montant = total attendu par apprenant pour cette année ; promotion vide = toutes.' 980 650
 $d=$screen.Form;$g=$screen.Grid;$g.ReadOnly=$false;$g.AllowUserToAddRows=$true;$g.AllowUserToDeleteRows=$true
 foreach($pair in @(@('Code','Code unique'),@('Name','Motif de paiement'),@('Amount','Montant annuel FC'),@('Promotion','Promotion (facultative)'))){[void]$g.Columns.Add($pair[0],$pair[1])};$check=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$check.Name='Active';$check.HeaderText='Actif';[void]$g.Columns.Add($check)
 $g.Add_DefaultValuesNeeded({param($sender,$eventArgs)$eventArgs.Row.Cells['Active'].Value=$true})
 foreach($c in $catalog.Items){[void]$g.Rows.Add($c.Code,$c.Name,$c.Amount,$c.Promotion,$c.Active)}
 $bar=$screen.Actions
 [void](Add-ScoliaButton $bar 'Enregistrer les motifs' {try{
  Assert-ITMPermission 'GererFrais';[void]$g.EndEdit();$next=New-Object ScoliaFees;$next.SchoolId=$script:scolia.Id;$next.Year=$script:scolia.Year
  foreach($row in $g.Rows){if($row.IsNewRow){continue};$c=New-Object ScoliaFee;$c.Code=([string]$row.Cells['Code'].Value).Trim().ToUpperInvariant();$c.Name=([string]$row.Cells['Name'].Value).Trim();$c.Amount=[decimal]$row.Cells['Amount'].Value;$c.Promotion=([string]$row.Cells['Promotion'].Value).Trim().ToUpperInvariant();$c.Active=[bool]$row.Cells['Active'].Value;$next.Items.Add($c)}
  [ScoliaFinance]::SaveFees($script:scoliaRoot,$script:scolia.Id,$next,$catalog.Revision);Write-ITMUserAudit 'Motifs de paiement configurés';$d.Close()
 }catch{[void][ITMMessageBox]::Show($d,$_.Exception.Message,'Motifs de paiement') }})
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Show-ScoliaCorrection {
 Assert-ITMPermission 'CorrigerPaiements'
 if(-not $script:scoliaFinanceGrid -or $script:scoliaFinanceGrid.SelectedRows.Count -ne 1){throw 'Sélectionnez un reçu dans le journal Comptabilité.'}
 $id=[string]$script:scoliaFinanceGrid.SelectedRows[0].Cells['Identifiant opération'].Value
 $e=@([ScoliaLedger]::Read($script:scoliaRoot,$script:scolia.Id)|Where-Object {$_.Id -ceq $id})[0];if(-not $e){throw 'Reçu introuvable.'}
 $d=New-ScoliaDialog ('Corriger / annuler · '+$e.Reference) 750 550;$t=New-ScoliaTable $d
 $amount=New-Object Windows.Forms.NumericUpDown;$amount.Maximum=100000000000;$amount.Value=$e.Montant;$amount.ThousandsSeparator=$true;[void](Add-ScoliaField $t 'Nouveau montant (0 = annulation)' $amount)
 $date=New-Object Windows.Forms.DateTimePicker;$date.Format='Short';$date.MaxDate=[datetime]::Today;$date.Value=[datetime]::ParseExact($e.Date,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture);[void](Add-ScoliaField $t 'Date du paiement' $date)
 $reason=Add-ScoliaText $t 'Motif obligatoire de la correction';$reason.Multiline=$true;$reason.Height=85
 $info=Add-ScoliaText $t 'Référence commune conservée' $e.Reference;$info.ReadOnly=$true
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$d.Controls.Add($bar)
 [void](Add-ScoliaButton $bar 'Valider la correction' {try{
  Assert-ITMPermission 'CorrigerPaiements';if([ITMMessageBox]::Show($d,('Confirmer le montant '+$amount.Value+' FC et synchroniser cette correction ? Le reçu initial restera dans lʼhistorique.'),'Correction du reçu','YesNo','Question') -ne 'Yes'){return}
  $saved=[ScoliaFinance]::Correct($script:scoliaRoot,$script:scolia.Id,$e.Id,$e.Checksum,[decimal]$amount.Value,$date.Value.ToString('yyyy-MM-dd'),$reason.Text,$script:scoliaSession.AccountId,$script:scoliaSession.Name,$script:scoliaSession.Revision)
  Write-ITMUserAudit 'Correction de paiement synchronisée' ($saved.Id+' / '+$saved.Revision);Request-ScoliaRefresh;$d.Close()
 }catch{[void][ITMMessageBox]::Show($d,$_.Exception.Message,'Correction') }})
 [void](Add-ScoliaButton $bar 'Historique du reçu' {Show-ScoliaRevisionHistory $e.Id});[void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Show-ScoliaRevisionHistory([string]$id=''){
 Assert-ITMPermission 'Lire';$rows=@([ScoliaFinance]::History($script:scoliaRoot,$script:scolia.Id)|Where-Object {-not $id -or $_.Id -ceq $id}|ForEach-Object {[pscustomobject]@{Reference=$_.Replacement.Reference;Version=$_.Number;Date=$_.Created;Auteur=$_.AuthorName;Montant=$_.Replacement.Montant;Motif=$_.Reason}})
 Show-ScoliaFinanceTable 'Historique des corrections' $rows @(@('Reference','Reçu'),@('Version','Version'),@('Date','Date UTC'),@('Auteur','Auteur'),@('Montant','Montant FC'),@('Motif','Motif'))
}
function Show-ScoliaFinanceTable([string]$title,$rows,$columns){
 $d=New-ScoliaDialog $title 1100 670;$g=New-Grid $d 0 0 1020 500;$g.Dock='Fill';Set-Grid $g @($rows) $columns
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$d.Controls.Add($bar)
 [void](Add-ScoliaButton $bar 'Exporter CSV' {try{Assert-ITMPermission 'Exporter';Save-ITMCsv @($rows) 'SCOLIA_Controle.csv'}catch{App-Error $_}});[void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});$d.Controls.SetChildIndex($g,0);Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Show-ScoliaReconciliation {
 Assert-ITMPermission 'Lire';$snapshot=New-Object ScoliaSnapshot;$snapshot.Entries=[ScoliaLedger]::Read($script:scoliaRoot,$script:scolia.Id);$snapshot.Roster=[ScoliaSharing]::ReadRoster($script:scoliaRoot,$script:scolia.Id,$script:scolia.Year)
 $rows=@([ScoliaFinance]::Reconcile($snapshot));$different=@($rows|Where-Object {$_.Status -notin @('Rapproché','Annulé synchronisé')}).Count
 Show-ScoliaFinanceTable ('Rapprochement · '+$different+' reçu(s) à vérifier · publication '+$snapshot.Roster.PublishedUtc) $rows @(@('Reference','Référence commune'),@('Matricule','Matricule'),@('Promotion','Promotion'),@('Category','Motif'),@('Shared','Journal partagé FC'),@('Secretariat','Secrétariat FC'),@('Difference','Écart FC'),@('Status','État'),@('Detail','Observation'))
}
function Save-ScoliaNamedAccount($candidate,[int]$revision){
 Assert-ITMPermission 'GererComptes';Test-ScoliaProfile $candidate
 $own=@($candidate.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $script:scoliaSession.AccountId})
 if($own.Count -ne 1 -or -not (Test-ScoliaAccountActive $own[0])){throw 'Votre propre compte doit rester actif pendant cette session.'}
 $gate=[IO.File]::Open($script:scoliaProfileFile+'.lock','OpenOrCreate','ReadWrite','None');$journalGate=$null
 try{$journalGate=[IO.File]::Open((Join-Path $script:scoliaRoot 'journal.lock'),'OpenOrCreate','ReadWrite','None');$current=[IO.File]::ReadAllText($script:scoliaProfileFile)|ConvertFrom-Json;if($current.Revision -ne $revision){throw 'Comptes modifiés sur un autre poste. Rouvrez cette fenêtre.'};$candidate.Revision=$revision+1;Write-ScoliaJson $script:scoliaProfileFile $candidate}finally{if($journalGate){$journalGate.Dispose()};$gate.Dispose()}
 $script:scolia=$candidate;$script:scoliaSession.Revision=$candidate.Revision;$script:scoliaSession.Role=$own[0].Role;$script:scoliaSession.Name=Get-ScoliaAccountName $own[0];$script:accountSession.Name=$script:scoliaSession.Name;$script:accountSession.Role=$script:scoliaSession.Role
 Write-ITMUserAudit 'Comptes nominatifs mis à jour';Set-ScoliaWorkspace
}
function Show-ScoliaAccounts {
 Assert-ITMPermission 'GererComptes';$editing=$script:scolia|ConvertTo-Json -Depth 12|ConvertFrom-Json
 $d=New-ScoliaDialog 'Configuration · Comptes nominatifs' 860 720
 $d.MinimumSize=New-Object Drawing.Size(520,520)

 # Le corps et la barre d'actions occupent désormais deux zones distinctes. Cela évite
 # que le dernier champ (confirmation du mot de passe) passe sous les boutons lorsque
 # Windows applique un DPI élevé ou une taille de texte plus grande.
 $root=New-Object Windows.Forms.TableLayoutPanel;$root.Dock='Fill';$root.Margin=New-Object Windows.Forms.Padding(0);$root.Padding=New-Object Windows.Forms.Padding(0);$root.ColumnCount=1;$root.RowCount=2
 [void]$root.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))
 [void]$root.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)))
 [void]$root.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
 $d.Controls.Add($root)
 $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.AutoScroll=$true;$body.Margin=New-Object Windows.Forms.Padding(0);$root.Controls.Add($body,0,0)
 $t=New-ScoliaTable $body;$t.Padding=New-Object Windows.Forms.Padding(22,18,22,16)

 $selector=New-Object Windows.Forms.ComboBox;$selector.DropDownStyle='DropDownList';[void]$selector.Items.Add('Créer un nouveau compte');foreach($a in $editing.Accounts){[void]$selector.Items.Add((Get-ScoliaAccountLabel $a)+' · '+$(if(Test-ScoliaAccountActive $a){'actif'}else{'désactivé'}))};[void](Add-ScoliaField $t 'Compte individuel' $selector)
 $name=Add-ScoliaText $t 'Nom et prénom de la personne';$role=New-Object Windows.Forms.ComboBox;$role.DropDownStyle='DropDownList';$role.Items.AddRange([object[]]@('Secrétariat','Comptabilité','Mixte'));[void](Add-ScoliaField $t 'Espace métier' $role)
 $active=New-Object ITMReadableCheckBox;$active.Text='Connexion autorisée';$active.AutoSize=$true;[void](Add-ScoliaField $t 'Compte actif' $active)
 $admin=New-Object ITMReadableCheckBox;$admin.Text='Gérer établissement, comptes et droits';$admin.AutoSize=$true;[void](Add-ScoliaField $t 'Administrateur' $admin)
 $correct=New-Object ITMReadableCheckBox;$correct.Text='Corriger et annuler les reçus';$correct.AutoSize=$true;[void](Add-ScoliaField $t 'Corrections de paiement' $correct)
 $fees=New-Object ITMReadableCheckBox;$fees.Text='Configurer les motifs et tarifs';$fees.AutoSize=$true;[void](Add-ScoliaField $t 'Motifs de paiement' $fees)

 # Les deux champs de sécurité ont exactement la même géométrie. Ils conservent une
 # hauteur de saisie confortable et restent alignés sur toute la largeur disponible.
 $pass=New-Object ScoliaPasswordBox;$pass.Height=46;$pass.MinimumSize=New-Object Drawing.Size(180,46);[void](Add-ScoliaField $t 'Mot de passe (8 caractères min., vide = conserver)' $pass);$pass.Margin=New-Object Windows.Forms.Padding(0,6,0,10)
 $confirm=New-Object ScoliaPasswordBox;$confirm.Height=46;$confirm.MinimumSize=New-Object Drawing.Size(180,46);[void](Add-ScoliaField $t 'Confirmer le mot de passe' $confirm);$confirm.Margin=New-Object Windows.Forms.Padding(0,6,0,14)
 $selector.Add_SelectedIndexChanged({$pass.Clear();$confirm.Clear();if($selector.SelectedIndex -eq 0){$name.Text='';$role.SelectedIndex=0;$active.Checked=$true;$admin.Checked=$false;$correct.Checked=$false;$fees.Checked=$false}else{$a=$editing.Accounts[$selector.SelectedIndex-1];$name.Text=Get-ScoliaAccountName $a;$role.SelectedItem=$a.Role;$active.Checked=Test-ScoliaAccountActive $a;$admin.Checked=Test-ScoliaAccountAdmin $a;$correct.Checked=($a.PSObject.Properties['CanCorrect'] -and $a.CanCorrect);$fees.Checked=($a.PSObject.Properties['CanManageFees'] -and $a.CanManageFees)}});$selector.SelectedIndex=0

 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Fill';$bar.AutoSize=$true;$bar.AutoSizeMode='GrowAndShrink';$bar.WrapContents=$true;$bar.Padding=New-Object Windows.Forms.Padding(16,8,16,10);$bar.Margin=New-Object Windows.Forms.Padding(0);$root.Controls.Add($bar,0,1)
 [void](Add-ScoliaButton $bar 'Enregistrer le compte' {try{
  $candidate=$editing|ConvertTo-Json -Depth 12|ConvertFrom-Json
  if($selector.SelectedIndex -eq 0){$a=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');Name='';Role='';Locked=$true;Secret=$null};$candidate.Accounts=@($candidate.Accounts)+@($a)}else{$a=$candidate.Accounts[$selector.SelectedIndex-1]}
  if(-not $a.Secret -and -not $pass.Text){throw 'Définissez un mot de passe personnel.'}
  if($pass.Text){if($pass.Text.Length -lt 8 -or $pass.Text -cne $confirm.Text){throw 'Mot de passe trop court ou confirmations différentes.'};$a.Secret=New-ITMSecret $pass.Text;$a.Locked=$true}
  $a|Add-Member -NotePropertyName Name -NotePropertyValue $name.Text.Trim() -Force;$a.Role=$role.Text
  foreach($pair in @(@('Active',$active.Checked),@('IsAdmin',$admin.Checked),@('CanCorrect',$correct.Checked),@('CanManageFees',$fees.Checked))){$a|Add-Member -NotePropertyName $pair[0] -NotePropertyValue ([bool]$pair[1]) -Force}
  Save-ScoliaNamedAccount $candidate $editing.Revision;$d.Close()
 }catch{[void][ITMMessageBox]::Show($d,$_.Exception.Message,'Compte nominatif')}})
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()})

 # Réagencement fluide du formulaire : en fenêtre étroite, le libellé passe au-dessus
 # du champ au lieu de réduire ou décaler le champ de confirmation.
 if(Test-Path function:Enable-ITMFluidTable){Enable-ITMFluidTable $t}
 Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$pass.Clear();$confirm.Clear();$d.Dispose()}
}
