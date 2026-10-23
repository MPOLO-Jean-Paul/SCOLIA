# Windows PowerShell 5.1 -STA : vrais controles WinForms, aucun appel Excel.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies System.Windows.Forms,System.Drawing}
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
. (Join-Path $PSScriptRoot 'Ergonomie.ps1')
function Assert-V53([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$script:displayPreferences=[pscustomobject]@{Theme='Clair';Alternating=$true;TextSize=10}
$script:busy=$false
$d=New-Object Windows.Forms.Form;$d.AutoScaleMode='Dpi';$d.Size=New-Object Drawing.Size(1040,760)
$script:main=$d;$script:editDialog=$d;$script:editControls=@{}
$header=New-Object Windows.Forms.Panel;$header.Dock='Top';$d.Controls.Add($header)
$title=New-Object ITMReadableLabel;$title.Text='Nouvelle inscription';$title.Top=17;$header.Controls.Add($title)
$description=New-Object ITMReadableLabel;$description.Text='Matricule automatique - Champs obligatoires *';$description.Top=55;$header.Controls.Add($description)
$script:editFeedback=New-Object ITMReadableLabel;$script:editFeedback.Top=78;$header.Controls.Add($script:editFeedback)
$footer=New-Object Windows.Forms.Panel;$footer.Dock='Bottom';$d.Controls.Add($footer)
$save=New-Object ITMActionButton;$save.Text='Enregistrer le dossier';$save.Primary=$true;$footer.Controls.Add($save);$script:editSave=$save
$cancel=New-Object ITMActionButton;$cancel.Text='Annuler';$footer.Controls.Add($cancel)
$body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$d.Controls.Add($body);$body.BringToFront()
$names=@('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Contact2','Promotion','Annee','Statut')
$labels=@();$fields=@()
foreach($name in $names){
    $label=New-Object ITMReadableLabel;$label.Text=$name+' *';$body.Controls.Add($label);$labels+=,$label
    if($name -in @('Sexe','Promotion','Annee','Statut')){$field=New-Object ITMThemeComboBox;$field.DropDownStyle='DropDownList'}
    elseif($name -eq 'Naissance'){$field=New-ITMDateField}
    else{$field=New-Object Windows.Forms.TextBox}
    $body.Controls.Add($field);$fields+=,$field;$script:editControls[$name]=$field
}
$addressLabel=New-Object ITMReadableLabel;$addressLabel.Text='Adresse de residence - facultatif';$body.Controls.Add($addressLabel)
$address=New-Object Windows.Forms.TextBox;$address.Multiline=$true;$body.Controls.Add($address);$script:editControls.Adresse=$address
$script:editErrors=New-Object Windows.Forms.ErrorProvider;$script:editErrors.ContainerControl=$d;$script:editErrors.BlinkStyle='NeverBlink'
try{
    Enable-ITMDossierLayout $d $body $header $footer $labels $fields $addressLabel $address $save $cancel
    Enable-ITMRegistrationForm $d
    $d.Show();[Windows.Forms.Application]::DoEvents();$q=Get-ITMDpiScale $d
    $state=$script:adaptivePanels[$d]
    Assert-V53 ($state.Groups.Count -eq 3) 'Les trois sections sont absentes.'
    for($first=0;$first -lt 3;$first++){for($second=$first+1;$second -lt 3;$second++){Assert-V53 (-not $state.Groups[$first].Card.Bounds.IntersectsWith($state.Groups[$second].Card.Bounds)) 'Les sections se recouvrent.'}}
    Assert-V53 (($state.Groups[1].Indexes -join ',') -eq '8,9,10') 'La scolarite est separee des champs attendus.'
    foreach($theme in @('Clair','Sombre','Clair')){
        $script:displayPreferences.Theme=$theme
        foreach($size in @(@(1040,680),@(560,480),@(440,420),@(1040,680))){
            $script:displayPreferences.TextSize=$(if($size[0] -eq 440){14}else{10})
            $d.Size=New-Object Drawing.Size([int]($size[0]*$q),[int]($size[1]*$q));[Windows.Forms.Application]::DoEvents();Update-ITMDossierLayout $d
            Assert-V53 ($fields[0].Font.SizeInPoints -ge $script:displayPreferences.TextSize) 'La preference de lecture est reduite par le redimensionnement.'
            Assert-V53 (-not $body.HorizontalScroll.Visible) 'Defilement horizontal inattendu.'
            foreach($group in $state.Groups){
                foreach($control in $group.Card.Controls){
                    Assert-V53 ($control.Left -ge 0 -and $control.Right -le $group.Card.ClientSize.Width -and $control.Bottom -le $group.Card.ClientSize.Height) ('Controle hors section : '+$control.Text)
                    if($control -is [Windows.Forms.Label]){Assert-V53 ($control.Height -ge (Get-ITMDossierLabelHeight $control $control.Width 0)) ('Libelle coupe : '+$control.Text)}
                }
            }
            Assert-V53 (-not $save.Bounds.IntersectsWith($cancel.Bounds)) 'Les actions se recouvrent.'
            Assert-V53 ($save.Bottom -le $footer.ClientSize.Height) 'Le bouton de sauvegarde est inaccessible.'
            foreach($index in 0..10){
                Assert-V53 ($fields[$index].Parent -eq $state.Wrappers[$index]) 'Un editeur natif a ete remplace.'
                Assert-V53 ($fields[$index].Height -ge $fields[$index].Font.Height) 'Texte de saisie coupe.'
                Assert-V53 ($fields[$index].Parent.BackColor -eq $fields[$index].BackColor) 'Editeur incoherent avec son theme.'
            }
        }
    }
    foreach($pair in @(@('Sexe','M'),@('Promotion','INF1'),@('Annee','2026-2027'),@('Statut','Actif'))){[void]$script:editControls[$pair[0]].Items.Add($pair[1]);$script:editControls[$pair[0]].SelectedIndex=0}
    foreach($pair in @(@('Nom','NSEYA'),@('PostNom','KABONGO'),@('Prenom','JEAN'),@('Naissance','01/09/2006'),@('Lieu','Kinshasa'),@('Contact1','0812345678'))){$script:editControls[$pair[0]].Text=$pair[1]}
    Assert-V53 (Test-ITMDossierFields) 'Un dossier complet valide est refuse.'
    $script:editControls.Contact1.Text='123'
    Assert-V53 (-not (Test-ITMDossierFields)) 'Un telephone incomplet est accepte.'
    Assert-V53 ($script:editControls.Contact1.Parent.Invalid) 'Le champ invalide ne porte pas de signal visuel.'
    Assert-V53 ($script:editFeedback.Tag -eq 'scolia:feedback:error') 'Le message de validation est absent.'
    $script:editControls.Contact1.Text='0812345678'
    Assert-V53 (-not $script:editControls.Contact1.Parent.Invalid) 'Le signal reste present apres correction.'
    Assert-V53 (Test-ITMDossierFields) 'Le dossier corrige reste refuse.'
    Assert-V53 ($script:editControls.Contact1.Text[0] -eq '0') 'Le zero initial du telephone est perdu.'
    Write-Output 'PASS : sections, themes, fenetre reduite, champs natifs, validation et correction sans Excel.'
}finally{
    $script:editDone=$true;$script:editErrors.Dispose();$script:editErrors=$null;$d.Dispose();$script:inputHelp.Dispose()
    foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}
}
