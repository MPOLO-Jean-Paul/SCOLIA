# Final layout pass: typography first, then navigation, then content.
$script:workspaceLayoutBusy=$false
function Update-ITMWorkspaceLayout {
    if($script:workspaceLayoutBusy -or -not $script:main -or $script:main.IsDisposed){return}
    $script:workspaceLayoutBusy=$true
    try{
        Update-ITMNavigation
        foreach($table in @($script:fluidTables.Keys)){if(-not $table.IsDisposed -and $table.Visible){Update-ITMFluidTable $table}}
        foreach($panel in @($script:pages.Values)+@($script:adminPages.Values)){if($panel.Visible){Update-ITMResponsivePanel $panel}}
    }finally{$script:workspaceLayoutBusy=$false}
}
function Enable-ITMWorkspaceLayout {
    $script:workspaceLayoutTimer=New-Object Windows.Forms.Timer;$script:workspaceLayoutTimer.Interval=60
    $script:workspaceLayoutTimer.Add_Tick({$script:workspaceLayoutTimer.Stop();Update-ITMWorkspaceLayout})
    $schedule={if(-not $script:workspaceLayoutBusy -and $script:workspaceLayoutTimer){$script:workspaceLayoutTimer.Stop();$script:workspaceLayoutTimer.Start()}}
    $script:main.Add_SizeChanged($schedule);$script:content.Add_SizeChanged($schedule)
    $script:main.Add_Shown({Update-ITMWorkspaceLayout})
    $script:main.Add_FormClosed({$script:workspaceLayoutTimer.Stop();$script:workspaceLayoutTimer.Dispose()})
}
