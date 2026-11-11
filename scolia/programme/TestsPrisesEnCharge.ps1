# Windows PowerShell 5.1. Temporary data only; no Excel or school data changes.
$ErrorActionPreference='Stop'
if(-not ('ITMPE' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'PrisesEnCharge.cs') -ReferencedAssemblies @('System.Xml')}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('ITM-PEC-test-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($folder)
try{
    $paid=New-Object decimal[] 10
    $record=[ITMPE]::Plan($null,'TEST-1','2026-2027','INF1',25000,0,9,0,$paid,'Garant test','Accord test')
    if([ITMPE]::Total($record,25000) -ne 250000 -or [ITMPE]::Payable($record,25000,$paid) -ne 0){throw 'Calcul des dix mois incorrect.'}
    $payant=[ITMPE]::Paying($record,4)
    if([ITMPE]::Total($payant,25000) -ne 100000 -or [ITMPE]::Payable($payant,25000,$paid) -ne 150000){throw 'Retour au statut payant incorrect.'}
    $paid[2]=1000;$rejected=$false
    try{[void][ITMPE]::Plan($null,'TEST-2','2026-2027','INF1',25000,0,9,0,$paid,'','Test')}catch{$rejected=$true}
    if(-not $rejected){throw 'Un mois payé a été couvert.'}
    $path=Join-Path $folder 'prises-en-charge.xml';$ledger=[ITMPE]::Load($path,'test-dataset')
    [ITMPE]::Apply($ledger,$record,'Test','Creation','Accord test');[ITMPE]::Save($path,$ledger,0)
    $stale=[ITMPE]::Load($path,'test-dataset');$current=[ITMPE]::Load($path,'test-dataset')
    [ITMPE]::Apply($current,$payant,'Test','Fin','Fin test');[ITMPE]::Save($path,$current,1)
    $rejected=$false;try{[ITMPE]::Save($path,$stale,1)}catch{$rejected=$true}
    if(-not $rejected -or -not [IO.File]::Exists($path+'.precedent')){throw 'Protection des modifications simultanées incorrecte.'}
    $current=[ITMPE]::Load($path,'test-dataset')
    if($current.History.Count -ne 2 -or $current.Records.Count -ne 1){throw 'Historique ou persistance incorrects.'}
    Write-Output 'PASS : dix mois, retour payant, paiement existant, persistance, historique et concurrence.'
}finally{[IO.Directory]::Delete($folder,$true)}
