# Native Windows PowerShell 5.1 -STA; no Excel or registry write.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($spec in @(@('PreferencesAffichage.ps1','Get-ITMDefaultDisplayPreferences'),@('PreferencesAffichage.ps1','Set-ITMGridAppearance'),@('Lisibilite.ps1','Set-ITMGridReadability'))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $spec[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Syntaxe invalide.'};$name=$spec[1]
    $fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($fn.Extent.Text))
}
$script:displayGridFonts=@{};$script:displayPreferences=Get-ITMDefaultDisplayPreferences
$script:gridReadabilityBusy=New-Object 'System.Collections.Generic.HashSet[System.Windows.Forms.DataGridView]'
function Get-ITMEffectiveDarkTheme {return $false}
$f=New-Object Windows.Forms.Form;$g=New-Object Windows.Forms.DataGridView;$g.Dock='Fill';$g.RowHeadersVisible=$false;$g.AllowUserToAddRows=$false;$f.Controls.Add($g)
try{
    foreach($name in @('Matricule','Nom complet','Promotion','Année scolaire','Mensualité FC','Total couvert FC','Reste payant FC','Observation')){[void]$g.Columns.Add($name,$name)}
    for($i=0;$i -lt 20;$i++){[void]$g.Rows.Add('TEST01','MWAMBA KASONGO MARIE-CHANTAL','INF1','2026-2027','25000','250000','0','Texte intégral sur plusieurs lignes')}
    $f.Show()
    foreach($width in @(360,480,680,960,1280,1600)){
        $f.ClientSize=New-Object Drawing.Size($width,680);[Windows.Forms.Application]::DoEvents()
        foreach($size in @(9,10,11,12,14)){
            $script:displayPreferences.TextSize=$size;Set-ITMGridReadability $g
            if($g.DefaultCellStyle.Font.SizeInPoints -ne $size){throw 'Préférence de taille écrasée.'}
            $sum=0;foreach($col in $g.Columns){$sum+=$col.Width}
            if($g.ScrollBars -ne 'Both'){throw 'Défilement interne non disponible.'}
            foreach($col in $g.Columns){if($col.Width -lt $col.MinimumWidth -or $col.MinimumWidth -lt 72){throw 'Colonne trop comprimée pour être lisible.'}}
            if($g.Right -gt $f.ClientSize.Width){throw 'La grille déborde de la fenêtre.'}
            if($g.Rows[0].Cells[1].Value -ne 'MWAMBA KASONGO MARIE-CHANTAL'){throw 'Donnée altérée pendant le redimensionnement.'}
            if($g.ColumnHeadersHeightSizeMode -ne 'AutoSize' -or $g.ColumnHeadersDefaultCellStyle.WrapMode -ne 'True'){throw 'Titres non adaptatifs.'}
        }
    }
    Write-Output 'PASS : largeurs 360–1600, tailles 9–14, colonnes lisibles avec défilement interne, titres adaptatifs, données conservées.'
}finally{$f.Dispose();foreach($font in $script:displayGridFonts.Values){$font.Dispose()}}
