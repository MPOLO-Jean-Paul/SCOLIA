# Windows PowerShell 5.1 -STA. UI only: no Excel, data, registry or account modification.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'IconesFenetres.ps1')
$wave=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'Assets/NotificationDouce.wav'))
if([Text.Encoding]::ASCII.GetString($wave,0,4) -ne 'RIFF' -or [Text.Encoding]::ASCII.GetString($wave,8,4) -ne 'WAVE'){throw 'Fichier sonore invalide.'}
foreach($dark in @($false,$true)){
    $owner=New-Object Windows.Forms.Form
    $owner.BackColor=$(if($dark){[Drawing.Color]::FromArgb(43,52,67)}else{[Drawing.Color]::FromArgb(246,248,252)})
    try{
        foreach($width in @(340,620)){
            $dialog=New-Object ITMErrorDialog -ArgumentList @($owner,'Diagnostic de test : aucune opération effectuée.','ITM CD')
            try{
                $dialog.ClientSize=New-Object Drawing.Size($width,410)
                $dialog.PerformLayout()
                if($dialog.BackColor -ne $owner.BackColor){throw 'Thème du propriétaire perdu.'}
                if(-not $dialog.AcceptButton -or -not $dialog.CancelButton){throw 'Actions clavier absentes.'}
                foreach($control in $dialog.Controls[0].Controls){if($control.Width -le 0 -or $control.Height -le 0){throw 'Contenu non disposé.'}}
            }finally{$dialog.Dispose()}
        }
        # Le toast de succès doit être instanciable sans dialogue bloquant.
        $toastType='ITMSuccessToast' -as [type]
        if(-not $toastType){throw 'Toast de succès moderne absent.'}
    }finally{$owner.Dispose()}
}
Write-Output 'PASS : notification clair/sombre, toast de succès, deux largeurs, actions clavier, fichier WAV.'
