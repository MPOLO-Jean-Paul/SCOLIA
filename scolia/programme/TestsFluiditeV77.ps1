$ErrorActionPreference='Stop'
function Import-TestFunction([string]$file,[string]$name){
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
 $node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(-not $node){throw $name};. ([scriptblock]::Create('function global:'+ $name + $node.Extent.Text.Substring(('function '+$name).Length)))
}
foreach($n in @('Get-ScoliaGridRowKey','Sync-ScoliaGridTable')){Import-TestFunction 'PerformanceInterface.ps1' $n}
Import-TestFunction 'RaccourcisFrequents.ps1' 'Add-ITMCommandUse'
function New-TestTable {
 $t=[Data.DataTable]::new();[void]$t.Columns.Add('Id',[string]);[void]$t.Columns.Add('Montant',[decimal]);return ,$t
}
$a=New-TestTable;[void]$a.Rows.Add('A',10);[void]$a.Rows.Add('B',20)
$b=$a.Copy();$kept=$a.Rows[0];$view=$a.DefaultView;$view.Sort='Montant DESC'
$script:events=0;$view.add_ListChanged({$script:events++})
if(-not (Sync-ScoliaGridTable $a $b @('Id')) -or $events -ne 0){throw 'Identical table notified UI'}
$b.Rows[0]['Montant']=15
if(-not (Sync-ScoliaGridTable $a $b @('Id')) -or $a.Rows[0]['Montant'] -ne 15 -or $events -ne 1){throw 'Changed cell not isolated'}
if(-not [object]::ReferenceEquals($a.Rows[0],$kept) -or $view.Sort -ne 'Montant DESC'){throw 'Row/sort replaced'}
$b.Rows.RemoveAt(1);[void]$b.Rows.Add('C',30)
if(-not (Sync-ScoliaGridTable $a $b @('Id')) -or $a.Rows.Count -ne 2 -or $a.Rows[1]['Id'] -ne 'C'){throw 'Insert/delete failed'}
[void]$b.Rows.Add('A',99);$eventsBefore=$events
if((Sync-ScoliaGridTable $a $b @('Id')) -or $events -ne $eventsBefore -or $a.Rows[0]['Montant'] -ne 15){throw 'Duplicate key mutated table'}
$c=New-TestTable;[void]$c.Rows.Add('A',15);[void]$c.Rows.Add('C',30)
if(-not (Sync-ScoliaGridTable $a $c @())){throw 'Identical unkeyed table changed'}
$c.Rows[0]['Montant']=100
if(Sync-ScoliaGridTable $a $c @()){throw 'Unkeyed selection unsafe'}
# Length-prefixed composite keys must not collide on delimiters.
$x=[Data.DataTable]::new();[void]$x.Columns.Add('A');[void]$x.Columns.Add('B')
[void]$x.Rows.Add('a:b','c');[void]$x.Rows.Add('a','b:c')
if((Get-ScoliaGridRowKey $x.Rows[0] @('A','B')) -ceq (Get-ScoliaGridRowKey $x.Rows[1] @('A','B'))){throw 'Key collision'}
$script:frequentCatalog=@{A=$true};$script:frequentCounts=@{};$script:frequentPending=@{}
function Update-ITMFrequentCommands {}
function Read-ITMCommandCounts {throw 'Registry read during click'}
function New-ItemProperty {throw 'Registry write during click'}
1..100 | ForEach-Object {Add-ITMCommandUse 'A'};Add-ITMCommandUse 'unknown'
if($frequentCounts.A -ne 100 -or $frequentPending.A -ne 100 -or $frequentPending.Count -ne 1){throw 'Usage counts lost'}
'PASS: unchanged table emits zero events; one changed cell emits one event; row identity and sorting preserved; insert/delete; duplicate keys rejected before mutation; unkeyed safety; composite keys; clicks use memory only.'
Import-TestFunction 'Application.ps1' 'Set-Grid'
$bound=New-TestTable;[void]$bound.Rows.Add('A',10)
$grid=[pscustomobject]@{SelectedRows=@();FirstDisplayedScrollingRowIndex=-1;DataSource=$bound;Columns=@([pscustomobject]@{Name='Id'},[pscustomobject]@{Name='Montant'});AutoSizeColumnsMode='None';AutoGenerateColumns=$false;Resumed=$false}
$grid|Add-Member ScriptMethod SuspendLayout {}
$grid|Add-Member ScriptMethod ResumeLayout {$this.Resumed=$true}
# Use a recognized business identity to exercise the actual Set-Grid fast path.
$bound.Columns[0].ColumnName='Matricule';$grid.Columns[0].Name='Matricule'
Set-Grid $grid @([pscustomobject]@{Id='A';Montant=12}) @(@('Id','Matricule'),@('Montant','Montant',[decimal]))
if(-not [object]::ReferenceEquals($grid.DataSource,$bound) -or $bound.Rows[0]['Montant'] -ne 12 -or -not $grid.Resumed -or $script:scoliaBindingGrids.Contains($grid)){throw 'Grid reconciliation/finally failed'}
'PASS: actual Set-Grid retains DataSource, updates values, resumes layout and clears binding guard.'
