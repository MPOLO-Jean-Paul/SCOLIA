$ErrorActionPreference='Stop'
# Native Windows test: no Excel, registry or school data access.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if(-not ('ITMDesign' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')}
foreach($spec in @(@('Application.ps1','New-NavIcon'),@('TableauDeBordModerne.ps1','Update-ITMDashboardIcons'),@('ThemesAffichage.ps1','Apply-ITMThemeToOpenForms'))){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$spec[0])}
    $name=$spec[1]
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if(-not $definition){throw ('Fonction absente : '+$name)}
    $code=$definition.Extent.Text
    if($name -eq 'New-NavIcon'){$code=$code.Replace('function New-NavIcon','function New-ITMTestOriginalNavIcon')}
    . ([scriptblock]::Create($code))
}
function New-NavIcon($name,$dark){if($script:failImage){throw 'Échec simulé de création de bitmap.'};return New-ITMTestOriginalNavIcon $name $dark}
function Get-ITMEffectiveDarkTheme {return $script:testDark}
function Apply-ITMThemeForm($form) {}
$script:nav=@{};$script:navIcons=@{};$script:dashboardCards=@();$script:currentPage='Apprenants';$script:testDark=$false;$script:failImage=$false
$script:main=New-Object Windows.Forms.Form;$script:main.Size=New-Object Drawing.Size(500,350)
$names=@('Apprenants','Paiements','Diagnostic','Sécurité')
try{
    for($i=0;$i -lt $names.Count;$i++){
        $name=$names[$i];$button=New-Object Windows.Forms.Button;$button.SetBounds(10,10+60*$i,180,50);$script:main.Controls.Add($button);$script:nav[$name]=$button
        foreach($active in @($false,$true)){
            $navIconKey=$name + '__' + [string]$active
            $script:navIcons[$navIconKey]=New-NavIcon $name $false
        }
        $inactiveKey=$name + '__False'
        $button.Image=$script:navIcons[$inactiveKey]
        $card=New-Object Windows.Forms.Panel;$card.SetBounds(210,10+60*$i,60,50);$script:main.Controls.Add($card)
        $glyph=New-Object Windows.Forms.PictureBox;$glyph.SizeMode='Zoom';$glyph.Dock='Fill';$glyph.Image=New-NavIcon $name $false;$card.Controls.Add($glyph)
        if([object]::ReferenceEquals($glyph.Image,$script:navIcons[$inactiveKey])){throw 'Bitmap partagé avec le cache.'}
        $glyph.Add_Disposed({param($sender,$e)$image=$sender.Image;$sender.Image=$null;if($image){$image.Dispose()}})
        $card.Tag=[pscustomobject]@{Glyph=$glyph;IconName=$name};$script:dashboardCards+=$card
    }
    $script:main.Show();[Windows.Forms.Application]::DoEvents()
    for($cycle=0;$cycle -lt 40;$cycle++){
        $script:testDark=($cycle%2 -eq 1);Apply-ITMThemeToOpenForms
        foreach($card in $script:dashboardCards){if($card.Tag.Glyph.Image.Width -ne 28 -or $card.Tag.Glyph.Image.Height -ne 28){throw 'Image du tableau de bord invalide.'};$card.Tag.Glyph.Refresh()}
        foreach($button in $script:nav.Values){if($button.Image.Width -ne 28){throw 'Image d''onglet invalide.'};$button.Refresh()}
        $canvas=New-Object Drawing.Bitmap($script:main.ClientSize.Width,$script:main.ClientSize.Height)
        try{$script:main.DrawToBitmap($canvas,(New-Object Drawing.Rectangle(0,0,$canvas.Width,$canvas.Height)))}finally{$canvas.Dispose()}
        [Windows.Forms.Application]::DoEvents()
    }
    $before=$script:dashboardCards[0].Tag.Glyph.Image;$script:failImage=$true;$failed=$false
    try{Update-ITMDashboardIcons $true}catch{$failed=$true}
    if(-not $failed -or -not [object]::ReferenceEquals($before,$script:dashboardCards[0].Tag.Glyph.Image) -or $before.Width -ne 28){throw 'Ancienne image perdue après un échec de création.'}
    $script:failImage=$false
    Write-Output 'Images : 40 applications de thème, peinture native et conservation après échec validées.'
}finally{
    $script:main.Close();$script:main.Dispose()
    foreach($image in $script:navIcons.Values){$image.Dispose()}
}
