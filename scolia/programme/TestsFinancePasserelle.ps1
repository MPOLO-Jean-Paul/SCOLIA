$ErrorActionPreference='Stop'
function LoadFn($file,$name){$t=$null;$e=$null;$a=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$t,[ref]$e);$f=$a.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create('function global:'+$name+$f.Extent.Text.Substring(('function '+$name).Length)))}
LoadFn 'FonctionsGestion.ps1' 'Invoke-ITMJsonDirect'
LoadFn 'MiseAJourPerformance.ps1' 'Invoke-ITMWriteMacro'
function Reset-ITMCompletedSaveLock {}
function Resolve-ITMLiveWorkbook($book){return $book}
function Api-Name($name){return $name}
function Assert-ITMPermission {}
function Invoke-ITMExcelRun($excel,$macro,$arguments){if(($macro -eq 'ITMAppScoliaSynchroniser' -and $arguments.Count -ne 11) -or ($macro -eq 'ITMAppScoliaImport' -and $arguments.Count -ne 8)){throw 'Bridge parameters lost'};return '{"ok":true}'}
$script:book=[pscustomobject]@{Name='simulated'}
[void](Invoke-ITMJsonDirect 'ITMAppScoliaSynchroniser' @(1,2,3,4,5,6,7,8,9,10,11))
[void](Invoke-ITMJsonDirect 'ITMAppScoliaImport' @(1,2,3,4,5,6,7,8))
if(-not $script:committedThisAction){throw 'Durable success not acknowledged'}
'PASS production bridge accepts imports with revision and synchronized corrections (Excel simulated)'
