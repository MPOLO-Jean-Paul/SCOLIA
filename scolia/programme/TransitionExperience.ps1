# Transition visuelle globale SCOLIA.
# L'indicateur ITMActivity vit sur son propre thread UI : il continue donc a tourner
# pendant le chargement des scripts, la construction de la configuration ou de l'accueil.
if(-not (Get-Variable ScoliaTransitionVisual -Scope Global -ErrorAction SilentlyContinue)){$global:ScoliaTransitionVisual=$null}
function Get-ScoliaTransitionDarkMode {
    try {
        $theme=''
        $saved=Get-ItemProperty -LiteralPath 'HKCU:\Software\ITMCD\Secretariat\Affichage' -ErrorAction Stop
        if($saved.PSObject.Properties['SettingsJson']){$theme=[string](([string]$saved.SettingsJson|ConvertFrom-Json -ErrorAction Stop).Theme)}
        elseif($saved.PSObject.Properties['Theme']){$theme=[string]$saved.Theme}
        if($theme -eq 'Sombre'){return $true}
        if($theme -eq 'Clair'){return $false}
    } catch { }
    try {
        $windows=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction Stop
        if($windows.PSObject.Properties['AppsUseLightTheme']){return ([int]$windows.AppsUseLightTheme -eq 0)}
    } catch { }
    return $false
}
function Show-ScoliaTransition([string]$message) {
    try {
        $text=$(if([string]::IsNullOrWhiteSpace($message)){'Préparation de votre espace...'}else{$message.Trim()})
        $display=$text
        if($global:ScoliaTransitionVisual){
            $global:ScoliaTransitionVisual.Update($display)
            return
        }
        $active=[Windows.Forms.Form]::ActiveForm
        $screen=$(if($active){[Windows.Forms.Screen]::FromControl($active).WorkingArea}else{[Windows.Forms.Screen]::FromPoint([Windows.Forms.Cursor]::Position).WorkingArea})
        $width=[Math]::Max(100,[Math]::Min(480,$screen.Width-32))
        $height=[Math]::Min(164,[Math]::Max(40,$screen.Height-32))
        $left=[int]($screen.Left+($screen.Width-$width)/2)
        $top=[int]($screen.Top+($screen.Height-$height)/2)
        $rect=New-Object Drawing.Rectangle($left,$top,$width,$height)
        $dark=Get-ScoliaTransitionDarkMode
        $global:ScoliaTransitionVisual=New-Object ITMActivity($display,$rect,[IntPtr]::Zero,'',$dark,$true)
        # Attendre le premier rendu avant de fermer l'écran courant, sans DoEvents réentrant.
        [void]$global:ScoliaTransitionVisual.WaitUntilVisible(1200)
    } catch {
        # Une indication de transition ne doit jamais empêcher SCOLIA de poursuivre.
        Close-ScoliaTransition
    }
}
function Update-ScoliaTransition([string]$message) {
    try {
        if(-not $global:ScoliaTransitionVisual){Show-ScoliaTransition $message;return}
        $text=$(if([string]::IsNullOrWhiteSpace($message)){'Préparation de votre espace...'}else{$message.Trim()})
        $global:ScoliaTransitionVisual.Update($text)
    } catch { }
}
function Close-ScoliaTransition {
    try{if($global:ScoliaTransitionVisual){$global:ScoliaTransitionVisual.Dispose()}}catch{}
    finally{$global:ScoliaTransitionVisual=$null}
}
