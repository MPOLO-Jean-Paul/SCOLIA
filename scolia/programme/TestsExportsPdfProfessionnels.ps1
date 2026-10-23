# Windows PowerShell 5.1. Vérification isolée : aucun accès à Excel, aux données ou au registre.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing,System.Xml,System.IO.Compression,System.IO.Compression.FileSystem
Add-Type -Path (Join-Path $PSScriptRoot 'CartesExamens.cs') -ReferencedAssemblies System.Drawing,System.Xml -IgnoreWarnings
$temp=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA_test_pdf_'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp)
function New-TestWorkbook([string]$path,[int]$mode){
 $rows=New-Object Text.StringBuilder
 $captions=$(if($mode -eq 3){@('Matricule','Nom','Post-nom','Prenom','Promotion','Contact','Statut','Annee scolaire')}else{@('Matricule','Nom','Post-nom','Prenom','Promotion','Mois','Du mensuel FC','Verse FC','Reste FC','Statut')})
 $all=@(@('ITM CD'),@('Rapport de test'),@('Promotion : TEST'),@(''),$captions)
 for($i=0;$i -lt 80;$i++){$all+=,@('TEST'+$i.ToString('0000'),'MUKENDI','KASONGO','Élisabeth','TEST',$(if($mode -eq 3){'0991234567'}else{'Septembre'}),$(if($mode -eq 3){'Actif'}else{'25000'}),$(if($mode -eq 3){'2026 - 2027'}else{'15000'}),'10000','PARTIEL')}
 for($r=0;$r -lt $all.Count;$r++){
  [void]$rows.Append('<row r="'+($r+1)+'">')
  for($c=0;$c -lt $all[$r].Count;$c++){$value=[Security.SecurityElement]::Escape([string]$all[$r][$c]);$address=[string][char](65+$c)+($r+1);[void]$rows.Append('<c r="'+$address+'" t="inlineStr"><is><t>'+ $value+'</t></is></c>')}
  [void]$rows.Append('</row>')
 }
 $parts=@{
  'xl/workbook.xml'='<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="TEST" sheetId="1" r:id="rId1"/></sheets></workbook>'
  'xl/_rels/workbook.xml.rels'='<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'
  'xl/worksheets/sheet1.xml'='<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>'+$rows.ToString()+'</sheetData></worksheet>'
 }
 $zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($name in $parts.Keys){$entry=$zip.CreateEntry($name);$writer=New-Object IO.StreamWriter($entry.Open(),(New-Object Text.UTF8Encoding($false)));try{$writer.Write($parts[$name])}finally{$writer.Dispose()}}}finally{$zip.Dispose()}
}
try{
 foreach($mode in @(2,3)){
  $source=Join-Path $temp ($mode.ToString()+'.xlsx');$output=Join-Path $temp ($mode.ToString()+'.pdf');New-TestWorkbook $source $mode
  [ITMExamCards]::ExportWorkbookPdf($source,$output,(Join-Path $PSScriptRoot 'Assets'),'ITM CD',$mode,(Join-Path $PSScriptRoot 'Assets/Logo_ITM_CD_Transparent.png'))
  $raw=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($output))
  if(-not $raw.StartsWith('%PDF-1.4') -or ([regex]::Matches($raw,'/Type /Page /Parent')).Count -lt 2){throw 'PDF ou pagination invalide'}
 }
 $target=Join-Path $temp 'protege.pdf';[IO.File]::WriteAllText($target,'ORIGINAL')
 try{[ITMExamCards]::ExportWorkbookPdf((Join-Path $temp 'absent.xlsx'),$target,(Join-Path $PSScriptRoot 'Assets'),'ITM CD',3,(Join-Path $PSScriptRoot 'Assets/Logo_ITM_CD_Transparent.png'))}catch{}
 if([IO.File]::ReadAllText($target) -ne 'ORIGINAL'){throw 'Le PDF existant a été altéré après un échec'}
 'PASS : listes de paiements et enregistrement multipages, import OOXML et conservation du PDF existant après un échec.'
}finally{Remove-Item -LiteralPath $temp -Recurse -Force}
