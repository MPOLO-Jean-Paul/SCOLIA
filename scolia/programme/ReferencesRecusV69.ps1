function New-ScoliaReceiptIdentity([datetime]$Created=(Get-Date),[string]$OperationId=''){
 if([string]::IsNullOrWhiteSpace($OperationId)){$OperationId=[guid]::NewGuid().ToString('N')}
 $id=[guid]::Empty
 if(-not [guid]::TryParseExact($OperationId,'N',[ref]$id)){throw 'Identifiant de reçu invalide.'}
 $normalized=$id.ToString('N')
 return [pscustomobject]@{Id=$normalized;Reference=('SC-'+$Created.ToString('yyyyMMdd',[Globalization.CultureInfo]::InvariantCulture)+'-'+$normalized.ToUpperInvariant())}
}
function Update-ScoliaCashTableLayout($Table){
 if(-not $Table -or $Table.IsDisposed){return}
 $width=[Math]::Max(80,$Table.ClientSize.Width-$Table.Padding.Horizontal)
 $Table.AutoScroll=$false
 foreach($control in @($Table.Controls)){
  if($control -is [Windows.Forms.Label]){
   $span=$Table.GetColumnSpan($control)
   $usable=$(if($span -gt 1){$width}else{[int]($width*.35)})-$control.Margin.Horizontal
   $control.MaximumSize=New-Object Drawing.Size([Math]::Max(40,$usable),0)
  }
 }
}
function Enable-ScoliaCashTableLayout($Table){
 $Table.Tag='scolia:cash-layout';$Table.AutoScroll=$false;$Table.Padding=New-Object Windows.Forms.Padding(16,12,16,8)
 foreach($control in @($Table.Controls)){
  if($control -is [Windows.Forms.Label] -and $Table.GetColumnSpan($control) -eq 1){$control.Margin=New-Object Windows.Forms.Padding(0,5,10,6)}
  elseif($control -isnot [Windows.Forms.Label]){$control.Margin=New-Object Windows.Forms.Padding(0,4,0,6)}
 }
 $Table.Add_SizeChanged({param($sender,$e)Update-ScoliaCashTableLayout $sender})
 $Table.Parent.Add_Shown({param($sender,$e)foreach($control in @($sender.Controls)){if($control.Tag -eq 'scolia:cash-layout'){Update-ScoliaCashTableLayout $control}}})
 Update-ScoliaCashTableLayout $Table
}
