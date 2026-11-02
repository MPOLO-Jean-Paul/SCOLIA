# Verrous et gel de paie sur fichiers temporaires réels ; aucun Excel.
$ErrorActionPreference='Stop'
foreach($name in @('FonctionnalitesV55.ps1','PresencesV55.ps1','PersonnelV56.ps1','SalairesV56.ps1','ServicesPersonnelV56.ps1')){. (Join-Path $PSScriptRoot $name)}
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'StockageClasseurs.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Syntaxe du stockage invalide.'}
$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Write-ITMAtomicJson'},$true);. ([scriptblock]::Create($node.Extent.Text))
function Assert-ServiceV56([bool]$ok,[string]$message){if(-not $ok){throw $message}}
function Reject-ServiceV56([scriptblock]$action,[string]$message){$failed=$false;try{[void](& $action)}catch{$failed=$true};Assert-ServiceV56 $failed $message}
function Assert-ITMPermission([string]$permission){if($permission -eq $script:deniedPermission){throw 'Droit refusé.'}}
$root=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA-personnel-tests-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
$first=(Get-Date).Year-1;$year=$first.ToString()+'-'+($first+1);$month=$first.ToString()+'-11'
$script:storeFolder=$root;$script:store=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N')};$script:book=[pscustomobject]@{FullName=(Join-Path $root 'test.xlsm')}
$script:scolia=[pscustomobject]@{Id=[guid]::NewGuid().ToString('N');StartMonth=9};$script:accountSession=[pscustomobject]@{Id='test';Name='Test';Role='Administrateur'}
$script:data=[pscustomobject]@{closed=$false;promotions=@([pscustomobject]@{Active=$true;Annee=$year})};$context=Get-ScoliaFeatureContext;$actor=Get-ScoliaFeatureActor
try{
 $teacher=New-ScoliaTeacher -Nom 'Enseignant A' -ActiveFrom ($first.ToString()+'-09-01') -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document) Set-ScoliaTeacher -Document $document -Teacher $teacher -Actor $actor -ExpectedRevision $document.Revision})
 # Les heures historiques sont des fixtures, jamais écrites par le service de pointage.
 $personnel=Read-ScoliaFeatureStore -Name personnel
 $entry=[datetimeoffset]::Parse($month+'-03T07:30:00+02:00');$exit=$entry.AddHours(8)
 Set-ScoliaTeacherPunch -Document $personnel -TeacherId $teacher.Id -Direction Entry -Now $entry -Actor $actor -ExpectedRevision $personnel.Revision
 Set-ScoliaTeacherPunch -Document $personnel -TeacherId $teacher.Id -Direction Exit -Now $exit -Actor $actor -ExpectedRevision $personnel.Revision
 Set-ScoliaTeacherPunch -Document $personnel -TeacherId $teacher.Id -Direction Entry -Now $entry.AddDays(1) -Actor $actor -ExpectedRevision $personnel.Revision
 Write-ITMAtomicJson (Get-ScoliaFeatureStorePath -Name personnel) $personnel
 $dates=@(($month+'-03'),($month+'-04'),($month+'-05'))
 $calendar=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context.Id -WorkDates $dates -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document) Set-ScoliaMonthlyCalendar -Document $document -Calendar $calendar -ExpectedRevision $document.Revision})
 $previousMonth=([datetime]::ParseExact($month+'-01','yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)).AddMonths(-1).ToString('yyyy-MM')
 $previousCalendar=New-ScoliaMonthlyCalendar -Month $previousMonth -Year $year -ContextId $context.Id -WorkDates @() -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $previousCalendar -ExpectedRevision $document.Revision})
 $salary=New-ScoliaMonthlySalary -Month $month -Year $year -ContextId $context.Id -TeacherId $teacher.Id -GrossFC 9000 -Actor $actor
 [void](Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document) Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision $document.Revision})
 $revision=0
 Reject-ServiceV56 {Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlySalary -Document $document -Salary $salary -ExpectedRevision $revision}} 'Le service remplace la révision chargée par la révision actuelle.'
 $path=Get-ScoliaFeatureStorePath -Name personnel;$hash=(Get-FileHash $path -Algorithm SHA256).Hash
 Reject-ServiceV56 {Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)($document.Items|Where-Object {$_.Kind -eq 'Punch'}|Select-Object -First 1).EntryAt=$entry.AddMinutes(5).ToString('o')}} 'Le service laisse modifier une heure existante.'
 Assert-ServiceV56 ((Get-FileHash $path -Algorithm SHA256).Hash -eq $hash) 'Une signature historique est modifiée.'
 $gate=[IO.File]::Open((Get-ScoliaFeatureStorePath -Name salaires)+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{Reject-ServiceV56 {Invoke-ScoliaFinalizeMonthlyPayroll $month} 'La finalisation ignore le verrou du fichier de salaire.'}finally{$gate.Dispose()}
 $final=Invoke-ScoliaFinalizeMonthlyPayroll $month;$pay=@($final.Items|Where-Object {$_.Kind -eq 'Payroll'})
 Assert-ServiceV56 ($pay.Count -eq 1 -and $pay[0].NetFC -eq 3000 -and $pay[0].DeductionsFC -eq 6000) 'Les deux absences ne sont pas déduites correctement.'
 Assert-ServiceV56 ($pay[0].RuleVersion -eq 2 -and $pay[0].SourceWeeksComplete) 'La finalisation ne conserve pas les calendriers hebdomadaires V57.'
 Reject-ServiceV56 {Invoke-ScoliaFinalizeMonthlyPayroll $month} 'Le mois est finalisé deux fois.'
 $before=(Get-FileHash $path -Algorithm SHA256).Hash
 Reject-ServiceV56 {Invoke-ScoliaFeatureUpdate -Name personnel -Change {param($document)Set-ScoliaTeacherJustification -Document $document -TeacherId $teacher.Id -Date ($month+'-04') -Reason 'Correction tardive' -Actor $actor -ExpectedRevision $document.Revision}} 'Une justification modifie une paie finalisée.'
 Assert-ServiceV56 ((Get-FileHash $path -Algorithm SHA256).Hash -eq $before) 'Le refus de justification altère le fichier.'
 $calendar2=New-ScoliaMonthlyCalendar -Month $month -Year $year -ContextId $context.Id -WorkDates @($month+'-03') -Actor $actor
 Reject-ServiceV56 {Invoke-ScoliaFeatureUpdate -Name salaires -Change {param($document)Set-ScoliaMonthlyCalendar -Document $document -Calendar $calendar2 -ExpectedRevision $document.Revision}} 'Le calendrier finalisé est modifiable.'
 $script:deniedPermission='Administrer';Reject-ServiceV56 {Invoke-ScoliaFinalizeMonthlyPayroll $month} 'Les droits de finalisation sont ignorés.'
 Write-Output 'PASS : verrous coordonnés, signatures immuables, retenues, finalisation atomique, gel et permissions.'
}finally{[IO.Directory]::Delete($root,$true)}
