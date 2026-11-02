# Run on Windows with Windows PowerShell 5.1 -STA. No Excel writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies @('System.Windows.Forms','System.Drawing')
Add-Type -Path (Join-Path $PSScriptRoot 'CartesExamens.cs') -ReferencedAssemblies @('System.Drawing','System.Xml')
# Load the actual feature: header settings are read, never written by these tests.
function Test-ITMPermission($permission){return $true}
function Get-ITMInstitutionName {return 'Institut de démonstration'}
function Get-ITMInstitutionLogo {return (Join-Path $PSScriptRoot 'Assets/Logo_ITM_CD_Transparent.png')}
function Key($v){return ([string]$v).Trim().ToUpperInvariant()}
function Promo($v){return [ITMExamCards]::PromotionCode([string]$v)}
function Set-Choices($combo,$values,$selected){$combo.Items.Clear();[void]$combo.Items.AddRange([object[]]$values);$combo.SelectedItem=$selected}
function New-Label($p,$text,$x,$y,$w,$h,$size=10,$bold=$false){$c=New-Object ITMReadableLabel;$c.Text=$text;$c.SetBounds($x,$y,$w,$h);$p.Controls.Add($c);return $c}
function New-Combo($p,$x,$y,$w){$c=New-Object ITMThemeComboBox;$c.DropDownStyle='DropDownList';$p.Controls.Add($c);return $c}
function New-Button($p,$text,$x,$y,$w,$primary=$false){$c=New-Object ITMActionButton;$c.Text=$text;$p.Controls.Add($c);return $c}
function New-Grid($p,$x,$y,$w,$h){$g=New-Object Windows.Forms.DataGridView;$g.AllowUserToAddRows=$false;$g.RowHeadersVisible=$false;$p.Controls.Add($g);return $g}
function Set-ITMGridReadability($grid){}
function Get-ITMDpiScale($p){return 1}
. (Join-Path $PSScriptRoot 'Scolarite.ps1')
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ITM-cartes-test-'+[Guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($temp)
$script:main=New-Object Windows.Forms.Form;$page=New-Object Windows.Forms.Panel;$page.Dock='Fill';$script:main.Controls.Add($page);$script:busy=$false;$script:data=$null
try{
    Build-ITMSchoolPage $page
    $script:main.Show();[Windows.Forms.Application]::DoEvents()
    $script:data=[pscustomobject]@{promotions=@([pscustomobject]@{Code='TLM1';Annee='2026-2027'},[pscustomobject]@{Code='ACC1';Annee='2026-2027'});learners=@(
        [pscustomobject]@{Nom='A';PostNom='B';Prenom='C';Promotion='TLM1';Matricule='DEMO1';Statut='Actif'},
        [pscustomobject]@{Nom='D';PostNom='E';Prenom='F';Promotion='ACC1';Matricule='DEMO2';Statut='Actif'},
        [pscustomobject]@{Nom='G';PostNom='H';Prenom='I';Promotion='TLM1';Matricule='DEMO3';Statut='Sorti'},
        [pscustomobject]@{Nom='J';PostNom='K';Prenom='';Promotion='TLM1';Matricule='DEMO4';Statut='Actif'},
        [pscustomobject]@{Nom='L';PostNom='M';Prenom='N';Promotion='TLM1';Matricule='DEMO5';Statut='Actif'},
        [pscustomobject]@{Nom='O';PostNom='P';Prenom='Q';Promotion='ACC1';Matricule='DEMO5';Statut='Actif'})}
    Refresh-ITMSchoolCards
    if($script:schoolGrid.Rows.Count -ne 2 -or $script:schoolExcluded -ne 3){throw 'Incomplets/doublons/statuts mal filtrés.'}
    $script:schoolPromo.SelectedItem='TLM1';if($script:schoolGrid.Rows.Count -ne 1){throw 'Filtre de promotion incorrect.'}
    $script:schoolGrid.Rows[0].Cells['Choix'].Value=$false;Update-ITMCardSelection
    $script:schoolPromo.SelectedItem='ACC1';$script:schoolPromo.SelectedItem='TLM1'
    if([bool]$script:schoolGrid.Rows[0].Cells['Choix'].Value){throw 'Choix perdu pendant le changement de promotion.'}
    $script:schoolInactive.Checked=$true;if($script:schoolGrid.Rows.Count -ne 2){throw 'Inclusion des sortis incorrecte.'}
    $script:schoolSearch.Text='DEMO3';if($script:schoolGrid.Rows.Count -ne 1){throw 'Recherche incorrecte.'}
    $script:schoolSearch.Text='';$script:schoolAll.PerformClick();$script:schoolCopies.Value=2
    if([ITMExamCards]::CardCount((Get-ITMExamBatch)) -ne 4){throw 'Nombre de cartes/copies incorrect.'}
    $script:main.Show();foreach($width in @(360,680,1000,1600)){$script:main.ClientSize=New-Object Drawing.Size($width,740);[Windows.Forms.Application]::DoEvents();Update-ITMSchoolLayout $page;foreach($c in @($script:schoolFields)+@($script:schoolLabels)+@($script:schoolButtons)){if($c.Left -lt 0 -or $c.Right -gt $page.ClientSize.Width){throw ('Contenu coupé à '+$width)}}}
    $batch=Get-ITMExamBatch;$batch.Header=[string[]]@('RÉPUBLIQUE DÉMOCRATIQUE DU CONGO','MINISTÈRE','DIRECTION','DIVISION','VILLE ET ZONE','INSTITUT TECHNIQUE MÉDICAL CŒUR DE DIEU','ADRESSE');$batch.Copies=1
    foreach($n in @(1,7,8,9,16,1001)){
        $batch.Learners=[ITMExamLearner[]]@(for($i=0;$i -lt $n;$i++){$x=New-Object ITMExamLearner;$x.Nom='DÉMO';$x.PostNom='TEST';$x.Prenom='Élisabeth';$x.Promotion='TLM1';$x.Matricule='DEMO'+$i;$x})
        $pdf=Join-Path $temp ($n.ToString()+'.pdf');$docx=Join-Path $temp ($n.ToString()+'.docx');[ITMExamCards]::ExportPdf($batch,$pdf);[ITMExamCards]::ExportWord($batch,$docx)
        $pages=[Math]::Ceiling($n/8.0);if([ITMExamCards]::PageCount($batch) -ne $pages -or ([regex]::Matches([IO.File]::ReadAllText($pdf),'/Type /Page /Parent')).Count -ne $pages){throw ('Pagination erronée pour '+$n)}
    }
    Write-Output 'PASS : sélection, filtres, mémorisation des coches, copies, largeurs 360–1600, PDF/Word et pagination de 1 à 1001 cartes.'
}finally{$script:main.Dispose();Remove-Item -LiteralPath $temp -Recurse -Force}
