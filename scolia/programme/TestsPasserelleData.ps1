# Windows PowerShell 5.1. Tests real data-opening functions with simulated Excel.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Open-ITMStoreBooks','Invoke-ITMStockMacro')){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
function Assert($v,$m){if(-not $v){throw $m}}
function Get-ITMStoreFiles($index){return $script:paths}
function Update-ITMStorePreparation {$script:prepared++}
function Invoke-ITMExcelRunNoArguments($application,$macro){
 $script:reads++;$script:lastMacro=$macro
 if($script:rejectVersion){return 'invalid-version'}
 if($macro.EndsWith('ITMAppCapacite')){return '{"learners":10}'}
 return 'ITM-DESKTOP-4'
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ITM-bridge-data-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
$script:paths=@((Join-Path $temp 'classeur-000001.xlsm'),(Join-Path $temp 'classeur-000002.xlsm'))
foreach($path in $script:paths){[IO.File]::WriteAllText($path,'test fixture')}
$script:store=[pscustomobject]@{Active='classeur-000002.xlsm'}
$books=[pscustomobject]@{}
$books | Add-Member ScriptMethod Open {param($path,$links,$readonly);return [pscustomobject]@{Name=[IO.Path]::GetFileName($path);ReadOnly=$false}}
$script:excel=[pscustomobject]@{Workbooks=$books;AutomationSecurity=3}
$script:excel | Add-Member ScriptMethod Run {throw 'Legacy read transport used.'}
try{
 $script:reads=0;$script:prepared=0;Open-ITMStoreBooks
 Assert ($script:reads -eq 2 -and $script:prepared -eq 1 -and $script:book.Name -eq $script:store.Active) 'Segment verification did not use the shared transport.'
 Assert ((Invoke-ITMStockMacro $script:book 'ITMAppCapacite') -eq '{"learners":10}') 'Capacity read did not use shared transport.'
 Assert ($script:lastMacro -eq "'classeur-000002.xlsm'!ModuleStockageITM.ITMAppCapacite") 'Wrong segment routed.'
 $script:rejectVersion=$true;$script:reads=0;$script:prepared=0;$failed=$false
 try{Open-ITMStoreBooks}catch{$failed=$true}
 Assert ($failed -and $script:reads -eq 1 -and $script:prepared -eq 0 -and -not $script:book) 'Invalid bridge version accepted or retried.'
 Write-Output 'PASS: all registered segments use shared read transport, active segment routing and strict version rejection.'
}finally{[IO.Directory]::Delete($temp,$true)}
