# Tests métier isolés : aucune fenêtre, aucun Excel et aucune donnée de production.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CentreRechercheV55.ps1')
function Assert-CentreV55([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Test-ITMPermission([string]$Permission){return $Permission -in @($script:permissions)}
function Assert-ITMPermission([string]$Permission){if(-not (Test-ITMPermission $Permission)){throw ('Accès refusé : '+$Permission)}}
function Get-ScoliaInboxRows{$script:inboxReads++;return $script:mockInbox}
function Get-ScoliaDraftRows{$script:cashDraftReads++;return $script:mockCashDrafts}
function Get-ScoliaFeatureContext{return $script:mockContext}
function Read-ScoliaFeatureStore([string]$Name,[switch]$Local){
 if($script:storeFailure -eq $Name){throw ('Lecture impossible : '+$Name)}
 if($Name -eq 'inscriptions' -and -not $Local){throw 'Les inscriptions doivent rester locales.'}
 return $script:mockStores[$Name]
}
function Show-Fiche([string]$Mat){$script:lastOpened='Fiche '+$Mat}
function Show-Page([string]$Name){$script:lastOpened='Page '+$Name}
function Refresh-ScoliaFinance{$script:financeRefreshes++}
function Reset-PaymentSelection([bool]$Refresh=$true){$script:resetRefresh=$Refresh;$script:payMat=''}
function Refresh-Payments{$script:paymentRefreshes++}
function Set-Grid($Grid,$Rows,$Columns){$script:capturedGridRows=@($Rows)}
function Test-ScoliaRegistrationDraftAlreadyRegistered($Draft){return [string]$Draft.Id -ceq $script:alreadyRegisteredDraft}
function Show-ScoliaInbox{$script:lastOpened='Réception'}
function Show-ScoliaDrafts{$script:lastOpened='Brouillons caisse'}
function Show-ScoliaPending{$script:lastOpened='Vérification caisse'}
function Show-Dossier{$script:lastOpened='Inscription'}
function Show-ScoliaReenrollment([string]$PlanId=''){$script:lastOpened='Réinscription '+$PlanId}
function Show-TextDetail([string]$Title,[string]$Text){$script:lastOpened=$Title+' '+$Text}
$script:permissions=@('Lire','Diagnostic','Saisie','Encaisser','Recevoir','Comptabilite','Administrer')
$script:scoliaSession=$null;$script:tabsNames=@();$script:busy=$false
$script:scolia=[pscustomobject]@{Id='school-a';Year='2026-2027'};$script:scoliaRoot='shared-a'
$script:mockContext=[pscustomobject]@{Id='dataset-a';Year='2026-2027';LocalKey='user-a'}
$script:book=[pscustomobject]@{FullName='isolated-fixture.xlsm'}
$script:data=[pscustomobject]@{
 learners=@(
  [pscustomobject]@{Matricule='ACC101';Nom='Émile';PostNom='MPOLO';Prenom='Jean';Promotion='INF1';Annee='2026-2027';Statut='Actif';Password='NEVER_INDEX_THIS';Adresse='NEVER_INDEX_ADDRESS'},
  [pscustomobject]@{Matricule='ACC102';Nom='Marie';PostNom='LONO';Prenom='Claire';Promotion='INF2';Annee='2026-2027';Statut='Actif'})
 payments=@(
  [pscustomobject]@{ID=1;Matricule='ACC101';NomComplet='Émile MPOLO Jean';Numero='REC123';Mois='Septembre';Date='10/10/2026';Verse=50;Statut='Partiel'},
  [pscustomobject]@{ID=2;Matricule='ACC101';NomComplet='Émile MPOLO Jean';Numero='REC123';Mois='Novembre';Date='10/10/2026';Verse=60;Statut='Partiel'})
 issues=@([pscustomobject]@{Matricule='ACC101';Feuille='Apprenants';Observation='Informations à compléter'})
}
$script:scoliaEntries=@(
 [pscustomobject]@{Id='payment-a';SchoolId='school-a';Kind='Paiement';Annee='2026-2027';Reference='CAISSE-017';Nom='Émile';PostNom='MPOLO';Prenom='Jean';Promotion='INF1';Date='2026-10-10';Montant=110;Parts=@([pscustomobject]@{Mois='Octobre'});Secret='NEVER_INDEX_SECRET'},
 [pscustomobject]@{Id='expense-a';SchoolId='school-a';Kind='Depense';Annee='2026-2027';Reference='DEPENSE-018';Description='Fournitures'},
 [pscustomobject]@{Id='old-a';SchoolId='school-a';Kind='Paiement';Annee='2025-2026';Reference='OLD-019';Nom='Émile'},
 [pscustomobject]@{Id='foreign-a';SchoolId='school-b';Kind='Paiement';Annee='2026-2027';Reference='FOREIGN-020';Nom='Émile'})
$rows=@(Get-ScoliaGlobalSearchRows 'emile ACC101' 'Apprenants')
Assert-CentreV55 ($rows.Count -eq 1 -and $rows[0].Reference -eq 'ACC101') 'Recherche avec accents et plusieurs termes incorrecte.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'emile inf2' 'Apprenants').Count -eq 0) 'Un terme absent est ignoré.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'NEVER_INDEX').Count -eq 0) 'La recherche expose un champ privé ou non prévu.'
$rows=@(Get-ScoliaGlobalSearchRows 'rec123 novembre' 'Paiements')
Assert-CentreV55 ($rows.Count -eq 1 -and $rows[0].SourceId -eq '2') 'Recherche ciblée dans les affectations mensuelles incorrecte.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'rec123' 'Paiements').Count -eq 2) 'Les lignes mensuelles du reçu sont perdues.'
$cash=@(Get-ScoliaGlobalSearchRows 'CAISSE-017' 'Paiements')
Assert-CentreV55 ($cash.Count -eq 1 -and $cash[0].Action -eq 'Cash') 'Le paiement de caisse est absent.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'OLD-019').Count -eq 0) 'La caisse expose une autre année.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'FOREIGN-020').Count -eq 0) 'La caisse expose une autre institution.'
Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows 'DEPENSE-018').Count -eq 0) 'Une dépense est présentée comme reçu de paiement.'
$script:scoliaSession=[pscustomobject]@{Role='Comptabilité'};$script:tabsNames=@('Comptabilité','Diagnostic','Données')
$script:permissions=@('Lire','Diagnostic','Encaisser','Comptabilite')
$rows=@(Get-ScoliaGlobalSearchRows 'emile')
Assert-CentreV55 ($rows.Count -eq 1 -and $rows[0].Action -eq 'Cash') 'La comptabilité accède aux fiches ou affectations du secrétariat.'
$script:permissions=@('Lire');$rows=@(Get-ScoliaGlobalSearchRows)
Assert-CentreV55 ($rows.Count -eq 0) 'Le cache de caisse est exposé sans droit métier.'
$script:permissions=@();Assert-CentreV55 (@(Get-ScoliaGlobalSearchRows).Count -eq 0) 'La recherche reste accessible sans droit Lire.'
$script:scoliaSession=$null;$script:permissions=@('Lire','Diagnostic','Saisie','Encaisser','Recevoir','Comptabilite','Administrer')
$learner=@(Get-ScoliaGlobalSearchRows 'acc101' 'Apprenants')[0]
Open-ScoliaSearchResult $learner
Assert-CentreV55 ($script:lastOpened -eq 'Fiche ACC101') 'La recherche ne rejoint pas la fiche réelle.'
$payment=@(Get-ScoliaGlobalSearchRows 'rec123 novembre' 'Paiements')[0]
$script:paymentRefreshes=0;$script:payIdentity=[pscustomobject]@{Text=''};$script:payFilterMonth=[pscustomobject]@{SelectedItem='Octobre'};$script:paymentGrid=$null;$script:pages=@{}
Open-ScoliaSearchResult $payment
Assert-CentreV55 ($script:lastOpened -eq 'Page Paiements' -and $script:payMat -eq 'ACC101' -and -not $script:resetRefresh -and $script:payFilterMonth.SelectedItem -eq 'Tous les mois' -and $script:paymentRefreshes -eq 1) 'La recherche ne rejoint pas l historique filtré du reçu.'
$script:financeRefreshes=0;$script:scoliaFinanceGrid=$null;$script:scoliaFinanceSearch=[pscustomobject]@{Text='ancienne recherche'}
Open-ScoliaSearchResult $cash[0]
Assert-CentreV55 ($script:lastOpened -eq 'Page Comptabilité' -and $script:scoliaFinanceQuery -eq 'CAISSE-017' -and $script:scoliaFinanceSearch.Text -eq 'CAISSE-017' -and $script:financeRefreshes -eq 1) 'La recherche ne rejoint pas le journal ciblé.'
$script:permissions=@('Lire');$rejected=$false
try{Open-ScoliaSearchResult $cash[0]}catch{$rejected=$true}
Assert-CentreV55 $rejected 'Ouvrir un ancien résultat contourne une permission retirée.'
$script:permissions=@('Lire','Diagnostic','Saisie','Encaisser','Recevoir','Comptabilite','Administrer')
$script:dataRevision=1
[void](Get-ScoliaCachedSearchRows -Force);$firstIndex=$script:scoliaSearchIndex
[void](Get-ScoliaCachedSearchRows)
Assert-CentreV55 ([object]::ReferenceEquals($firstIndex,$script:scoliaSearchIndex)) 'L index normalisé est reconstruit sans modification de source.'
$script:data.learners[1].Nom='Anaïs';$script:dataRevision++
$cached=@(Get-ScoliaCachedSearchRows)
Assert-CentreV55 (-not [object]::ReferenceEquals($firstIndex,$script:scoliaSearchIndex) -and @($cached|Where-Object {$_.Action -eq 'Learner' -and $_.SearchKey.Contains('ANAIS')}).Count -eq 1) 'Une révision des données ne renouvelle pas l index normalisé.'
$state=[pscustomobject]@{
 SourceRows=@(1..500|ForEach-Object {[pscustomobject]@{Key=('row-'+$_);Action='Learner';SearchKey='EMILE ACC'+$_}})
 SourceStamp=(Get-ScoliaSearchIndexStamp);Search=[pscustomobject]@{Text='emile'};Kind=[pscustomobject]@{SelectedItem='Tous'}
 Message=[pscustomobject]@{Text='';Tag=$null};Rows=@();OpenButton=[pscustomobject]@{Enabled=$true}
 Grid=[pscustomobject]@{Enabled=$true;Columns=@{'Clé'=[pscustomobject]@{Visible=$true}}}
}
Update-ScoliaSearchFeature $state
Assert-CentreV55 ($state.Rows.Count -eq 200 -and $script:capturedGridRows.Count -eq 200 -and $state.Message.Text -like '500 résultat*Affinez*') 'Le plafond affiché ou le compteur de recherche est incorrect.'
$state.Search.Text='e';Update-ScoliaSearchFeature $state
Assert-CentreV55 ($state.Rows.Count -eq 0 -and -not $state.OpenButton.Enabled -and $state.Message.Text -like '*au moins deux*') 'Une recherche trop courte affiche la base entière.'
$script:mockInbox=@([pscustomobject]@{Reference='TRANSFERT-001';Nom='Émile MPOLO';Date='2026-10-10';Etat='Erreur';Observation='Dossier ambigu'})
$script:mockCashDrafts=@([pscustomobject]@{Reference='DRAFT-001';Apprenant='Marie LONO';Date='2026-10-10';Etat='Brouillon · non comptabilisé'})
$script:mockStores=@{
 inscriptions=[pscustomobject]@{Items=@(
  [pscustomobject]@{Id='draft-1';Kind='Inscription';State='Draft';Context=$script:mockContext;UpdatedAt='2026-10-11T09:00:00Z';Fields=[pscustomobject]@{Nom='Émile';PostNom='MPOLO';Prenom='Jean'}},
  [pscustomobject]@{Id='draft-2';Kind='Inscription';State='Draft';Context=$script:mockContext;UpdatedAt='2026-10-11T10:00:00Z';Fields=[pscustomobject]@{Nom='Marie';PostNom='LONO';Prenom='Claire'}},
  [pscustomobject]@{Id='saved';Kind='Inscription';State='Saved';Context=$script:mockContext;UpdatedAt='2026-10-11T11:00:00Z';Fields=[pscustomobject]@{Nom='Enregistré'}},
  [pscustomobject]@{Id='foreign';Kind='Inscription';State='Draft';Context=[pscustomobject]@{Id='dataset-b';Year='2026-2027';LocalKey='user-a'};UpdatedAt='2026-10-11T12:00:00Z';Fields=[pscustomobject]@{Nom='Autre base'}})}
 reinscriptions=[pscustomobject]@{Items=@(
  [pscustomobject]@{Id='plan-1';ContextId='dataset-a';Year='2026-2027';State='Partiel';UpdatedUtc='2026-10-11T10:00:00Z';Entries=@([pscustomobject]@{State='Appliqué'},[pscustomobject]@{State='À vérifier'})},
  [pscustomobject]@{Id='plan-done';ContextId='dataset-a';Year='2026-2027';State='Appliqué';UpdatedUtc='2026-10-11T11:00:00Z';Entries=@([pscustomobject]@{State='Appliqué'})})}
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('scolia-centre-v55-'+[guid]::NewGuid().ToString('N'))
try{
 [void][IO.Directory]::CreateDirectory($temp);$script:scoliaLocalRoot=$temp
 $pending=[pscustomobject]@{SchoolId='school-a';Root='shared-a';Reference='PENDING-001';Date='2026-10-11'}
 $pendingPath=Join-Path $temp 'operation-caisse-en-attente.json';[IO.File]::WriteAllText($pendingPath,($pending|ConvertTo-Json),[Text.Encoding]::UTF8)
 $script:inboxReads=0;$script:cashDraftReads=0;$script:storeFailure=''
 $actions=@(Get-ScoliaActionRows)
 Assert-CentreV55 ($actions.Count -eq 6) 'Le centre ne réunit pas tous les états existants.'
 $registration=@($actions|Where-Object {$_.Action -eq 'Registration'})
 Assert-CentreV55 ($registration.Count -eq 1 -and $registration[0].State -eq '2 brouillon(s)' -and $registration[0].Identity -eq 'Marie LONO Claire') 'Les brouillons enregistrés ou étrangers sont présentés à reprendre.'
 $script:alreadyRegisteredDraft='draft-2';$filteredActions=@(Get-ScoliaActionRows);$filteredRegistration=@($filteredActions|Where-Object {$_.Action -eq 'Registration'})
 Assert-CentreV55 ($filteredRegistration.Count -eq 1 -and $filteredRegistration[0].State -eq '1 brouillon(s)' -and $filteredRegistration[0].Identity -eq 'Émile MPOLO Jean') 'Un dossier déjà inscrit reste proposé par un ancien brouillon.'
 $script:alreadyRegisteredDraft=''
 $plan=@($actions|Where-Object {$_.Action -eq 'Reenrollment'})
 Assert-CentreV55 ($plan.Count -eq 1 -and $plan[0].Reference -eq 'plan-1' -and $plan[0].Identity -eq '1 dossier(s) à vérifier') 'Le centre ne cible pas le plan partiellement appliqué.'
 Open-ScoliaActionResult $plan[0]
 Assert-CentreV55 ($script:lastOpened -eq 'Réinscription plan-1') 'Le centre ne rejoint pas le plan exact.'
 Open-ScoliaActionResult $registration[0]
 Assert-CentreV55 ($script:lastOpened -eq 'Inscription') 'La reprise ne rejoint pas le formulaire réel.'
 $script:permissions=@('Lire','Diagnostic');$script:inboxReads=0;$script:cashDraftReads=0
 $actions=@(Get-ScoliaActionRows)
 Assert-CentreV55 ($actions.Count -eq 1 -and $actions[0].Action -eq 'Diagnostic' -and $script:inboxReads -eq 0 -and $script:cashDraftReads -eq 0) 'Le centre lit des sources sans droit métier.'
 $rejected=$false;try{Open-ScoliaActionResult $registration[0]}catch{$rejected=$true}
 Assert-CentreV55 $rejected 'Ouvrir une action ancienne contourne une permission retirée.'
 $script:permissions=@('Lire','Diagnostic','Saisie','Encaisser','Recevoir','Comptabilite','Administrer');$script:storeFailure='inscriptions'
 $actions=@(Get-ScoliaActionRows)
 Assert-CentreV55 (@($actions|Where-Object {$_.Type -eq 'Brouillons inscription' -and $_.State -eq 'Lecture impossible' -and $_.Description -like '*inscriptions*'}).Count -eq 1) 'Un fichier illisible disparaît silencieusement du centre.'
 $script:storeFailure='';$pending.SchoolId='school-b';[IO.File]::WriteAllText($pendingPath,($pending|ConvertTo-Json),[Text.Encoding]::UTF8)
 $actions=@(Get-ScoliaActionRows);$foreign=@($actions|Where-Object {$_.Type -eq 'Opération en attente'})
 Assert-CentreV55 ($foreign.Count -eq 1 -and $foreign[0].Action -eq 'Detail' -and -not $foreign[0].Reference) 'Une opération étrangère peut être vérifiée dans le mauvais dossier.'
 Assert-CentreV55 ([IO.File]::Exists($pendingPath)) 'Une lecture du centre a effacé une opération en attente.'
}finally{if([IO.Directory]::Exists($temp)){[IO.Directory]::Delete($temp,$true)}}
'PASS : recherche multi-termes et accents, cache et plafond de résultats, isolation métier et année, ouverture réelle, états existants, reprise ciblée et erreurs visibles.'
