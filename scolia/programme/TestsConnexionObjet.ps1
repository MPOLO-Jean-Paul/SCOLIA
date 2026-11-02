# Windows PowerShell 5.1. Mocked Excel, no original workbook or registry mutation.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Application.ps1'),[ref]$tokens,[ref]$errors)
foreach($name in @('Connect-Book','Refresh-Data','Api-Name','Invoke-ITMExcelRead')){
    $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    . ([scriptblock]::Create($node.Extent.Text))
}
function Invoke-ITMExcelRunNoArguments($application,[string]$macro){return $application.Run($macro)}
function Assert($value,$message){if(-not $value){throw $message}}
function Test-ITMWorkbookSet {}
function Disconnect-Book {$script:disconnectCount++;$script:book=$null;$script:excel=$null;$script:store=$null}
function Initialize-ITMStore {$script:book=$script:fakeBook}
function Update-ITMManagedRuntime {}
function Set-ITMActivityMessage {}
function Load-ITMPEC {}
function Rebuild-ITMLearnerIndex {$script:dataRevision++}
function Key($value){return [string]$value}
function Promo($value){return [string]$value}
function Refresh-ITMSnapshotChoices {if($script:uiFailure){throw [NullReferenceException]::new('Simulated grid binding error.')}}
function Refresh-ITMCurrentPage {}
function Update-WorkspaceState {}
function Save-ConnectionChoice {$script:savedChoices++;return $true}
function Write-ITMConnectionDiagnostic($phase,$failure){$script:diagnosticPhase=$phase;return 'diagnostic-test.txt'}
function New-Object {param([string]$TypeName,[object[]]$ArgumentList,[string]$ComObject)
    if($ComObject -eq 'Excel.Application'){return $script:fakeExcel}
    if($PSBoundParameters.ContainsKey('ArgumentList')){return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName -ArgumentList $ArgumentList}
    return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ITM-connection-test-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($temp);$path=Join-Path $temp 'test.xlsm'
[IO.File]::WriteAllText($path,'test fixture');[IO.File]::WriteAllText((Join-Path $temp 'registre.json'),'{}')
$script:fakeBook=[pscustomobject]@{Name='test.xlsm';FullName=$path;ReadOnly=$false}
$script:fakeExcel=[pscustomobject]@{Visible=$false;DisplayAlerts=$false;EnableEvents=$false;AutomationSecurity=3;Workbooks=[pscustomobject]@{}}
$script:fakeExcel | Add-Member ScriptMethod Run {param($macro)if($macro.EndsWith('ITMAppVersion')){return 'ITM-DESKTOP-4'};return $script:snapshotJSON}
$script:fileLabel=[pscustomobject]@{Text='';ForeColor=[Drawing.Color]::Black;IsDisposed=$false}
$script:status=[pscustomobject]@{Text=''};$script:payDate=[pscustomobject]@{Enabled=$false;MaxDate=[DateTime]::Now;Value=[DateTime]::Now}
$script:payDateHint=[pscustomobject]@{Text=''};$script:inputHelp=[pscustomobject]@{}
$script:inputHelp | Add-Member ScriptMethod SetToolTip {param($control,$text)}
try{
    $script:snapshotJSON='{"learners":[],"payments":[],"promotions":[],"months":[],"balances":[],"issues":[],"closed":false}'
    $script:uiFailure=$true;$script:disconnectCount=0;$script:savedChoices=0;$script:viewRevision=$null
    Connect-Book $path
    Assert ($script:book -and $script:snapshotValid -and $script:savedChoices -eq 1) 'Valid connection lost on UI failure.'
    Assert ($script:disconnectCount -eq 1 -and $script:diagnosticPhase -eq 'Affichage après connexion') 'UI failure disconnected verified data.'
    Assert ($null -ne $script:viewRevision) 'Missing view cache not initialized.'
    $script:uiFailure=$false;$script:savedChoices=0
    function Test-CallerShadow {$excel=$null;$book=$null;Connect-Book $path}
    Test-CallerShadow
    Assert ($script:book -and $script:savedChoices -eq 1) 'Caller variable shadow broke owned Excel instance.'
    foreach($bad in @('', 'null', '{"learners":[]}')){
        $script:snapshotJSON=$bad;$script:savedChoices=0;$failed=$false
        try{Connect-Book $path}catch{$failed=$true;Assert ($_.Exception.Message -match 'Lecture des données') 'Missing phase detail.'}
        Assert ($failed -and -not $script:book -and $script:savedChoices -eq 0) 'Invalid snapshot accepted or remembered.'
    }
    Write-Output 'PASS: verified connection survives view failure, empty cache, caller scope, invalid snapshots and diagnostic phases.'
}finally{[IO.Directory]::Delete($temp,$true)}
