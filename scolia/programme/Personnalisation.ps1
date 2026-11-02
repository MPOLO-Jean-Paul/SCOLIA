. (Join-Path $PSScriptRoot 'SignatairesV63.ps1')
# Institution-wide profile. Classes and fees remain in the connected data store.
$script:institutionProfilePath=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'configuration\institution.json'
function Get-ITMDefaultInstitution {
    [pscustomobject]@{Name='ITM Cœur de Dieu';Subtitle='Institut technique médical · Secrétariat scolaire';Address='N° 1, Av. EDGE | Q. Kamatete | C. Annexe';Logo='';SignatoriesByInstitution=[pscustomobject]@{};Header=[string[]]$script:schoolHeader.Clone()}
}
$script:institutionProfile=Get-ITMDefaultInstitution
if([IO.File]::Exists($script:institutionProfilePath)){
    try{$saved=[IO.File]::ReadAllText($script:institutionProfilePath) | ConvertFrom-Json
        if([string]::IsNullOrWhiteSpace([string]$saved.Name) -or @($saved.Header).Count -ne 7){throw 'Profil institution incomplet.'}
        $script:institutionProfile=$saved
    }catch{throw ('Personnalisation illisible : '+$_.Exception.Message)}
}
function Get-ITMInstitutionName {return [string]$script:institutionProfile.Name}
function Get-ITMInstitutionLogo {
    if($script:institutionProfile.Logo){$path=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) $script:institutionProfile.Logo;if([IO.File]::Exists($path)){return $path}}
    return (Join-Path $PSScriptRoot 'Assets/Logo_ITM_CD_Transparent.png')
}
function Apply-ITMInstitution {
    if($script:scolia){$script:institutionProfile.Name=$script:scolia.School}
    $script:schoolHeader=[string[]]$script:institutionProfile.Header
    $script:schoolHeader[5]=[string]$script:institutionProfile.Name;$script:schoolHeader[6]=[string]$script:institutionProfile.Address
    if($script:main){$script:main.Text=(Get-ITMInstitutionName)+' | Secrétariat scolaire'}
    if($script:brandTitle){$script:brandTitle.Text=Get-ITMInstitutionName;$script:brandNote.Text=[string]$script:institutionProfile.Subtitle}
    $path=Join-Path $PSScriptRoot 'Assets/SCOLIA.png'
    if($script:brandLogo){
        $source=[Drawing.Image]::FromFile($path)
        try{$next=New-Object Drawing.Bitmap($source)}finally{$source.Dispose()}
        $old=$script:schoolLogo;$script:schoolLogo=$next;$script:brandLogo.Image=$next
        if($old){$old.Dispose()}
    }
    $script:windowIconPath=Join-Path $PSScriptRoot 'Assets/SCOLIA.ico'
    [ITMErrorDialog]::BrandIconPath=$script:windowIconPath
    [ITMErrorDialog]::BrandImagePath=$path
    [ITMErrorDialog]::InstitutionName=Get-ITMInstitutionName
    if($script:scolia -and $script:main){$script:main.Text='SCOLIA | '+$script:scolia.School;$script:brandTitle.Text='SCOLIA';$script:brandNote.Text=$script:scolia.School}
    foreach($window in @([Windows.Forms.Application]::OpenForms)){if(-not $window.InvokeRequired){Set-ITMWindowIcon $window}}
    if(Test-Path function:Update-ITMInstitutionShortcutIcons){Update-ITMInstitutionShortcutIcons}
    if($script:main){$script:main.PerformLayout();$script:main.Invalidate($true)}
}
function Save-ITMInstitution($profile,[string]$logoSource='') {
    Assert-ITMPermission 'Administrer'
    foreach($name in @('Name','Subtitle','Address')){if([string]::IsNullOrWhiteSpace([string]$profile.$name) -or ([string]$profile.$name).Length -gt 160){throw ('Champ institution invalide : '+$name)}}
    if(@($profile.Header).Count -ne 7 -or @($profile.Header | Where-Object {[string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Length -gt 160}).Count){throw 'Renseignez les sept mentions, au maximum 160 caractères chacune.'}
    $dir=[IO.Path]::GetDirectoryName($script:institutionProfilePath);[void][IO.Directory]::CreateDirectory($dir)
    $newLogo=''
    if($logoSource){
        $image=[Drawing.Image]::FromFile($logoSource)
        try{
            if($image.Width -gt 6000 -or $image.Height -gt 6000){throw 'Logo trop grand (maximum 6000 pixels).'}
            $newLogo=Join-Path $dir ('logo-'+[Guid]::NewGuid().ToString('N')+'.png')
            $image.Save($newLogo,[Drawing.Imaging.ImageFormat]::Png)
        }finally{$image.Dispose()}
        $profile.Logo='configuration\'+[IO.Path]::GetFileName($newLogo)
    }
    $temp=$script:institutionProfilePath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
    try{
        $profile.Header[5]=$profile.Name;$profile.Header[6]=$profile.Address
        [IO.File]::WriteAllText($temp,($profile | ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($true))
        if([IO.File]::Exists($script:institutionProfilePath)){[IO.File]::Replace($temp,$script:institutionProfilePath,$script:institutionProfilePath+'.bak')}else{[IO.File]::Move($temp,$script:institutionProfilePath)}
        $script:institutionProfile=$profile;Apply-ITMInstitution
    }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Show-ITMInstitutionEditor {
    Assert-ITMPermission 'Administrer'
    $d=New-Object Windows.Forms.Form;Set-ITMWindowIcon $d;$d.Text='Personnalisation de l''établissement';$d.Size=New-Object Drawing.Size(860,660);$d.MinimumSize=New-Object Drawing.Size(400,420);$d.StartPosition='CenterParent'
    $table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Fill';$table.AutoScroll=$true;$table.Padding=New-Object Windows.Forms.Padding(18);$table.ColumnCount=2
    [void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent',32)));[void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle('Percent',68)));$d.Controls.Add($table)
    $fields=@{};$names=@('Name','Subtitle','Address');$labels=@('Nom de l''institution','Description','Adresse')
    for($i=0;$i -lt 10;$i++){
        $key=$(if($i -lt 3){$names[$i]}else{'Header'+($i-3)});$text=$(if($i -lt 3){$labels[$i]}else{'Mention d''en-tête '+($i-2)})
        $table.RowCount=$i+1;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',44)))
        $label=New-Object ITMReadableLabel;$label.Text=$text;$label.Dock='Fill';$label.TextAlign='MiddleLeft';$table.Controls.Add($label,0,$i)
        $box=New-Object Windows.Forms.TextBox;$box.Dock='Fill';$box.MaxLength=160;$box.Text=$(if($i -lt 3){[string]$script:institutionProfile.$key}else{[string]$script:schoolHeader[$i-3]});$table.Controls.Add($box,1,$i);$fields[$key]=$box
    }
    $fields.Header5.ReadOnly=$true;$fields.Header6.ReadOnly=$true
    $fields.Name.Tag=$fields.Header5;$fields.Address.Tag=$fields.Header6
    $fields.Name.Add_TextChanged({param($sender,$e)$sender.Tag.Text=$sender.Text});$fields.Address.Add_TextChanged({param($sender,$e)$sender.Tag.Text=$sender.Text})
    foreach($role in @('Prefet','Proviseur','Secretaire')){
        $row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',44)))
        $current=Get-ScoliaInstitutionSignatory $role
        $label=New-Object ITMReadableLabel;$label.Text=$current.Label+' - nom complet';$label.Dock='Fill';$label.TextAlign='MiddleLeft';$table.Controls.Add($label,0,$row)
        $box=New-Object Windows.Forms.TextBox;$box.Dock='Fill';$box.MaxLength=160;$box.Text=$current.Name;$table.Controls.Add($box,1,$row);$fields[$role]=$box
    }
    $table.RowCount=15;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',44)));[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',48)))
    $logo=New-Button $table 'Choisir le logo PNG/JPG' 0 0 250;$logo.Dock='Fill';$table.SetCellPosition($logo,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,13)));$table.SetColumnSpan($logo,2)
    $logo.Add_Click({param($sender,$e)$picker=New-Object Windows.Forms.OpenFileDialog;$picker.Filter='Images|*.png;*.jpg;*.jpeg';try{if($picker.ShowDialog($sender.FindForm()) -eq 'OK'){$sender.Tag=$picker.FileName;$sender.Text='Logo : '+[IO.Path]::GetFileName($picker.FileName)}}finally{$picker.Dispose()}})
    $save=New-Button $table 'Enregistrer et appliquer' 0 0 250 $true;$save.Dock='Fill';$table.SetCellPosition($save,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,14)));$table.SetColumnSpan($save,2)
    $save.Tag=[pscustomobject]@{Fields=$fields;Logo=$logo};$save.Add_Click({param($sender,$e)try{
        $f=$sender.Tag.Fields;$profile=Get-ITMDefaultInstitution;foreach($name in @('Name','Subtitle','Address')){$profile.$name=$f[$name].Text.Trim()}
        $profile.Logo=$script:institutionProfile.Logo
        if($script:institutionProfile.PSObject.Properties['SignatoriesByInstitution']){$profile.SignatoriesByInstitution=$script:institutionProfile.SignatoriesByInstitution}
        $names=[pscustomobject]@{Prefet=$f.Prefet.Text;Proviseur=$f.Proviseur.Text;Secretaire=$f.Secretaire.Text}
        $profile=Set-ScoliaInstitutionSignatories $profile $names
    $profile.Header=[string[]]@(for($i=0;$i -lt 7;$i++){
        $headerKey='Header' + [string]$i
        $f[$headerKey].Text.Trim()
    })
        Save-ITMInstitution $profile ([string]$sender.Tag.Logo.Tag);$sender.FindForm().DialogResult='OK'
    }catch{App-Error $_}})
    Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Build-ITMInstitutionPage($page){
    [void](New-Label $page 'Personnalisation de l''établissement' 24 20 920 36 16 $true)
    [void](New-Label $page 'Le nom, le logo, l''adresse et les mentions sont partagés par la connexion, les cartes et le code QR. Les noms du préfet, du proviseur et du secrétaire alimentent les documents selon la fonction demandée. Les classes et leurs frais sont gérés dans la base connectée.' 24 70 920 80 10)
    $edit=New-Button $page 'Identité, logo et responsables' 24 166 320 $true;$edit.Add_Click({try{Show-ITMInstitutionEditor}catch{App-Error $_}})
    $signers=New-Button $page 'Responsables et signatures' 370 166 360;$signers.Add_Click({try{Show-ITMInstitutionEditor}catch{App-Error $_}})
    $classes=New-Button $page 'Ajouter une classe et ses frais' 24 226 320;$classes.Add_Click({try{Show-Promotion}catch{App-Error $_}})
    $list=New-Button $page 'Gérer les classes et les montants' 370 226 360;$list.Add_Click({$script:adminTabs.SelectedTab=$script:adminPages['Promotions']})
    Enable-ITMResponsivePanel $page 1000
}
