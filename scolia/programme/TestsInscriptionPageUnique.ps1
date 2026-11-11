# Windows PowerShell 5.1 -STA : contrôles de placement, sans Excel ni registre.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
$d=New-Object Windows.Forms.Form;$d.AutoScaleMode='Dpi';$d.Size=New-Object Drawing.Size(900,650)
$script:main=$d
$header=New-Object Windows.Forms.Panel;$header.Dock='Top';$d.Controls.Add($header)
$title=New-Object Windows.Forms.Label;$title.Text='Inscription d''un apprenant';$title.Font=New-Object Drawing.Font('Segoe UI',14,[Drawing.FontStyle]::Bold);$header.Controls.Add($title)
$footer=New-Object Windows.Forms.Panel;$footer.Dock='Bottom';$d.Controls.Add($footer)
$save=New-Object Windows.Forms.Button;$save.Text='Enregistrer le dossier';$footer.Controls.Add($save)
$cancel=New-Object Windows.Forms.Button;$cancel.Text='Annuler';$footer.Controls.Add($cancel)
$body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$d.Controls.Add($body);$body.BringToFront()
$labels=@();$fields=@()
foreach($text in @('Nom *','Post-nom *','Prénom *','Sexe *','Date de naissance (jj/mm/aaaa) *','Lieu de naissance *','Contact parent 1 (10 chiffres) *','Contact parent 2 (facultatif)','Promotion *','Année scolaire *','Statut de l''apprenant *')){
    $l=New-Object Windows.Forms.Label;$l.Text=$text;$body.Controls.Add($l);$labels+=,$l
    $f=New-Object Windows.Forms.TextBox;$body.Controls.Add($f);$fields+=,$f
}
$al=New-Object Windows.Forms.Label;$al.Text='Adresse de résidence';$body.Controls.Add($al)
$a=New-Object Windows.Forms.TextBox;$a.Multiline=$true;$body.Controls.Add($a)
try{
    Enable-ITMDossierLayout $d $body $header $footer $labels $fields $al $a $save $cancel
    $d.Show();[Windows.Forms.Application]::DoEvents();$q=Get-ITMDpiScale $d
    foreach($size in @(@(1040,680),@(760,480),@(560,560),@(440,420),@(1040,680))){
        $d.Size=New-Object Drawing.Size([int]($size[0]*$q),[int]($size[1]*$q));[Windows.Forms.Application]::DoEvents();Update-ITMDossierLayout $d
        $state=$script:adaptivePanels[$d]
        if(-not $body.AutoScroll -or @($body.Controls | Where-Object {$_ -is [Windows.Forms.TabControl]}).Count){throw "L'inscription doit rester un formulaire unique avec defilement vertical si necessaire."}
        if($body.HorizontalScroll.Visible){throw 'Le formulaire impose un defilement horizontal.'}
        foreach($group in $state.Groups){
            if($group.Card.Left -lt 0 -or $group.Card.Right -gt $body.ClientSize.Width){throw 'Une section depasse la fenetre.'}
            foreach($c in $group.Card.Controls){if($c.Left -lt 0 -or $c.Right -gt $group.Card.ClientSize.Width -or $c.Bottom -gt $group.Card.ClientSize.Height){throw ('Champ hors section : '+$c.Text)}}
        }
        for($i=0;$i -lt $fields.Count;$i++){
            $wrapper=$state.Wrappers[$i]
            if($wrapper.Top -lt $labels[$i].Bottom){throw 'Un libelle recouvre sa saisie.'}
            if($fields[$i].Height -lt $fields[$i].Font.Height){throw 'Le texte saisi est coupe.'}
        }
        if($save.Bounds.IntersectsWith($cancel.Bounds) -or $save.Right -gt $footer.ClientSize.Width -or $save.Bottom -gt $footer.ClientSize.Height){throw 'Les actions sont coupees ou se recouvrent.'}
    }
    $body.ScrollControlIntoView($a)
    [Windows.Forms.Application]::DoEvents()
    if($a.RectangleToScreen($a.ClientRectangle).Bottom -gt $body.RectangleToScreen($body.ClientRectangle).Bottom){throw "L'adresse reste hors de la zone accessible."}
    $login=New-Object ITMLoginForm
    try{
        if(-not $login.Password.UseSystemPasswordChar){throw 'Le mot de passe apparaît au démarrage.'}
        if($login.Password.Parent -ne $login.Reveal.Parent){throw 'Le bouton œil n''est pas intégré au champ.'}
        if($login.Password.Bounds.IntersectsWith($login.Reveal.Bounds)){throw 'Le bouton œil recouvre la saisie.'}
        if($login.Password.AutoSize -or $login.Password.Height -lt $login.Password.PreferredHeight){throw 'Le champ de mot de passe risque de couper le texte.'}
        if($login.Password.Height -lt 28){throw 'La zone de texte du mot de passe est trop basse.'}
    }finally{$login.Dispose()}
    Write-Output 'PASS : formulaire unique, sections sans chevauchement, defilement vertical accessible, actions fixes et oeil integre au champ.'
}finally{$d.Close();$d.Dispose();foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}}
