# Contrôle WinForms sur Windows, sans Excel ni données réelles.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'CompilationCache.ps1')
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'EspaceFonctionnelV67.ps1')
function Get-ITMCommandPermission($name){return 'Lire'}
function Assert-ITMPermission($name){}
$script:main=New-Object Windows.Forms.Form;$script:main.ClientSize=New-Object Drawing.Size(900,600)
$page=New-Object Windows.Forms.Panel;$page.Dock='Fill';$script:main.Controls.Add($page);$script:pages=@{'Scolarité'=$page};$script:currentPage='Scolarité';$script:ribbonFlow=$null
$script:main.Show()
try{
 $one=New-Object Windows.Forms.Form;$field=New-Object Windows.Forms.TextBox;$field.Text='Filtre conservé';$one.Controls.Add($field)
 $script:scoliaInlineRequest=[pscustomobject]@{Name='Recherche globale';Mounted=$false};if(-not (Mount-ScoliaWorkspaceForm $one)){throw 'Interface non intégrée.'}
 $two=New-Object Windows.Forms.Form;$script:scoliaInlineRequest=[pscustomobject]@{Name='Présences';Mounted=$false};[void](Mount-ScoliaWorkspaceForm $two)
 if($one.TopLevel -or $two.TopLevel -or $two.Parent -ne $page -or $one.Visible -or -not $two.Visible){throw 'Intégration ou visibilité incorrecte.'}
 $script:book=[pscustomobject]@{FullName='test.xlsm'};$script:data=[pscustomobject]@{};$script:scolia=[pscustomobject]@{Id='demo';Year='2026-2027'};$script:accountSession=[pscustomobject]@{Id='demo';Role='Mixte'};$script:scoliaInlineIdentity=Get-ScoliaWorkspaceIdentity;$script:scoliaInlineRequest=$null
 Invoke-ScoliaWorkspaceView 'Recherche globale'
 if(-not $one.Visible -or $two.Visible -or $field.Text -ne 'Filtre conservé' -or $field.FindForm() -ne $one){throw 'Filtre ou contexte du formulaire perdu.'}
 foreach($width in @(360,680,1200)){$script:main.ClientSize=New-Object Drawing.Size($width,600);[Windows.Forms.Application]::DoEvents();if($one.Size -ne $page.ClientSize -or $one.Location.X -ne 0 -or $one.Location.Y -ne 0){throw 'Interface ne remplit pas la zone centrale.'}}
 Reset-ScoliaInlineViews;if(-not $one.IsDisposed -or -not $two.IsDisposed -or $script:scoliaInlineViews.Count){throw 'Nettoyage incomplet.'}
 'PASS : interfaces intégrées, contexte FindForm et filtres conservés, tailles 360–1200 et nettoyage.'
}finally{Reset-ScoliaInlineViews;$script:main.Dispose()}
