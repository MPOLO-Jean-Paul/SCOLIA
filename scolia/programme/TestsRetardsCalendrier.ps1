$ErrorActionPreference='Stop'
function Import-TestFunction([string]$file,[string]$name){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
 $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw $name};. ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($n in @('Due-Months','Refresh-Dashboard')){Import-TestFunction 'Application.ps1' $n}
Add-Type -Path (Join-Path $PSScriptRoot 'PrisesEnCharge.cs')
function Get-ITMPECRecord($a){if($script:covered){return $true};return $null}
function Learner-Rows {return $script:people}
function Promo($s){return $s}
function Money($n){return [decimal]$n}
function Paid($mat,$month){if($month){return [decimal]$script:paid[$month]};return [decimal](@($script:paid.Values)|Measure-Object -Sum).Sum}
function Get-ITMPECDue($a,$m){if($script:covered){return [decimal]0};return [Math]::Max(0,100-(Paid $a.Matricule $m))}
function Get-ITMPECPayingBalance {return 900}
function Set-Grid {}
function Update-ITMPECDashboard {}
function Update-ITMDashboardVisuals {}
$person=[pscustomobject]@{Matricule='A1';Annee='2026 - 2027';Eligible=$true;Promotion='INF1';Nom='Alpha';PostNom='Beta';Prenom='Gamma';Contact1='';Contact2=''}
foreach($case in @(@('2026-10-05',0),@('2026-10-06',1),@('2026-11-05',1),@('2026-11-06',2),@('2027-01-06',4),@('2027-06-06',9))){
 $actual=@(Due-Months $person ([datetime]$case[0]));if($actual.Count -ne $case[1]){throw "Seuil incorrect $($case[0]): $($actual.Count)"}
}
$script:today=[datetime]'2026-11-06';function Get-Date {return $script:today}
$script:data=[pscustomobject]@{issues=@()};$script:people=@($person);$script:fees=@{'INF1'=[pscustomobject]@{Mensuel=100}}
$script:dashPromo=[pscustomobject]@{Text='Toutes les promotions'};$script:dashMonth=[pscustomobject]@{Text='Tous les mois'};$script:dashLate=[pscustomobject]@{Text='Tous'}
$script:dashNote=[pscustomobject]@{Text=''};$script:cardValues=@(1..4|ForEach-Object {[pscustomobject]@{Text=''}});$script:fr=[Globalization.CultureInfo]::GetCultureInfo('fr-FR');$script:paid=@{Septembre=100;Octobre=40}
Refresh-Dashboard
if($script:lateRows.Count -ne 1 -or $script:lateRows[0].MoisEnRetard -ne 1 -or $script:lateRows[0].Periode -ne 'Octobre' -or $script:lateRows[0].Dette -ne 60){throw 'Mois payé ou solde partiel mal classé'}
$script:dashMonth.Text='Novembre';Refresh-Dashboard;if($script:lateRows.Count -ne 0){throw 'Mois courant déclaré en retard'}
$script:paid.Octobre=100;$script:dashMonth.Text='Tous les mois';Refresh-Dashboard;if($script:lateRows.Count -ne 0){throw 'Mois soldé encore déclaré en retard'}
'PASS seuil du 6, année civile, paiement complet/partiel, libellés des seuls mois impayés, filtre du mois courant'

$script:covered=$true;$script:paid=@{};$script:today=[datetime]'2027-07-06'
if(@(Due-Months $person $script:today).Count -ne 10){throw 'Dixième mois des prises en charge absent'}
Refresh-Dashboard;if($script:lateRows.Count -ne 0){throw 'Prise en charge présentée comme un impayé'}
'PASS dix mois des prises en charge et exclusion des mensualités couvertes'
