# Tests métier isolés : aucun Excel, aucune donnée de production et aucune fermeture annuelle réelle.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReinscriptionV55.ps1')
function Assert-Reenrollment([bool]$value,[string]$message){if(-not $value){throw $message}}
function Copy-ReenrollmentValue($value){return ($value | ConvertTo-Json -Depth 20 -Compress | ConvertFrom-Json)}
function Import-ReenrollmentFunction([string]$name){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide dans StockageClasseurs.ps1.'}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $node){throw ('Fonction absente : '+$name)}
    . ([scriptblock]::Create('function global:'+$name+$node.Extent.Text.Substring(('function '+$name).Length)))
}
function Key($value){return ([string]$value).Trim().ToUpperInvariant()}
function Promo($value){return Key $value}
Import-ReenrollmentFunction 'Get-ITMIdentityPart';Import-ReenrollmentFunction 'Assert-ITMLearnerIdentity'
function Assert-ITMPermission([string]$name){if($name -eq $script:deniedPermission){throw 'Droit refusé.'}}
function Get-ITMPECRecord($learner){return $script:blockPEC}
function Get-ScoliaFeatureContext {return $script:context}
function Read-ScoliaFeatureStore([string]$Name){Assert-Reenrollment ($Name -eq 'reinscriptions') 'Mauvais stockage.';return Copy-ReenrollmentValue $script:document}
function Invoke-ScoliaFeatureUpdate([string]$Name,[scriptblock]$Change){
    Assert-Reenrollment ($Name -eq 'reinscriptions') 'Mauvais stockage.'
    $copy=Copy-ReenrollmentValue $script:document;[void](& $Change $copy)
    $entries=@($copy.Items | ForEach-Object {$_.Entries})
    if($script:failWritingJournal -and @($entries | Where-Object {$_.State -eq 'Écriture en cours'}).Count){throw 'Journal indisponible avant écriture.'}
    if($script:failFinalJournalOnce -and @($entries | Where-Object {$_.State -eq 'Appliqué'}).Count){$script:failFinalJournalOnce=$false;throw 'Panne après validation Excel.'}
    $copy.Revision++;$script:document=$copy
}
function Invoke-ITMJsonDirect([string]$name,[object[]]$arguments=@()){
    switch($name){
        'ITMAppSnapshot' {return Copy-ReenrollmentValue $script:mockSnapshot}
        'ITMAppEtatApprenant' {
            if($script:failReadBackOnce){$script:failReadBackOnce=$false;throw 'Relecture temporairement indisponible.'}
            $rows=@($script:mockSnapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $arguments[0])})
            return [pscustomobject]@{ok=($rows.Count -eq 1);matricule=[string]$arguments[0];learner=$(if($rows.Count -eq 1){Copy-ReenrollmentValue $rows[0]}else{$null})}
        }
        default {throw ('API inattendue : '+$name)}
    }
}
function Invoke-ITMDossier([object[]]$arguments){
    $script:writeAttempts++;$script:lastArguments=@($arguments)
    Assert-Reenrollment ($arguments.Count -eq 14) 'Le contrat de 14 arguments est rompu.'
    if($script:failMat -eq [string]$arguments[0]){throw 'Erreur métier simulée.'}
    $rows=@($script:mockSnapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $arguments[0])});Assert-Reenrollment ($rows.Count -eq 1) 'Modification sans dossier unique.'
    $record=$rows[0];$names=@('Matricule','Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Tuteur','Contact1','Contact2','Promotion','Annee','Statut','Adresse')
    for($i=0;$i -lt $names.Count;$i++){$record.($names[$i])=[string]$arguments[$i]}
    $script:successfulWrites++
    return ('{"ok":true,"matricule":"'+$arguments[0]+'","nouveau":false}')
}
function Reset-ReenrollmentTest {
    $script:context=[pscustomobject]@{Id='dataset-test';Year='2027-2028';LocalKey='test'}
    $script:document=[pscustomobject]@{Schema=1;Context='dataset-test';Revision=0;Items=@()}
    $script:mockSnapshot=[pscustomobject]@{closed=$false;learners=@(
        [pscustomobject]@{Matricule='INF101';Nom='NGOIE';PostNom='KABILA';Prenom='ELISE';Sexe='F';Naissance='03/05/2008';Lieu='Kolwezi';Tuteur='Parent de Elise';Contact1='0991234567';Contact2='0997654321';Promotion='INF1';Annee='2027-2028';Statut='Actif';Adresse='Quartier Central';Inscription='01/09/2026'},
        [pscustomobject]@{Matricule='INF102';Nom='KASONGO';PostNom='MWAMBA';Prenom='JEAN';Sexe='M';Naissance='08/12/2007';Lieu='Likasi';Tuteur='Tante de Jean';Contact1='0811234567';Contact2='';Promotion='INF1';Annee='2027-2028';Statut='Suspendu';Adresse='Avenue Ecole';Inscription='01/09/2026'}
    );payments=@();balances=@();promotions=@([pscustomobject]@{Code='INF1';Annee='2027-2028';Active=$true;Mensuel=100;Annuel=900},[pscustomobject]@{Code='INF2';Annee='2027-2028';Active=$true;Mensuel=110;Annuel=990})}
    $script:data=Copy-ReenrollmentValue $script:mockSnapshot;$script:store=$null;$script:snapshotValid=$true
    $script:deniedPermission='';$script:blockPEC=$false;$script:failWritingJournal=$false;$script:failFinalJournalOnce=$false;$script:failReadBackOnce=$false;$script:failMat=''
    $script:writeAttempts=0;$script:successfulWrites=0;$script:lastArguments=@();$script:committedThisAction=$false
}
function New-ReenrollmentTestPlan([string]$decision='Passage',[string]$promotion='INF2'){
    return New-ScoliaReenrollmentPlan @([pscustomobject]@{Matricule='INF101';Decision=$decision;TargetPromotion=$promotion}) (Copy-ReenrollmentValue $script:mockSnapshot)
}
function Assert-ReenrollmentReject([scriptblock]$action,[string]$message){$failed=$false;try{[void](& $action)}catch{$failed=$true};Assert-Reenrollment $failed $message}

Reset-ReenrollmentTest
$before=Copy-ReenrollmentValue $script:mockSnapshot.learners[0];$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan
Assert-Reenrollment ($script:writeAttempts -eq 0) 'L''aperçu écrit déjà les dossiers.'
$summary=Invoke-ScoliaReenrollment $plan.Id
Assert-Reenrollment ($summary.Written -eq 1 -and $script:successfulWrites -eq 1) 'Le passage n''a pas été appliqué.'
Assert-Reenrollment ($script:mockSnapshot.learners[0].Promotion -eq 'INF2') 'Promotion cible absente.'
foreach($name in @('Matricule','Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Tuteur','Contact1','Contact2','Adresse','Inscription')){Assert-Reenrollment ([string]$script:mockSnapshot.learners[0].$name -ceq [string]$before.$name) ('Champ historique altéré : '+$name)}
Assert-Reenrollment ([string]$script:lastArguments[7] -ceq [string]$before.Tuteur) 'Tuteur perdu dans le contrat.'
$summary=Invoke-ScoliaReenrollment $plan.Id
Assert-Reenrollment ($summary.Written -eq 0 -and $summary.Confirmed -eq 1 -and $script:writeAttempts -eq 1) 'Un plan appliqué a été réécrit.'
Assert-Reenrollment ($script:document.Items[0].State -eq 'Appliqué') 'Le journal final n''est pas confirmé.'

Reset-ReenrollmentTest;$script:mockSnapshot.payments=@([pscustomobject]@{Matricule='INF101';Verse=0;Numero='TEST01'})
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un passage avec paiement, même nul, a été accepté.'
$paymentBefore=$script:mockSnapshot.payments | ConvertTo-Json -Compress
$plan=New-ReenrollmentTestPlan 'Sortie';Save-ScoliaReenrollmentPlan $plan;[void](Invoke-ScoliaReenrollment $plan.Id)
Assert-Reenrollment ($script:mockSnapshot.learners[0].Statut -eq 'Sorti' -and $script:mockSnapshot.learners.Count -eq 2) 'Une sortie a supprimé le dossier.'
Assert-Reenrollment (($script:mockSnapshot.payments | ConvertTo-Json -Compress) -ceq $paymentBefore) 'Les paiements ont été modifiés.'
Reset-ReenrollmentTest;$script:mockSnapshot.balances=@([pscustomobject]@{Matricule='INF101';Verse=0})
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un historique de solde a autorisé un changement de promotion.'
Reset-ReenrollmentTest;$script:blockPEC=$true
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un passage a ignoré une prise en charge existante.'

Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:context.Id='autre-dataset'
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un plan d''un autre dossier de données a été appliqué.'
Assert-Reenrollment ($script:writeAttempts -eq 0) 'Écriture avant contrôle du contexte.'
Reset-ReenrollmentTest;$dialogState=[pscustomobject]@{Context=(Copy-ReenrollmentValue $script:context)};$script:context.Id='autre-dataset'
Assert-ReenrollmentReject {Assert-ScoliaReenrollmentDialogContext $dialogState} 'Un assistant ouvert dans une autre base a préparé de nouveaux choix.'
Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:context.Year='2028-2029'
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un plan d''une autre année a été appliqué.'
Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:mockSnapshot.learners[0].Contact1='0990000000'
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un dossier modifié depuis l''aperçu a été écrasé.'
Assert-Reenrollment ($script:writeAttempts -eq 0) 'La révision du dossier est vérifiée trop tard.'
Reset-ReenrollmentTest;$script:mockSnapshot.closed=$true
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Une année clôturée a été modifiée.'
Reset-ReenrollmentTest;$script:deniedPermission='Administrer'
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un utilisateur sans droit Administrer a préparé un plan.'
Reset-ReenrollmentTest;$script:deniedPermission='Saisie'
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un utilisateur sans droit Saisie a préparé un plan.'

Reset-ReenrollmentTest
$duplicate=Copy-ReenrollmentValue $script:mockSnapshot.learners[0];$duplicate.Matricule='INF201';$duplicate.Promotion='INF2';$script:mockSnapshot.learners+=@($duplicate)
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Une collision d''identité dans la promotion cible a été acceptée.'
Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan
Assert-ReenrollmentReject {Save-ScoliaReenrollmentPlan $plan} 'Un aperçu immuable a été remplacé.'
$script:mockSnapshot.payments=@([pscustomobject]@{Matricule='INF101';Verse=100})
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un paiement ajouté après l''aperçu n''a pas bloqué le passage.'
Assert-Reenrollment ($script:writeAttempts -eq 0) 'Paiement vérifié après une écriture.'

Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:failWritingJournal=$true
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Une écriture a ignoré un journal local indisponible.'
Assert-Reenrollment ($script:writeAttempts -eq 0) 'Une écriture a précédé le journal durable.'
Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:failFinalJournalOnce=$true
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'La panne de finalisation du journal n''a pas été signalée.'
Assert-Reenrollment ($script:successfulWrites -eq 1 -and $script:document.Items[0].Entries[0].State -eq 'À vérifier') 'Le résultat incertain n''a pas été conservé.'
$summary=Invoke-ScoliaReenrollment $plan.Id
Assert-Reenrollment ($summary.Confirmed -eq 1 -and $script:writeAttempts -eq 1) 'Une reprise après validation Excel a réappliqué le dossier.'
Reset-ReenrollmentTest;$plan=New-ReenrollmentTestPlan;Save-ScoliaReenrollmentPlan $plan;$script:failReadBackOnce=$true
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Une relecture indisponible n''a pas arrêté le lot.'
[void](Invoke-ScoliaReenrollment $plan.Id)
Assert-Reenrollment ($script:writeAttempts -eq 1 -and $script:document.Items[0].State -eq 'Appliqué') 'La reprise après panne de relecture a réécrit un résultat confirmé.'

Reset-ReenrollmentTest
$plan=New-ScoliaReenrollmentPlan @([pscustomobject]@{Matricule='INF101';Decision='Passage';TargetPromotion='INF2'},[pscustomobject]@{Matricule='INF102';Decision='Redoublement';TargetPromotion='INF1'}) $script:mockSnapshot
Save-ScoliaReenrollmentPlan $plan;$script:failMat='INF102'
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un lot partiel a été annoncé terminé.'
Assert-Reenrollment ($script:successfulWrites -eq 1 -and $script:document.Items[0].State -eq 'Partiel') 'Le lot partiel n''est pas journalisé.'
$script:failMat='';$summary=Invoke-ScoliaReenrollment $plan.Id
Assert-Reenrollment ($script:successfulWrites -eq 2 -and $summary.Written -eq 1 -and $summary.Confirmed -eq 1) 'La reprise du lot partiel a répété un dossier.'
Assert-Reenrollment ($script:mockSnapshot.learners[1].Statut -eq 'Actif') 'Le redoublement explicite n''a pas activé le dossier.'
$script:mockSnapshot.learners[0].Statut='Suspendu'
Assert-ReenrollmentReject {Invoke-ScoliaReenrollment $plan.Id} 'Un résultat déjà appliqué et ensuite modifié a été écrasé.'

Reset-ReenrollmentTest
$plan=New-ScoliaReenrollmentPlan @([pscustomobject]@{Matricule='INF102';Decision='Maintien';TargetPromotion='INF1'}) $script:mockSnapshot
Assert-Reenrollment ($plan.Entries[0].TargetStatus -eq 'Suspendu') 'Le maintien a changé implicitement le statut.'
Assert-ReenrollmentReject {New-ReenrollmentTestPlan 'Passage' 'INF1'} 'Un passage sans changement de promotion a été accepté.'
Assert-ReenrollmentReject {New-ReenrollmentTestPlan 'Inconnue' 'INF2'} 'Une décision inconnue a été acceptée.'
$script:mockSnapshot.learners[0].Annee='2026-2027'
Assert-ReenrollmentReject {New-ReenrollmentTestPlan} 'Un dossier historique a été migré sans préparation annuelle.'
'PASS : réinscription réelle, identité et contacts conservés, paiements protégés, droits et contexte, collisions, journal avant écriture, reprise idempotente et traitement partiel.'
