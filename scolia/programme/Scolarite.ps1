if(-not (Test-Path function:Import-ITMCompiledSource)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
# Examination access cards. No Excel writes and no payment eligibility inference.
if(-not ('ITMExamCards' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'CartesExamens.cs') -ReferencedAssemblies @('System.Drawing','System.Xml')}
$script:schoolHeader=@('RÉPUBLIQUE DÉMOCRATIQUE DU CONGO','MINISTÈRE DE L''ENSEIGNEMENT PUBLIC, HYGIÈNE ET PRÉVOYANCE SOCIALE','DIRECTION DE L''ENSEIGNEMENT DES SCIENCES DE SANTÉ','DIVISION PROVINCIALE DU HAUT-KATANGA','VILLE DE LUBUMBASHI / ZONE DE SANTÉ DE LUBUMBASHI','INSTITUT TECHNIQUE MÉDICAL CŒUR DE DIEU','N° 1, Av. EDGE | Q. Kamatete | C. Annexe')
$script:schoolHeaderKey='HKCU:\Software\ITMCD\Secretariat\Scolarite'
try{$h=(Get-ItemProperty -LiteralPath $script:schoolHeaderKey -Name Entete -ErrorAction Stop).Entete | ConvertFrom-Json;if(@($h).Count -eq 7 -and @($h | Where-Object {[string]::IsNullOrWhiteSpace([string]$_)}).Count -eq 0){$script:schoolHeader=[string[]]$h}}catch{}
$script:schoolChoices=@{};$script:schoolUpdating=$false;$script:schoolAutoAcademic=$true
$script:schoolRosterCache=$null
$script:schoolSearchTimer=New-Object Windows.Forms.Timer;$script:schoolSearchTimer.Interval=45
$script:schoolSearchTimer.Add_Tick({$script:schoolSearchTimer.Stop();if($script:schoolGrid -and -not $script:schoolGrid.IsDisposed){Refresh-ITMSchoolCards -SkipDisciplineSync}})
function Schedule-ITMSchoolCards {$script:schoolSearchTimer.Stop();$script:schoolSearchTimer.Start()}
function Get-ITMSchoolRosterCache {
    $source=$(if($script:data){$script:data.learners}else{$null})
    if($script:schoolRosterCache -and [object]::ReferenceEquals($script:schoolRosterCache.Source,$source)){return $script:schoolRosterCache}
    $counts=@{};$promos=@{}
    foreach($a in @($source)){$key=Key ([string]$a.Matricule);if($key){if($counts.ContainsKey($key)){$counts[$key]++}else{$counts[$key]=1}};if($a.Promotion){$promos[[string]$a.Promotion]=$true}}
    $duplicates=@{};foreach($key in $counts.Keys){if($counts[$key] -gt 1){$duplicates[$key]=$true}}
    $script:schoolRosterCache=[pscustomobject]@{Source=$source;Sorted=@($source|Sort-Object Nom,PostNom,Prenom,Matricule);Duplicates=$duplicates;Promotions=@($promos.Keys|Sort-Object)}
    return $script:schoolRosterCache
}
function Get-ITMCardFolder {$p=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'exports\cartes';[void][IO.Directory]::CreateDirectory($p);return $p}
function Update-ITMCardSelection {
    if(-not $script:schoolGrid -or $script:schoolUpdating){return}
    $n=0;foreach($r in $script:schoolGrid.Rows){$value=[bool]$r.Cells['Choix'].Value;$script:schoolChoices[[string]$r.Tag.Matricule]=$value;if($value){$n++}}
    $cards=$n*[int]$script:schoolCopies.Value
    $script:schoolCount.Text="$n apprenant(s) coché(s) — $cards carte(s), $([Math]::Ceiling($cards/8.0)) page(s) A4. $($script:schoolExcluded) dossier(s) écarté(s) : identité incomplète ou matricule répété."
    foreach($b in @($script:schoolPreview,$script:schoolPdf,$script:schoolWord,$script:schoolPrint)){$b.Enabled=($n -gt 0 -and -not $script:busy -and (Test-ITMPermission 'Exporter'))}
    if($script:ribbonFlow -and $script:ribbonPage -eq $script:currentPage -and $script:currentPage -eq 'Exports'){Update-ITMRibbonState}
}
function Refresh-ITMSchoolCards([switch]$SkipDisciplineSync) {
    if(-not $SkipDisciplineSync -and (Test-Path function:Sync-ScoliaDisciplineSnapshot)){[void](Sync-ScoliaDisciplineSnapshot)}
    if(-not $script:schoolGrid){return};$script:schoolUpdating=$true
    try{
        $current=[string]$script:schoolPromo.SelectedItem
        $roster=Get-ITMSchoolRosterCache
        $items=@('Toutes les promotions')+@($roster.Promotions)
        Set-Choices $script:schoolPromo $items $(if($items -contains $current){$current}else{'Toutes les promotions'})
        if($script:schoolAutoAcademic){$years=@($script:data.promotions | Where-Object {$_.Annee -and ($script:schoolPromo.SelectedItem -eq 'Toutes les promotions' -or (Promo $_.Code) -eq (Promo $script:schoolPromo.SelectedItem))} | ForEach-Object {$_.Annee} | Select-Object -Unique);if($years.Count -eq 1){$script:schoolAcademic.Text=[string]$years[0]}}
        $script:schoolGrid.Rows.Clear();$script:schoolExcluded=0
        $duplicates=$roster.Duplicates
        foreach($a in @($roster.Sorted)){
            if(@('Nom','PostNom','Prenom','Promotion','Matricule' | Where-Object {[string]::IsNullOrWhiteSpace([string]$a.$_)}).Count -or $duplicates.ContainsKey((Key $a.Matricule))){$script:schoolExcluded++;continue}
            if($script:schoolPromo.SelectedItem -ne 'Toutes les promotions' -and (Promo $a.Promotion) -ne (Promo $script:schoolPromo.SelectedItem)){continue}
            if(-not $script:schoolInactive.Checked -and (Key $a.Statut) -in @('SORTI','SORTIE','SUSPENDU','SUSPENDUE')){continue}
            if($script:schoolSearch.Text.Trim() -and (Key ($a.Nom+' '+$a.PostNom+' '+$a.Prenom+' '+$a.Matricule)).IndexOf((Key $script:schoolSearch.Text.Trim()),[StringComparison]::Ordinal) -lt 0){continue}
            $chosen=$true;if($script:schoolChoices.ContainsKey([string]$a.Matricule)){$chosen=$script:schoolChoices[[string]$a.Matricule]}
            $i=$script:schoolGrid.Rows.Add([object[]]@($chosen,$a.Nom,$a.PostNom,$a.Prenom,(Promo $a.Promotion),$a.Matricule));$script:schoolGrid.Rows[$i].Tag=$a
        }
    }finally{$script:schoolUpdating=$false}
    Set-ITMGridReadability $script:schoolGrid
    Update-ITMCardSelection
}
function Get-ITMExamBatch {
    [void]$script:schoolGrid.EndEdit();$list=New-Object 'System.Collections.Generic.List[ITMExamLearner]'
    foreach($r in $script:schoolGrid.Rows){if([bool]$r.Cells['Choix'].Value){$a=$r.Tag;if(Test-Path function:Assert-ScoliaDisciplineAdmission){Assert-ScoliaDisciplineAdmission ([string]$a.Matricule)};$x=New-Object ITMExamLearner;foreach($field in @('Nom','PostNom','Prenom','Promotion','Matricule')){$x.$field=[string]$a.$field};$list.Add($x)}}
    $b=New-Object ITMExamBatch;$b.ResourceFolder=Join-Path $PSScriptRoot 'Assets';$b.Institution=Get-ITMInstitutionName;$b.Learners=$list.ToArray();$b.Header=[string[]]$script:schoolHeader.Clone()
    $b.Session='SESSION '+([Globalization.CultureInfo]::GetCultureInfo('fr-FR').DateTimeFormat.GetMonthName($script:schoolMonth.SelectedIndex+1)).ToUpperInvariant()+' '+[int]$script:schoolYear.Value
    $b.AcademicYear=$script:schoolAcademic.Text.Trim();$b.Copies=[int]$script:schoolCopies.Value;$b.CutMarks=$script:schoolCut.Checked
    $b.BatchId=[Guid]::NewGuid().ToString('N').Substring(0,12).ToUpperInvariant();$b.LogoPath=Get-ITMInstitutionLogo
    [ITMExamCards]::Validate($b);return $b
}
function Save-ITMCardExport([string]$format){
    Assert-ITMPermission 'Exporter'
    $batch=Get-ITMExamBatch;$d=New-Object Windows.Forms.SaveFileDialog;$d.InitialDirectory=Get-ITMCardFolder;$d.DefaultExt=$format;$d.AddExtension=$true;$d.OverwritePrompt=$true
    $d.Filter=$(if($format -eq 'pdf'){'Document PDF (*.pdf)|*.pdf'}else{'Document Word (*.docx)|*.docx'})
    $promo=([string]$script:schoolPromo.SelectedItem) -replace '[^a-zA-Z0-9_-]','_';$d.FileName='Cartes_'+$promo+'_'+$script:schoolYear.Value.ToString()+('_'+($script:schoolMonth.SelectedIndex+1))+'_'+$batch.BatchId+'.'+$format
    try{if($d.ShowDialog($script:main) -ne 'OK'){return};$script:cardExportBatch=$batch;$script:cardExportPath=$d.FileName;$script:cardExportFormat=$format
        Do-Work {
            if($script:cardExportFormat -eq 'pdf'){[ITMExamCards]::ExportPdf($script:cardExportBatch,$script:cardExportPath)}else{[ITMExamCards]::ExportWord($script:cardExportBatch,$script:cardExportPath)}
            $script:committedThisAction=$true
            $record=[pscustomobject]@{Date=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss');Session=$script:cardExportBatch.Session;Promotion=[string]$script:schoolPromo.SelectedItem;Cartes=[ITMExamCards]::CardCount($script:cardExportBatch);Pages=[ITMExamCards]::PageCount($script:cardExportBatch);Format=$script:cardExportFormat;Fichier=$script:cardExportPath;Lot=$script:cardExportBatch.BatchId}
            $script:lastCardExport=$script:cardExportPath
            try{$history=Join-Path (Get-ITMCardFolder) 'historique';[void][IO.Directory]::CreateDirectory($history);[IO.File]::WriteAllText((Join-Path $history ($record.Lot+'.json')),($record | ConvertTo-Json),[Text.UTF8Encoding]::new($true));$script:status.Text='Cartes exportées : '+$script:cardExportPath}catch{$script:status.Text='Export créé. Historique indisponible : '+$_.Exception.Message}
        } 'Création des cartes...'
    }finally{$d.Dispose()}
}
function Show-ITMCardPreview([bool]$print=$false){
    Assert-ITMPermission 'Exporter'
    $batch=Get-ITMExamBatch;$doc=[ITMExamCards]::CreatePrintDocument($batch)
    try{if($print){$d=New-Object Windows.Forms.PrintDialog;$d.Document=$doc;$d.UseEXDialog=$true;$d.AllowSomePages=$false;try{if($d.ShowDialog($script:main) -eq 'OK'){$doc.Print()}}finally{$d.Dispose()}}else{$d=New-Object Windows.Forms.PrintPreviewDialog;$d.Document=$doc;$d.Text=$batch.Session+' — '+[ITMExamCards]::CardCount($batch)+' cartes';$d.WindowState='Maximized';try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}}}finally{$doc.Dispose()}
}
function Show-ITMCardHeader { Show-ITMInstitutionEditor }
function Show-ITMCardHistory {
    $dir=Join-Path (Get-ITMCardFolder) 'historique';$rows=@();if(Test-Path -LiteralPath $dir){foreach($f in @(Get-ChildItem -LiteralPath $dir -Filter '*.json' | Sort-Object LastWriteTime -Descending)){try{$rows+=([IO.File]::ReadAllText($f.FullName) | ConvertFrom-Json)}catch{}}}
    $d=New-Object Windows.Forms.Form;$d.Text='Historique des cartes — double-cliquez pour ouvrir un export';$d.StartPosition='CenterParent';$d.Size=New-Object Drawing.Size(1000,550);$g=New-Grid $d 0 0 950 480;$g.Dock='Fill'
    Set-Grid $g $rows @(@('Date','Date'),@('Session','Session'),@('Promotion','Promotion'),@('Cartes','Cartes'),@('Pages','Pages'),@('Format','Format'),@('Fichier','Fichier'))
    $g.Add_CellDoubleClick({param($sender,$e)try{if($e.RowIndex -ge 0){$path=[string]$sender.Rows[$e.RowIndex].Cells['Fichier'].Value;if([IO.Path]::GetExtension($path) -notin @('.pdf','.docx') -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Cet export est introuvable. Le fichier a peut-être été déplacé.'};Start-Process -FilePath $path}}catch{App-Error $_}})
    Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{$d.Dispose()}
}
function Build-ITMSchoolPage($page){
    $script:schoolFields=@();$script:schoolLabels=@();$script:schoolButtons=@()
    foreach($text in @('Promotion','Recherche','Mois de session','Année de session','Année scolaire','Exemplaires par apprenant')){$script:schoolLabels+=New-Label $page $text 16 16 200 26 10}
    $script:schoolPromo=New-Combo $page 16 44 220;$script:schoolSearch=New-Object Windows.Forms.TextBox;$page.Controls.Add($script:schoolSearch)
    $script:schoolMonth=New-Combo $page 16 44 200;[void]$script:schoolMonth.Items.AddRange([object[]]@('Janvier','Février','Mars','Avril','Mai','Juin','Juillet','Août','Septembre','Octobre','Novembre','Décembre'));$script:schoolMonth.SelectedIndex=(Get-Date).Month-1
    $script:schoolYear=New-Object Windows.Forms.NumericUpDown;$script:schoolYear.Minimum=2000;$script:schoolYear.Maximum=2199;$script:schoolYear.Value=(Get-Date).Year;$page.Controls.Add($script:schoolYear)
    $script:schoolAcademic=New-Object Windows.Forms.TextBox;$script:schoolAcademic.MaxLength=40;$year=(Get-Date).Year;if((Get-Date).Month -lt 9){$year--};$script:schoolAcademic.Text="$year–$($year+1)";$page.Controls.Add($script:schoolAcademic)
    $script:schoolCopies=New-Object Windows.Forms.NumericUpDown;$script:schoolCopies.Minimum=1;$script:schoolCopies.Maximum=5;$script:schoolCopies.Value=1;$page.Controls.Add($script:schoolCopies)
    $script:schoolFields=@($script:schoolPromo,$script:schoolSearch,$script:schoolMonth,$script:schoolYear,$script:schoolAcademic,$script:schoolCopies)
    $script:schoolInactive=New-Object ITMReadableCheckBox;$script:schoolInactive.Text='Inclure les sortis et suspendus';$page.Controls.Add($script:schoolInactive)
    $script:schoolCut=New-Object ITMReadableCheckBox;$script:schoolCut.Text='Repères de découpe (PDF et impression)';$script:schoolCut.Checked=$true;$page.Controls.Add($script:schoolCut)
    $script:schoolCount=New-Label $page '' 16 150 950 40 10
    foreach($pair in @(@('schoolPreview','Aperçu des cartes'),@('schoolPdf','Exporter les cartes PDF'),@('schoolWord','Exporter les cartes Word'),@('schoolPrint','Imprimer les cartes'),@('schoolAll','Cocher les cartes'),@('schoolNone','Décocher les cartes'),@('schoolHeaderButton','En-tête des cartes'),@('schoolNow','Session actuelle'),@('schoolHistory','Historique des cartes'),@('schoolFolder','Dossier des cartes'),@('schoolGradesFolder','Grilles remplies'))){$b=New-Button $page $pair[1] 16 190 190;Set-Variable -Scope Script -Name $pair[0] -Value $b;$script:schoolButtons+=$b}
    $script:schoolGrid=New-Grid $page 16 300 950 380;$script:schoolGrid.ReadOnly=$false
    $check=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$check.Name='Choix';$check.HeaderText='Carte';$check.Width=65;[void]$script:schoolGrid.Columns.Add($check)
    foreach($col in @(@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Promotion','Classe'),@('Matricule','Matricule'))){$c=New-Object Windows.Forms.DataGridViewTextBoxColumn;$c.Name=$col[0];$c.HeaderText=$col[1];$c.Width=160;$c.ReadOnly=$true;[void]$script:schoolGrid.Columns.Add($c)}
    $script:schoolGrid.Add_CurrentCellDirtyStateChanged({if($script:schoolGrid.IsCurrentCellDirty){[void]$script:schoolGrid.CommitEdit([Windows.Forms.DataGridViewDataErrorContexts]::Commit)}})
    $script:schoolGrid.Add_CellValueChanged({Update-ITMCardSelection})
    $script:schoolGrid.Add_DataError({param($sender,$e)$e.ThrowException=$false;$e.Cancel=$true})
    $script:schoolAcademic.Add_TextChanged({if(-not $script:schoolUpdating){$script:schoolAutoAcademic=$false}})
    $script:schoolPromo.Add_SelectedIndexChanged({if(-not $script:schoolUpdating){Refresh-ITMSchoolCards}});$script:schoolSearch.Add_TextChanged({if(-not $script:schoolUpdating){Schedule-ITMSchoolCards}});$script:schoolInactive.Add_CheckedChanged({Refresh-ITMSchoolCards});$script:schoolCopies.Add_ValueChanged({Update-ITMCardSelection})
    $script:schoolAll.Add_Click({$script:schoolUpdating=$true;try{foreach($r in $script:schoolGrid.Rows){$r.Cells['Choix'].Value=$true}}finally{$script:schoolUpdating=$false};Update-ITMCardSelection});$script:schoolNone.Add_Click({$script:schoolUpdating=$true;try{foreach($r in $script:schoolGrid.Rows){$r.Cells['Choix'].Value=$false}}finally{$script:schoolUpdating=$false};Update-ITMCardSelection})
    $script:schoolPreview.Add_Click({try{Show-ITMCardPreview}catch{App-Error $_}});$script:schoolPrint.Add_Click({try{Show-ITMCardPreview $true}catch{App-Error $_}})
    $script:schoolPdf.Add_Click({try{Save-ITMCardExport 'pdf'}catch{App-Error $_}});$script:schoolWord.Add_Click({try{Save-ITMCardExport 'docx'}catch{App-Error $_}})
    $script:schoolHeaderButton.Add_Click({try{Show-ITMCardHeader}catch{App-Error $_}});$script:schoolNow.Add_Click({$script:schoolMonth.SelectedIndex=(Get-Date).Month-1;$script:schoolYear.Value=(Get-Date).Year})
    $script:schoolHistory.Add_Click({try{Show-ITMCardHistory}catch{App-Error $_}});$script:schoolFolder.Add_Click({try{Start-Process -FilePath 'explorer.exe' -ArgumentList ('"'+(Get-ITMCardFolder)+'"')}catch{App-Error $_}})
    $script:schoolGradesFolder.Add_Click({try{Start-Process -FilePath (Get-ITMGradesFolder)}catch{App-Error $_}})
    Refresh-ITMSchoolCards
}
function Update-ITMSchoolLayout($page){
    $q=Get-ITMDpiScale $page;$pad=[int](16*$q);$gap=[int](8*$q);$width=[Math]::Max(100,$page.ClientSize.Width-2*$pad-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
    $cols=[Math]::Min(3,[Math]::Max(1,[int][Math]::Floor($width/(230*$q))));$cell=[int](($width-($cols-1)*$gap)/$cols);$y=$pad
    for($i=0;$i -lt 6;$i+=$cols){$height=0;for($j=$i;$j -lt [Math]::Min(6,$i+$cols);$j++){$label=$script:schoolLabels[$j];$h=[Windows.Forms.TextRenderer]::MeasureText($label.Text,$label.Font,(New-Object Drawing.Size($cell,2000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height+4;$height=[Math]::Max($height,$h)}
        $fieldHeight=0;for($j=$i;$j -lt [Math]::Min(6,$i+$cols);$j++){$x=$pad+($j-$i)*($cell+$gap);$script:schoolLabels[$j].SetBounds($x,$y,$cell,$height);$f=$script:schoolFields[$j];$h=[Math]::Max([int](28*$q),$f.PreferredSize.Height);$f.SetBounds($x,$y+$height,$cell,$h);$fieldHeight=[Math]::Max($fieldHeight,$h)};$y+=$height+$fieldHeight+$gap}
    foreach($c in @($script:schoolInactive,$script:schoolCut,$script:schoolCount)){$h=[Windows.Forms.TextRenderer]::MeasureText($c.Text,$c.Font,(New-Object Drawing.Size($width,2000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height+8;$c.SetBounds($pad,$y,$width,$h);$y+=$h+$gap}
    $buttons=@($script:schoolButtons | Where-Object {-not ((Test-Path function:Test-ScoliaRibbonSource) -and (Test-ScoliaRibbonSource $_))})
    $buttonCols=[Math]::Min(5,[Math]::Max(1,[int][Math]::Floor($width/(185*$q))));$bw=[int](($width-($buttonCols-1)*$gap)/$buttonCols)
    for($i=0;$i -lt $buttons.Count;$i+=$buttonCols){$bh=[int](36*$q);for($j=$i;$j -lt [Math]::Min($buttons.Count,$i+$buttonCols);$j++){$b=$buttons[$j];$bh=[Math]::Max($bh,([Windows.Forms.TextRenderer]::MeasureText($b.Text,$b.Font,(New-Object Drawing.Size(($bw-12),2000)),[Windows.Forms.TextFormatFlags]::WordBreak).Height+14))};for($j=$i;$j -lt [Math]::Min($buttons.Count,$i+$buttonCols);$j++){$buttons[$j].SetBounds(($pad+($j-$i)*($bw+$gap)),$y,$bw,$bh)};$y+=$bh+$gap}
    $gh=[Math]::Max([int](200*$q),$page.ClientSize.Height-$y-$pad);$script:schoolGrid.SetBounds($pad,$y,$width,$gh);$page.AutoScrollMinSize=New-Object Drawing.Size(0,($y+$gh+$pad))
}

function Build-ScoliaSchoolOverview($page){
    $note=New-Label $page 'Connectez les données scolaires pour afficher la recherche globale.' 24 24 900 60 11
    $note.Dock='Top';$note.AutoSize=$true;$note.Padding=New-Object Windows.Forms.Padding(24)
    $script:schoolGrades=$null
}
