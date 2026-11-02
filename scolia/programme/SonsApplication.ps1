$script:soundKey='HKCU:\Software\ITMCD\Secretariat\Son'
function Read-ITMSoundPreferences {
    try{$p=(Get-ItemProperty -LiteralPath $script:soundKey -Name SettingsJson -ErrorAction Stop).SettingsJson | ConvertFrom-Json
        [ITMErrorDialog]::SoundEnabled=[bool]$p.Enabled;[ITMErrorDialog]::SoundVolume=[Math]::Max(0,[Math]::Min(100,[int]$p.Volume))
        if([string]$p.Tone -in @('Douce','Discrète','Cloche')){[ITMErrorDialog]::SoundTone=[string]$p.Tone}
    }catch{}
}
Read-ITMSoundPreferences
function Build-ITMSoundPreferences($table,$addHeading,$addChoice,$addCheck) {
    & $addHeading 'Son de l''application' 'Le volume et la tonalité s''appliquent immédiatement aux notifications. Ces réglages sont conservés pour votre compte Windows.'
    $enabled=& $addCheck 'Activer les sons';$enabled.Checked=[ITMErrorDialog]::SoundEnabled
    $volume=& $addChoice 'Volume' @('0 %','10 %','25 %','35 %','50 %','75 %','100 %') ([string][ITMErrorDialog]::SoundVolume+' %')
    $tone=& $addChoice 'Tonalité' @('Douce','Discrète','Cloche') ([ITMErrorDialog]::SoundTone)
    $context=[pscustomobject]@{Enabled=$enabled;Volume=$volume;Tone=$tone}
    foreach($control in @($enabled,$volume,$tone)){$control.Tag=$context}
    $change={param($sender,$e)try{
        $c=$sender.Tag;$p=[pscustomobject]@{Enabled=[bool]$c.Enabled.Checked;Volume=[int](([string]$c.Volume.SelectedItem).Replace(' %',''));Tone=[string]$c.Tone.SelectedItem}
        [void](New-Item -Path $script:soundKey -Force);[void](New-ItemProperty -Path $script:soundKey -Name SettingsJson -Value ($p | ConvertTo-Json -Compress) -PropertyType String -Force)
        [ITMErrorDialog]::SoundEnabled=$p.Enabled;[ITMErrorDialog]::SoundVolume=$p.Volume;[ITMErrorDialog]::SoundTone=$p.Tone
        $script:status.Text='Réglages du son enregistrés.'
    }catch{App-Error $_}}
    $enabled.Add_CheckedChanged($change);$volume.Add_SelectionChangeCommitted($change);$tone.Add_SelectionChangeCommitted($change)
    $row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle('Absolute',48)))
    $test=New-Button $table 'Écouter la tonalité' 0 0 220;$test.Dock='Fill';$table.SetCellPosition($test,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)));$table.SetColumnSpan($test,2);$test.Add_Click({[ITMErrorDialog]::PlayNotification()})
}
