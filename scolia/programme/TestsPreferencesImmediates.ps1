$ErrorActionPreference='Stop'
# Native Windows test, no Excel or real registry writes.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
foreach($name in @('Get-ITMDefaultDisplayPreferences','Set-ITMGridAppearance','Set-ITMDisplayPreferenceFields','Set-ITMLiveDisplayPreferences')){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PreferencesAffichage.ps1'),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide.'}
    $definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw $name};. ([scriptblock]::Create($definition.Extent.Text))
}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Lisibilite.ps1'),[ref]$tokens,[ref]$errors)
$definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Set-ITMGridReadability'},$true)
. ([scriptblock]::Create($definition.Extent.Text))
$script:gridReadabilityBusy=New-Object 'System.Collections.Generic.HashSet[System.Windows.Forms.DataGridView]'
$script:displayGridFonts=@{};$script:displayFieldsSync=$false;$script:displayChanging=$false;$script:busy=$false
$script:displayPreferences=Get-ITMDefaultDisplayPreferences
foreach($field in @('displayRibbon','displayTheme','displaySize','displayDensity','displayWindowMode')){Set-Variable -Scope Script -Name $field -Value ([pscustomobject]@{SelectedItem='';Text=''})}
foreach($field in @('displayTips','displayAlternating','displayMaximized')){Set-Variable -Scope Script -Name $field -Value ([pscustomobject]@{Checked=$false})}
$script:displayNewWindow=[pscustomobject]@{Enabled=$false}
$script:status=[pscustomobject]@{Text=''};$script:saveCount=0;$script:applyCount=0;$script:failSave=$false
function Save-ITMDisplayPreferences {if($script:failSave){throw 'Écriture refusée.'};$script:saveCount++}
function Apply-ITMDisplayPreferences {$script:applyCount++;Set-ITMGridAppearance $script:testGrid}
$script:main=New-Object Windows.Forms.Form;$script:main.Size=New-Object Drawing.Size(600,400)
$g=New-Object Windows.Forms.DataGridView;$g.Dock='Fill';$g.AllowUserToAddRows=$false;$g.SelectionMode='FullRowSelect';$g.MultiSelect=$false
[void]$g.Columns.Add('Nom','Nom');$g.Columns[0].Width=250
for($i=0;$i -lt 30;$i++){[void]$g.Rows.Add(('Dossier '+$i))}
$script:testGrid=$g;$script:main.Controls.Add($g)
try{
    $script:main.Show();[Windows.Forms.Application]::DoEvents();$g.CurrentCell=$g.Rows[5].Cells[0]
    $p=Get-ITMDefaultDisplayPreferences;$p.TextSize=9;$p.Density='Compact';Set-ITMLiveDisplayPreferences $p
    $small=$g.Rows[5].Height
    $p=Get-ITMDefaultDisplayPreferences;$p.TextSize=14;$p.Density='Aéré';Set-ITMLiveDisplayPreferences $p
    if($g.DefaultCellStyle.Font.SizeInPoints -ne 14 -or $g.Rows[5].Height -le $small){throw 'Taille ou espacement sans effet immédiat.'}
    if($g.CurrentRow.Cells[0].Value -ne 'Dossier 5'){throw 'Sélection perdue.'}
    # Rebinding/readability and resizing must preserve the chosen font and padding.
    $g.Font=New-Object Drawing.Font('Segoe UI',9);Set-ITMGridReadability $g
    if($g.Font.SizeInPoints -ne 14 -or $g.DefaultCellStyle.Padding.Top -ne 7){throw 'Préférence écrasée par lisibilité.'}
    $g.Width=450;Set-ITMGridReadability $g
    if($g.Font.SizeInPoints -ne 14){throw 'Préférence écrasée au redimensionnement.'}
    $before=$script:displayPreferences;$script:failSave=$true;$failed=$false
    $p=Get-ITMDefaultDisplayPreferences
    try{Set-ITMLiveDisplayPreferences $p}catch{$failed=$true}
    if(-not $failed -or -not [object]::ReferenceEquals($before,$script:displayPreferences) -or $script:displaySize.SelectedItem -ne '14 pt'){throw 'Choix précédent non restauré.'}
    if($script:displayChanging -or $script:displayFieldsSync){throw 'Verrou de synchronisation non libéré.'}
    $script:failSave=$false;$script:busy=$true;$count=$script:saveCount
    Set-ITMLiveDisplayPreferences (Get-ITMDefaultDisplayPreferences)
    if($script:saveCount -ne $count){throw 'Écriture pendant un traitement.'};$script:busy=$false
    $p=Get-ITMDefaultDisplayPreferences;$p.Maximized=$true;Set-ITMLiveDisplayPreferences $p
    if($script:main.WindowState -ne 'Maximized'){throw 'Maximisation retardée.'}
    Set-ITMLiveDisplayPreferences (Get-ITMDefaultDisplayPreferences)
    if($script:main.WindowState -ne 'Normal'){throw 'Retour à la fenêtre normale absent.'}
    $count=$script:saveCount;Set-ITMLiveDisplayPreferences (Get-ITMDefaultDisplayPreferences)
    if($count -ne $script:saveCount){throw 'Enregistrement identique répété.'}
    $script:displayFieldsSync=$true;$count=$script:saveCount;$p=Get-ITMDefaultDisplayPreferences;$p.TextSize=12;Set-ITMLiveDisplayPreferences $p
    if($count -ne $script:saveCount){throw 'Initialisation ou synchronisation enregistrée.'};$script:displayFieldsSync=$false
}finally{$script:main.Close();$script:main.Dispose();foreach($font in $script:displayGridFonts.Values){$font.Dispose()}}
Write-Output 'Application immédiate, lignes existantes, taille persistante après redimensionnement, sélection, reprise après refus, maximisation et protection contre récursion vérifiées.'
