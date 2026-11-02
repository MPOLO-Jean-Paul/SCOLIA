function Get-ScoliaPaymentLearners {
 if($script:scoliaSession.Role -in @('Secrétariat','Mixte') -and $script:book -and $script:data){
  if($script:data.closed){throw 'Cette année scolaire est clôturée.'}
  $year=[ScoliaSharing]::YearKey($script:scolia.Year)
  $learners=@($script:data.learners|Where-Object {[ScoliaSharing]::YearKey($_.Annee) -eq $year -and (Key $_.Statut) -notin @('SORTI','SORTIE','SUSPENDU','SUSPENDUE')})
  return @($learners|Sort-Object Promotion,Nom,PostNom,Prenom,Matricule)
 }
 return @(Get-ScoliaImportedLearners)
}
function Get-ScoliaPaymentAccount {
 if($script:scoliaSession.Role -in @('Secrétariat','Mixte') -and $script:book -and $script:data){return 'Secrétariat'}
 return 'Comptabilité'
}
function Get-ScoliaFinanceImportArguments($Entry,[string[]]$Packed,[bool]$Create){
 $modern=$false
 try{$modern=([string](Invoke-ITMExcelRun $script:excel (Api-Name 'ITMAppScoliaFinanceVersion') @()) -ceq 'SCOLIA-FINANCE-2')}catch{}
 $args=@($Entry.Id,($Packed -join "`t"),(@($Entry.Parts|ForEach-Object {$_.Mois}) -join '|'),(@($Entry.Parts|ForEach-Object {$_.Montant.ToString('0',[Globalization.CultureInfo]::InvariantCulture)}) -join '|'),$Entry.Date,$Create)
 if($modern){$args+=@([int]$Entry.Revision,$(if($Entry.CommonReceipt -eq '1'){$Entry.Reference}else{''}))}
 elseif($Entry.Revision){throw 'Une correction de reçu nécessite la passerelle Finance 2. Le reçu initial reste conservé.'}
 # La passerelle historique importe avec six arguments et journalise le même UUID.
 # La référence commune reste également conservée dans la pièce locale et partagée.
 return ,$args
}
function Test-ScoliaReceiptAutoRecovery($Entry,$State){
 if([string]::IsNullOrWhiteSpace([string]$Entry.OperatorId) -or [string]$Entry.OperatorId -cne [string]$script:scoliaSession.AccountId){return $false}
 if($State -and $State.Status -eq 'Erreur'){return ([string]$State.Message -match 'passerelle Excel doit être mise à jour')}
 return ($Entry.CommonReceipt -eq '1')
}
