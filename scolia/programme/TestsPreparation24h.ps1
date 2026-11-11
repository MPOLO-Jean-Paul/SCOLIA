# Regression sans Excel : teste les fonctions reelles de validite et de lecture XLSM.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'StockageClasseurs.ps1')
function Assert-Preparation([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$start=[DateTimeOffset]::Parse('2026-10-08T12:00:00.0000000+00:00')
$index=[pscustomobject]@{Id='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';Segments=@('classeur-000001.xlsm')}
Assert-Preparation (-not (Test-ITMPreparationCurrent $index $start.UtcDateTime)) 'Ancienne base acceptee comme cache valide.'
$stamp=[pscustomobject]@{DataSet=$index.Id;VerifiedUtc=$start.ToString('o');Signature=(Get-ITMPreparationSignature);Segments='classeur-000001.xlsm'}
$index | Add-Member -NotePropertyName Preparation -NotePropertyValue $stamp
Assert-Preparation (Test-ITMPreparationCurrent $index $start.UtcDateTime) 'Preparation initiale non valide.'
Assert-Preparation (Test-ITMPreparationCurrent $index ($start.UtcDateTime.AddHours(24).AddTicks(-1))) 'Expiration avant 24 heures.'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index ($start.UtcDateTime.AddHours(24)))) 'Preparation encore valide a 24 heures.'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index ($start.UtcDateTime.AddHours(25)))) 'Preparation expiree non detectee.'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index ($start.UtcDateTime.AddMinutes(-1)))) 'Horloge reculee non detectee.'
$index.Segments+=@('classeur-000002.xlsm')
Assert-Preparation (-not (Test-ITMPreparationCurrent $index $start.UtcDateTime)) 'Nouveau segment ne declenche pas le controle.'
$index.Segments=@('classeur-000001.xlsm');$stamp.DataSet='autre'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index $start.UtcDateTime)) 'Cache accepte pour une autre base.'
$stamp.DataSet=$index.Id;$stamp.Signature='ancienne-version'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index $start.UtcDateTime)) 'Ancienne version acceptee.'
$stamp.Signature=Get-ITMPreparationSignature;$stamp.VerifiedUtc='invalide'
Assert-Preparation (-not (Test-ITMPreparationCurrent $index $start.UtcDateTime)) 'Date invalide acceptee.'
Add-Type -AssemblyName System.IO.Compression
$path=Join-Path ([IO.Path]::GetTempPath()) ('ITM-identite-'+[guid]::NewGuid().ToString('N')+'.xlsm')
try{
    $stream=[IO.File]::Create($path)
    $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
    try{
        $entry=$archive.CreateEntry('docProps/custom.xml');$writer=New-Object IO.StreamWriter($entry.Open())
        try{$writer.Write('<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"><!--controle--><property name="ITM_DataSet"><lpwstr xmlns="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa</lpwstr></property></Properties>')}finally{$writer.Dispose()}
    }finally{$archive.Dispose();$stream.Dispose()}
    Assert-Preparation ((Get-ITMPersistedIdentity $path) -eq $index.Id) 'Identite XLSM sauvegardee non lue.'
}finally{if([IO.File]::Exists($path)){[IO.File]::Delete($path)}}
Write-Output 'Preparation : cache 24h, expiration, horloge, segments, version, identite et lecture XLSM controles.'
