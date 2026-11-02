param()
$ErrorActionPreference='Stop'
# Isolated test: no Excel instance, workbook write or real registry change.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Application.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Syntaxe Application.ps1 invalide.'}
foreach($name in @('Set-Grid','Save-ConnectionChoice')){
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw ('Fonction absente : '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Set-ITMGridReadability($grid) {}
function New-Item {param($Path,[switch]$Force,$ErrorAction)}
function New-ItemProperty {param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force,$ErrorAction)}
$script:connectionKey='TEST_ONLY';$script:autoChoice='non';$script:preferenceWrites=0
function Get-ITMLocationPreference($name){return $script:autoChoice}
function Set-ITMLocationPreference($name,$value){$script:autoChoice=$value;$script:preferenceWrites++;return $true}
if(-not (Save-ConnectionChoice (Join-Path $env:TEMP 'itm-test.xlsm'))){throw 'Mémorisation simulée refusée.'}
if($script:autoChoice -ne 'non' -or $script:preferenceWrites -ne 0){throw 'La connexion manuelle a modifié le choix automatique.'}
$script:autoChoice=''
if(-not (Save-ConnectionChoice (Join-Path $env:TEMP 'itm-test.xlsm')) -or $script:autoChoice -ne 'oui'){throw 'Réglage initial absent.'}
$form=New-Object Windows.Forms.Form;$grid=New-Object Windows.Forms.DataGridView
$grid.ReadOnly=$true;$grid.MultiSelect=$false;$grid.AllowUserToAddRows=$false;$grid.SelectionMode='FullRowSelect';$grid.Dock='Fill';$form.Controls.Add($grid)
$columns=@(@('Matricule','Matricule'),@('Nom','Nom'))
try{
    $form.Show();[Windows.Forms.Application]::DoEvents()
    Set-Grid $grid @([pscustomobject]@{Matricule='A';Nom='Alice'},[pscustomobject]@{Matricule='B';Nom='Bob'}) $columns
    $grid.CurrentCell=$grid.Rows[1].Cells[0];$grid.Rows[1].Selected=$true
    Set-Grid $grid @([pscustomobject]@{Matricule='B';Nom='Bob modifié'},[pscustomobject]@{Matricule='A';Nom='Alice'}) $columns
    if($grid.SelectedRows.Count -ne 1 -or [string]$grid.SelectedRows[0].Cells['Matricule'].Value -ne 'B'){throw 'Identité perdue après actualisation.'}
    Set-Grid $grid @([pscustomobject]@{Matricule='A';Nom='Alice'}) $columns
    if($grid.SelectedRows.Count -ne 0){throw 'Un autre dossier a été sélectionné après filtrage.'}
    $grid.CurrentCell=$grid.Rows[0].Cells[0];$grid.Rows[0].Selected=$true
    Set-Grid $grid @([pscustomobject]@{Matricule='A';Nom='Alice'},[pscustomobject]@{Matricule='A';Nom='Doublon'}) $columns
    if($grid.SelectedRows.Count -ne 0){throw 'Une identité ambiguë a été sélectionnée.'}
    Write-Output 'Revue : mémorisation et sélection des listes validées.'
}finally{if($grid.DataSource -is [Data.DataTable]){$grid.DataSource.Dispose()};$form.Close();$form.Dispose()}
