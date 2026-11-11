# Regression Windows PowerShell 5.1 : journal multi-classeurs, sans Excel et sans ecriture.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'StockageClasseurs.ps1')
function Invoke-ITMJsonDirect {return [pscustomobject]@{journal=@();backups=@();duplicates=@()}}
$script:storeFolder=$PSScriptRoot
$script:storeBooks=@([pscustomobject]@{Name='premier.xlsm'},[pscustomobject]@{Name='second.xlsm'})
$script:excel=[pscustomobject]@{First='[]';Second='[]'}
$script:excel | Add-Member -MemberType ScriptMethod -Name Run -Value {
    param($macro)
    if($macro.Contains('premier.xlsm')){return $this.First}
    return $this.Second
}
function Assert-Journal($condition,[string]$message){if(-not $condition){throw $message}}
$result=Get-ITMManagedAdministration
Assert-Journal (@($result.journal).Count -eq 0) 'Journal vide incorrect.'
$script:excel.First='[{"Row":12,"Action":"inscription"}]'
$result=Get-ITMManagedAdministration
Assert-Journal (@($result.journal).Count -eq 1) 'Entree unique incorrecte.'
Assert-Journal ($result.journal[0].Row -eq 1 -and $result.journal[0].Source -eq 'premier.xlsm') 'Source ou numero incorrect.'
$script:excel.First='[{"Row":12,"Action":"inscription"},{"Row":15,"Action":"paiement"}]'
$script:excel.Second='[{"Row":12,"Source":"ancienne","Action":"correction"}]'
$result=Get-ITMManagedAdministration
Assert-Journal (@($result.journal).Count -eq 3) 'Tableau imbrique ou entree perdue.'
Assert-Journal ($result.journal[1].Action -eq 'paiement') 'Ordre du journal incorrect.'
Assert-Journal ($result.journal[2].Row -eq 3 -and $result.journal[2].Source -eq 'second.xlsm') 'Identifiants non uniques entre classeurs.'
$script:excel.First='[]';$script:excel.Second='[{"Action":"ancien journal sans Row"}]'
$result=Get-ITMManagedAdministration
Assert-Journal (@($result.journal).Count -eq 1 -and $result.journal[0].Row -eq 1) 'Champ Row absent non pris en charge.'
Write-Output 'Journal : cas vide, unique, multiple, multi-classeurs et ancien format valides.'
