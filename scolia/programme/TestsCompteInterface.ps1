# Windows PowerShell 5.1 -STA. Isolated UI tests: no account, Excel or registry write.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
function Assert($value,[string]$message){if(-not $value){throw $message}}
foreach($mode in @(@($false,$false),@($true,$false),@($true,$true))){
    $d=New-Object ITMAccountForm($mode[0],$mode[1],$false)
    try{
        $d.SetBrand($null,'Jean Paul · Application · Administrateur');$d.Show();[Windows.Forms.Application]::DoEvents()
        Assert (-not $d.Submit.Enabled) 'Empty form enabled.'
        $d.Username.Text='jean.paul';$d.DisplayName.Text='Jean Paul';$d.Password.Text='Secret123';$d.Confirmation.Text='Secret123'
        Assert ($d.Submit.Enabled) 'Valid form disabled.'
        Assert ($d.Password.UseSystemPasswordChar -and $d.Confirmation.UseSystemPasswordChar) 'Password visible by default.'
        $d.SetBusy($true);$d.Password.Text='Secret456'
        Assert (-not $d.Submit.Enabled -and -not $d.RolesAction.Enabled) 'Busy lock cleared by input.'
        $d.SetBusy($false);$d.Confirmation.Text='Secret456';Assert ($d.Submit.Enabled) 'Busy state not released.'
        if($mode[0]){$d.Confirmation.Text='Secret457';Assert (-not $d.Submit.Enabled) 'Mismatched confirmation accepted.';$d.Confirmation.Text='Secret456'}
        $d.MessageLabel.Text='Informations incorrectes. Vérifiez votre identifiant et réessayez.'
        $d.Username.Text='identifiant invalide';Assert (-not $d.Submit.Enabled) 'Invalid username enabled.';$d.Username.Text='jean.paul'
        $q=$d.CurrentAutoScaleDimensions.Height/96.0
        foreach($width in @(560,440,360,560)){
            $d.ClientSize=New-Object Drawing.Size([int]($width*$q),[int]($(if($mode[0]){540}else{600})*$q))
            [Windows.Forms.Application]::DoEvents()
            foreach($c in $d.Controls){if($c.Visible){Assert ($c.Left -ge 0 -and $c.Right -le $d.ClientSize.Width) ('Clipped: '+$c.Text)}}
            Assert (-not $d.Submit.Bounds.IntersectsWith($d.CloseAction.Bounds)) 'Overlapping actions.'
            Assert (-not $d.HorizontalScroll.Visible) 'Unexpected horizontal scrolling.'
        }
        $d.ClientSize=New-Object Drawing.Size([int](360*$q),[int](480*$q));[Windows.Forms.Application]::DoEvents()
        Assert (-not $d.HorizontalScroll.Visible) 'Compact layout has horizontal scrolling.'
        if($d.VerticalScroll.Visible){$d.AutoScrollPosition=New-Object Drawing.Point(0,1000);$d.PerformLayout();Assert ($d.AutoScrollPosition.Y -lt 0) 'Vertical position reset.'}
        if($mode[0]){$d.ShowSavedRecovery('test-code-123456789');$d.Username.Text='autre.compte';Assert (-not $d.Submit.Enabled -and $d.DisplayName.ReadOnly) 'Saved account can be written twice.'}
    }finally{$d.Password.Clear();$d.Confirmation.Clear();$d.DisplayName.Clear();$d.Dispose()}
}
Write-Output 'PASS: account, registration, recovery, masks, confirmation, busy state and responsive layouts.'
