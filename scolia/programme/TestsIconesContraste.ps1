# Windows PowerShell 5.1 -STA : UI only, no Excel/data/account/registry modification.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
if(-not ('ITMRibbonCommand' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
foreach($dark in @($false,$true)){
    foreach($dpi in @(96,144,192)){
        foreach($symbol in @('pdf','word','csv')){
            $button=New-Object ITMRibbonCommand;$button.Size=New-Object Drawing.Size(180,52)
            $button.BackColor=$(if($dark){[Drawing.Color]::FromArgb(53,65,84)}else{[Drawing.Color]::White})
            $button.IconOnly=$true;$button.Symbol=$symbol;$button.Text='Exporter Word'
            $bitmap=New-Object Drawing.Bitmap(180,52);$bitmap.SetResolution($dpi,$dpi);$g=[Drawing.Graphics]::FromImage($bitmap)
            $event=New-Object Windows.Forms.PaintEventArgs($g,(New-Object Drawing.Rectangle(0,0,180,52)))
            try{
                $g.Clear($button.BackColor)
                [void]$button.GetType().GetMethod('OnPaint',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($button,[object[]]@($event))
                $size=18*$dpi/96.0
                $bounds=New-Object Drawing.Rectangle(([int][Math]::Floor((180-$size)/2)-2),([int][Math]::Floor((52-$size)/2)-2),([int]$size+5),([int]$size+5))
                $ink=0
                for($y=0;$y -lt 52;$y++){for($x=0;$x -lt 180;$x++){
                    if($bitmap.GetPixel($x,$y).ToArgb() -ne $button.BackColor.ToArgb()){
                        $ink++;if(-not $bounds.Contains($x,$y)){throw ('Icône débordante : '+$symbol+' / '+$dpi+' DPI')}
                    }
                }}
                if($ink -lt 20){throw 'Icône vide.'}
            }finally{$event.Dispose();$g.Dispose();$bitmap.Dispose();$button.Dispose()}
        }
    }
}
foreach($enabled in @($false,$true)){
    $back=[Drawing.Color]::FromArgb(67,81,103)
    $ink=[ITMReadableColors]::Ink([Drawing.Color]::Black,$back,$enabled)
    if([ITMReadableColors]::Contrast($ink,$back) -lt 4.5){throw 'Contraste insuffisant.'}
}
Write-Output 'PASS : icônes sans débordement, trois DPI, clair/sombre ; contraste activé/désactivé.'
