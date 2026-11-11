# Windows PowerShell -STA. Données fictives, aucun classeur ouvert.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestsInterfaceQualiteV65.ps1')
$form=New-Object Windows.Forms.Form
$form.MinimumSize=New-Object Drawing.Size(0,0)
$flow=New-Object Windows.Forms.FlowLayoutPanel
$flow.Dock='Top';$flow.AutoSize=$true;$flow.WrapContents=$true
$form.Controls.Add($flow)
foreach($text in @('Enregistrer le paiement sélectionné','Annuler','Exporter le rapport de tous les apprenants')){
 $button=New-Object Windows.Forms.Button;$button.Text=$text;$flow.Controls.Add($button)
}
try{
 $form.Show()
 foreach($size in @(10,12,14)){
  $script:displayPreferences=[pscustomobject]@{TextSize=$size}
  Set-ScoliaUniformTypography $form
  foreach($width in @(320,480,1024,320,1024)){
   $form.ClientSize=New-Object Drawing.Size($width,400)
   Update-ScoliaFlowSpacing $flow
   [Windows.Forms.Application]::DoEvents()
   $previous=@()
   foreach($button in $flow.Controls){
    if($button.Right -gt $flow.ClientSize.Width -or $button.Bottom -gt $flow.ClientSize.Height){throw 'Commande hors de sa barre.'}
    foreach($other in $previous){if($button.Bounds.IntersectsWith($other.Bounds)){throw 'Commandes superposées.'}}
    if($button.Height -lt (Get-ITMTextHeight $button $button.Width 38)){throw 'Texte de commande coupé.'}
    $previous+=$button
   }
  }
 }
 'OK : commandes sans recouvrement, largeurs 320/480/1024, texte 10/12/14.'
}finally{$form.Dispose()}
