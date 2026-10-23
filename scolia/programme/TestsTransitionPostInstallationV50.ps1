# Régression V50 : la transition onboarding ne doit plus dépendre d'un Timer local capturé.
$session=Join-Path $PSScriptRoot 'InstallationSession.ps1'
$text=[IO.File]::ReadAllText($session)
if($text -match '\$transitionTimer\s*=\s*New-Object\s+Windows\.Forms\.Timer'){
    throw 'Régression : un Timer local de transition est encore utilisé après installation.'
}
if($text -notmatch '\$form\.DialogResult=\[Windows\.Forms\.DialogResult\]::OK' -or $text -notmatch '\$form\.Close\(\)'){
    throw 'La transition directe vers l étape suivante du parcours n est pas présente.'
}
'V50 OK : après une installation réussie, la fenêtre se ferme directement sans Timer local ni rupture visuelle.'
