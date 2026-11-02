$ErrorActionPreference='Stop'
if($env:OS -ne 'Windows_NT'){throw 'Ce contrôle nécessite Windows et WinForms.'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'PerformanceInterface.ps1'),[ref]$t,[ref]$e)
$fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Wait-ScoliaNetworkTask'},$true);. ([scriptblock]::Create($fn.Extent.Text))
function New-ScoliaDialog($title,$width,$height){$f=New-Object Windows.Forms.Form;$f.Text=$title;return $f}
function New-Label($parent,$text,$x,$y,$w,$h){$l=New-Object Windows.Forms.Label;$l.Text=$text;$parent.Controls.Add($l);return $l}
function Apply-ITMThemeForm {}
$script:main=New-Object Windows.Forms.Form;$heartbeat=New-Object Windows.Forms.Timer;$heartbeat.Interval=20;$script:ticks=0;$heartbeat.Add_Tick({$script:ticks++})
try{
 $heartbeat.Start();$task=[Threading.Tasks.Task]::Delay(600)
 [void](Wait-ScoliaNetworkTask $task 'Test de réactivité : cette fenêtre se ferme automatiquement.')
 if(-not $task.IsCompleted -or $script:ticks -lt 2){throw 'La boucle de messages a été bloquée pendant le réseau.'}
 'PASS WinForms : événements traités pendant l attente asynchrone.'
}finally{$heartbeat.Stop();$heartbeat.Dispose();$script:main.Dispose()}
