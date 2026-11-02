$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReferencesRecusV69.ps1')
if($PSVersionTable.PSVersion.Major -le 5){Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}else{Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs')}
$seen=New-Object 'System.Collections.Generic.HashSet[string]'
for($i=0;$i -lt 1000;$i++){$identity=New-ScoliaReceiptIdentity;if(-not $seen.Add($identity.Reference)){throw 'Référence répétée.'}}
$receiptIdentity=New-ScoliaReceiptIdentity
$same=New-ScoliaReceiptIdentity -OperationId $receiptIdentity.Id
if($same.Reference -cne $receiptIdentity.Reference){throw 'Référence non reproductible pour la même opération.'}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaIntegration.ps1'),[ref]$tokens,[ref]$errors)
$show=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Show-ScoliaEntry'},$true)
$assignment=$show.Find({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$buildEntry'},$true)
$expression=$assignment.Right.Find({param($n)$n -is [Management.Automation.Language.ScriptBlockExpressionAst]},$true)
$build=$expression.ScriptBlock.GetScriptBlock()
function Assert-ITMPermission($permission){}
function Get-ScoliaPaymentAccount {return 'Comptabilité'}
function Get-ScoliaTypedAmount($text,$maximum){return [decimal]$text}
$expense=$false;$fields=@{};$learnerFields=@('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Adresse')
$cashLearners=@([pscustomobject]@{Matricule='INF001';Promotion='INF1';Nom='TEST';PostNom='APPRENANT';Prenom='Jean';Sexe='M';Naissance='2000-01-01';Lieu='Lubumbashi';Contact1='0990000000';Contact2='';Adresse='Adresse conservée'})
$selector=[pscustomobject]@{SelectedIndex=0};$reference=[pscustomobject]@{Text='Texte externe ignoré'};$date=[pscustomobject]@{Value=[datetime]::Today};$amount=[pscustomobject]@{Text='50';Maximum=100000000000;Value=0}
$category=[pscustomobject]@{SelectedIndex=0};$feeOptions=@([pscustomobject]@{Code='SCOLARITE';Name='Scolarité'})
$script:scolia=[pscustomobject]@{Year='2026-2027';Id=([guid]::NewGuid().ToString('N'))};$script:scoliaSession=[pscustomobject]@{AccountId=([guid]::NewGuid().ToString('N'));Name='Compte test';Revision=1}
$first=& $build;$retry=& $build
if($first.Id -cne $receiptIdentity.Id -or $first.Reference -cne $receiptIdentity.Reference -or $retry.Id -cne $first.Id -or $retry.Reference -cne $first.Reference){throw 'Identité changée lors de la validation ou reprise.'}
if($first.Contact1 -ne '0990000000' -or $first.Sexe -ne 'M' -or $first.Matricule -ne 'INF001' -or $first.Montant -ne 50){throw 'Dossier ou montant incomplet dans la vue compacte.'}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('scolia-reference-'+[guid]::NewGuid().ToString('N'))
try{
 $file=[ScoliaLedger]::SaveDraft($temp,$script:scolia.Id,$first);$restored=[ScoliaLedger]::ReadDraft($file,$script:scolia.Id)
 if($restored.Reference -cne $first.Reference -or $restored.Id -cne $first.Id){throw 'Brouillon perdant sa référence.'}
}finally{if([IO.Directory]::Exists($temp)){[IO.Directory]::Delete($temp,$true)}}
'PASS : 1000 références distinctes, constructeur réel, reprise stable, identité complète et référence du brouillon conservée.'
