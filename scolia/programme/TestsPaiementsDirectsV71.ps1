$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -le 5){Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}else{Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs')}
. (Join-Path $PSScriptRoot 'PaiementsDirectsV71.ps1')
. (Join-Path $PSScriptRoot 'TypographieV71.ps1')
function Key($text){return ([string]$text).Trim().ToUpperInvariant()}
function Get-ScoliaImportedLearners{return [pscustomobject]@{Matricule='COM001'}}
$script:scoliaSession=[pscustomobject]@{Role='Secrétariat';AccountId='test'};$script:scolia=[pscustomobject]@{Year='2026-2027';Id=([guid]::NewGuid().ToString('N'))};$script:book=$true
$script:data=[pscustomobject]@{closed=$false;learners=@([pscustomobject]@{Matricule='SEC001';Annee='2026-2027';Statut='Actif'},[pscustomobject]@{Matricule='AUTRE';Annee='2025-2026';Statut='Actif'},[pscustomobject]@{Matricule='SORTI';Annee='2026-2027';Statut='Sorti'})}
if(@(Get-ScoliaPaymentLearners).Count -ne 1 -or (Get-ScoliaPaymentLearners).Matricule -ne 'SEC001' -or (Get-ScoliaPaymentAccount) -ne 'Secrétariat'){throw 'Paiement direct du secrétariat incorrect.'}
$script:scoliaSession.Role='Comptabilité'
if((Get-ScoliaPaymentLearners).Matricule -ne 'COM001' -or (Get-ScoliaPaymentAccount) -ne 'Comptabilité'){throw 'Répertoire du compte comptable altéré.'}
function Api-Name($name){return $name}
$script:bridge='SCOLIA-FINANCE-2'
function Invoke-ITMExcelRun($excel,$macro,$args){if($script:bridge -eq 'absent'){throw 'Macro de version absente'};return $script:bridge}
$entry=[pscustomobject]@{Id=('a'*32);Reference='SC-20261013-EXEMPLE';CommonReceipt='1';Revision='';Date='2026-10-13';Parts=@([pscustomobject]@{Mois='Septembre';Montant=[decimal]50})}
$modern=Get-ScoliaFinanceImportArguments $entry @('INF001','Nom') $false
if($modern.Count -ne 8 -or $modern[7] -cne $entry.Reference){throw 'Import moderne sans référence commune.'}
$script:bridge='absent';$legacy=Get-ScoliaFinanceImportArguments $entry @('INF001','Nom') $false
if($legacy.Count -ne 6 -or $legacy[0] -cne $modern[0] -or $legacy[2] -cne 'Septembre' -or $legacy[3] -cne '50'){throw 'Import historique ou identité de reprise incorrecte.'}
$entry.Revision='1';$rejected=$false;try{[void](Get-ScoliaFinanceImportArguments $entry @('INF001') $false)}catch{$rejected=$true};if(-not $rejected){throw 'Correction appliquée sur une passerelle incompatible.'}
$receipt=[pscustomobject]@{OperatorId='test';CommonReceipt='1';Compte='Comptabilité'}
if(-not (Test-ScoliaReceiptAutoRecovery $receipt $null)){throw 'Paiement propre en attente non récupérable.'}
$errorState=[pscustomobject]@{Status='Erreur';Message='La passerelle Excel doit être mise à jour.'}
if(-not (Test-ScoliaReceiptAutoRecovery $receipt $errorState)){throw 'Ancienne erreur de passerelle non récupérable.'}
$errorState.Message='Identité ambiguë';if(Test-ScoliaReceiptAutoRecovery $receipt $errorState){throw 'Erreur métier relancée automatiquement.'}
$receipt.OperatorId='autre';if(Test-ScoliaReceiptAutoRecovery $receipt $null){throw 'Paiement dʼun autre compte importé sans préférence.'}
# Exécuter le véritable import initial sur la passerelle historique simulée.
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaIntegration.ps1'),[ref]$t,[ref]$e)
$n=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Import-ScoliaReceipt'},$true);. ([scriptblock]::Create($n.Extent.Text))
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('scolia-compat-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($testRoot)
$script:scolia.Id=[guid]::NewGuid().ToString('N');$script:store=$null;$script:importCalls=0;$script:applied=@{}
function Assert-ITMPermission($permission){}
function Test-ScoliaOutgoingReceipt($entry){return $false}
function Test-ScoliaReceiptYear($entry){return $true}
function Get-ScoliaStateRoot{return $testRoot}
function Test-ScoliaApplied($entry,$state){return $script:applied.ContainsKey($entry.Id)}
function Find-ScoliaLearner($entry){return [pscustomobject]@{Matricule='SEC001'}}
function Invoke-ITMJsonDirect($macro,$payload){$script:importCalls++;if($payload.Count -ne 6){throw 'Passerelle historique appelée avec huit arguments.'};return [pscustomobject]@{ok=$true;matricule='SEC001'}}
function Save-ScoliaAppliedReceipt($entry){$script:applied[$entry.Id]=$entry.Reference}
function Set-ScoliaReceiptState{}
function Update-ITMAfterWrite{}
function Write-ITMUserAudit{}
$payment=New-Object ScoliaEntry;$payment.Id=[guid]::NewGuid().ToString('N');$payment.SchoolId=$script:scolia.Id;$payment.Kind='Paiement';$payment.CommonReceipt='1';$payment.Reference='SC-TEST-UNIQUE';$payment.Matricule='SEC001';$payment.Nom='TEST';$payment.PostNom='APPRENANT';$payment.Prenom='Jean';$payment.Promotion='INF1';$payment.Annee='2026-2027';$payment.Date='2026-10-13';$payment.Montant=50;$part=New-Object ScoliaPart;$part.Mois='Septembre';$part.Montant=50;$payment.Parts.Add($part)
try{
 [void](Import-ScoliaReceipt $payment $false);[void](Import-ScoliaReceipt $payment $false)
 if($script:importCalls -ne 1 -or $script:applied[$payment.Id] -cne $payment.Reference){throw 'Reprise créant un doublon ou perdant la référence commune.'}
}finally{[IO.Directory]::Delete($testRoot,$true)}
foreach($size in @(9,10,11,12,14)){$script:displayPreferences=[pscustomobject]@{TextSize=$size};if((Get-ScoliaInterfaceTextSize) -ne $size){throw 'Taille globale non respectée.'}}
$script:displayPreferences=[pscustomobject]@{TextSize=99};if((Get-ScoliaInterfaceTextSize) -ne 10){throw 'Taille de repli incorrecte.'}
'PASS : paiement direct, import à 6 ou 8 arguments, UUID inchangé, reprise autorisée uniquement, correction protégée et taille unique.'
