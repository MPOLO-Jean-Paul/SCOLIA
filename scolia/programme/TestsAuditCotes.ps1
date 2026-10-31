# Regression checks for fractional grades, stale promotion/course selection and retry state.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CotesDeliberation.ps1')
$fraction=[decimal]20/[decimal]3
if([decimal][double]$fraction -eq $fraction){throw 'Fixture must expose the old decimal round-trip failure'}
if(-not (Test-ITMSavedGrade ([double]$fraction) $fraction)){throw 'Fractional grade incorrectly rejected'}
foreach($bad in @($null,$true,'abc',6.6,0)){if(Test-ITMSavedGrade $bad $fraction){throw 'Changed or invalid saved grade accepted'}}
if(-not (Test-ITMSavedGrade 0 0)){throw 'Zero lost'}
$layout=[pscustomobject]@{Code='INF1';Courses=@([pscustomobject]@{Column=3;Title='Anatomie';Maximum=20})}
$config=[pscustomobject]@{Promotion='INF1';Course='Anatomie';CourseColumn=3}
Assert-ITMGradeSelection $layout $config
foreach($change in @('Promotion','Course','CourseColumn')){
 $test=[pscustomobject]@{Promotion='INF1';Course='Anatomie';CourseColumn=3}
 if($change -eq 'Promotion'){$test.Promotion='INF2'}elseif($change -eq 'Course'){$test.Course='Physique'}else{$test.CourseColumn=4}
 $rejected=$false;try{Assert-ITMGradeSelection $layout $test}catch{$rejected=$true};if(-not $rejected){throw ('Stale '+$change+' accepted')}
}
$context=[pscustomobject]@{Running=$false;Fields=@{Course=[pscustomobject]@{Enabled=$true}};Replace=[pscustomobject]@{Enabled=$true};Open=[pscustomobject]@{Enabled=$true};Save=[pscustomobject]@{Enabled=$true};Output='saved.xlsx';Plan=[pscustomobject]@{Rows=@([pscustomobject]@{Probleme=''})}}
Set-ITMGradeBusy $context $true
if(-not $context.Running -or $context.Replace.Enabled -or $context.Open.Enabled -or $context.Save.Enabled -or $context.Fields.Course.Enabled){throw 'Reentrant controls still enabled'}
try{throw 'Simulated startup or Excel failure'}catch{}finally{Set-ITMGradeBusy $context $false}
if($context.Running -or -not $context.Replace.Enabled -or -not $context.Save.Enabled -or -not $context.Open.Enabled){throw 'Retry remains blocked after failure'}
$context.Plan=$null;Set-ITMGradeBusy $context $false;if($context.Save.Enabled){throw 'Save enabled without analysis'}
$path=Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N')+'.txt')
try{
 [IO.File]::WriteAllText($path,'before');$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;Assert-ITMGradeHash $path $hash
 [IO.File]::WriteAllText($path,'after');$rejected=$false;try{Assert-ITMGradeHash $path $hash}catch{$rejected=$true};if(-not $rejected){throw 'Changed source accepted'}
}finally{if([IO.File]::Exists($path)){[IO.File]::Delete($path)}}
'PASS fractional grades, invalid saved values, promotion/course guards, busy state, retry and changed-file protection'

# Execute the actual click handlers with failing startup/cleanup; no Forms or Office needed.
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CotesDeliberation.ps1'),[ref]$tokens,[ref]$errors)
function App-Error($errorRecord){$script:capturedGradeError=$errorRecord}
function Start-ITMActivity($message,$anchor){if($script:failGradeStartup){throw 'Simulated spinner startup failure'}}
function Stop-ITMActivity{if($script:failGradeStop){throw 'Simulated spinner cleanup failure'}}
function Read-ITMGradePlan($config){throw 'Simulated Excel read failure'}
function Save-ITMGradePlan($plan){throw 'Simulated Excel write failure'}
foreach($button in @('analyse','save','prepare')){
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $n.Member.Value -eq 'Add_Click' -and $n.Expression -is [Management.Automation.Language.VariableExpressionAst] -and $n.Expression.VariablePath.UserPath -eq $button},$true)
 if(-not $node){throw ('Missing handler '+$button)}
 $body=$node.Arguments[0].ScriptBlock.Extent.Text;$handler=[scriptblock]::Create($body.Substring(1,$body.Length-2))
 foreach($failure in @('startup','readwrite','cleanup')){
  $context.Plan=[pscustomobject]@{Rows=@([pscustomobject]@{Probleme=''})};$context.Fields=@{Course=[pscustomobject]@{Text='Anatomie';Enabled=$true};Grid=[pscustomobject]@{Text='test.xlsx';Enabled=$true};Promotion=[pscustomobject]@{Text='INF1';Enabled=$true}};$context.Replace | Add-Member Checked $false -Force
  $sender=[pscustomobject]@{Tag=$context;Enabled=$true};$script:capturedGradeError=$null;$script:failGradeStartup=$failure -eq 'startup';$script:failGradeStop=$failure -eq 'cleanup'
  & $handler $sender $null
  if($context.Running -or -not $context.Fields.Course.Enabled -or -not $context.Replace.Enabled){throw ('Controls stuck after '+$button+' '+$failure)}
  if(-not $script:capturedGradeError){throw ('Failure hidden for '+$button+' '+$failure)}
  if($button -eq 'save' -and -not $context.Save.Enabled){throw 'Cannot retry failed write'}
 }
}
'PASS actual analyse/save/prepare handlers release controls after startup, read/write and cleanup errors'
