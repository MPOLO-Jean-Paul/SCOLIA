# Command counts belong to the Windows profile, never to school workbooks.
$script:frequentCatalog=@{};$script:frequentTargets=@{};$script:frequentCounts=@{};$script:frequentButtons=@();$script:frequentBuilding=$false
function Register-ITMFrequentCommand($command) {
    if($command.Target -and $script:frequentTargets.ContainsKey($command.Target)){$id=$script:frequentTargets[$command.Target]}
    else{$id=$script:currentPage+'|'+$command.Label}
    $command | Add-Member -NotePropertyName UsageId -NotePropertyValue $id -Force
    $command | Add-Member -NotePropertyName Page -NotePropertyValue $(if($script:currentPage){$script:currentPage}else{'Tableau de bord'}) -Force
    if(-not $script:frequentCatalog.ContainsKey($id)){$script:frequentCatalog[$id]=$command}
    if($command.Target -and -not $script:frequentTargets.ContainsKey($command.Target)){
        $script:frequentTargets[$command.Target]=$id
        $command.Target.Add_Click({param($sender,$e)
            if(-not $script:busy -and $script:frequentTargets.ContainsKey($sender)){Add-ITMCommandUse $script:frequentTargets[$sender]}
        })
    }
}
function Read-ITMCommandCounts {
    $counts=@{}
    try{
        $key=Get-ItemProperty -LiteralPath 'HKCU:\Software\ITMCD\Secretariat\Raccourcis' -ErrorAction Stop
        $saved=[string]$key.CountsJson | ConvertFrom-Json -ErrorAction Stop
        foreach($entry in $saved.PSObject.Properties){if([long]$entry.Value -ge 0){$counts[$entry.Name]=[long]$entry.Value}}
    }catch{}
    return $counts
}
# Clicks only update memory. Optional usage statistics are merged once at shutdown.
$script:frequentPending=@{}
function Add-ITMCommandUse([string]$id) {
    if(-not $script:frequentCatalog.ContainsKey($id)){return}
    $script:frequentCounts[$id]=[Math]::Min(1000000000,([long]$script:frequentCounts[$id]+1))
    $script:frequentPending[$id]=[long]$script:frequentPending[$id]+1
    Update-ITMFrequentCommands
}
function Save-ITMCommandCounts {
    if(-not $script:frequentPending.Count){return}
    $gate=$null;$acquired=$false
    try{
        $name='Local\ITMCD.Raccourcis.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $gate=[Threading.Mutex]::new($false,$name)
        try{$acquired=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException]{$acquired=$true}
        if(-not $acquired){return}
        $counts=Read-ITMCommandCounts
        foreach($id in $script:frequentPending.Keys){$counts[$id]=[Math]::Min(1000000000,([long]$counts[$id]+[long]$script:frequentPending[$id]))}
        $key='HKCU:\Software\ITMCD\Secretariat\Raccourcis'
        if(-not (Test-Path -LiteralPath $key)){[void](New-Item -Path $key -Force -ErrorAction Stop)}
        [void](New-ItemProperty -Path $key -Name CountsJson -Value ($counts | ConvertTo-Json -Compress) -PropertyType String -Force -ErrorAction Stop)
        $script:frequentPending.Clear()
    }catch{} # Usage ranking is optional; never impede shutdown or financial writes.
    finally{if($gate){if($acquired){$gate.ReleaseMutex()};$gate.Dispose()}}
}
function Get-ITMTopCommands {
    $fallback=@('Inscrire','Paiements','Exporter PDF')
    return @($script:frequentCatalog.Values | Where-Object {Test-ITMPermission $_.Permission} | Sort-Object @{Expression={-[long]$script:frequentCounts[$_.UsageId]}},@{Expression={$i=[Array]::IndexOf($fallback,$_.Label);if($i -lt 0){100}else{$i}}},'UsageId' | Select-Object -First 3)
}
function Update-ITMFrequentCommands {
    if($script:frequentBuilding -or -not $script:frequentButtons.Count){return}
    $top=Get-ITMTopCommands
    for($i=0;$i -lt $script:frequentButtons.Count;$i++){
        $button=$script:frequentButtons[$i]
        if($i -ge $top.Count){$button.Visible=$false;continue}
        $c=$top[$i];$button.Visible=$true;$button.Tag=$c;$button.Symbol=$c.Symbol
        $button.AccessibleName=$c.Label;Set-ITMCommandHelp $button $c
        $button.Enabled=(Test-ITMPermission $c.Permission) -and (-not $script:busy) -and ((-not $c.Book) -or [bool]($script:book -and $script:data)) -and ((-not $c.Write) -or (-not $script:data.closed -and -not $script:payNeedsRefresh))
        if($c.Target){$button.Enabled=$button.Enabled -and $c.Target.Enabled}
        $button.Invalidate()
    }
}
function Initialize-ITMFrequentCommands {
    $script:frequentCounts=Read-ITMCommandCounts
    $oldPage=$(if($script:currentPage){$script:currentPage}else{'Tableau de bord'});$script:frequentBuilding=$true
    try{foreach($page in $script:tabsNames){$script:currentPage=$page;Update-ITMRibbonState}}
    finally{$script:currentPage=$oldPage;$script:frequentBuilding=$false;Update-ITMRibbonState}
    for($i=0;$i -lt 3;$i++){
        $button=New-Object ITMRibbonCommand;$button.IconOnly=$true;$button.Text='';$button.TabIndex=20+$i
        $script:brand.Controls.Add($button);$script:frequentButtons+=,$button
        $button.Add_Click({param($sender,$e)
            if($script:busy -or -not $sender.Enabled){return}
            $command=$sender.Tag;$previousSource=$script:activitySource;$script:activitySource=$sender
            try{
                Assert-ITMPermission $command.Permission
                Show-Page $command.Page
                if($command.Target){Invoke-ITMRibbonControl $command.Target}
                else{[void](& $command.Action);Add-ITMCommandUse $command.UsageId}
            }catch{App-Error $_}finally{$script:activitySource=$previousSource}
        })
    }
    Update-ITMFrequentCommands;Update-ITMNavigation
}
