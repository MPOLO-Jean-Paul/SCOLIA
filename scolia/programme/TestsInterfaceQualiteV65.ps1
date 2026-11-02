$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
function Get-ITMTextHeight($Control,[int]$Width,[int]$Minimum){return [Math]::Max($Minimum,[Windows.Forms.TextRenderer]::MeasureText($Control.Text,$Control.Font,(New-Object Drawing.Size($Width,10000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height+10)}
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
. (Join-Path $PSScriptRoot 'InterfaceQualiteV65.ps1')
$script:main=$null
$form=New-Object Windows.Forms.Form;$form.Size=New-Object Drawing.Size(1800,1200);$form.MinimumSize=New-Object Drawing.Size(1600,1000)
$context=[pscustomobject]@{Identity='test';Revision=3};$form.Tag=$context
$table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Fill';$table.ColumnCount=2;$table.RowCount=6;$form.Controls.Add($table)
$boxes=@()
for($i=0;$i -lt 6;$i++){
 $label=New-Object Windows.Forms.Label;$label.Text='Champ avec un intitulé administratif complet';$label.Dock='Fill';$table.Controls.Add($label,0,$i)
 $box=New-Object Windows.Forms.TextBox;$box.Text='Valeur préservée '+$i;$box.Dock='Fill';$table.Controls.Add($box,1,$i);$boxes+=$box
}
try{
 Enable-ScoliaProfessionalWindow $form;$form.Show();[Windows.Forms.Application]::DoEvents()
 $area=[Windows.Forms.Screen]::FromControl($form).WorkingArea
 if($form.Left -lt $area.Left -or $form.Top -lt $area.Top -or $form.Right -gt $area.Right -or $form.Bottom -gt $area.Bottom){throw 'Fenêtre hors écran.'}
 $form.MinimumSize=New-Object Drawing.Size(0,0);$form.ClientSize=New-Object Drawing.Size(380,500);[Windows.Forms.Application]::DoEvents()
 if($table.ColumnCount -ne 1){throw 'Les champs ne passent pas sur une colonne en largeur réduite.'}
 if(-not [object]::ReferenceEquals($form.Tag,$context)){throw 'Le contexte du formulaire a changé.'}
 for($i=0;$i -lt 6;$i++){if($boxes[$i].Text -cne ('Valeur préservée '+$i)){throw 'Une valeur du formulaire a changé.'}}
 if($script:scoliaWindowQuality[$form].Error){throw $script:scoliaWindowQuality[$form].Error}
 Write-Host 'OK : fenêtre ajustée, formulaire sur une colonne, valeurs et contexte conservés.' -ForegroundColor Green
}finally{$form.Close();$form.Dispose()}
if($script:scoliaWindowQuality.ContainsKey($form)){throw 'La fenêtre fermée reste dans le registre de présentation.'}
