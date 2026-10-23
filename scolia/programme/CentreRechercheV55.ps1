# Recherche et centre des actions : lecture du cache courant, aucune écriture métier.
function ConvertTo-ScoliaSearchKey([string]$Text){
 if('ScoliaLedger' -as [type]){return [ScoliaLedger]::Key($Text)}
 $value=$Text.Normalize([Text.NormalizationForm]::FormD);$builder=New-Object Text.StringBuilder
 foreach($character in $value.ToCharArray()){if([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark){[void]$builder.Append($character)}}
 return $builder.ToString().Normalize([Text.NormalizationForm]::FormC).ToUpperInvariant()
}
function Test-ScoliaSearchTokens([string]$Key,[string[]]$Tokens){
 foreach($token in $Tokens){if(-not $Key.Contains($token)){return $false}};return $true
}
function Test-ScoliaSearchText([string]$Text,[string]$Query){
 return (Test-ScoliaSearchTokens (ConvertTo-ScoliaSearchKey $Text) @((ConvertTo-ScoliaSearchKey $Query) -split '\s+'|Where-Object {$_}))
}
function Test-ScoliaFeaturePageAllowed([string]$Page){
 if(-not (Test-ITMPermission 'Lire')){return $false}
 if($script:scoliaSession){return $Page -in @($script:tabsNames)}
 return $true
}
function Get-ScoliaGlobalSearchRows([string]$Query='',[ValidateSet('Tous','Apprenants','Paiements')][string]$Type='Tous'){
 if(-not (Test-ITMPermission 'Lire')){return @()}
 $rows=New-Object 'System.Collections.Generic.List[object]';$index=0;$tokens=@((ConvertTo-ScoliaSearchKey $Query) -split '\s+'|Where-Object {$_})
 if($Type -in @('Tous','Apprenants') -and (Test-ScoliaFeaturePageAllowed 'Apprenants')){
  foreach($learner in @($script:data.learners)){
   if(-not $learner){continue};$index++;$identity=([string]$learner.Nom+' '+[string]$learner.PostNom+' '+[string]$learner.Prenom).Trim()
   $text=$identity+' '+$learner.Matricule+' '+$learner.Promotion+' '+$learner.Annee+' '+$learner.Statut
   $searchKey=ConvertTo-ScoliaSearchKey $text
   if(Test-ScoliaSearchTokens $searchKey $tokens){$rows.Add([pscustomobject]@{Key=('learner:'+$index);Type='Apprenant';Reference=[string]$learner.Matricule;Identity=$identity;Promotion=[string]$learner.Promotion;Year=[string]$learner.Annee;Date='';Amount='';Description=[string]$learner.Statut;Action='Learner';Matricule=[string]$learner.Matricule;SourceId='';Receipt='';Month='';SearchKey=$searchKey})}
  }
 }
 if($Type -in @('Tous','Paiements') -and (Test-ScoliaFeaturePageAllowed 'Paiements')){
  foreach($payment in @($script:data.payments)){
   if(-not $payment){continue};$index++;$text=([string]$payment.NomComplet+' '+$payment.Matricule+' '+$payment.Numero+' '+$payment.Mois+' '+$payment.Date)
   $searchKey=ConvertTo-ScoliaSearchKey $text
   if(Test-ScoliaSearchTokens $searchKey $tokens){$rows.Add([pscustomobject]@{Key=('payment:'+$index);Type='Affectation de reçu';Reference=[string]$payment.Numero;Identity=[string]$payment.NomComplet;Promotion='';Year='';Date=[string]$payment.Date;Amount=[string]$payment.Verse;Description=([string]$payment.Mois+' · '+[string]$payment.Statut);Action='Payment';Matricule=[string]$payment.Matricule;SourceId=[string]$payment.ID;Receipt=[string]$payment.Numero;Month=[string]$payment.Mois;SearchKey=$searchKey})}
  }
 }
 if($Type -in @('Tous','Paiements') -and (Test-ScoliaFeaturePageAllowed 'Comptabilité') -and (Test-ITMPermission 'Comptabilite')){
  foreach($entry in @($script:scoliaEntries)){
   if(-not $entry -or $entry.Kind -ne 'Paiement' -or $entry.SchoolId -cne $script:scolia.Id -or (([string]$entry.Annee -replace '\s','') -cne ([string]$script:scolia.Year -replace '\s',''))){continue}
   $index++;$identity=([string]$entry.Nom+' '+[string]$entry.PostNom+' '+[string]$entry.Prenom).Trim();$months=(@($entry.Parts|ForEach-Object {[string]$_.Mois}) -join ', ')
   $details=New-Object 'System.Collections.Generic.List[string]'
   if([string]$entry.Cancelled -ceq '1'){$details.Add('Annulé')}
   elseif([string]$entry.Revision -match '^[1-9][0-9]*$'){$details.Add('Corrigé · révision '+$entry.Revision)}
   if($months){$details.Add($months)}
   if($entry.Category){$details.Add([string]$entry.Category)}
   $description=$details.ToArray() -join ' · '
   $text=$identity+' '+$entry.Matricule+' '+$entry.Reference+' '+$entry.Id+' '+$entry.Promotion+' '+$entry.Annee+' '+$entry.Date+' '+$description
   $searchKey=ConvertTo-ScoliaSearchKey $text
   if(Test-ScoliaSearchTokens $searchKey $tokens){$rows.Add([pscustomobject]@{Key=('cash:'+$index);Type='Paiement de caisse';Reference=[string]$entry.Reference;Identity=$identity;Promotion=[string]$entry.Promotion;Year=[string]$entry.Annee;Date=[string]$entry.Date;Amount=[string]$entry.Montant;Description=$description;Action='Cash';Matricule=[string]$entry.Matricule;SourceId=[string]$entry.Id;Receipt=[string]$entry.Reference;Month='';SearchKey=$searchKey})}
  }
 }
 return $rows.ToArray()
}
function Get-ScoliaSearchIndexStamp {
 $dataId=$(if($script:data){[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($script:data)}else{0})
 $learnerId=$(if($script:data.learners){[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($script:data.learners)}else{0})
 $paymentId=$(if($script:data.payments){[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($script:data.payments)}else{0})
 $permissions=@('Lire','Comptabilite'|ForEach-Object {$_+':'+[string](Test-ITMPermission $_)}) -join ','
 $entryId=$(if($null -ne $script:scoliaEntries){[Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($script:scoliaEntries)}else{0})
 $sessionId=([string]$script:scoliaSession.AccountId+'|'+[string]$script:scoliaSession.Role+'|'+[string]([bool]$script:scoliaSession))
 $entries=@($script:scoliaEntries);$last=$(if($entries.Count){[string]$entries[-1].Id}else{''})
 return (@($dataId,$learnerId,$paymentId,$entryId,$sessionId,[string]$script:dataRevision,[string]$script:scolia.Id,[string]$script:scolia.Year,[string]$script:scoliaRoot,[string]$script:scoliaLedgerStamp,[string]$entries.Count,$last,$permissions,(@($script:tabsNames) -join ',')) -join '|')
}
function Get-ScoliaCachedSearchRows([switch]$Force){
 $stamp=Get-ScoliaSearchIndexStamp
 if($Force -or -not $script:scoliaSearchIndex -or $script:scoliaSearchIndex.Stamp -cne $stamp){
  $activity=[bool]($script:main -and (Get-Command Start-ITMActivity -ErrorAction SilentlyContinue))
  if($activity){Start-ITMActivity 'Préparation de la recherche...' $script:main}
  try{$script:scoliaSearchIndex=[pscustomobject]@{Stamp=$stamp;Rows=@(Get-ScoliaGlobalSearchRows)}}finally{if($activity){Stop-ITMActivity}}
 }
 return $script:scoliaSearchIndex.Rows
}
function Start-ScoliaSearchFeatureTimer($State){
 if($State.Timer -and -not $State.Form.IsDisposed){
  $State.OpenButton.Enabled=$false;$State.Grid.Enabled=$false;$State.Message.Tag=$null;$State.Message.Text='Recherche en cours...'
  $State.Timer.Stop();$State.Timer.Start()
 }
}
function Select-ScoliaFeatureGridRow($Grid,[string]$Column,[string]$Value,[string]$Matricule='',[string]$Month=''){
 if(-not $Grid -or -not $Grid.Columns.Contains($Column)){return}
 $Grid.ClearSelection()
 foreach($row in $Grid.Rows){
  if([string]$row.Cells[$Column].Value -cne $Value){continue}
  if($Matricule -and [string]$row.Cells['Matricule'].Value -cne $Matricule){continue}
  if($Month -and [string]$row.Cells['Mois'].Value -cne $Month){continue}
  $row.Selected=$true;$cell=@($row.Cells|Where-Object {$_.Visible})|Select-Object -First 1
  if($cell){$Grid.CurrentCell=$cell};if($row.Index -ge 0){$Grid.FirstDisplayedScrollingRowIndex=$row.Index};[void]$Grid.Focus();break
 }
}
function Open-ScoliaSearchResult($Row){
 Assert-ITMPermission 'Lire';if($script:busy){throw 'Attendez la fin de l opération en cours.'};if(-not $Row){throw 'Sélectionnez un résultat.'}
 switch([string]$Row.Action){
  'Learner'{
   if(-not (Test-ScoliaFeaturePageAllowed 'Apprenants')){throw 'Votre espace ne permet pas de consulter cette fiche.'}
   $matches=@($script:data.learners|Where-Object {[string]$_.Matricule -ceq [string]$Row.Matricule})
   if($matches.Count -ne 1){throw 'Dossier absent ou matricule en double. Actualisez les données ou consultez Diagnostic.'}
   Show-Fiche ([string]$matches[0].Matricule)
  }
  'Payment'{
   if(-not (Test-ScoliaFeaturePageAllowed 'Paiements')){throw 'Votre espace ne permet pas de consulter cet historique.'}
   $matches=@($script:data.payments|Where-Object {([string]$_.Matricule -ceq [string]$Row.Matricule) -and ([string]$_.Numero -ceq [string]$Row.Receipt) -and ([string]$_.Mois -ceq [string]$Row.Month) -and ((-not $Row.SourceId) -or ([string]$_.ID -ceq [string]$Row.SourceId))})
   if($matches.Count -ne 1){throw 'Affectation absente ou ambiguë. Actualisez les données avant de la consulter.'}
   Reset-PaymentSelection $false;$script:payMat=[string]$Row.Matricule
   if($script:payIdentity){$script:payIdentity.Text='Historique de '+$Row.Matricule+' · reçu '+$Row.Receipt}
   if($script:payFilterMonth){$script:payFilterMonth.SelectedItem='Tous les mois'}
   Refresh-Payments;Show-Page 'Paiements';Select-ScoliaFeatureGridRow $script:paymentGrid 'N° enregistrement' ([string]$Row.Receipt) ([string]$Row.Matricule) ([string]$Row.Month)
   if($script:pages['Paiements'] -and $script:paymentGrid){$script:pages['Paiements'].ScrollControlIntoView($script:paymentGrid)}
  }
  'Cash'{
   Assert-ITMPermission 'Comptabilite';if(-not (Test-ScoliaFeaturePageAllowed 'Comptabilité')){throw 'Votre espace ne permet pas de consulter le journal de caisse.'}
   $matches=@($script:scoliaEntries|Where-Object {$_.Id -ceq $Row.SourceId -and $_.SchoolId -ceq $script:scolia.Id -and $_.Kind -eq 'Paiement' -and (([string]$_.Annee -replace '\s','') -ceq ([string]$script:scolia.Year -replace '\s',''))})
   if($matches.Count -ne 1){throw 'Paiement absent ou ambigu. Actualisez le journal avant de le consulter.'}
   $script:scoliaFinanceQuery=[string]$matches[0].Reference
   if($script:scoliaFinanceSearch){$script:scoliaFinanceSearch.Text=$script:scoliaFinanceQuery}
   Refresh-ScoliaFinance;Show-Page 'Comptabilité';Select-ScoliaFeatureGridRow $script:scoliaFinanceGrid 'Identifiant opération' ([string]$matches[0].Id)
  }
  default{throw 'Ce résultat ne peut pas être ouvert.'}
 }
}
function Get-ScoliaActionRows {
 if(-not (Test-ITMPermission 'Lire')){return @()}
 $rows=New-Object 'System.Collections.Generic.List[object]';$index=0
 if(Test-ITMPermission 'Diagnostic'){
  foreach($issue in @($script:data.issues)){
   if(-not $issue){continue};$index++
   $rows.Add([pscustomobject]@{Key=('diagnostic:'+$index);Type='Diagnostic';Reference=[string]$issue.Matricule;Identity=[string]$issue.Feuille;Date='';State='À vérifier';Description=[string]$issue.Observation;Action='Diagnostic'})
  }
 }
 if((Test-ITMPermission 'Recevoir') -and (Get-Command Get-ScoliaInboxRows -ErrorAction SilentlyContinue)){
  try{foreach($entry in @(Get-ScoliaInboxRows)){
   $index++;$rows.Add([pscustomobject]@{Key=('reception:'+$index);Type='Transfert';Reference=[string]$entry.Reference;Identity=[string]$entry.Nom;Date=[string]$entry.Date;State=[string]$entry.Etat;Description=[string]$entry.Observation;Action='Reception'})
  }}catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Réception';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
 }
 if(Test-ITMPermission 'Encaisser'){
  if(Get-Command Get-ScoliaDraftRows -ErrorAction SilentlyContinue){
   try{foreach($draft in @(Get-ScoliaDraftRows)){
    $index++;$rows.Add([pscustomobject]@{Key=('cash-draft:'+$index);Type='Brouillon de caisse';Reference=[string]$draft.Reference;Identity=[string]$draft.Apprenant;Date=[string]$draft.Date;State=[string]$draft.Etat;Description='Aucune recette comptabilisée avant validation du brouillon.';Action='CashDrafts'})
   }}catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Brouillons de caisse';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
  }
  if($script:scoliaLocalRoot){
   $pendingPath=Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'
   if([IO.File]::Exists($pendingPath)){
    try{
     $pending=[IO.File]::ReadAllText($pendingPath,[Text.Encoding]::UTF8)|ConvertFrom-Json -ErrorAction Stop
     $same=($pending.SchoolId -ceq $script:scolia.Id -and $pending.Root -ceq $script:scoliaRoot);$index++
     $rows.Add([pscustomobject]@{Key=('pending:'+$index);Type='Opération en attente';Reference=$(if($same){[string]$pending.Reference}else{''});Identity='';Date=$(if($same){[string]$pending.Date}else{''});State='À vérifier';Description=$(if($same){'Vérifiez le journal avant toute nouvelle saisie ; une coupure ne signifie pas que le paiement a échoué.'}else{'Une opération concerne un autre dossier SCOLIA. Reconnectez son emplacement avant de la vérifier.'});Action=$(if($same){'Pending'}else{'Detail'})})
    }catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Opération en attente';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
   }
  }
 }
 if((Test-ITMPermission 'Saisie') -and (Test-ScoliaFeaturePageAllowed 'Apprenants') -and $script:data -and $script:book -and (Get-Command Read-ScoliaFeatureStore -ErrorAction SilentlyContinue)){
  try{
   $context=Get-ScoliaFeatureContext;$document=Read-ScoliaFeatureStore -Name inscriptions -Local
   $drafts=@($document.Items|Where-Object {
    if(-not $_ -or $_.Kind -ne 'Inscription' -or $_.State -eq 'Saved' -or $_.Context.Id -cne $context.Id -or $_.Context.Year -cne $context.Year -or $_.Context.LocalKey -cne $context.LocalKey){return $false}
    if((Get-Command Test-ScoliaRegistrationDraftAlreadyRegistered -ErrorAction SilentlyContinue) -and (Test-ScoliaRegistrationDraftAlreadyRegistered $_)){return $false}
    return $true
   })
   if($drafts.Count){
    $latest=@($drafts|Sort-Object UpdatedAt -Descending)[0];$index++;$identity=([string]$latest.Fields.Nom+' '+[string]$latest.Fields.PostNom+' '+[string]$latest.Fields.Prenom).Trim()
    $rows.Add([pscustomobject]@{Key=('registration:'+$index);Type='Inscription';Reference='';Identity=$identity;Date=[string]$latest.UpdatedAt;State=([string]$drafts.Count+' brouillon(s)');Description='Ouvrez une nouvelle inscription pour reprendre le dernier brouillon conservé.';Action='Registration'})
   }
  }catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Brouillons inscription';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
 }
 if((Test-ITMPermission 'Administrer') -and (Test-ITMPermission 'Saisie') -and (Test-ScoliaFeaturePageAllowed 'Scolarité') -and $script:data -and $script:book -and (Get-Command Read-ScoliaFeatureStore -ErrorAction SilentlyContinue)){
  try{
   $context=Get-ScoliaFeatureContext;$document=Read-ScoliaFeatureStore -Name reinscriptions
   foreach($plan in @($document.Items|Where-Object {$_ -and $_.ContextId -ceq $context.Id -and $_.Year -ceq $context.Year -and $_.State -in @('Préparé','Partiel')})){
    $remaining=@($plan.Entries|Where-Object {$_.State -ne 'Appliqué'}).Count;if(-not $remaining){continue};$index++
    $rows.Add([pscustomobject]@{Key=('reenrollment:'+$index);Type='Réinscription';Reference=[string]$plan.Id;Identity=([string]$remaining+' dossier(s) à vérifier');Date=[string]$plan.UpdatedUtc;State=[string]$plan.State;Description='Ouvrez le plan enregistré, vérifiez les décisions puis confirmez son application.';Action='Reenrollment'})
   }
  }catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Réinscription';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
 }
 if((Test-ITMPermission 'Lire') -and (Test-ScoliaFeaturePageAllowed 'Apprenants') -and $script:data -and $script:book -and (Get-Command Get-ScoliaPupilAbsenceAlerts -ErrorAction SilentlyContinue)){
  try{
   $context=Get-ScoliaFeatureContext;$tracking=Read-ScoliaFeatureStore -Name suiviapprenants;$presence=Read-ScoliaFeatureStore -Name presences
   Assert-ScoliaPupilCalendars $tracking;Assert-ScoliaAttendanceDocument $presence
   $academic=Get-ScoliaAttendanceYearBounds $context.Year;$now=[datetimeoffset]::Now
   $end=$(if($now.Date -gt $academic.End){$academic.End}else{$now.Date})
   if($end -ge $academic.Start){
    $latest=New-Object datetime($end.Year,$end.Month,1);$months=@($latest)
    if($latest.AddMonths(-1) -ge $academic.Start){$months+=$latest.AddMonths(-1)}
    foreach($month in $months){
     foreach($alert in @(Get-ScoliaPupilAbsenceAlerts -Learners @($script:data.learners) -Sheets @($presence.Items) -Calendars @($tracking.Items) -Month $month -Year $context.Year -ContextId $context.Id -Now $now)){
      $index++;$rows.Add([pscustomobject]@{Key=('pupil-absence:'+$index);Type='Absences apprenant';Reference=$alert.Matricule;Identity=$alert.Nom;Date=$alert.Month;State=([string]$alert.Absents+' absence(s) non justifiée(s)');Description=$alert.Alert;Action='PupilAbsence';Month=$alert.Month;Promotion=$alert.Promotion})
     }
    }
   }
  }catch{$index++;$rows.Add([pscustomobject]@{Key=('error:'+$index);Type='Suivi des absences';Reference='';Identity='';Date='';State='Lecture impossible';Description=$_.Exception.Message;Action='Detail'})}
 }
 return $rows.ToArray()
}
function Open-ScoliaActionResult($Row){
 Assert-ITMPermission 'Lire';if($script:busy){throw 'Attendez la fin de l opération en cours.'};if(-not $Row){throw 'Sélectionnez une action.'}
 switch([string]$Row.Action){
  'Diagnostic'{Assert-ITMPermission 'Diagnostic';Show-Page 'Diagnostic';if($Row.Reference){Select-ScoliaFeatureGridRow $script:issuesGrid 'Matricule' ([string]$Row.Reference)}}
  'Reception'{Assert-ITMPermission 'Recevoir';Show-ScoliaInbox}
  'CashDrafts'{Assert-ITMPermission 'Encaisser';Show-ScoliaDrafts}
  'Pending'{Assert-ITMPermission 'Encaisser';Show-ScoliaPending}
  'PupilAbsence'{Assert-ITMPermission 'Lire';if(-not (Test-ScoliaFeaturePageAllowed 'Apprenants')){throw 'Votre espace ne permet pas ce suivi.'};$month=[datetime]::ParseExact([string]$Row.Month+'-01','yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture);Show-ScoliaPupilMonthlyAttendance -Month $month -Promotion ([string]$Row.Promotion) -Matricule ([string]$Row.Reference)}
  'Registration'{Assert-ITMPermission 'Saisie';Show-Dossier}
  'Reenrollment'{Assert-ITMPermission 'Administrer';Assert-ITMPermission 'Saisie';Show-ScoliaReenrollment -PlanId ([string]$Row.Reference)}
  'Detail'{Show-TextDetail ([string]$Row.Type) ([string]$Row.Description)}
  default{throw 'Cette action ne peut pas être ouverte.'}
 }
}
function Get-ScoliaFeatureSelectedRow($State){
 if($State.PSObject.Properties['Timer'] -and $State.Timer.Enabled){throw 'La recherche est en cours. Patientez un instant.'}
 if($State.Grid.SelectedRows.Count -ne 1){throw 'Sélectionnez une ligne à ouvrir.'}
 $key=[string]$State.Grid.SelectedRows[0].Cells['Clé'].Value;$matches=@($State.Rows|Where-Object {$_.Key -ceq $key})
 if($matches.Count -ne 1){throw 'La liste a changé. Actualisez avant de sélectionner cette ligne.'};return $matches[0]
}
function Update-ScoliaSearchFeature($State,[switch]$Reload,[switch]$Force){
 $State.Message.Tag=$null;$query=[string]$State.Search.Text;$kind=[string]$State.Kind.SelectedItem
 if($query.Trim().Length -ge 2 -or $Force){
  $stamp=Get-ScoliaSearchIndexStamp
  if($Reload -or $Force -or $State.SourceStamp -cne $stamp){$State.SourceRows=@(Get-ScoliaCachedSearchRows -Force:$Force);$State.SourceStamp=$stamp}
 }
 if($query.Trim().Length -lt 2){$State.Rows=@();$State.Message.Text='Saisissez un nom, un matricule ou une référence (au moins deux caractères).'}
 else{
  $tokens=@((ConvertTo-ScoliaSearchKey $query) -split '\s+'|Where-Object {$_});$shown=New-Object 'System.Collections.Generic.List[object]';$count=0
  foreach($row in $State.SourceRows){
   if($kind -eq 'Apprenants' -and $row.Action -ne 'Learner'){continue}
   if($kind -eq 'Paiements' -and $row.Action -notin @('Payment','Cash')){continue}
   if(-not (Test-ScoliaSearchTokens $row.SearchKey $tokens)){continue}
   $count++;if($shown.Count -lt 200){$shown.Add($row)}
  }
  $State.Rows=$shown.ToArray()
  $State.Message.Text=$(if($count -gt 200){[string]$count+' résultat(s), les 200 premiers affichés. Affinez la recherche.'}else{[string]$count+' résultat(s). Un reçu peut avoir plusieurs affectations mensuelles.'})
  if(-not $State.SourceRows.Count){$State.Message.Text='Aucune donnée autorisée chargée. Connectez ou actualisez votre base / journal habituel.'}
 }
 Set-Grid $State.Grid $State.Rows @(@('Key','Clé'),@('Type','Type'),@('Reference','Référence'),@('Identity','Nom complet'),@('Promotion','Promotion'),@('Year','Année'),@('Date','Date'),@('Amount','Montant FC'),@('Description','Détail'))
 $State.Grid.Columns['Clé'].Visible=$false;$State.Grid.Enabled=$true;$State.OpenButton.Enabled=($State.Rows.Count -gt 0)
}
function Show-ScoliaGlobalSearch([switch]$LayoutOnly) {
 Assert-ITMPermission 'Lire';if($script:busy){return}
 $state=New-ScoliaFeatureDialog -Title 'Recherche globale' -Subtitle 'Recherchez un apprenant ou un reçu par nom, matricule ou référence.' -Width 1060 -Height 680
 $state|Add-Member -NotePropertyName SourceRows -NotePropertyValue @();$state|Add-Member -NotePropertyName SourceStamp -NotePropertyValue '';$state|Add-Member -NotePropertyName Rows -NotePropertyValue @();$state|Add-Member -NotePropertyName OpenRequested -NotePropertyValue $null
 $search=New-Object Windows.Forms.TextBox;$search.Width=330;$search.Margin=New-Object Windows.Forms.Padding(6,10,6,6);$search.AccessibleName='Nom, matricule, numéro du reçu ou référence du paiement';$state.Toolbar.Controls.Add($search)
 $kind=New-Object Windows.Forms.ComboBox;$kind.Width=155;$kind.DropDownStyle='DropDownList';$kind.Margin=New-Object Windows.Forms.Padding(6,10,6,6);$kind.Items.AddRange([object[]]@('Tous','Apprenants','Paiements'));$kind.SelectedIndex=0;$kind.AccessibleName='Type de résultat';$state.Toolbar.Controls.Add($kind)
 $state|Add-Member -NotePropertyName Search -NotePropertyValue $search;$state|Add-Member -NotePropertyName Kind -NotePropertyValue $kind
 $timer=New-Object Windows.Forms.Timer;$timer.Interval=300;$state|Add-Member -NotePropertyName Timer -NotePropertyValue $timer
 if(-not $script:scoliaFeatureSearchTimers){$script:scoliaFeatureSearchTimers=@{}};$script:scoliaFeatureSearchTimers[$timer]=$state
 $timer.Add_Tick({param($sender,$e)$sender.Stop();$s=$script:scoliaFeatureSearchTimers[$sender];if(-not $s -or $s.Form.IsDisposed){return};if($script:busy){$sender.Start();return};try{Update-ScoliaSearchFeature $s}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}})
 $open=Add-ScoliaButton $state.Actions 'Ouvrir la sélection' {param($sender,$e)$s=$sender.FindForm().Tag;try{$s.OpenRequested=Get-ScoliaFeatureSelectedRow $s;$s.Form.Close()}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}
 $state|Add-Member -NotePropertyName OpenButton -NotePropertyValue $open
 $refresh=Add-ScoliaButton $state.Actions 'Actualiser la liste' {param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaSearchFeature $s -Reload -Force}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}
 $close=Add-ScoliaButton $state.Actions 'Fermer' {param($sender,$e)$sender.FindForm().Close()};$state.Form.CancelButton=$close
 $state.Form.MinimumSize=New-Object Drawing.Size(320,540)
 $open.Text='Ouvrir';$open.Width=86;$refresh.Text='Actualiser';$refresh.Width=104;$close.Width=70
 foreach($button in @($open,$refresh,$close)){$button.AutoSize=$false;$button.Margin=New-Object Windows.Forms.Padding(4);$button.Height=38}
 $open.AccessibleDescription='Ouvrir la sélection';$refresh.AccessibleDescription='Actualiser la liste'
 $open.Primary=$true;$open.TabIndex=2;$refresh.TabIndex=3;$close.TabIndex=4;$state.Form.AcceptButton=$open
 $state.Toolbar.Controls.Clear();$state.Toolbar.AutoSize=$false;$state.Toolbar.WrapContents=$false;$state.Toolbar.Padding=New-Object Windows.Forms.Padding(18,2,18,10)
 $search.Font=New-Object Drawing.Font('Segoe UI',11);$search.BorderStyle='None';$search.TabIndex=0
 $kind.Font=New-Object Drawing.Font('Segoe UI',10);$kind.FlatStyle='Flat';$kind.TabIndex=1
 $fields=New-Object ITMSearchFields($search,$kind);$fields.Dock='None';$state.Toolbar.Controls.Add($fields)
 $state|Add-Member -NotePropertyName SearchFields -NotePropertyValue $fields
 $state.Form.Add_KeyDown({param($sender,$e)if($e.Control -and $e.KeyCode -eq 'F'){$sender.Tag.Search.Focus();$sender.Tag.Search.SelectAll();$e.SuppressKeyPress=$true}});$state.Form.KeyPreview=$true
 Update-ScoliaFeatureDialogLayout $state
 $search.Add_TextChanged({param($sender,$e)$s=$sender.FindForm().Tag;Start-ScoliaSearchFeatureTimer $s})
 $kind.Add_SelectedIndexChanged({param($sender,$e)$s=$sender.FindForm().Tag;Start-ScoliaSearchFeatureTimer $s})
 $search.Add_KeyDown({param($sender,$e)if($e.KeyCode -eq 'Enter'){$e.Handled=$true;$e.SuppressKeyPress=$true;$s=$sender.FindForm().Tag;$s.Timer.Stop();try{Update-ScoliaSearchFeature $s;if($s.Rows.Count -eq 1){$s.OpenRequested=$s.Rows[0];$s.Form.Close()}elseif($s.Rows.Count){[void]$s.Grid.Focus()}}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}})
 $state.Grid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -ge 0){$s=$sender.FindForm().Tag;try{$s.OpenRequested=Get-ScoliaFeatureSelectedRow $s;$s.Form.Close()}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}})
 $state.Form.Add_Shown({param($sender,$e)[void]$sender.Tag.Search.Focus()})
 if($LayoutOnly){return $state}
 if($script:scoliaInlineRequest){
  Update-ScoliaSearchFeature $state -Reload
  if(Mount-ScoliaWorkspaceForm $state.Form){
   $searchTimers=$script:scoliaFeatureSearchTimers
   $state.Form.Add_Disposed({$timer.Stop();$searchTimers.Remove($timer);$timer.Dispose()}.GetNewClosure())
   $openResult=(Get-Command Open-ScoliaSearchResult).ScriptBlock
   $state.Form.Add_FormClosed({if($state.OpenRequested){& $openResult $state.OpenRequested}}.GetNewClosure());return
  }
 }
 try{Update-ScoliaSearchFeature $state -Reload;[void]$state.Form.ShowDialog($script:main)}finally{$timer.Stop();$script:scoliaFeatureSearchTimers.Remove($timer);$timer.Dispose();$state.Form.Dispose()}
 if($state.OpenRequested){Open-ScoliaSearchResult $state.OpenRequested}
}
function Update-ScoliaActionFeature($State){
 $State.Message.Tag=$null;$State.Rows=@(Get-ScoliaActionRows)
 Set-Grid $State.Grid $State.Rows @(@('Key','Clé'),@('Type','À traiter'),@('State','État'),@('Reference','Référence'),@('Identity','Dossier'),@('Date','Date'),@('Description','Conseil'))
 $State.Grid.Columns['Clé'].Visible=$false;$State.OpenButton.Enabled=($State.Rows.Count -gt 0)
 $State.Message.Text=$(if($State.Rows.Count){[string]$State.Rows.Count+' élément(s). Ouvrir une ligne ne valide ni paiement ni inscription.'}else{'Aucune action en attente dans les données chargées et les états locaux de votre espace.'})
}
function Show-ScoliaActionCenter {
 Assert-ITMPermission 'Lire';if($script:busy){return}
 $state=New-ScoliaFeatureDialog -Title 'À traiter' -Subtitle 'Les observations, transferts et brouillons existants, réunis au même endroit. Vérifiez les détails avant toute validation.' -Width 1060 -Height 680
 $state|Add-Member -NotePropertyName Rows -NotePropertyValue @();$state|Add-Member -NotePropertyName OpenRequested -NotePropertyValue $null
 $open=Add-ScoliaButton $state.Actions 'Ouvrir la sélection' {param($sender,$e)$s=$sender.FindForm().Tag;try{$s.OpenRequested=Get-ScoliaFeatureSelectedRow $s;$s.Form.Close()}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}
 $state|Add-Member -NotePropertyName OpenButton -NotePropertyValue $open
 [void](Add-ScoliaButton $state.Actions 'Actualiser la liste' {param($sender,$e)$s=$sender.FindForm().Tag;try{Update-ScoliaActionFeature $s}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}})
 $close=Add-ScoliaButton $state.Actions 'Fermer' {param($sender,$e)$sender.FindForm().Close()};$state.Form.CancelButton=$close
 $state.Grid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -ge 0){$s=$sender.FindForm().Tag;try{$s.OpenRequested=Get-ScoliaFeatureSelectedRow $s;$s.Form.Close()}catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}}})
 Update-ScoliaActionFeature $state
 if((Get-Command Mount-ScoliaWorkspaceForm -ErrorAction SilentlyContinue) -and (Mount-ScoliaWorkspaceForm $state.Form)){$openResult=(Get-Command Open-ScoliaActionResult).ScriptBlock;$state.Form.Add_FormClosed({if($state.OpenRequested){& $openResult $state.OpenRequested}}.GetNewClosure());return}
 try{[void]$state.Form.ShowDialog($script:main)}finally{$state.Form.Dispose()}
 if($state.OpenRequested){Open-ScoliaActionResult $state.OpenRequested}
}
