# Contrôle Windows des formulaires courants, sans Excel et sans données réelles.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'NotificationsModernes.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
$script:displayPreferences=[pscustomobject]@{TextSize=10}
$form=New-Object Windows.Forms.Form;$form.ClientSize=New-Object Drawing.Size(800,620)
$table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Fill';$table.ColumnCount=2;$table.RowCount=7;$table.Padding=New-Object Windows.Forms.Padding(18);$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,35)));$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,65)));$form.Controls.Add($table)
for($i=0;$i -lt 7;$i++){
 $label=New-Object Windows.Forms.Label;$label.Text='Champ '+($i+1);$label.Font=New-Object Drawing.Font('Times New Roman',18);$label.Margin=New-Object Windows.Forms.Padding(0,5,10,6);$table.Controls.Add($label,0,$i)
 $input=New-Object Windows.Forms.TextBox;$input.Text='Valeur conservée '+$i;$input.Font=New-Object Drawing.Font('Arial',12);$input.Margin=New-Object Windows.Forms.Padding(0,4,0,6);$table.Controls.Add($input,1,$i)
}
$footer=New-Object Windows.Forms.FlowLayoutPanel;$footer.Dock='Bottom';$footer.Height=54;$form.Controls.Add($footer)
$save=New-Object Windows.Forms.Button;$save.Text='Enregistrer';$save.Font=New-Object Drawing.Font('Arial',14);$footer.Controls.Add($save)
try{
 Set-ScoliaUniformTypography $form;Enable-ITMFluidTable $table;$form.Show();[Windows.Forms.Application]::DoEvents()
 foreach($control in @($table.Controls)+@($save)){if($control.Font.FontFamily.Name -ne 'Segoe UI' -or $control.Font.SizeInPoints -ne 10){throw 'Typographie différente dans un champ ou bouton.'}}
 if($table.Parent -eq $form -or $table.Parent.Dock -ne 'Fill' -or $form.AutoScroll){throw 'Corps non séparé des commandes fixes.'}
 if($footer.Bottom -gt $form.ClientSize.Height -or $table.Parent.Bottom -gt $footer.Top){throw 'Boutons masqués ou recouverts.'}
 $script:displayPreferences.TextSize=12;Set-ScoliaUniformTypography $form;Update-ITMFluidTable $table
 foreach($control in @($table.Controls)+@($save)){if($control.Font.SizeInPoints -ne 12){throw 'Préférence globale non appliquée.'}}
 foreach($control in @($table.Controls|Where-Object {$_ -is [Windows.Forms.TextBox]})){if($control.Text -notlike 'Valeur conservée*'){throw 'Donnée de saisie modifiée.'}}
 [ITMErrorDialog]::InterfaceTextSize=12
 $prompt=New-Object ITMStandardDialog($form,'Confirmer cette action ?','Confirmation',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question,[Windows.Forms.MessageBoxDefaultButton]::Button2)
 try{if($prompt.AcceptButton.DialogResult -ne 'No' -or $prompt.Font.SizeInPoints -ne 12 -or $prompt.AcceptButton.Font.SizeInPoints -ne 12){throw 'Confirmation modifiant le choix par défaut ou la taille commune.'}}finally{$prompt.Dispose()}
 'PASS : police et taille uniformes, corps adaptable, validation fixe, confirmations et valeurs conservées.'
}finally{$form.Dispose()}
