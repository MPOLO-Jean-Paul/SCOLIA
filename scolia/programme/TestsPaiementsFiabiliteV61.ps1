$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PaiementsFiabiliteV61.ps1')
function Check($condition,$message){if(-not $condition){throw $message};$script:checks++}
$script:checks=0
$fr=[Globalization.CultureInfo]::GetCultureInfo('fr-FR');$en=[Globalization.CultureInfo]::GetCultureInfo('en-US')
foreach($text in @('1500','1 500',('1'+[char]0xa0+'500'),('1'+[char]0x202f+'500'),'1500,00')){Check ((Get-ScoliaTypedAmount $text 10000 $fr) -eq 1500) ('French amount '+$text)}
foreach($text in @('','-1','1,50','1 50','1 500 00','10001','1e3','FC 1000')){Check ($null -eq (Get-ScoliaTypedAmount $text 10000 $fr)) ('Invalid amount '+$text)}
Check ((Get-ScoliaTypedAmount '1,500' 10000 $en) -eq 1500) 'US grouping'
Check ($null -eq (Get-ScoliaTypedAmount '1,2' 10000 $en)) 'US malformed grouping'
Check ((Get-ScoliaTypedAmount '0' 10000 $fr) -eq 0) 'Zero parsed but not payable'
function LoadFn($file,$name){$t=$null;$e=$null;$a=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$t,[ref]$e);$f=$a.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create('function global:'+$name+$f.Extent.Text.Substring(('function '+$name).Length)))}
LoadFn 'Application.ps1' 'Update-PaymentButton'
function Test-ITMPermission {return $true}
function Test-ScoliaPermission {return $true}
$prior=[Globalization.CultureInfo]::CurrentCulture
try{
 [Globalization.CultureInfo]::CurrentCulture=$fr
 $script:paySave=[pscustomobject]@{Enabled=$false};$script:payAmount=[pscustomobject]@{Text='1500';Maximum=10000;Value=0};$script:book=[pscustomobject]@{Name='Open'};$script:data=[pscustomobject]@{closed=$false};$script:payMat='A001';$script:payReste=2000;$script:busy=$false;$script:payNeedsRefresh=$false
 Update-PaymentButton;Check $script:paySave.Enabled 'Typed amount must enable before Value commits'
 $script:payAmount.Text='';$script:payAmount.Value=1500;Update-PaymentButton;Check (-not $script:paySave.Enabled) 'Blank text must not use old Value'
 foreach($text in @('3000','-1','1,50')){$script:payAmount.Text=$text;Update-PaymentButton;Check (-not $script:paySave.Enabled) ('Disabled invalid '+$text)}
 $script:payAmount.Text='1500';$script:busy=$true;Update-PaymentButton;Check (-not $script:paySave.Enabled) 'Concurrent save gate'
 $script:busy=$false;$script:payNeedsRefresh=$true;Update-PaymentButton;Check (-not $script:paySave.Enabled) 'Unconfirmed refresh gate'
}finally{[Globalization.CultureInfo]::CurrentCulture=$prior}
# Exercise exact-path workbook reuse and preserve settings with an isolated fake Excel.
$wanted=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'test-folder/same.xlsm'))
$other=[pscustomobject]@{FullName=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'other-folder/same.xlsm'))}
$expected=[pscustomobject]@{FullName=$wanted}
$collection=[pscustomobject]@{Count=2;Items=@($other,$expected)}
$fake=[pscustomobject]@{Workbooks=$collection;DisplayAlerts=$true;EnableEvents=$true}
function Get-ITMExcelProperty($target,$name,$index=@()){if($name -eq 'Item'){return $target.Items[$index[0]-1]};return $target.$name}
function Set-ITMExcelProperty($target,$name,$value){$target.$name=$value}
Check ([object]::ReferenceEquals((Find-ScoliaOpenWorkbook $fake $wanted),$expected)) 'Same name in another folder excluded'
$script:excel=$fake;$script:excelBorrowed=$true;$script:excelOriginalSettings=@{DisplayAlerts=$false;EnableEvents=$false}
Restore-ScoliaBorrowedExcelSettings;Check (-not $fake.DisplayAlerts -and -not $fake.EnableEvents) 'Original Excel settings restored'
LoadFn 'StockageClasseurs.ps1' 'Start-ITMStoreTransaction'
$dir=Join-Path ([IO.Path]::GetTempPath()) ('scolia-save-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($dir)
try{
 $script:storeFolder=$dir;$script:store=[pscustomobject]@{Segments=@('classeur-000001.xlsm')};$script:storeBooks=@([pscustomobject]@{Name='classeur-000001.xlsm'})
 function Resolve-ITMLiveWorkbook($book){return $book}
 function Invoke-ITMExcelMethod($book,$name,$arguments){Check ($name -eq 'SaveCopyAs') 'Managed dispatcher used on open workbook';[IO.File]::WriteAllText($arguments[0],'previous learner data')}
 function Write-ITMAtomicJson($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 8))}
 Start-ITMStoreTransaction
 Check ([IO.File]::Exists((Join-Path $dir 'transaction.json'))) 'Recovery journal created'
 Check (@(Get-ChildItem $dir -Recurse -Filter '*.xlsm').Count -eq 1) 'Open workbook backup preserved'
}finally{Remove-Item -LiteralPath $dir -Recurse -Force}
LoadFn 'Application.ps1' 'Disconnect-Book'
$script:closed=0;$script:quit=0
$script:book=[pscustomobject]@{Name='Borrowed'};$script:book|Add-Member ScriptMethod Close {$script:closed++}
$script:excel=$fake;$fake|Add-Member ScriptMethod Quit {$script:quit++}
$script:excelBorrowed=$true;$script:excelOriginalSettings=@{DisplayAlerts=$true;EnableEvents=$true}
function Close-ITMStore {}
Disconnect-Book
Check ($script:closed -eq 0 -and $script:quit -eq 0) 'Disconnect keeps borrowed workbook and Excel open'
Check ($fake.DisplayAlerts -and $fake.EnableEvents) 'Disconnect restores settings before release'
LoadFn 'StockageClasseurs.ps1' 'Open-ITMStoreBooks'
$dir=Join-Path ([IO.Path]::GetTempPath()) ('scolia-open-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($dir)
try{
 $script:openPath=Join-Path $dir 'classeur-000001.xlsm';[IO.File]::WriteAllText($script:openPath,'isolated fake workbook')
 $already=[pscustomobject]@{FullName=$script:openPath;Name='classeur-000001.xlsm';ReadOnly=$false}
 $books=[pscustomobject]@{Count=1;Items=@($already)};$books|Add-Member ScriptMethod Open {throw 'An already open workbook must not be opened twice'}
 $script:excel=[pscustomobject]@{Workbooks=$books;AutomationSecurity=3};$script:store=[pscustomobject]@{Active='classeur-000001.xlsm'}
 function Get-ITMStoreFiles($index){return $script:openPath}
 function Get-ITMExcelMacroName($book,$module,$name){return $name}
 function Invoke-ITMExcelRunNoArguments($excel,$macro){return 'ITM-DESKTOP-4'}
 function Update-ITMStorePreparation {}
 Open-ITMStoreBooks
 Check ([object]::ReferenceEquals($script:book,$already)) 'Already open segment reused without Workbooks.Open'
 Check ($script:excel.AutomationSecurity -eq 3) 'Reuse leaves automation security unchanged'
}finally{Remove-Item -LiteralPath $dir -Recurse -Force}
'PASS: '+$script:checks+' targeted checks (Excel simulated); text amount, button states, path reuse, settings and transaction backup.'
