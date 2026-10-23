$ErrorActionPreference='Stop'
Add-Type -TypeDefinition 'public static class ScoliaSharing { public static string YearKey(string s){return s.Replace(" ","");}} public static class ScoliaFinance {public static string CategoryKey(object e){return "SCOLARITE";}}'
function Load-TestFunction($file,$name){
 $t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$t,[ref]$e)
 $fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create('function global:'+ $name + $fn.Extent.Text.Substring(('function '+$name).Length)))
}
Load-TestFunction 'Application.ps1' 'Key'
foreach($name in @('Initialize-ScoliaFinanceIndex','Get-ScoliaFinanceRows')){Load-TestFunction 'ScoliaIntegration.ps1' $name}
. (Join-Path $PSScriptRoot 'EtatEspace.ps1')
function Money($v){return [string]$v}
function Check($ok,$why){if(-not $ok){throw $why}}
$script:scolia=[pscustomobject]@{Year='2026-2027'}
$script:scoliaEntries=@(
 [pscustomobject]@{Id='a';Annee='2026 - 2027';Date='2026-10-14';Reference='R1';Nom='Émile';PostNom='TEST';Prenom='Jean';Promotion='INF1';Description='Scolarité';Kind='Paiement';OperatorName='Agent';Montant=20;Parts=@();CategoryName='Scolarité';Cancelled='';Revision=''},
 [pscustomobject]@{Id='b';Annee='2026-2027';Date='2026-10-12';Reference='D1';Nom='';PostNom='';Prenom='';Promotion='';Description='Papier';Kind='Depense';OperatorName='Agent';Montant=5;Parts=@();CategoryName='';Cancelled='';Revision=''},
 [pscustomobject]@{Id='c';Annee='2025-2026';Date='2025-10-14';Reference='R2';Nom='Autre';Kind='Paiement';Montant=100}
)
foreach($query in @('','emile inf1','papier','agent','emile inf2','   ')){
 $script:scoliaFinanceQuery=$query;$actual=@(Get-ScoliaFinanceRows)
 $expected=@($script:scoliaEntries|Where-Object {([ScoliaSharing]::YearKey($_.Annee)) -eq '2026-2027' -and (Test-ITMFinanceSearch $_ $query)}|Sort-Object Date,Reference,Id)
 Check (($actual.Id -join ',') -ceq ($expected.Id -join ',')) ('Filter differs: '+$query)
}
Check ($script:scoliaFinanceIndex.Income -eq 20 -and $script:scoliaFinanceIndex.Expense -eq 5) 'Totals differ'
$old=$script:scoliaFinanceIndex;$script:scoliaFinanceQuery='';[void](Get-ScoliaFinanceRows);Check ([object]::ReferenceEquals($old,$script:scoliaFinanceIndex)) 'Index rebuilt on typing'
$script:scoliaEntries=@([pscustomobject]@{Id='d';Annee='2026-2027';Date='2026-10-14';Reference='R3';Nom='Émile';Kind='Paiement';Montant=30;Cancelled='1';Revision='2';Parts=@()})
$rows=@(Get-ScoliaFinanceRows);Check ($rows[0].Etat -ceq 'Annulé' -and $script:scoliaFinanceIndex.Income -eq 30) 'Replacement/correction stale'
$script:scolia.Year='2025-2026';Check (@(Get-ScoliaFinanceRows).Count -eq 0 -and $script:scoliaFinanceIndex.Income -eq 0) 'Year change reused old totals'
'PASS finance index: equivalence to original search, accents/multiple terms, order, totals, reused index, correction and year invalidation.'
