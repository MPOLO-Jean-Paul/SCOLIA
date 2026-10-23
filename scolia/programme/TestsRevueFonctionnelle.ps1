$ErrorActionPreference='Stop'
function Import-TestFunction($file,$name){$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors);$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);if(-not $node){throw $name};. ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))}
Import-TestFunction 'ScoliaIntegration.ps1' 'Show-ScoliaPending'
function Assert-ITMPermission {}
function New-ScoliaDialog {throw 'Ouverture prématurée du dialogue'}
$script:scoliaCommitTask=[pscustomobject]@{IsCompleted=$false};$rejected=$false
try{Show-ScoliaPending}catch{if($_.Exception.Message -notlike '*encore en cours*'){throw};$rejected=$true}
if(-not $rejected){throw 'Vérification autorisée pendant un encaissement'}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-revue-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
try{
 $script:scoliaLocalRoot=$root;$script:scoliaCommitTask=[pscustomobject]@{IsCompleted=$true};$script:status=[pscustomobject]@{Text=''};Show-ScoliaPending
 if($script:status.Text -ne 'Aucune opération de caisse en attente.'){throw 'Fin du traitement encore bloquée'}
 Add-Type -Path (Join-Path $PSScriptRoot 'CotesMatching.cs');Import-TestFunction 'CotesDeliberation.ps1' 'Read-ITMGradePlan'
 function Promo($s){return ([string]$s).Trim().ToUpperInvariant()};function Key($s){return ([string]$s).Trim().ToUpperInvariant()}
 function Open-ITMGradeExcel {throw 'TEST_VALIDATION_ATTEINTE'}
 $script:book=[pscustomobject]@{};$script:payNeedsRefresh=$false;$script:data=[pscustomobject]@{learners=@([pscustomobject]@{Nom='A';PostNom='B';Prenom='C';Matricule='ABC001';Promotion='INF3';Statut='Actif'})}
 $grid=Join-Path $root 'grille.xlsx';$notes=Join-Path $root 'notes.xlsx';[IO.File]::WriteAllText($grid,'test');[IO.File]::WriteAllText($notes,'test')
 $config=[pscustomobject]@{Grid=$grid;Notes=$notes;Promotion='INF3';Course='Anatomie';Maximum=20;FirstRow=450;LastRow=470;NameColumn=2;CourseRow=400;NotesFirstRow=2;NotesNameColumn=1;NotesGradeColumn=2}
 $accepted=$false;try{Read-ITMGradePlan $config}catch{if($_.Exception.Message -ne 'TEST_VALIDATION_ATTEINTE'){throw};$accepted=$true};if(-not $accepted){throw 'Ligne de cours au-delà de 200 refusée'}
 $config.CourseRow=50001;$rejected=$false;try{Read-ITMGradePlan $config}catch{if($_.Exception.Message -notlike '*Repères de grille incorrects*'){throw};$rejected=$true};if(-not $rejected){throw 'Ligne de cours hors limite acceptée'}
 'PASS vérification bloquée pendant encaissement, fin de traitement libérée, ligne de cours 400 acceptée, repère hors limite refusé (ouverture Excel simulée).'
}finally{[IO.Directory]::Delete($root,$true)}
