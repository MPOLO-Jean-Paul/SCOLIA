# Mise a jour atomique simulee : aucun Excel ni classeur de production utilise.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'MiseAJourPerformance.ps1')
function Assert-Migration([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$script:manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$script:backups=0;$script:commits=0;$script:edits=0;$script:store=[pscustomobject]@{Id='test'}
function Set-ITMActivityMessage {param($message)}
function Start-ITMStoreTransaction {$script:backups++}
function Complete-ITMStoreTransaction {$script:commits++}
function New-MigrationBook {
    $map=@{}
    foreach($item in $script:manifest){
        $path=Join-Path (Join-Path $PSScriptRoot 'Corrections') $item.file
        if($item.name -in @('ModuleFiabilite','ModuleApplicationITM')){$path=Join-Path $PSScriptRoot ('Compatibilite/'+$item.name+'_AvantPerformance.txt')}
        $code=Get-Content -LiteralPath $path -Raw -Encoding UTF8
        $cm=[pscustomobject]@{Code=$code;CountOfLines=(@($code -split '\r?\n').Count)}
        $cm | Add-Member -MemberType ScriptMethod -Name Lines -Value {param($start,$count);return $this.Code}
        $cm | Add-Member -MemberType ScriptMethod -Name DeleteLines -Value {param($start,$count);if(-not $script:backups){throw 'Modification avant sauvegarde.'};$script:edits++;$this.Code='';$this.CountOfLines=0}
        $cm | Add-Member -MemberType ScriptMethod -Name InsertLines -Value {param($start,$value);$this.Code=$value;$this.CountOfLines=@($value -split '\r?\n').Count}
        $map[[string]$item.name]=[pscustomobject]@{CodeModule=$cm;Type=1}
    }
    $components=[pscustomobject]@{Map=$map;Count=$map.Count}
    $components | Add-Member -MemberType ScriptMethod -Name Item -Value {param($name);if(-not $this.Map.ContainsKey($name)){throw 'Absent'};return $this.Map[$name]}
    return [pscustomobject]@{Name='test.xlsm';VBProject=[pscustomobject]@{Protection=0;VBComponents=$components}}
}
# Feature probe is simulated from the module text; comparison and migration are real functions.
function Test-ITMPerformanceBook($book){
    $m=$book.VBProject.VBComponents.Map
    return ($m.ModuleFiabilite.CodeModule.Code.Contains('ITM-PERF-2') -and $m.ModuleApplicationITM.CodeModule.Code.Contains('SCOLIA-PERF-7'))
}
$book=New-MigrationBook;$script:storeBooks=@($book)
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:commits -eq 1 -and $script:edits -eq 2) 'Migration sans sauvegarde, incomplete ou hors perimetre.'
Assert-Migration (Test-ITMPerformanceBook $book) 'Deux modules non actualises.'
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:edits -eq 2) 'Mise a jour repetee a chaque demarrage.'
$book=New-MigrationBook;$book.VBProject.VBComponents.Map.ModuleFiabilite.CodeModule.Code+='\nPublic Sub CODE_PERSONNALISE(): End Sub'
$script:storeBooks=@($book);$script:backups=0;$script:edits=0;$script:commits=0
Update-ITMManagedRuntime
Assert-Migration ($script:edits -eq 0 -and $script:backups -eq 0 -and $script:commits -eq 0 -and [bool]$script:runtimeUpdateNote) 'Code personnalise remplace ou modification avant preverification complete.'
$book=New-MigrationBook;$book.VBProject.Protection=1;$script:storeBooks=@($book)
Update-ITMManagedRuntime
Assert-Migration ($script:edits -eq 0 -and $script:backups -eq 0 -and [bool]$script:runtimeUpdateNote) 'Projet verrouille modifie.'
Write-Output 'PASS : sauvegarde avant deux modules, migration unique, preverification globale, protection du code personnalise.'

# Migration de la version V75 réellement livrée : seul le module applicatif change.
$book=New-MigrationBook
$book.VBProject.VBComponents.Map.ModuleFiabilite.CodeModule.Code=Get-Content (Join-Path $PSScriptRoot 'Corrections/ModuleFiabilite.txt') -Raw -Encoding UTF8
$cm=$book.VBProject.VBComponents.Map.ModuleApplicationITM.CodeModule
$cm.Code=Get-Content (Join-Path $PSScriptRoot 'Compatibilite/ModuleApplicationITM_AvantPerformanceV76.txt') -Raw -Encoding UTF8
$cm.CountOfLines=@($cm.Code -split '\r?\n').Count
$script:storeBooks=@($book);$script:backups=0;$script:edits=0;$script:commits=0
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:commits -eq 1 -and $script:edits -eq 1) 'Migration V75 vers V76 non ciblée.'
Assert-Migration (Test-ITMPerformanceBook $book) 'Version V76 non confirmée.'
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:edits -eq 1) 'Migration V76 répétée.'
'PASS migration V75 vers V76 : sauvegarde préalable, un module, vérification et absence de répétition'

# Migration directe de la V78 réellement livrée vers V79.
$book=New-MigrationBook
$cm=$book.VBProject.VBComponents.Map.ModuleFiabilite.CodeModule
$cm.Code=Get-Content (Join-Path $PSScriptRoot 'Compatibilite/ModuleFiabilite_AvantPerformanceV78.txt') -Raw -Encoding UTF8;$cm.CountOfLines=@($cm.Code -split '\r?\n').Count
$cm=$book.VBProject.VBComponents.Map.ModuleApplicationITM.CodeModule
$cm.Code=Get-Content (Join-Path $PSScriptRoot 'Compatibilite/ModuleApplicationITM_AvantPerformanceV78.txt') -Raw -Encoding UTF8;$cm.CountOfLines=@($cm.Code -split '\r?\n').Count
$script:storeBooks=@($book);$script:backups=0;$script:edits=0;$script:commits=0
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:commits -eq 1 -and $script:edits -eq 2) 'Migration V78 vers V79 non ciblée.'
Assert-Migration (Test-ITMPerformanceBook $book) 'Version V79 non confirmée après migration V78.'
'PASS migration V78 vers V79 : sauvegarde préalable, deux modules ciblés et vérification finale'

# Migration directe de la V79 réellement livrée vers V80 : seul le module applicatif change.
$book=New-MigrationBook
$cm=$book.VBProject.VBComponents.Map.ModuleFiabilite.CodeModule
$cm.Code=Get-Content (Join-Path $PSScriptRoot 'Corrections/ModuleFiabilite.txt') -Raw -Encoding UTF8;$cm.CountOfLines=@($cm.Code -split '\r?\n').Count
$cm=$book.VBProject.VBComponents.Map.ModuleApplicationITM.CodeModule
$cm.Code=Get-Content (Join-Path $PSScriptRoot 'Compatibilite/ModuleApplicationITM_AvantPerformanceV80.txt') -Raw -Encoding UTF8;$cm.CountOfLines=@($cm.Code -split '\r?\n').Count
$script:storeBooks=@($book);$script:backups=0;$script:edits=0;$script:commits=0
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:commits -eq 1 -and $script:edits -eq 1) 'Migration V79 vers V80 non ciblée.'
Assert-Migration (Test-ITMPerformanceBook $book) 'Version V80 non confirmée après migration V79.'
Assert-Migration ($book.VBProject.VBComponents.Map.ModuleApplicationITM.CodeModule.Code.Contains('JTexte(parts, n)')) 'Construction linéaire du JSON absente après migration.'
Update-ITMManagedRuntime
Assert-Migration ($script:backups -eq 1 -and $script:edits -eq 1) 'Migration V80 répétée.'
'PASS migration V79 vers V80 : sauvegarde préalable, module applicatif seul, vérification et absence de répétition'
