function Get-ScoliaInterfaceTextSize {
 if($script:displayPreferences -and [int]$script:displayPreferences.TextSize -in @(9,10,11,12,14)){return [double]$script:displayPreferences.TextSize}
 return [double]10
}
function Set-ScoliaUniformTypography($Root){
 if(-not $Root -or $Root.IsDisposed){return}
 if(-not $script:scoliaUniformFonts){$script:scoliaUniformFonts=@{}}
 $size=Get-ScoliaInterfaceTextSize
 if('ITMErrorDialog' -as [type]){[ITMErrorDialog]::InterfaceTextSize=[single]$size}
 if('ITMActivity' -as [type]){[ITMActivity]::InterfaceTextSize=[single]$size}
 if('ITMDesign' -as [type]){[ITMDesign]::InterfaceTextSize=[single]$size}
 $style=$Root.Font.Style;$key=[string]$size+'|'+[string]$style
 if(-not $script:scoliaUniformFonts.ContainsKey($key)){$script:scoliaUniformFonts[$key]=New-Object Drawing.Font('Segoe UI',[single]$size,$style)}
 if($Root.Font.FontFamily.Name -ne 'Segoe UI' -or [Math]::Abs($Root.Font.SizeInPoints-$size) -gt .01){$Root.Font=$script:scoliaUniformFonts[$key]}
 if($Root -is [Windows.Forms.DateTimePicker]){$Root.CalendarFont=$Root.Font}
 if($Root -is [Windows.Forms.DataGridView] -and (Get-Command Set-ITMGridAppearance -ErrorAction SilentlyContinue)){Set-ITMGridAppearance $Root}
 if($Root -is [Windows.Forms.ToolStrip]){foreach($item in @($Root.Items)){Set-ScoliaUniformTypography $item}}
 if($Root -is [Windows.Forms.ToolStripDropDownItem]){foreach($item in @($Root.DropDownItems)){Set-ScoliaUniformTypography $item}}
 foreach($child in @($Root.Controls)){Set-ScoliaUniformTypography $child}
}
