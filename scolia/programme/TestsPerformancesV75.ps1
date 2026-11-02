$ErrorActionPreference='Stop'
function Import-PerformanceFunction([string]$File,[string]$Name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($errors | Out-String)}
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name},$true)
    if(-not $node){throw ('Fonction absente: '+$Name)}
    . ([scriptblock]::Create('function global:'+$Name+$node.Extent.Text.Substring(('function '+$Name).Length)))
}
function Assert-Performance($Condition,[string]$Message){if(-not $Condition){throw $Message}}
Import-PerformanceFunction 'Application.ps1' 'Key'
function Legacy-Key([object]$value){
    $t=([string]$value).Trim().ToUpperInvariant().Normalize([Text.NormalizationForm]::FormD)
    return (-join ($t.ToCharArray() | Where-Object {[Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne [Globalization.UnicodeCategory]::NonSpacingMark})).Normalize([Text.NormalizationForm]::FormC)
}
foreach($sample in @($null,'','  Jean-Paul  ','école','E'+[char]0x301,'TLM1','001234','İstanbul','Straße','ÉCOLE','Élève – 001','中文')){
    Assert-Performance ((Key $sample) -ceq (Legacy-Key $sample)) ('Normalisation différente: '+$sample)
}
for($i=0;$i -lt 16500;$i++){[void](Key ('Dossier'+$i))}
Assert-Performance ($script:scoliaTextKeys.Count -le 16384) 'Cache texte non borné'
[void](Key 'Élève – 001')
$old=Measure-Command {for($i=0;$i -lt 3000;$i++){[void](Legacy-Key 'Élève – 001')}}
$new=Measure-Command {for($i=0;$i -lt 3000;$i++){[void](Key 'Élève – 001')}}
'PASS normalisation Unicode, clés ordinales, limite mémoire'
'BENCH texte, 3000 appels: ancien={0:N1} ms; nouveau={1:N1} ms' -f $old.TotalMilliseconds,$new.TotalMilliseconds

# Ribbon tests use in-memory controls, never Excel or real school data.
foreach($name in @('Clear-ScoliaRibbonViews','Get-ScoliaRibbonContext','Update-ITMRibbonState')){Import-PerformanceFunction 'StyleModerne.ps1' $name}
$script:scoliaRibbonViews=@{};$script:buttonCommands=@{};$script:groupsBuilt=0;$script:groupsDisposed=0;$script:allowed=$true
$script:main=[pscustomobject]@{CurrentAutoScaleDimensions=[pscustomobject]@{Height=96}}
$script:ribbonTips=[pscustomobject]@{Active=$true}
$script:displayPreferences=[pscustomobject]@{Theme='Clair';TextSize=10;Density='Confortable';Tips=$true}
$script:book=$true;$script:data=[pscustomobject]@{closed=$false}
$script:accountSession=[pscustomobject]@{Id='test';Role='Secrétariat'}
$script:ribbonFlow=[pscustomobject]@{Controls=(New-Object System.Collections.ArrayList)}
$script:ribbonFlow | Add-Member ScriptMethod SuspendLayout {}
$script:ribbonFlow | Add-Member ScriptMethod ResumeLayout {param($Perform)}
function Test-ITMPermission {param($Permission);return $script:allowed}
function Update-ITMFrequentCommands {}
function Update-ITMNavigation {}
function Update-ScoliaActiveWorkspaceCommand {}
function Remove-ITMThemeRecords {param($Control)}
function New-ITMCommand {
    param($Label,$Target,$Action,$Book=$true,$Write=$false,$Primary=$false)
    return [pscustomobject]@{Label=$Label;Permission='Lire';Target=$Target;Book=$Book;Write=$Write;Action=$Action}
}
function Add-ITMRibbonGroup {
    param($Title,$Commands)
    $script:groupsBuilt++
    $group=[pscustomobject]@{IsDisposed=$false;Controls=@($Commands | ForEach-Object {[pscustomobject]@{Tag=$_;Enabled=$true}})}
    $group | Add-Member ScriptMethod Dispose {$this.IsDisposed=$true;$script:groupsDisposed++}
    [void]$script:ribbonFlow.Controls.Add($group)
}
$script:currentPage='Apprenants';Update-ITMRibbonState
$first=$script:ribbonFlow.Controls[0]
$script:currentPage='Paiements';Update-ITMRibbonState
$count=$script:groupsBuilt
$script:currentPage='Apprenants';Update-ITMRibbonState
Assert-Performance ($script:groupsBuilt -eq $count) 'Ruban reconstruit au retour sur un onglet'
Assert-Performance ([object]::ReferenceEquals($first,$script:ribbonFlow.Controls[0])) 'Groupes non réutilisés'
$script:busy=$true;Update-ITMRibbonState
foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){Assert-Performance (-not $button.Enabled) 'Commande active pendant une écriture'}}
$script:busy=$false;$script:allowed=$false;Update-ITMRibbonState
foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){Assert-Performance (-not $button.Enabled) 'Droits ignorés par le cache'}}
$script:allowed=$true;$script:data.closed=$true;Update-ITMRibbonState
foreach($group in $script:ribbonFlow.Controls){foreach($button in $group.Controls){if($button.Tag.Write){Assert-Performance (-not $button.Enabled) 'Écriture active sur année clôturée'}}}
$script:data.closed=$false
$script:displayPreferences.Theme='Sombre';Update-ITMRibbonState
Assert-Performance ($script:groupsBuilt -gt $count) 'Thème changé sans invalidation'
$count=$script:groupsBuilt;$script:ribbonPage='';Update-ITMRibbonState
Assert-Performance ($script:groupsBuilt -gt $count) 'Reconstruction explicite ignorée'
$count=$script:groupsBuilt;$script:accountSession.Role='Consultation';Update-ITMRibbonState
Assert-Performance ($script:groupsBuilt -gt $count) 'Changement de rôle sans invalidation'
$script:currentPage='Paiements';Update-ITMRibbonState
$script:currentPage='Apprenants';Update-ITMRibbonState
$count=$script:groupsBuilt
$repeat=Measure-Command {for($i=0;$i -lt 100;$i++){$script:currentPage=$(if($i%2){'Apprenants'}else{'Paiements'});Update-ITMRibbonState}}
Assert-Performance ($script:groupsBuilt -eq $count) 'Reconstructions lors des navigations répétées'
'PASS cache ruban, droits, activité, clôture, thème, rôle, reconstruction forcée'
'BENCH rubans simulés: 100 changements={0:N1} ms; 0 groupe reconstruit' -f $repeat.TotalMilliseconds
Clear-ScoliaRibbonViews

# Dashboard business results remain identical; calendar is evaluated once per learner.
Import-PerformanceFunction 'Application.ps1' 'Refresh-Dashboard'
$script:scoliaSession=$null;$script:loading=$false;$script:calendarCalls=0;$script:dueCalls=0
$script:data=[pscustomobject]@{issues=@()}
$script:dashPromo=[pscustomobject]@{Text='Toutes les promotions'}
$script:dashMonth=[pscustomobject]@{Text='Tous les mois'}
$script:dashLate=[pscustomobject]@{Text='Tous'}
$script:dashNote=[pscustomobject]@{Text=''};$script:fr=[Globalization.CultureInfo]::GetCultureInfo('fr-FR')
$script:cardValues=@(1..4 | ForEach-Object {[pscustomobject]@{Text=''}})
$script:fees=@{TLM1=[pscustomobject]@{Mensuel=100}}
function Learner-Rows {param($Search,$Promotion);return [pscustomobject]@{Matricule='000001';Nom='Test';PostNom='';Prenom='';Promotion='TLM1';Eligible=$true;Contact1='';Contact2=''}}
function Promo {param($Value);return $Value}
function Money {param($Value);return [string]$Value}
function Paid {param($Mat,$Month='');return 50}
function Due-Months {param($Person);$script:calendarCalls++;return @('Septembre','Octobre')}
function Get-ITMPECDue {param($Person,$Month);$script:dueCalls++;if($Month -eq 'Septembre'){return 0};return 100}
function Get-ITMPECPayingBalance {param($Person);return 500}
function Set-Grid {param($Grid,$Rows,$Columns)}
function Update-ITMPECDashboard {param($People)}
function Update-ITMDashboardVisuals {param($People,$Debts)}
Refresh-Dashboard
Assert-Performance ($script:calendarCalls -eq 1 -and $script:dueCalls -eq 2) 'Calculs dupliqués'
Assert-Performance ($script:lateRows.Count -eq 1 -and $script:lateRows[0].DetteValeur -eq 100 -and $script:lateRows[0].MoisEnRetard -eq 1) 'Dette ou retards modifiés'
Assert-Performance ($script:dashboardDate -eq (Get-Date).ToString('yyyyMMdd')) 'Date du tableau de bord non mémorisée'
$script:dashMonth.Text='Septembre';Refresh-Dashboard
Assert-Performance ($script:lateRows.Count -eq 0) 'Filtre mensuel incorrect'
'PASS montants, mois de retard, filtre mensuel, date du tableau de bord'
