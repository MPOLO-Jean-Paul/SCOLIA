$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'CompilationCache.ps1')
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'NotificationsModernes.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
$form=New-Object Windows.Forms.Form
try {
 $button=New-Object ITMActionButton;$button.Text='Enregistrer';$button.Primary=$true;$form.Controls.Add($button)
 $tab=New-Object ITMWorkspaceTab;$tab.Text='Apprenants';$tab.Active=$true;$form.Controls.Add($tab)
 $ribbon=New-Object ITMRibbonCommand;$ribbon.Text='Actualiser';$ribbon.Symbol='refresh';$form.Controls.Add($ribbon)
 if(-not $button -or -not $tab -or -not $ribbon){throw 'Contrôles interactifs introuvables.'}
 'OK - contrôles interactifs V41 chargés.'
} finally {$form.Dispose()}
