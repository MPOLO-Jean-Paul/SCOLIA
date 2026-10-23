# Windows PowerShell 5.1 -STA : no Excel, data, registry or file write.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
if(-not ('ITMReadableCheckBox' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
Add-Type -TypeDefinition @'
using System;
public class ITMCopyArgumentFixture {
 public int Calls; public string Path;
 public void SaveCopyAs(object path){
  if(path==null||path.GetType()!=typeof(string))throw new ArgumentException("Filename must be a native string");
  Calls++;Path=(string)path;
 }
 public void Probe(object number,object flag,[System.Runtime.InteropServices.Optional] object missing){
  if(number.GetType()!=typeof(int)||flag.GetType()!=typeof(bool)||!Object.ReferenceEquals(missing,System.Reflection.Missing.Value))throw new Exception("Native argument types lost");
 }
}
'@
$copy=New-Object ITMCopyArgumentFixture
$path=Join-Path $env:TEMP "École ITM\copie test.xlsm"
$wrapped=New-Object Management.Automation.PSObject -ArgumentList $path
[ITMExcelDispatch]::SaveCopyAs($copy,$wrapped)
if($copy.Calls -ne 1 -or $copy.Path -ne [IO.Path]::GetFullPath($path)){throw 'Copy filename type or invocation count incorrect.'}
$wrappedInteger=New-Object Management.Automation.PSObject -ArgumentList 4
$wrappedBoolean=New-Object Management.Automation.PSObject -ArgumentList $false
[void][ITMExcelDispatch]::Call($copy,'Probe',[object[]]@($wrappedInteger,$wrappedBoolean,[Reflection.Missing]::Value))
$failed=$false
try{[ITMExcelDispatch]::SaveCopyAs($copy,42)}catch{$failed=$true}
if(-not $failed -or $copy.Calls -ne 1){throw 'Invalid path accepted or write retried.'}
foreach($dark in @($false,$true)){
 $form=New-Object Windows.Forms.Form
 $form.BackColor=$(if($dark){[Drawing.Color]::FromArgb(43,52,67)}else{[Drawing.Color]::White})
 $box=New-Object ITMReadableCheckBox
 $box.Text='Reconnecter le classeur mémorisé au démarrage'
 $box.Size=New-Object Drawing.Size(700,40)
 $box.BackColor=[Drawing.Color]::Transparent
 $box.ForeColor=[Drawing.Color]::Black
 $form.Controls.Add($box)
 $bitmap=New-Object Drawing.Bitmap(700,40)
 try{
  $box.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,700,40)))
  $pixels=0
  for($x=25;$x -lt 700;$x++){for($y=0;$y -lt 40;$y++){
   $pixel=$bitmap.GetPixel($x,$y)
   if([ITMReadableColors]::Contrast($pixel,$form.BackColor) -ge 4.5){$pixels++}
  }}
  if($pixels -lt 100){throw 'Checkbox caption missing or unreadable.'}
  if($box.FlatStyle -ne [Windows.Forms.FlatStyle]::Flat){throw 'Native checkbox rendering still overrides custom caption.'}
 }finally{$bitmap.Dispose();$form.Dispose()}
}
Write-Output 'PASS : native copy filename, wrapped argument types, no retry, checkbox caption on both themes.'
