# No Forms/Excel: the actual catalog pass must not create or destroy visual controls.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestsCatalogueCommandes.ps1')
$script:testCommands=@();$script:frequentBuilding=$false;$script:ribbonPage='';$script:currentPage='Tableau de bord';Update-ITMRibbonState
. (Import-CatalogueFunction $style 'Add-ITMRibbonGroup')
$script:frequentBuilding=$true;$script:ribbonPage='Tableau de bord'
$control=[pscustomobject]@{Controls=@()};$script:ribbonFlow=[pscustomobject]@{Controls=@($control)}
foreach($page in $pages){$script:currentPage=$page;Update-ITMRibbonState}
if($script:ribbonPage -ne 'Tableau de bord' -or $script:ribbonFlow.Controls.Count -ne 1 -or $script:ribbonFlow.Controls[0] -ne $control){throw 'Hidden-tab registration rebuilt the visible ribbon'}
$labels=@($script:testCommands.Label | Sort-Object -Unique)
if($labels -notcontains 'Cotes et délibération' -or $labels -notcontains 'Grilles remplies' -or $labels.Count -ne 66){throw 'Frequent catalog lost commands'}
$script:frequentBuilding=$false
# Production scripts stay checked; developer test files cannot delay or block launch.
$runtime=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File | Where-Object {$_.Name -notlike 'Tests*.ps1'})
if(@($runtime | Where-Object {$_.Name -like 'Tests*.ps1'}).Count -or 'Application.ps1' -notin $runtime.Name -or 'CompilationCache.ps1' -notin $runtime.Name){throw 'Production validation omitted required files'}
"PASS : catalog registration keeps the visible ribbon, no hidden-group controls constructed; $($runtime.Count) production scripts retained for validation"
