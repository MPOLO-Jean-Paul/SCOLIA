# Native Excel regression: creates and removes only its own blank test source.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'GestionDonnees.ps1')
$script:book=$null
$template=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'ressources/FICHIER_SOURCE.xlsm'
$before=(Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash
$name='TEST_CREATION_'+[Guid]::NewGuid().ToString('N');$result=$null;$excel=$null;$book=$null
try{
    $result=New-ITMBlankSource $name
    if($result.Prepared){throw 'Source non preparee attendue pour ce test.'}
    if((Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash -ne $before){throw 'Le modele original a ete modifie.'}
    $excel=New-Object -ComObject Excel.Application;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
    $book=$excel.Workbooks.Open($result.Path,0,$true)
    foreach($sheetName in @('APPRENANTS','PAIEMENTS')){
        $sheet=$book.Worksheets.Item($sheetName)
        if($sheet.ListObjects.Item(1).HeaderRowRange.Row -ne 4){throw ('En-tete non conforme : '+$sheetName)}
        $last=Get-ITMSourceLastCell $sheet;$cols=$(if($sheetName -eq 'APPRENANTS'){15}else{14})
        if($excel.WorksheetFunction.CountA($sheet.Range($sheet.Cells.Item(5,1),$sheet.Cells.Item([Math]::Max(5,$last.Row),$cols))) -ne 0){throw ('Donnees de test non vides : '+$sheetName)}
    }
    foreach($required in @('PARAMETRES','PROMOTIONS','APPRENANTS','PAIEMENTS','SITUATION','ACCUEIL','FORM_PAIEMENT','FICHE_APPRENANT','JOURNAL_ITM')){[void]$book.Worksheets.Item($required)}
    Write-Output 'Creation de source : modele conserve, donnees vides, feuilles presentes et premiere ligne de saisie 5.'
}finally{
    if($book){try{$book.Close($false)}catch{}}
    if($excel){try{$excel.Quit()}catch{};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)}
    if($result -and [IO.Path]::GetFileNameWithoutExtension($result.Path) -eq $name -and [IO.File]::Exists($result.Path)){[IO.File]::Delete($result.Path)}
}
