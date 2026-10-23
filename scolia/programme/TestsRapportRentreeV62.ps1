$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'RapportRentreeV62.ps1')
function Key([string]$v){return $v.Trim().ToUpperInvariant()}
function Promo([string]$v){return (Key $v) -replace '[- ]',''}
function Get-ITMInstitutionName{return 'ITM Test & Formation'}
function Get-ITMInstitutionLogo{return ''}
function Get-ScoliaTeachingRows($a,$b){return @()}
function Assert($Condition,[string]$Message){if(-not $Condition){throw $Message}}
$script:institutionProfile=[pscustomobject]@{Name='ITM Test & Formation';Address='Avenue de la formation';Header=@('RÉPUBLIQUE DÉMOCRATIQUE DU CONGO','Enseignement','Direction','Province','Ville','ITM Test & Formation','Avenue de la formation')}
$learners=@(
 [pscustomobject]@{Matricule='000001';Nom='Alpha';PostNom='A';Prenom='Anne';Sexe='F';Promotion='INF1';Annee='2026-2027';Statut='Actif'},
 [pscustomobject]@{Matricule='000002';Nom='Beta';PostNom='B';Prenom='Ben';Sexe='M';Promotion='INF1';Annee='2026 – 2027';Statut='Suspendu'},
 [pscustomobject]@{Matricule='000003';Nom='Gamma';PostNom='C';Prenom='Chris';Sexe='';Promotion='INF2';Annee='2026-2027';Statut='Actif'},
 [pscustomobject]@{Matricule='000004';Nom='Sorti';PostNom='D';Prenom='Denis';Sexe='M';Promotion='INF2';Annee='2026-2027';Statut='Sorti'},
 [pscustomobject]@{Matricule='000005';Nom='Ancien';PostNom='E';Prenom='Eva';Sexe='F';Promotion='INF1';Annee='2025-2026';Statut='Actif'})
$script:data=[pscustomobject]@{learners=$learners;promotions=@([pscustomobject]@{Code='INF1';Annee='2026-2027';Mensuel=10},[pscustomobject]@{Code='INF2';Annee='2026-2027';Mensuel=20},[pscustomobject]@{Code='INF3';Annee='2026-2027';Mensuel=10})}
$p=Get-ScoliaRentreePopulation $script:data '2026-2027'
Assert ($p.Total -eq 3 -and $p.F -eq 1 -and $p.M -eq 1 -and $p.Unknown -eq 1) 'Effectifs incorrects.'
Assert ($p.Stats.Count -eq 3 -and @($p.Stats|Where-Object {$_.Promotion -eq 'INF3'})[0].Total -eq 0) 'Classe vide absente.'
Assert (($p.Stats|Measure-Object Total -Sum).Sum -eq $p.Total) 'Totaux par classe incohérents.'
$duplicate=[pscustomobject]@{learners=$learners+@($learners[0]);promotions=$script:data.promotions};$blocked=$false
try{Get-ScoliaRentreePopulation $duplicate '2026-2027'|Out-Null}catch{$blocked=$true};Assert $blocked 'Doublon non bloqué.'
$base=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Assets/RapportRentree.json'))|ConvertFrom-Json
$model=[pscustomobject]@{Schema=1;Context=[pscustomobject]@{Year='2026-2027'};Blocks=$base.Blocks;Meta=[pscustomobject]@{Ville='Lubumbashi';Localisation='Localisation à confirmer';Dates='Calendrier à confirmer';Reunion='Réunion à préciser';Formation='Formation à préciser';Internat='Internat à préciser';Signataire='La direction';Mois=9}}
$script:institutionProfile=Set-ScoliaInstitutionSignatories $script:institutionProfile ([pscustomobject]@{Prefet='Préfet de démonstration';Proviseur='Proviseur de démonstration';Secretaire='Secrétaire de démonstration'})
$model.Meta|Add-Member NoteProperty SignatoryRole 'Proviseur' -Force
$chosenBody=Get-ScoliaRentreeBody $model $p
[xml]$chosenXml=('<root xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'+$chosenBody+'</root>')
$manager=New-Object Xml.XmlNamespaceManager $chosenXml.NameTable;$manager.AddNamespace('w','http://schemas.openxmlformats.org/wordprocessingml/2006/main')
$chosenSignature=$chosenXml.SelectSingleNode('//w:tbl[w:tblPr/w:tblBorders/w:top[@w:val="nil"]]',$manager)
Assert ($chosenSignature.InnerText.Contains('Proviseur de démonstration') -and -not $chosenSignature.InnerText.Contains('Préfet de démonstration')) 'Le signataire choisi n est pas récupéré automatiquement.'
$model.Meta.SignatoryRole='Prefet'
$body=Get-ScoliaRentreeBody $model $p
[xml]$xml=('<root xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'+$body+'</root>')
Assert ($body.Contains('ITM Test &amp; Formation')) 'Caractères XML non protégés.'
Assert (-not ($body -match 'Belle Vie|Kasumbalesa|NGOIE MWAMBI|KABONGO MBAYA|\{INSTITUT\}|\{ANNEE\}|\{POPULATION\}')) 'Données du modèle ou champs non remplacés.'
Assert ($body.Contains('000001') -and -not $body.Contains('000004') -and -not $body.Contains('000005')) 'Listes nominatives incorrectes.'
$empty=Get-ScoliaRentreePopulation ([pscustomobject]@{learners=@();promotions=@()}) '2026-2027';Assert ($empty.Total -eq 0) 'Population vide incorrecte.'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('test-rentree-'+[Guid]::NewGuid().ToString('N')+'.docx')
try{
 Write-ScoliaRentreeDocx $model $p $temp
 $zip=[IO.Compression.ZipFile]::OpenRead($temp)
 try{Assert ($null -ne $zip.GetEntry('word/document.xml')) 'Document Word manquant.';$reader=New-Object IO.StreamReader ($zip.GetEntry('word/document.xml').Open());try{[xml]$content=$reader.ReadToEnd()}finally{$reader.Dispose()};Assert ($content.DocumentElement.InnerText.Contains('000001')) 'Export incomplet.'}finally{$zip.Dispose()}
 if($env:SCOLIA_RENTREE_QA){Copy-Item -LiteralPath $temp -Destination $env:SCOLIA_RENTREE_QA -Force}
}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}}
foreach($file in @('RapportRentreeV62.ps1','Application.ps1','MettreAJourV57.ps1')){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors);Assert ($errors.Count -eq 0) ('Syntaxe incorrecte : '+$file)}
Write-Host 'OK : effectifs, sexe inconnu, année, classe vide, doublons, XML, listes nominatives, DOCX et syntaxe.' -ForegroundColor Green
