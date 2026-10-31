# Les actions gardent la référence de leur fonction, même depuis une fermeture.
function New-ScoliaWorkspaceViewAction([string]$Name){
 $invoker=(Get-Command Invoke-ScoliaWorkspaceView -CommandType Function -ErrorAction Stop).ScriptBlock
 $view=$Name
 return {& $invoker $view}.GetNewClosure()
}
function Get-ScoliaCachedRibbonLayout([int[]]$Counts,[int]$Width,[int]$MaxHeight,[double]$Scale,[int[]]$TextWidths,[int[]]$TextHeights){
 if(-not $script:scoliaRibbonPlanCache){$script:scoliaRibbonPlanCache=@{}}
 $key=@(($Counts -join ','),$Width,$MaxHeight,$Scale.ToString('R',[Globalization.CultureInfo]::InvariantCulture),($TextWidths -join ','),($TextHeights -join ',')) -join '|'
 if($script:scoliaRibbonPlanCache.ContainsKey($key)){return $script:scoliaRibbonPlanCache[$key]}
 $plan=Get-ScoliaRibbonLayout $Counts $Width $MaxHeight $Scale $TextWidths $TextHeights
 if($script:scoliaRibbonPlanCache.Count -ge 32){$script:scoliaRibbonPlanCache.Clear()}
 $script:scoliaRibbonPlanCache[$key]=$plan;return $plan
}
