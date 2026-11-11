$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'RubanAdaptatifV64.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
foreach($spec in @(@('StyleModerne.ps1','Update-ITMNavigation'),@('StyleModerne.ps1','Test-ITMRibbonExpanded'),@('InterfaceResponsive.ps1','Get-ITMDpiScale'),@('InterfaceResponsive.ps1','Get-ITMFluidTextSize'),@('InterfaceResponsive.ps1','Set-ITMAdaptiveFont'),@('StyleModerne.ps1','Update-ITMFluidRibbon'),@('StyleModerne.ps1','Set-ITMRibbonRowLayout'),@('Lisibilite.ps1','Get-ITMTextHeight'),@('FonctionnalitesV55.ps1','Update-ScoliaFeatureCommandLayout'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$spec[0])}
    $name=$spec[1];$definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw ('Fonction absente : '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:adaptiveFonts=@{};$script:fluidBases=@{};$script:navigationLayoutBusy=$false;$script:nav=@{}
$script:displayPreferences=[pscustomobject]@{RibbonMode='Onglets et commandes'};$script:ribbonPeek=$false
$script:tabsNames=@('Tableau de bord','Apprenants','Paiements','Exports','Diagnostic','Administration','Sécurité','Préférences','Données','Scolarité')
$script:main=New-Object Windows.Forms.Form;$script:main.AutoScaleMode='Dpi'
$script:hostPanel=New-Object Windows.Forms.Panel;$script:hostPanel.Dock='Fill';$script:main.Controls.Add($script:hostPanel)
$script:top=New-Object Windows.Forms.Panel;$script:top.Dock='Top';$script:hostPanel.Controls.Add($script:top)
$script:brand=New-Object Windows.Forms.Panel;$script:brand.Dock='Top';$script:top.Controls.Add($script:brand)
$script:brandLogo=New-Object Windows.Forms.PictureBox;$script:brand.Controls.Add($script:brandLogo)
foreach($name in @('brandTitle','brandNote','pageTitle','fileLabel')){
    $label=New-Object Windows.Forms.Label;$label.Text='ITM CD';Set-Variable -Name $name -Value $label -Scope Script
    if($name -in @('brandTitle','brandNote')){$script:brand.Controls.Add($label)}else{$script:top.Controls.Add($label)}
}
$script:tabs=New-Object Windows.Forms.Panel;$script:top.Controls.Add($script:tabs)
$script:ribbon=New-Object ITMRibbonPanel;$script:top.Controls.Add($script:ribbon)
$script:ribbonFlow=New-Object Windows.Forms.FlowLayoutPanel;$script:ribbonFlow.Padding=New-Object Windows.Forms.Padding(8,3,8,2);$script:ribbon.Controls.Add($script:ribbonFlow)
foreach($name in $script:tabsNames){$button=New-Object ITMWorkspaceTab;$button.Text=$name;$script:tabs.Controls.Add($button);$script:nav[$name]=$button}
foreach($names in @(@('Inscrire','Modifier','Fiche complète','Dernier dossier'),@('Actualiser','Exporter la liste complète'),@('Aide / guide','Rechercher','Imprimer'))){
    $group=New-Object ITMRibbonGroup
    foreach($text in $names){$command=New-Object ITMRibbonCommand;$command.Text=$text;$group.Controls.Add($command)}
    $caption=New-Object Windows.Forms.Label;$caption.Text='DOSSIERS ET DOCUMENTS';$group.Controls.Add($caption)
    $script:ribbonFlow.Controls.Add($group)
}
$script:ribbonToggle=New-Object Windows.Forms.Button;$script:ribbonToggle.Text='Masquer les commandes';$script:brand.Controls.Add($script:ribbonToggle)
$script:accountButton=New-Object Windows.Forms.Button;$script:accountButton.Text='Compte';$script:brand.Controls.Add($script:accountButton)
$script:frequentButtons=@()
for($i=0;$i -lt 3;$i++){$b=New-Object ITMRibbonCommand;$b.IconOnly=$true;$script:brand.Controls.Add($b);$script:frequentButtons+=,$b}
$script:featureMenu=New-Object Windows.Forms.Panel;$script:brand.Controls.Add($script:featureMenu);$script:featureMenuItems=@{}
foreach($name in @('Recherche globale','Outils scolaires')){$b=New-Object ITMWorkspaceTab;$b.Text=$name;$script:featureMenu.Controls.Add($b);$script:featureMenuItems[$name]=$b}
try{
    $script:main.Show();[Windows.Forms.Application]::DoEvents()
    $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96.0))
    foreach($logicalWidth in @(1600,1440,1280,1100,900,680,520,480,400,360,1280)){
        $script:main.ClientSize=New-Object Drawing.Size([int]($logicalWidth*$q),700)
        [Windows.Forms.Application]::DoEvents();Update-ITMNavigation
        foreach($b in $script:frequentButtons){if($b.Right -gt $script:hostPanel.ClientSize.Width -or $b.Bounds.IntersectsWith($script:brandTitle.Bounds)){throw 'Raccourci fréquent coupé ou superposé au titre.'}}
        if($script:featureMenu.Top -lt 0 -or $script:featureMenu.Right -gt $script:hostPanel.ClientSize.Width -or $script:featureMenu.Bounds.IntersectsWith($script:brandLogo.Bounds)){throw 'Le bouton Recherche globale déborde ou recouvre le logo.'}
        if($script:featureMenu.Bounds.IntersectsWith($script:accountButton.Bounds) -or $script:featureMenu.Bounds.IntersectsWith($script:ribbonToggle.Bounds)){throw 'Le bouton Recherche globale recouvre Compte ou le bouton du ruban.'}
        if($script:featureMenu.Height -gt [int](72*$q)){throw 'La zone de Recherche globale devient trop haute dans une fenêtre réduite.'}
        foreach($b in $script:featureMenuItems.Values){if($b.Left -lt 0 -or $b.Right -gt $script:featureMenu.ClientSize.Width -or $b.Bottom -gt $script:featureMenu.ClientSize.Height -or $b.Width -le 0 -or $b.Height -le 0){throw 'Le bouton Recherche globale sort de sa zone responsive.'}}
        if($logicalWidth -ge 1280 -and [Math]::Abs($script:featureMenu.Top-$script:accountButton.Top) -gt [int](2*$q)){throw 'Recherche globale n''est pas aligné avec le bouton Compte en largeur normale.'}
        if($script:ribbonFlow.AutoScroll -or $script:top.AutoScroll){throw 'Le ruban utilise encore un défilement.'}
        $rows=@($script:nav.Values | ForEach-Object {$_.Top} | Select-Object -Unique)
        if($logicalWidth -ge 1280 -and $rows.Count -ne 1){throw ('Les onglets ne tiennent pas à '+$logicalWidth+' unités.')}
        foreach($button in $script:nav.Values){if($button.Left -lt 0 -or $button.Right -gt $script:hostPanel.ClientSize.Width){throw 'Onglet hors de la fenêtre.'}}
        if($logicalWidth -le 520 -and @($script:nav.Values | Where-Object {-not $_.IconOnly}).Count){throw 'Les onglets étroits ne basculent pas en icônes seules.'}
        if($logicalWidth -ge 900 -and @($script:nav.Values | Where-Object {$_.IconOnly}).Count){throw 'Les onglets larges restent inutilement en icônes seules.'}
        $available=[Math]::Max(40,$script:hostPanel.ClientSize.Width-$script:ribbonFlow.Padding.Horizontal-17)
        Update-ITMFluidRibbon $available $q
        $ribbonButtons=@($script:ribbonFlow.Controls | ForEach-Object {$_.Controls | Where-Object {$_ -is [Windows.Forms.Button]}})
        if($available/$q -lt 900 -and @($ribbonButtons | Where-Object {-not $_.IconOnly}).Count){throw 'Le ruban compact conserve encore du texte sur les commandes.'}
        if($available/$q -ge 900){
            $widthTotal=0;foreach($g in $script:ribbonFlow.Controls){$widthTotal+=$g.Width+$g.Margin.Horizontal}
            if($widthTotal -gt $available){throw 'Les groupes ne tiennent pas sur une seule rangée en largeur suffisante.'}
            if(@($script:ribbonFlow.Controls | ForEach-Object {$_.Height} | Select-Object -Unique).Count -ne 1){throw 'Les groupes larges ne sont pas alignés.'}
        }
        $row=@();$used=0
        foreach($group in $script:ribbonFlow.Controls){
            if($row.Count -and $used+$group.Width+$group.Margin.Horizontal -gt $available){
                if(@($row | ForEach-Object {$_.Controls | Where-Object {$_ -is [Windows.Forms.Label]} | ForEach-Object {$_.Top}} | Select-Object -Unique).Count -ne 1){throw 'Noms des groupes désalignés.'}
                if($row[-1].ShowSeparator){throw 'Séparateur après le dernier groupe.'}
                $row=@();$used=0
            }
            $row+=,$group;$used+=$group.Width+$group.Margin.Horizontal
            foreach($command in @($group.Controls | Where-Object {$_ -is [Windows.Forms.Button]})){
                if($command.Right -gt $group.Width -or $command.Bottom -gt @($group.Controls | Where-Object {$_ -is [Windows.Forms.Label]})[0].Top){throw 'Commande en dehors de son groupe.'}
            }
        }
        if($row[-1].ShowSeparator){throw 'Séparateur après le dernier groupe.'}
        if(@($row | ForEach-Object {$_.Height} | Select-Object -Unique).Count -ne 1){throw 'Hauteurs des groupes désalignées.'}
        $script:main.ClientSize=New-Object Drawing.Size([int]($logicalWidth*$q),[int]($(if($logicalWidth -lt 680){600}else{440})*$q))
        [Windows.Forms.Application]::DoEvents();Update-ITMNavigation
        if($script:top.Height+[int](160*$q) -gt $script:hostPanel.ClientSize.Height){throw 'Le bandeau masque la page dans une fenêtre basse.'}
        if($script:displayPreferences.RibbonMode -ne 'Onglets et commandes'){throw 'La compacité temporaire change la préférence mémorisée.'}
        $script:main.ClientSize=New-Object Drawing.Size([int]($logicalWidth*$q),[int](1000*$q))
        [Windows.Forms.Application]::DoEvents();Update-ITMNavigation
        Write-Output ($logicalWidth.ToString()+' unités : '+$rows.Count+' ligne(s).')
    }
}finally{$script:main.Close();$script:main.Dispose();foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}}
