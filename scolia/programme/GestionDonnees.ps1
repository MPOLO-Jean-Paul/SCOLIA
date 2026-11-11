function Remember-ITMCurrentWorkbook {
    if(-not $script:book){throw 'Connectez d''abord le classeur à mémoriser.'}
    if(-not (Save-ConnectionChoice $script:book.FullName)){throw 'La connexion n''a pas pu être mémorisée.'}
    if(-not (Set-ITMLocationPreference 'ConnexionAutomatique' 'oui')){throw 'Windows a refusé de mémoriser le réglage de reconnexion.'}
    $script:rememberedPath=[string]$script:book.FullName
    if($script:dataAuto){$script:dataAuto.Checked=$true}
}
# Workbook creation only works on a NEW copy. Current workbooks are never cleared.
function Get-ITMSourceLastCell($sheet,[int]$order=1) {
    return $sheet.Cells.Find('*',$sheet.Cells.Item(1,1),-4123,2,$order,2,$false,$false,$false)
}
function New-ITMBlankSource([string]$name) {
    Assert-ITMPermission 'Administrer'
    $name=$name.Trim();if($name.EndsWith('.xlsm',[StringComparison]::OrdinalIgnoreCase)){$name=$name.Substring(0,$name.Length-5)}
    if(-not $name -or $name -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' -or $name.EndsWith('.') -or $name -in @('.','..') -or $name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $name.Contains('/') -or $name.Contains('\')){throw 'Donnez un nom de fichier simple, sans chemin.'}
    $root=[IO.Path]::GetDirectoryName($PSScriptRoot);$dir=Join-Path $root 'sources';[void][IO.Directory]::CreateDirectory($dir)
    $final=Join-Path $dir ($name+'.xlsm');if([IO.File]::Exists($final)){throw 'Ce classeur existe déjà dans sources. Choisissez un autre nom.'}
    $temp=Join-Path $dir ('creation-'+[Guid]::NewGuid().ToString('N')+'.xlsm')
    $prepared=[bool]$script:book;$excel=$null;$copy=$null;$published=$false
    try{
        if($prepared){[void]$script:book.SaveCopyAs($temp)}else{[IO.File]::Copy((Join-Path $root 'ressources/FICHIER_SOURCE.xlsm'),$temp,$false)}
        $excel=New-Object -ComObject Excel.Application;$excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
        $copy=$excel.Workbooks.Open($temp,0,$false);if($copy.ReadOnly){throw 'La copie est en lecture seule.'}
        foreach($required in @('PARAMETRES','PROMOTIONS','APPRENANTS','PAIEMENTS','SITUATION','ACCUEIL','FORM_PAIEMENT','FICHE_APPRENANT')){
            try{$sheet=$copy.Worksheets.Item($required)}catch{throw ('Feuille manquante dans le modèle : '+$required)}
        }
        $excel.Calculation=-4135
        foreach($sheetName in @('APPRENANTS','PAIEMENTS')){
            $sheet=$copy.Worksheets.Item($sheetName);$sheet.Unprotect()
            $header=4;if($sheet.ListObjects.Count){$header=[int]$sheet.ListObjects.Item(1).HeaderRowRange.Row}
            if($header -eq 3){[void]$sheet.Rows.Item(3).Insert(-4121);$header=4}
            if($header -notin @(3,4)){throw ('En-tête inattendu dans '+$sheetName+'. Création interrompue.')}
            $last=Get-ITMSourceLastCell $sheet;$lastColumn=Get-ITMSourceLastCell $sheet 2
            $cols=[Math]::Max($(if($sheetName -eq 'APPRENANTS'){15}else{14}),[int]$lastColumn.Column)
            $bottom=[Math]::Max($header+1,[int]$last.Row)
            $range=$sheet.Range($sheet.Cells.Item($header+1,1),$sheet.Cells.Item($bottom,$cols));[void]$range.ClearContents()
            foreach($table in $sheet.ListObjects){[void]$table.Resize($sheet.Range($sheet.Cells.Item($header,1),$sheet.Cells.Item($header+1,$table.Range.Columns.Count)))}
            [void]$range.ClearContents()
            if($excel.WorksheetFunction.CountA($range) -ne 0){throw ('Des données subsistent dans '+$sheetName+'. Création annulée.')}
        }
        try{$journal=$copy.Worksheets.Item('JOURNAL_ITM')}catch{$journal=$copy.Worksheets.Add();$journal.Name='JOURNAL_ITM'}
        $last=Get-ITMSourceLastCell $journal
        if($last -and $last.Row -ge 2){[void]$journal.Range($journal.Cells.Item(2,1),$journal.Cells.Item($last.Row,9)).ClearContents()}
        $headers=@('Date','Utilisateur','Action','Matricule','Numero','Avant','Apres','Motif','Annee')
        for($i=0;$i -lt $headers.Count;$i++){$journal.Cells.Item(1,$i+1).Value2=$headers[$i]}
        foreach($property in @('ITM_DataSet','ITM_SegmentCount','ITM_AnneeCloturee')){try{$copy.CustomDocumentProperties.Item($property).Delete()}catch{}}
        # Retain promotion/fee configuration and every template header, formula, macro and form.
        [void]$excel.CalculateFull();[void]$copy.Save();[void]$copy.Close($false);$copy=$null
        [IO.File]::Move($temp,$final);$published=$true
        return [pscustomobject]@{Path=$final;Prepared=$prepared}
    }finally{
        if($copy){try{$copy.Close($false)}catch{}}
        if($excel){try{$excel.Quit()}catch{};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}
        if(-not $published -and [IO.File]::Exists($temp)){[IO.File]::Delete($temp)}
    }
}
function Open-ITMSourceInstaller([string]$path) {
    Assert-ITMPermission 'Administrer'
    $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Installer.ps1')+'" -Classeur "'+$path+'"'
    [void](Start-Process -FilePath $ps -ArgumentList $arguments)
}
function Refresh-ITMDataPage {
    if(-not $script:dataNote){return}
    $path=$(if($script:book){[string]$script:book.FullName}else{''})
    $remembered=Get-ITMLocationPreference 'Classeur'
    $script:dataNote.Text=$(if($path){'Connecté : '+$path}else{'Aucun classeur connecté.'})+"`r`n"+$(if($remembered){'Mémorisé : '+$remembered}else{'Aucun classeur mémorisé.'})
    $script:dataAuto.Checked=((Get-ITMLocationPreference 'ConnexionAutomatique') -ne 'non')
    $rows=@(foreach($book in @($script:storeBooks)){
        foreach($sheet in $book.Worksheets){
            $last=Get-ITMSourceLastCell $sheet;$column=Get-ITMSourceLastCell $sheet 2
            [pscustomobject]@{Classeur=$book.Name;Feuille=$sheet.Name;DerniereLigne=$(if($last){$last.Row}else{0});Colonnes=$(if($column){$column.Column}else{0})}
        }
    })
    if(-not $script:storeBooks -and $script:book){
        $rows=@(foreach($sheet in $script:book.Worksheets){$last=Get-ITMSourceLastCell $sheet;$column=Get-ITMSourceLastCell $sheet 2;[pscustomobject]@{Classeur=$script:book.Name;Feuille=$sheet.Name;DerniereLigne=$(if($last){$last.Row}else{0});Colonnes=$(if($column){$column.Column}else{0})}})
    }
    Set-Grid $script:dataSheets $rows @(@('Classeur','Classeur'),@('Feuille','Feuille'),@('DerniereLigne','Dernière ligne renseignée'),@('Colonnes','Colonnes présentes'))
}
function Build-ITMDataPage($page) {
    [void](New-Label $page 'Classeurs et feuilles' 24 20 930 34 14 $true)
    $script:dataNote=New-Label $page 'Aucun classeur connecté.' 24 66 930 80 10
    $script:dataAuto=New-Object ITMReadableCheckBox;$script:dataAuto.Text='Reconnecter le classeur mémorisé au démarrage';$script:dataAuto.SetBounds(24,154,930,32);$page.Controls.Add($script:dataAuto)
    $script:dataRemember=New-Button $page 'Mémoriser ce classeur' 24 198 280
    $script:dataReopen=New-Button $page 'Ouvrir le classeur mémorisé' 318 198 340
    $script:dataRemember.Add_Click({
        if($script:busy){return}
        try{Remember-ITMCurrentWorkbook;Refresh-ITMDataPage;$script:status.Text='Classeur mémorisé : reconnexion automatique activée.'}catch{App-Error $_}
    })
    $script:dataAuto.Add_Click({
        if($script:busy){return}
        if(-not (Set-ITMLocationPreference 'ConnexionAutomatique' $(if($script:dataAuto.Checked){'oui'}else{'non'}))){
            $script:dataAuto.Checked=((Get-ITMLocationPreference 'ConnexionAutomatique') -ne 'non')
            App-Error 'Windows a refusé de mémoriser le réglage de reconnexion.'
        }
    })
    $script:dataReopen.Add_Click({
        if($script:busy){return}
        $path=Get-ITMLocationPreference 'Classeur'
        if(-not $path){App-Error 'Aucun classeur mémorisé.';return}
        Do-Work {Connect-Book $path;Refresh-ITMDataPage} 'Connexion au classeur mémorisé...'
    })
    [void](New-Label $page 'Nouveau classeur dans le dossier sources' 24 260 930 30 11 $true)
    $script:dataNewName=New-Object Windows.Forms.TextBox;$script:dataNewName.SetBounds(24,304,420,30);$script:dataNewName.Text='Classeur_ITM_'+(Get-Date -Format 'yyyyMMdd_HHmmss');$page.Controls.Add($script:dataNewName)
    $script:dataGenerate=New-Button $page 'Générer et connecter' 460 300 300 $true
    [void](New-Label $page 'Le nouveau classeur est vide d''apprenants et de paiements. Les promotions et frais du modèle sont conservés.' 24 350 930 56 10)
    $script:dataGenerate.Add_Click({
        if($script:busy){return}
        Do-Work {
            $result=New-ITMBlankSource $script:dataNewName.Text
            $script:committedThisAction=$true;$script:committedSourcePath=$result.Path
            $script:lastCreatedSource=$result.Path;$script:lastCreatedPrepared=[bool]$result.Prepared
            if(-not (Set-ITMLocationPreference 'DernierClasseurCree' $result.Path)){throw 'Windows a refusé de mémoriser le chemin du nouveau classeur.'}
            if(-not (Set-ITMLocationPreference 'DernierClasseurCreePrepare' $(if($result.Prepared){'oui'}else{'non'}))){throw 'Windows a refusé de mémoriser l''état du nouveau classeur.'}
            if($result.Prepared){Connect-Book $result.Path;if($script:scolia){Apply-ScoliaWorkbookConfiguration};$script:status.Text='Nouveau classeur créé et connecté.';Refresh-ITMDataPage}
            else{Open-ITMSourceInstaller $result.Path;$script:status.Text='Classeur créé dans sources. Terminez sa préparation dans l''installateur ouvert.'}
            $script:dataNewName.Text='Classeur_ITM_'+(Get-Date -Format 'yyyyMMdd_HHmmss')
        } 'Création du nouveau classeur...'
    })
    $script:dataRefresh=New-Button $page 'Actualiser les feuilles' 24 420 280
    $script:dataRefresh.Add_Click({Do-Work {Refresh-ITMDataPage} 'Lecture des feuilles...'})
    $script:dataLastCreated=New-Button $page 'Ouvrir le dernier créé' 318 420 340
    $script:dataLastCreated.Add_Click({
        if($script:busy){return}
        $path=$script:lastCreatedSource
        $prepared=$script:lastCreatedPrepared
        if(-not $path){$path=Get-ITMLocationPreference 'DernierClasseurCree';$prepared=((Get-ITMLocationPreference 'DernierClasseurCreePrepare') -eq 'oui')}
        if(-not $path){App-Error 'Aucun nouveau classeur enregistré.';return}
        if(-not (Test-Path -LiteralPath $path -PathType Leaf)){App-Error ('Le classeur créé est introuvable : '+$path+'. Utilisez Classeur existant pour choisir son nouvel emplacement.');return}
        $script:sourceToReopen=$path;$script:sourceToReopenPrepared=$prepared
        Do-Work {
            if($script:sourceToReopenPrepared){Connect-Book $script:sourceToReopen;Refresh-ITMDataPage}
            else{Open-ITMSourceInstaller $script:sourceToReopen;$script:status.Text='Préparation du classeur existant ouverte.'}
        } 'Ouverture du dernier classeur créé...'
    })
    $script:dataCleanup=New-Button $page 'Nettoyer les sauvegardes' 672 420 282
    $script:dataCleanup.Destructive=$true;$script:dataCleanup.Add_Click({try{Show-ITMBackupCleanup}catch{App-Error $_}})
    $script:dataSheets=New-Grid $page 24 478 930 230
    $script:main.Add_Shown({Refresh-ITMDataPage})
}
