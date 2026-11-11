# Windows PowerShell 5.1 -STA; real WinForms, no Excel and no registry writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($file in @('ActivityIndicator.cs','ChromeModerne.cs','DashboardChart.cs')){Add-Type -Path (Join-Path $PSScriptRoot $file) -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')}
function Get-ItemProperty {throw 'Registry intentionally unavailable in this isolated test.'}
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
. (Join-Path $PSScriptRoot 'PreferencesAffichage.ps1')
function Save-ITMDisplayPreferences {throw 'Unexpected settings write.'}
function Get-ITMEffectiveDarkTheme {return $false}
foreach($name in @('New-Label','New-Button','New-Combo','Set-Choices')){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Application.ps1'),[ref]$tokens,[ref]$errors)
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    if(-not $node){throw $name};. ([scriptblock]::Create($node.Extent.Text))
}
function Assert($value,$message){if(-not $value){throw $message}}
$script:main=New-Object Windows.Forms.Form;$script:main.AutoScaleMode='Dpi';$script:main.Font=New-Object Drawing.Font('Segoe UI',10)
$script:status=[pscustomobject]@{Text=''};$script:loading=$false
$page=New-Object Windows.Forms.Panel;$page.Dock='Fill';$script:main.Controls.Add($page)
Build-ITMDisplayPreferences $page
$table=@($script:fluidTables.Keys)[0];$scroll=$table.Parent
Enable-ITMFluidTypography $script:main
$largeFont=0;$smallFont=0
try{
    $script:main.Show();[Windows.Forms.Application]::DoEvents();$q=Get-ITMDpiScale $script:main
    foreach($width in @(1280,960,680,480,360,1280)){
        $script:main.ClientSize=New-Object Drawing.Size([int]($width*$q),[int](620*$q));[Windows.Forms.Application]::DoEvents();Update-ITMFluidTable $table
        Assert ($table.Width -le $scroll.ClientSize.Width-$scroll.Padding.Horizontal) 'Preferences outside viewport'
        foreach($c in $table.Controls){
            Assert ($c.Left -ge 0 -and $c.Right -le $table.ClientSize.Width+1 -and $c.Height -gt 0) ('Clipped control: '+$c.Text)
            if($c -is [Windows.Forms.Label]){Assert ($c.Height -ge (Get-ITMTextHeight $c $c.Width 0)-1) ('Clipped text: '+$c.Text)}
        }
        Assert (-not $scroll.HorizontalScroll.Visible) 'Unexpected horizontal preferences scrollbar'
        Assert ($table.ColumnCount -eq $(if($table.Width/$q -lt 650){1}else{2})) 'Preferences columns did not reflow'
        if($width -eq 1280){if($largeFont -eq 0){$largeFont=$table.Controls[0].Font.SizeInPoints}else{Assert ($largeFont -eq $table.Controls[0].Font.SizeInPoints) 'Cumulative font shrinking'}}
        if($width -eq 360){$smallFont=$table.Controls[0].Font.SizeInPoints}
    }
    Assert ($smallFont -lt $largeFont -and $smallFont -ge 9) 'Font not responsive or unreadable'
    $grid=New-Object Windows.Forms.DataGridView;$grid.SetBounds(0,0,300,200);$page.Controls.Add($grid);[void]$grid.Columns.Add('Nom','Nom');[void]$grid.Rows.Add('Apprenant exemple')
    $script:displayPreferences.TextSize=14
    $script:main.ClientSize=New-Object Drawing.Size([int](360*$q),[int](620*$q));Set-ITMGridAppearance $grid;$small=$grid.Font.SizeInPoints
    $script:main.ClientSize=New-Object Drawing.Size([int](1280*$q),[int](620*$q));Set-ITMGridAppearance $grid
    Assert ($grid.Font.SizeInPoints -eq 14 -and $small -eq 14 -and $script:displayPreferences.TextSize -eq 14) 'User text-size preference was overwritten by resizing'
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$page.Controls.Add($body);$body.BringToFront()
    $label=New-Label $body ('Description très longue à afficher entièrement. '*8) 20 20 900 26 12
    $field=New-Object Windows.Forms.TextBox;$field.SetBounds(20,80,700,30);$body.Controls.Add($field)
    $button=New-Button $body 'Enregistrer les informations de cet apprenant' 20 130 450
    Enable-ITMResponsivePanel $body 1000
    foreach($width in @(960,480,360,960)){
        $script:main.ClientSize=New-Object Drawing.Size([int]($width*$q),[int](620*$q));Update-ITMResponsivePanel $body
        Assert ($label.Right -le $body.ClientSize.Width -and $field.Right -le $body.ClientSize.Width -and $button.Right -le $body.ClientSize.Width) 'Generic dialog clipping'
        Assert ($field.Top -ge $label.Bottom -and $button.Top -ge $field.Bottom) 'Generic dialog overlap'
        Assert ($label.Height -ge (Get-ITMTextHeight $label $label.Width 0)) 'Generic description clipped'
    }

    . (Join-Path $PSScriptRoot 'PagesModernes.ps1')
    foreach($pageName in @('Diagnostic','Sécurité','Données','Promotions','Sauvegardes','Année scolaire','Journal','Doublons')){
        $testPage=New-Object Windows.Forms.Panel;$testPage.Dock='Fill';$testPage.AutoScroll=$true;$page.Controls.Add($testPage);$testPage.BringToFront()
        $heading=New-Label $testPage $pageName 24 20 900 38 18 $true
        $description=New-Label $testPage ('Instruction longue, lisible dans les deux thèmes et sur une fenêtre réduite. '*4) 24 78 900 64 10
        $check=New-Object Windows.Forms.CheckBox;$check.Text='Activer une préférence dont le libellé doit rester lisible intégralement sur plusieurs lignes';$check.SetBounds(24,170,900,32);$testPage.Controls.Add($check)
        $choice=New-Combo $testPage 24 220 900;Set-Choices $choice @('Choix au libellé particulièrement long pour vérifier la liste déroulante') ''
        $action=New-Button $testPage 'Enregistrer les modifications sélectionnées' 24 280 500
        Enable-ITMPageDesign $testPage;Enable-ITMResponsivePanel $testPage 1000
        foreach($width in @(1280,960,680,480,360,1280)){
            $script:main.ClientSize=New-Object Drawing.Size([int]($width*$q),[int](620*$q));[Windows.Forms.Application]::DoEvents();Apply-ITMFluidTypography $script:main;Update-ITMResponsivePanel $testPage
            $surface=$script:modernPages[$testPage].Surface
            foreach($c in $surface.Controls){
                Assert ($c.Left -ge 0 -and $c.Right -le $surface.ClientSize.Width -and $c.Bottom -le $surface.ClientSize.Height) ('Contenu hors page : '+$pageName)
                if($c -is [Windows.Forms.Label]){Assert ($c.Height -ge (Get-ITMTextHeight $c $c.Width 0)) ('Texte coupé : '+$pageName)}
            }
            Assert ($check.Bottom -le $choice.Top -and $choice.Bottom -le $action.Top) ('Commandes superposées : '+$pageName)
            Assert ($choice.ItemHeight -ge $choice.Font.Height+6) 'Police coupée dans une liste déroulante'
            Assert (-not $testPage.HorizontalScroll.Visible) ('Défilement horizontal de page : '+$pageName)
        }
        $testPage.Dispose()
    }
    $login=New-Object ITMLoginForm
    try{
        $login.MessageLabel.Text='Le compte ne peut pas être ouvert. Vérifiez les informations saisies et réessayez.'
        foreach($width in @(510,360,280,510)){
            $login.ClientSize=New-Object Drawing.Size([int]($width*$q),[int](400*$q));$login.PerformLayout()
            foreach($c in $login.Controls){Assert ($c.Right -le $login.ClientSize.Width -and $c.Left -ge 0) 'Login outside viewport'}
            Assert ($login.SignIn.Bounds.IntersectsWith($login.CancelAction.Bounds) -eq $false) 'Login actions overlap'
            Assert ($login.PasswordFieldSurface.Top -ge $login.Controls[4].Bottom) 'Login labels overlap password'
        }
    }finally{$login.Dispose()}
    Write-Output 'PASS: 360/480/680/960/1280 widths, wrapping, preferences columns, reference font preservation, repeated resizing, generic dialogs and login.'
}finally{
    $script:main.Close();$script:main.Dispose()
    foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}
}
