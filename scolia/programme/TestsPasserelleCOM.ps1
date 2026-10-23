# Windows PowerShell 5.1: tests the real reflection transport without Excel or files.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Application.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Invoke-ITMExcelRead','Api-Name')){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 . ([scriptblock]::Create($node.Extent.Text))
}
Add-Type -TypeDefinition @'
public class ITMRunTransportFixture {
 public int Calls; public string LastMacro; public bool Fail;
 public object Run(object macro, [System.Runtime.InteropServices.Optional] object arg1, [System.Runtime.InteropServices.Optional] object arg2, [System.Runtime.InteropServices.Optional] object arg3, [System.Runtime.InteropServices.Optional] object arg4, [System.Runtime.InteropServices.Optional] object arg5, [System.Runtime.InteropServices.Optional] object arg6, [System.Runtime.InteropServices.Optional] object arg7, [System.Runtime.InteropServices.Optional] object arg8, [System.Runtime.InteropServices.Optional] object arg9, [System.Runtime.InteropServices.Optional] object arg10, [System.Runtime.InteropServices.Optional] object arg11, [System.Runtime.InteropServices.Optional] object arg12, [System.Runtime.InteropServices.Optional] object arg13, [System.Runtime.InteropServices.Optional] object arg14, [System.Runtime.InteropServices.Optional] object arg15, [System.Runtime.InteropServices.Optional] object arg16, [System.Runtime.InteropServices.Optional] object arg17, [System.Runtime.InteropServices.Optional] object arg18, [System.Runtime.InteropServices.Optional] object arg19, [System.Runtime.InteropServices.Optional] object arg20, [System.Runtime.InteropServices.Optional] object arg21, [System.Runtime.InteropServices.Optional] object arg22, [System.Runtime.InteropServices.Optional] object arg23, [System.Runtime.InteropServices.Optional] object arg24, [System.Runtime.InteropServices.Optional] object arg25, [System.Runtime.InteropServices.Optional] object arg26, [System.Runtime.InteropServices.Optional] object arg27, [System.Runtime.InteropServices.Optional] object arg28, [System.Runtime.InteropServices.Optional] object arg29, [System.Runtime.InteropServices.Optional] object arg30) {
  Calls++;LastMacro=(string)macro;if(Fail)throw new System.NullReferenceException("Simulated COM failure");
  if(!(Object.ReferenceEquals(arg1, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg2, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg3, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg4, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg5, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg6, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg7, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg8, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg9, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg10, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg11, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg12, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg13, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg14, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg15, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg16, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg17, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg18, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg19, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg20, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg21, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg22, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg23, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg24, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg25, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg26, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg27, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg28, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg29, System.Reflection.Missing.Value) && Object.ReferenceEquals(arg30, System.Reflection.Missing.Value))) throw new System.Exception("Optional arguments must be Missing.Value.");
  if(((string)macro).Contains("ITMAppVersion")) return "ITM-DESKTOP-4";
  return "{\"learners\":[]}";
 }
}
'@
function Assert($value,$message){if(-not $value){throw $message}}
$script:excel=New-Object ITMRunTransportFixture
$script:book=[pscustomobject]@{Name="École d'ITM.xlsm"}
Assert ((Invoke-ITMExcelRead 'ITMAppVersion') -eq 'ITM-DESKTOP-4') 'Version transport failed.'
Assert ((Invoke-ITMExcelRead 'ITMAppSnapshot') -eq '{"learners":[]}') 'Snapshot transport failed.'
Assert ($script:excel.Calls -eq 2) 'Unexpected duplicate invocation.'
Assert ($script:excel.LastMacro -eq "'École d''ITM.xlsm'!ModuleApplicationITM.ITMAppSnapshot") 'Workbook name not escaped correctly.'
foreach($name in @('ITMAppEnregistrer','ITMAppExporter')){
 $failed=$false;try{Invoke-ITMExcelRead $name}catch{$failed=$true}
 Assert $failed 'Mutating command accepted by read transport.'
}
Assert ($script:excel.Calls -eq 2) 'Mutating command invoked.'
$script:book.Name='';$failed=$false;try{Invoke-ITMExcelRead 'ITMAppVersion'}catch{$failed=$true}
Assert $failed 'Empty workbook name accepted.'
$failed=$false;try{Invoke-ITMExcelRunNoArguments $null 'test'}catch{$failed=$true}
Assert $failed 'Missing Excel session accepted.'
$script:excel.Fail=$true;$before=$script:excel.Calls;$failed=$false
try{Invoke-ITMExcelRunNoArguments $script:excel 'failure-test'}catch{$failed=$true;Assert ($_.Exception.Message -match 'failure-test') 'Macro context missing from failure.'}
Assert ($failed -and $script:excel.Calls -eq $before+1) 'Failed macro automatically repeated.'
Write-Output 'PASS: explicit Run transport, thirty missing optional arguments, no duplicate calls, read-only allowlist and absent object guards.'
